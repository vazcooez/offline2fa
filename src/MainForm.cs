using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Offline2FA
{
    sealed class BufferedFlowPanel : FlowLayoutPanel
    {
        public BufferedFlowPanel()
        {
            DoubleBuffered = true;
        }
    }

    sealed class MainForm : Form, IMessageFilter
    {
        const int AutoLockMinutes = 10;
        const int ClipboardClearSeconds = 30;

        Vault vault;
        readonly bool isNewVault;
        readonly int initialFailedAttempts;

        readonly Panel header;
        readonly Label titleLabel;
        readonly FlowLayoutPanel toolbar;
        readonly Button addButton, historyButton, removedButton, lockButton, moreButton;
        readonly TextBox search;
        readonly BufferedFlowPanel list;
        readonly Label emptyLabel;
        readonly Label status;
        readonly ContextMenuStrip moreMenu;
        readonly Timer tickTimer;
        readonly Timer clipboardTimer;

        readonly NotifyIcon tray;
        readonly ContextMenuStrip trayMenu;
        readonly List<ToolStripMenuItem> trayCodeItems = new List<ToolStripMenuItem>();

        string lastCopiedCode;
        int modalDepth;
        int activeCount;
        DateTime statusExpires = DateTime.MaxValue;
        bool exiting;
        bool trayHintShown;
        UnlockForm pendingUnlock;

        public MainForm(Vault vault, bool isNewVault, int failedAttempts)
        {
            this.vault = vault;
            this.isNewVault = isNewVault;
            initialFailedAttempts = failedAttempts;

            Text = Ui.AppName;
            Icon = Ui.AppIcon;
            Font = Ui.BaseFont;
            BackColor = Ui.Window;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Ui.Px(560), Ui.Px(680));
            MinimumSize = new Size(Ui.Px(480), Ui.Px(420));
            KeyPreview = true;

            // Account list
            list = new BufferedFlowPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Ui.Window,
                Padding = new Padding(Ui.Px(16), Ui.Px(16), Ui.Px(16), Ui.Px(6))
            };
            list.ClientSizeChanged += delegate { ResizeCards(); };
            emptyLabel = new Label
            {
                AutoSize = false,
                Height = Ui.Px(180),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Ui.Muted,
                Margin = Padding.Empty
            };

            // Header: title + toolbar, then Add button + search
            header = new Panel { Dock = DockStyle.Top, Height = Ui.Px(104), BackColor = Ui.Surface };
            header.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Ui.Border))
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            };
            titleLabel = new Label { Text = Ui.AppName, Font = Ui.HeadingFont, ForeColor = Ui.Text, AutoSize = true };
            toolbar = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                BackColor = Ui.Surface,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            historyButton = Ui.MakeToolbarButton("History");
            removedButton = Ui.MakeToolbarButton("Removed");
            lockButton = Ui.MakeToolbarButton("Lock");
            moreButton = Ui.MakeToolbarButton("More ▾");
            toolbar.Controls.AddRange(new Control[] { historyButton, removedButton, lockButton, moreButton });
            toolbar.SizeChanged += delegate { LayoutHeader(); };
            addButton = Ui.MakeButton("+  Add account", true);
            search = Ui.MakeTextBox(false);
            Ui.SetPlaceholder(search, "Search accounts");
            header.Controls.AddRange(new Control[] { titleLabel, toolbar, addButton, search });
            header.Resize += delegate { LayoutHeader(); };

            status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = Ui.Px(30),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Ui.Surface,
                ForeColor = Ui.Muted,
                Font = Ui.SmallFont,
                Padding = new Padding(Ui.Px(14), 0, Ui.Px(14), 0),
                AutoEllipsis = true
            };
            status.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Ui.Border))
                    e.Graphics.DrawLine(pen, 0, 0, status.Width, 0);
            };

            // Fill control first so it docks last.
            Controls.Add(list);
            Controls.Add(header);
            Controls.Add(status);

            moreMenu = new ContextMenuStrip { Font = Ui.BaseFont };
            moreMenu.Items.Add("Change master password…", null, delegate { ChangePassword(); });
            moreMenu.Items.Add("Back up vault file…", null, delegate { BackupVault(); });
            moreMenu.Items.Add("Move vault file…", null, delegate { MoveVault(); });
            moreMenu.Items.Add("Show vault file in Explorer", null, delegate { ShowVaultInExplorer(); });
            moreMenu.Items.Add(new ToolStripSeparator());
            moreMenu.Items.Add("What's new", null, delegate { ShowWhatsNew(false); });
            moreMenu.Items.Add("Keyboard shortcuts", null, delegate { ShowShortcuts(); });
            moreMenu.Items.Add("About " + Ui.AppName, null, delegate { ShowAbout(); });
            moreMenu.Items.Add(new ToolStripSeparator());
            moreMenu.Items.Add("Exit", null, delegate { ExitApplication(); });

            // Tray icon: closing the window keeps the app running here.
            trayMenu = new ContextMenuStrip { Font = Ui.BaseFont };
            trayMenu.Opening += delegate { BuildTrayMenu(); };
            tray = new NotifyIcon
            {
                Icon = new Icon(Ui.AppIcon, SystemInformation.SmallIconSize),
                Text = Ui.AppName,
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowFromTray(); };

            addButton.Click += delegate { AddAccount(); };
            historyButton.Click += delegate { ShowHistory(); };
            removedButton.Click += delegate { ShowRemoved(); };
            lockButton.Click += delegate { Lock("Locked manually"); };
            moreButton.Click += delegate { moreMenu.Show(moreButton, new Point(0, moreButton.Height)); };
            search.TextChanged += delegate { Rebuild(); };
            search.KeyDown += OnSearchKeyDown;

            tickTimer = new Timer { Interval = 250 };
            tickTimer.Tick += OnTick;
            clipboardTimer = new Timer { Interval = ClipboardClearSeconds * 1000 };
            clipboardTimer.Tick += delegate
            {
                clipboardTimer.Stop();
                if (ClearClipboardIfOurs()) SetStatus("Clipboard cleared.", true);
            };

            SystemEvents.SessionSwitch += OnSessionSwitch;
            Application.AddMessageFilter(this);

            Rebuild();
        }

        // ---------------------------------------------------------------- lifecycle

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LayoutHeader();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!isNewVault)
            {
                vault.Log("Unlocked", null, FailedAttemptsText(initialFailedAttempts));
                TrySave();
            }
            tickTimer.Start();
            Activate();
            if (activeCount == 0) addButton.Focus();
            else search.Focus();
            if (ReleaseNotes.HasUnseenChanges(AppSettings.LastSeenVersion))
                BeginInvoke((MethodInvoker)delegate { ShowWhatsNew(true); });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // The close button hides the window to the tray; Exit (tray or More menu) really quits.
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            base.OnFormClosing(e);
            if (e.Cancel || vault == null) return;

            tickTimer.Stop();
            HistoryEntry entry = vault.Log("Closed", null, null);
            try
            {
                vault.Save();
            }
            catch (Exception ex)
            {
                DialogResult answer = MessageBox.Show(this, "Your latest changes could not be saved:\r\n" + ex.Message + "\r\n\r\nClose anyway?",
                    Ui.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes && e.CloseReason == CloseReason.UserClosing)
                {
                    vault.Data.History.Remove(entry);
                    e.Cancel = true;
                    exiting = false;
                    tickTimer.Start();
                    return;
                }
            }
            ClearClipboardIfOurs();
            vault.Close();
            vault = null;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            Application.RemoveMessageFilter(this);
            tray.Visible = false;
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tray.Visible = false;
                tray.Dispose();
                trayMenu.Dispose();
                tickTimer.Dispose();
                clipboardTimer.Dispose();
                moreMenu.Dispose();
                emptyLabel.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---------------------------------------------------------------- layout

        void LayoutHeader()
        {
            int pad = Ui.Px(16);
            int width = header.ClientSize.Width;
            toolbar.Location = new Point(width - toolbar.Width - Ui.Px(10), Ui.Px(12));
            titleLabel.Location = new Point(pad - Ui.Px(2), toolbar.Top + (toolbar.Height - titleLabel.Height) / 2);

            int rowY = Ui.Px(56);
            addButton.Location = new Point(pad, rowY);
            search.Left = addButton.Right + Ui.Px(10);
            search.Top = rowY + (addButton.Height - search.Height) / 2;
            search.Width = Math.Max(Ui.Px(80), width - search.Left - pad);
        }

        int CardWidth(int itemCount)
        {
            int width = list.ClientSize.Width - list.Padding.Horizontal;
            int contentHeight = list.Padding.Vertical + itemCount * Ui.Px(94);
            if (!list.VerticalScroll.Visible && contentHeight > list.ClientSize.Height)
                width -= SystemInformation.VerticalScrollBarWidth;
            return Math.Max(Ui.Px(200), width);
        }

        void ResizeCards()
        {
            int width = CardWidth(list.Controls.Count);
            list.SuspendLayout();
            foreach (Control c in list.Controls) c.Width = width;
            list.ResumeLayout();
        }

        void Rebuild()
        {
            if (vault == null) return;

            string filter = search.Text.Trim();
            long now = Totp.UnixNow();
            var items = new List<Control>();
            int active = 0, removed = 0;
            foreach (Account a in vault.Data.Accounts)
            {
                if (a.Hidden)
                {
                    removed++;
                    continue;
                }
                active++;
                if (filter.Length > 0 && !Matches(a, filter)) continue;

                var card = new AccountCard(a);
                card.CopyRequested += delegate(object s, EventArgs e) { CopyCode((AccountCard)s); };
                card.RemoveRequested += delegate(object s, EventArgs e) { RemoveAccount((AccountCard)s); };
                card.RenameRequested += delegate(object s, EventArgs e) { RenameAccount((AccountCard)s); };
                card.Tick(now);
                items.Add(card);
            }
            if (items.Count == 0)
            {
                emptyLabel.Text = active == 0
                    ? "No accounts yet.\n\nClick \"+ Add account\" to register your first 2FA code."
                    : "No accounts match \"" + filter + "\".";
                items.Add(emptyLabel);
            }

            list.SuspendLayout();
            ClearList();
            int width = CardWidth(items.Count);
            foreach (Control c in items) c.Width = width;
            list.Controls.AddRange(items.ToArray());
            list.ResumeLayout(true);

            activeCount = active;
            removedButton.Text = removed > 0 ? "Removed (" + removed + ")" : "Removed";
            ShowDefaultStatus();
        }

        void ClearList()
        {
            var old = new Control[list.Controls.Count];
            list.Controls.CopyTo(old, 0);
            list.Controls.Clear();
            foreach (Control c in old)
                if (c != emptyLabel) c.Dispose();
        }

        static bool Matches(Account a, string filter)
        {
            return (a.Issuer != null && a.Issuer.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                || (a.Name != null && a.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        IEnumerable<AccountCard> Cards()
        {
            foreach (Control c in list.Controls)
            {
                var card = c as AccountCard;
                if (card != null) yield return card;
            }
        }

        // ---------------------------------------------------------------- status

        void ShowDefaultStatus()
        {
            statusExpires = DateTime.MaxValue;
            status.ForeColor = Ui.Muted;
            status.Text = activeCount == 0
                ? "Add your first account to get started."
                : activeCount + (activeCount == 1 ? " account" : " accounts") + "   ·   Click a code to copy it";
        }

        void SetStatus(string message, bool temporary)
        {
            status.ForeColor = Ui.Text;
            status.Text = message;
            statusExpires = temporary ? DateTime.UtcNow.AddSeconds(8) : DateTime.MaxValue;
        }

        // ---------------------------------------------------------------- timers & locking

        void OnTick(object sender, EventArgs e)
        {
            if (vault == null) return;
            long now = Totp.UnixNow();
            if (Visible)
                foreach (AccountCard card in Cards()) card.Tick(now);
            if (trayMenu.Visible) UpdateTrayCodes(now);

            if (DateTime.UtcNow > statusExpires) ShowDefaultStatus();

            if (modalDepth == 0 && IdleTime() > TimeSpan.FromMinutes(AutoLockMinutes))
                Lock("Auto-locked after " + AutoLockMinutes + " minutes of inactivity");
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason != SessionSwitchReason.SessionLock || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate
            {
                if (vault != null && modalDepth == 0) Lock("Windows session locked");
            });
        }

        /// <summary>
        /// Locks the vault. If the window was on screen the unlock dialog appears in its place;
        /// otherwise the app stays locked in the tray until it is opened again.
        /// </summary>
        void Lock(string reason)
        {
            if (vault == null || modalDepth > 0) return;

            bool wasOnScreen = Visible && WindowState != FormWindowState.Minimized;
            tickTimer.Stop();
            clipboardTimer.Stop();
            ClearClipboardIfOurs();
            trayMenu.Close();
            vault.Log("Locked", null, reason);
            TrySave();
            vault.Close();
            vault = null;
            ClearList();
            Hide();

            if (wasOnScreen) ShowFromTray();
        }

        /// <summary>Shows the unlock dialog. Returns false if the user closed it (the app stays in the tray).</summary>
        bool Unlock()
        {
            if (pendingUnlock != null)
            {
                pendingUnlock.Activate();
                return false;
            }
            using (var unlock = new UnlockForm(true))
            {
                pendingUnlock = unlock;
                DialogResult result;
                try { result = unlock.ShowDialog(); }
                finally { pendingUnlock = null; }
                if (result != DialogResult.OK) return false;

                vault = unlock.Result;
                vault.Log("Unlocked", null, FailedAttemptsText(unlock.FailedAttempts));
                TrySave();
            }
            search.Text = "";
            Rebuild();
            tickTimer.Start();
            return true;
        }

        void ShowFromTray()
        {
            if (vault == null && !Unlock()) return;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void HideToTray()
        {
            Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                tray.ShowBalloonTip(3000, Ui.AppName,
                    "Still running in the notification area. Right-click the icon to copy a code or exit.", ToolTipIcon.Info);
            }
        }

        void ExitApplication()
        {
            if (modalDepth > 0 || pendingUnlock != null)
            {
                if (pendingUnlock != null) pendingUnlock.Activate();
                else ShowFromTray();
                return;
            }
            exiting = true;
            Close();
        }

        // ---------------------------------------------------------------- tray menu

        void BuildTrayMenu()
        {
            foreach (ToolStripItem old in trayMenu.Items.Cast<ToolStripItem>().ToArray()) old.Dispose();
            trayMenu.Items.Clear();
            trayCodeItems.Clear();

            if (vault == null)
            {
                trayMenu.Items.Add(new ToolStripMenuItem("Locked") { Enabled = false });
                var unlock = new ToolStripMenuItem("Unlock…", null, delegate { ShowFromTray(); }) { Font = new Font(Ui.BaseFont, FontStyle.Bold) };
                trayMenu.Items.Add(unlock);
            }
            else
            {
                foreach (Account account in vault.Data.Accounts)
                {
                    if (account.Hidden) continue;
                    Account target = account;
                    var item = new ToolStripMenuItem(account.DisplayName.Replace("&", "&&"), null, delegate { CopyCode(target, true); });
                    item.Tag = account;
                    item.ToolTipText = "Click to copy the code";
                    trayCodeItems.Add(item);
                }
                if (trayCodeItems.Count == 0)
                {
                    trayMenu.Items.Add(new ToolStripMenuItem("No accounts yet") { Enabled = false });
                }
                else
                {
                    trayMenu.Items.Add(new ToolStripMenuItem("Click an account to copy its code") { Enabled = false });
                    trayMenu.Items.AddRange(trayCodeItems.ToArray());
                    UpdateTrayCodes(Totp.UnixNow());
                }
            }

            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(new ToolStripMenuItem("Show " + Ui.AppName, null, delegate { ShowFromTray(); })
            {
                Font = new Font(Ui.BaseFont, FontStyle.Bold)
            });
            if (vault != null)
                trayMenu.Items.Add(new ToolStripMenuItem("Lock", null, delegate { Lock("Locked from the tray"); }) { Enabled = modalDepth == 0 });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { ExitApplication(); }));
        }

        void UpdateTrayCodes(long now)
        {
            foreach (ToolStripMenuItem item in trayCodeItems)
            {
                // Codes are never shown in the tray; only the time left before the current code changes.
                var account = (Account)item.Tag;
                int period = account.SafePeriod;
                item.ShortcutKeyDisplayString = CodeFor(account, now) == null
                    ? "invalid key"
                    : (period - now % period) + "s left";
            }
        }

        static string CodeFor(Account account, long now)
        {
            byte[] key;
            try { key = Base32.Decode(account.Secret); }
            catch (FormatException) { return null; }
            string code = Totp.Generate(key, now, account.SafePeriod, account.SafeDigits, account.Algorithm);
            Array.Clear(key, 0, key.Length);
            return code;
        }

        static string FailedAttemptsText(int failed)
        {
            if (failed <= 0) return null;
            return failed + (failed == 1 ? " incorrect password attempt" : " incorrect password attempts") + " before unlocking";
        }

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        static TimeSpan IdleTime()
        {
            var info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(info);
            if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
        }

        // Send mouse-wheel input to the account list whenever the pointer is over it.
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg != WM_MOUSEWHEEL || vault == null || !Visible || modalDepth > 0 || m.HWnd == list.Handle) return false;
            long lParam = m.LParam.ToInt64();
            var point = new Point((short)(lParam & 0xffff), (short)((lParam >> 16) & 0xffff));
            if (!list.RectangleToScreen(list.ClientRectangle).Contains(point)) return false;
            Ui.SendMessage(list.Handle, m.Msg, m.WParam, m.LParam);
            return true;
        }

        // ---------------------------------------------------------------- keyboard

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (vault != null && modalDepth == 0)
            {
                switch (keyData)
                {
                    case Keys.Control | Keys.N: AddAccount(); return true;
                    case Keys.Control | Keys.F: search.Focus(); search.SelectAll(); return true;
                    case Keys.Control | Keys.H: ShowHistory(); return true;
                    case Keys.Control | Keys.L: Lock("Locked manually"); return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && search.Text.Length > 0)
            {
                search.Text = "";
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                foreach (AccountCard card in Cards())
                {
                    CopyCode(card);
                    break;
                }
                e.SuppressKeyPress = true;
            }
        }

        // ---------------------------------------------------------------- modal helpers

        DialogResult RunModal(Func<DialogResult> show)
        {
            modalDepth++;
            try { return show(); }
            finally { modalDepth--; }
        }

        DialogResult ShowMessage(string text, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return RunModal(delegate { return MessageBox.Show(this, text, Ui.AppName, buttons, icon); });
        }

        bool TrySave()
        {
            try
            {
                vault.Save();
                return true;
            }
            catch (Exception ex)
            {
                ShowMessage("Could not save the vault:\r\n" + ex.Message + "\r\n\r\nFile: " + Vault.VaultPath, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // ---------------------------------------------------------------- actions

        void CopyCode(AccountCard card)
        {
            CopyCode(card.Account, false);
        }

        void CopyCode(Account account, bool fromTray)
        {
            if (vault == null) return;
            string code = CodeFor(account, Totp.UnixNow());
            if (string.IsNullOrEmpty(code))
            {
                SetStatus("\"" + account.DisplayName + "\" has an invalid secret key.", true);
                return;
            }
            try
            {
                var data = new DataObject();
                data.SetData(DataFormats.UnicodeText, code);
                // Keep one-time codes out of Windows clipboard history and cloud clipboard sync.
                data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
                data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
                data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
                Clipboard.SetDataObject(data, true, 10, 50);
            }
            catch (Exception ex)
            {
                SetStatus("Could not copy to the clipboard: " + ex.Message, true);
                if (fromTray) tray.ShowBalloonTip(3000, Ui.AppName, "Could not copy to the clipboard.", ToolTipIcon.Error);
                return;
            }

            lastCopiedCode = code;
            clipboardTimer.Stop();
            clipboardTimer.Start();
            foreach (AccountCard card in Cards())
                if (card.Account == account) card.FlashCopied();
            string message = "Copied code for " + account.DisplayName + ". The clipboard clears in " + ClipboardClearSeconds + " seconds.";
            SetStatus(message, true);
            if (fromTray) tray.ShowBalloonTip(2000, Ui.AppName, message, ToolTipIcon.None);
            vault.Log("Code copied", account, fromTray ? "From the tray menu" : null);
            TrySave();
        }

        bool ClearClipboardIfOurs()
        {
            if (lastCopiedCode == null) return false;
            string code = lastCopiedCode;
            lastCopiedCode = null;
            try
            {
                if (Clipboard.ContainsText() && Clipboard.GetText() == code)
                {
                    Clipboard.Clear();
                    return true;
                }
            }
            catch (ExternalException) { }
            return false;
        }

        void AddAccount()
        {
            if (vault == null) return;
            using (var form = new AddAccountForm(vault.Data.Accounts))
            {
                if (RunModal(delegate { return form.ShowDialog(this); }) != DialogResult.OK) return;

                Account account = form.Result;
                vault.Data.Accounts.Add(account);
                HistoryEntry entry = vault.Log("Account added", account,
                    account.Digits + " digits, " + account.Period + "s, " + account.Algorithm);
                if (!TrySave())
                {
                    vault.Data.Accounts.Remove(account);
                    vault.Data.History.Remove(entry);
                    return;
                }

                search.Text = "";
                Rebuild();
                foreach (AccountCard card in Cards())
                    if (card.Account == account) list.ScrollControlIntoView(card);
                SetStatus("Added " + account.DisplayName + ".", true);
            }
        }

        void RenameAccount(AccountCard card)
        {
            if (vault == null) return;
            Account account = card.Account;
            using (var form = new RenameAccountForm(account.DisplayName))
            {
                if (RunModal(delegate { return form.ShowDialog(this); }) != DialogResult.OK || vault == null) return;

                string oldName = account.DisplayName;
                string oldIssuer = account.Issuer, oldAccountName = account.Name;
                if (form.NewName == oldName) return;
                account.Issuer = "";
                account.Name = form.NewName;
                HistoryEntry entry = vault.Log("Account renamed", account, "Was \"" + oldName + "\"");
                if (!TrySave())
                {
                    account.Issuer = oldIssuer;
                    account.Name = oldAccountName;
                    vault.Data.History.Remove(entry);
                    return;
                }
                Rebuild();
                SetStatus("Renamed " + oldName + " to " + account.DisplayName + ".", true);
            }
        }

        void RemoveAccount(AccountCard card)
        {
            if (vault == null) return;
            Account account = card.Account;
            DialogResult answer = ShowMessage(
                "Remove \"" + account.DisplayName + "\" from the list?\r\n\r\n" +
                "It will not be deleted. The account stays in your encrypted vault and can be restored at any time from \"Removed\".",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes || vault == null) return;

            account.Hidden = true;
            account.HiddenAt = Stamp.Now();
            HistoryEntry entry = vault.Log("Account removed", account, "Hidden from the list (can be restored)");
            if (!TrySave())
            {
                account.Hidden = false;
                account.HiddenAt = null;
                vault.Data.History.Remove(entry);
                return;
            }
            Rebuild();
            SetStatus("Removed " + account.DisplayName + ". You can restore it from \"Removed\".", true);
        }

        void ShowRemoved()
        {
            if (vault == null) return;
            using (var form = new RemovedAccountsForm(vault, TrySave))
            {
                RunModal(delegate { return form.ShowDialog(this); });
                if (form.Changed) Rebuild();
            }
        }

        void ShowHistory()
        {
            if (vault == null) return;
            using (var form = new HistoryForm(vault))
                RunModal(delegate { return form.ShowDialog(this); });
        }

        void ChangePassword()
        {
            if (vault == null) return;
            using (var form = new ChangePasswordForm(vault))
            {
                if (RunModal(delegate { return form.ShowDialog(this); }) == DialogResult.OK)
                    ShowMessage("Your master password has been changed.", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        void BackupVault()
        {
            if (vault == null || !TrySave()) return;
            using (var dialog = new SaveFileDialog
            {
                Title = "Back up vault file",
                FileName = "Offline2FA-backup-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".dat",
                Filter = "Vault backup (*.dat)|*.dat|All files (*.*)|*.*",
                OverwritePrompt = true
            })
            {
                if (RunModal(delegate { return dialog.ShowDialog(this); }) != DialogResult.OK) return;
                try
                {
                    File.Copy(Vault.VaultPath, dialog.FileName, true);
                }
                catch (Exception ex)
                {
                    ShowMessage("Could not create the backup:\r\n" + ex.Message, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                vault.Log("Backup created", null, dialog.FileName);
                TrySave();
                ShowMessage("Backup saved to:\r\n" + dialog.FileName +
                    "\r\n\r\nIt is encrypted with your current master password. To restore it, exit " + Ui.AppName +
                    " (tray icon → Exit) and replace this file with the backup:\r\n" + Vault.VaultPath,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        void MoveVault()
        {
            if (vault == null) return;
            string target = null;
            RunModal(delegate
            {
                target = VaultFileDialogs.ChooseNewLocation(this, Vault.VaultPath, "Move vault file to");
                return DialogResult.None;
            });
            if (target == null) return;

            string oldPath = Vault.VaultPath;
            try
            {
                Cursor = Cursors.WaitCursor;
                vault.MoveTo(target);
            }
            catch (Exception ex)
            {
                ShowMessage("Could not move the vault file:\r\n" + ex.Message, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            string note = AppSettings.RememberVaultPath()
                ? ""
                : "\r\n\r\nWarning: the new location could not be remembered. Next time, use \"Use a different vault file…\" on the unlock screen.";
            ShowMessage("The vault file was moved.\r\n\r\nFrom: " + oldPath + "\r\nTo: " + Vault.VaultPath + note,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void ShowVaultInExplorer()
        {
            try
            {
                Process.Start("explorer.exe", "/select,\"" + Vault.VaultPath + "\"");
            }
            catch (Exception ex)
            {
                ShowMessage("Could not open the folder:\r\n" + ex.Message, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void ShowWhatsNew(bool automatic)
        {
            if (automatic && (modalDepth > 0 || !Visible)) return;
            using (var form = new WhatsNewForm(automatic ? ReleaseNotes.Since(AppSettings.LastSeenVersion) : new List<ReleaseNote>(ReleaseNotes.All)))
                RunModal(delegate { return form.ShowDialog(this); });
            AppSettings.MarkVersionSeen(AppVersion.Number);
        }

        void ShowShortcuts()
        {
            ShowMessage(
                "Ctrl+N\tAdd account\r\n" +
                "Ctrl+F\tSearch\r\n" +
                "Enter\tCopy the first code in the search results\r\n" +
                "Esc\tClear search\r\n" +
                "Ctrl+H\tHistory\r\n" +
                "Ctrl+L\tLock\r\n\r\n" +
                "Closing the window keeps " + Ui.AppName + " in the notification area.\r\n" +
                "Right-click the tray icon to copy a code or exit.",
                MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        void ShowAbout()
        {
            ShowMessage(
                Ui.AppName + " " + AppVersion.Display + "\r\n" +
                "Published by " + AppVersion.Publisher + "\r\n" + AppVersion.Copyright + "\r\n\r\n" +
                "Generates time-based one-time passwords (TOTP) entirely offline.\r\n\r\n" +
                "Secret keys are encrypted with AES-256 using a key derived from your master password " +
                "(PBKDF2-SHA256, " + Vault.DefaultIterations.ToString("N0", CultureInfo.InvariantCulture) + " iterations). " +
                "The app locks after " + AutoLockMinutes + " minutes of inactivity or when Windows is locked.\r\n\r\n" +
                "Codes depend on your computer's clock, so keep the Windows time accurate.\r\n\r\n" +
                "Vault file:\r\n" + Vault.VaultPath,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
