using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Offline2FA
{
    /// <summary>Auto-sizing single-column dialog with a heading, fields, an error line and a button row.</summary>
    class DialogBase : Form
    {
        protected readonly int ContentWidth;
        protected FlowLayoutPanel ButtonRow { get; private set; }
        readonly TableLayoutPanel body;
        Label errorLabel;

        public DialogBase(string title, int width)
        {
            ContentWidth = Ui.Px(width);
            Text = title;
            Font = Ui.BaseFont;
            BackColor = Ui.Surface;
            ForeColor = Ui.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            Icon = Ui.AppIcon;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            body = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Location = Point.Empty,
                Margin = Padding.Empty,
                Padding = new Padding(Ui.Px(24), Ui.Px(18), Ui.Px(24), Ui.Px(18))
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Controls.Add(body);
        }

        protected Label AddHeading(string text)
        {
            return AddControl(new Label { Text = text, Font = Ui.HeadingFont, AutoSize = true }, Ui.Px(6));
        }

        protected Label AddText(string text, bool muted)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                ForeColor = muted ? Ui.Muted : Ui.Text
            };
            return AddControl(label, Ui.Px(12));
        }

        protected T AddField<T>(string label, T control) where T : Control
        {
            AddControl(new Label { Text = label, Font = Ui.LabelFont, AutoSize = true }, Ui.Px(4));
            return AddControl(control, Ui.Px(12));
        }

        protected T AddControl<T>(T control, int bottomMargin) where T : Control
        {
            control.Margin = new Padding(0, 0, 0, bottomMargin);
            control.Width = ContentWidth;
            body.Controls.Add(control);
            return control;
        }

        protected void AddError()
        {
            errorLabel = AddControl(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                ForeColor = Ui.Danger,
                Visible = false
            }, Ui.Px(4));
        }

        protected void SetError(string message)
        {
            errorLabel.Text = message ?? "";
            errorLabel.Visible = !string.IsNullOrEmpty(message);
        }

        /// <summary>Adds the button row. The secondary button closes the dialog (Esc); pass null primaryText for none.</summary>
        protected Button AddButtons(string primaryText, string secondaryText, out Button secondary)
        {
            ButtonRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Height = Ui.Px(36),
                Padding = Padding.Empty
            };
            secondary = Ui.MakeButton(secondaryText, false);
            secondary.DialogResult = DialogResult.Cancel;
            secondary.Margin = new Padding(Ui.Px(8), 0, 0, 0);
            ButtonRow.Controls.Add(secondary);
            CancelButton = secondary;

            Button primary = null;
            if (primaryText != null)
            {
                primary = Ui.MakeButton(primaryText, true);
                primary.Margin = new Padding(Ui.Px(8), 0, 0, 0);
                ButtonRow.Controls.Add(primary);
            }
            AddControl(ButtonRow, 0).Margin = new Padding(0, Ui.Px(12), 0, 0);
            return primary;
        }

        protected bool Busy(Func<bool> work)
        {
            Cursor = Cursors.WaitCursor;
            Enabled = false;
            try { return work(); }
            finally
            {
                Enabled = true;
                Cursor = Cursors.Default;
            }
        }

        protected static string CheckNewPassword(string password, string confirm)
        {
            if (password.Length < Vault.MinPasswordLength)
                return "The password must be at least " + Vault.MinPasswordLength + " characters long.";
            if (password != confirm)
                return "The passwords do not match.";
            return null;
        }
    }

    static class VaultFileDialogs
    {
        const string Filter = "Offline 2FA vault (*.dat;*.vault)|*.dat;*.vault|All files (*.*)|*.*";

        /// <summary>Asks where a vault file should be written. Refuses existing files so nothing is overwritten.</summary>
        public static string ChooseNewLocation(IWin32Window owner, string current, string title)
        {
            using (var dialog = new SaveFileDialog { Title = title, Filter = Filter, OverwritePrompt = false, AddExtension = true, DefaultExt = "dat" })
            {
                try
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(current);
                    dialog.FileName = Path.GetFileName(current);
                }
                catch (Exception) { }
                while (dialog.ShowDialog(owner) == DialogResult.OK)
                {
                    if (!File.Exists(dialog.FileName)) return dialog.FileName;
                    MessageBox.Show(owner, "A file named \"" + Path.GetFileName(dialog.FileName) + "\" already exists there.\r\n\r\n" +
                        "Choose a different name or folder. To use an existing vault file, open it instead.",
                        Ui.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return null;
            }
        }

        public static string ChooseExisting(IWin32Window owner, string current)
        {
            using (var dialog = new OpenFileDialog { Title = "Open vault file", Filter = Filter, CheckFileExists = true })
            {
                try
                {
                    string dir = Path.GetDirectoryName(current);
                    if (Directory.Exists(dir)) dialog.InitialDirectory = dir;
                }
                catch (Exception) { }
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
            }
        }
    }

    sealed class SetupForm : DialogBase
    {
        readonly TextBox password, confirm, location;
        public Vault Result { get; private set; }

        /// <summary>Set when the user chose to open an existing vault file instead (DialogResult.Retry).</summary>
        public string ExistingVaultPath { get; private set; }

        public SetupForm(string missingVaultPath) : base(Ui.AppName + " – Setup", 400)
        {
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            ShowIcon = true;

            if (missingVaultPath != null)
            {
                AddHeading("Vault file not found");
                AddText("Your vault file was not found at:\r\n" + missingVaultPath +
                    "\r\n\r\nIf it was moved or is on a drive that isn't connected, open it from its current location. Otherwise, create a new vault.", false)
                    .ForeColor = Ui.Danger;
            }
            else
            {
                AddHeading("Create a master password");
                AddText("Your 2FA secret keys will be encrypted with this password. You will need it every time you open " + Ui.AppName + ".", false);
            }

            password = AddField("Master password", Ui.MakeTextBox(true));
            confirm = AddField("Confirm password", Ui.MakeTextBox(true));
            AddText("At least " + Vault.MinPasswordLength + " characters. There is no way to recover a forgotten password — without it, your saved secrets cannot be decrypted.", true);

            location = Ui.MakeTextBox(false);
            location.ReadOnly = true;
            location.BackColor = Ui.Window;
            location.Text = missingVaultPath != null ? Path.GetFullPath(AppSettings.DefaultVaultPath) : Vault.VaultPath;
            Button browse = Ui.MakeButton("Browse…", false);
            AddField("Vault file location", PathRow(location, browse));
            browse.Click += delegate
            {
                string chosen = VaultFileDialogs.ChooseNewLocation(this, location.Text, "Choose where to save the vault file");
                if (chosen != null) location.Text = chosen;
            };

            var open = AddControl(new LinkLabel
            {
                Text = "Already have a vault file? Open it…",
                AutoSize = true,
                LinkColor = Ui.Accent,
                ActiveLinkColor = Ui.AccentDark,
                LinkBehavior = LinkBehavior.HoverUnderline
            }, Ui.Px(4));
            open.LinkClicked += delegate
            {
                string chosen = VaultFileDialogs.ChooseExisting(this, location.Text);
                if (chosen == null) return;
                ExistingVaultPath = chosen;
                DialogResult = DialogResult.Retry;
            };

            AddError();
            Button exit;
            Button create = AddButtons("Create vault", "Exit", out exit);
            AcceptButton = create;
            create.Click += delegate { Submit(); };
        }

        internal static Control PathRow(TextBox box, Button button)
        {
            var row = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Padding = Padding.Empty, Height = button.MinimumSize.Height };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            box.Margin = new Padding(0, 0, Ui.Px(8), 0);
            button.Margin = Padding.Empty;
            row.Controls.Add(box, 0, 0);
            row.Controls.Add(button, 1, 0);
            return row;
        }

        void Submit()
        {
            string problem = CheckNewPassword(password.Text, confirm.Text);
            if (problem == null && File.Exists(location.Text))
                problem = "A file already exists at the chosen location. Choose another location, or open that file instead.";
            if (problem != null)
            {
                SetError(problem);
                password.Focus();
                return;
            }
            SetError(null);
            Busy(delegate
            {
                string previous = Vault.VaultPath;
                try
                {
                    Vault.SetVaultPath(location.Text);
                    Result = Vault.Create(password.Text);
                    AppSettings.RememberVaultPath();
                    DialogResult = DialogResult.OK;
                }
                catch (Exception ex)
                {
                    Vault.SetVaultPath(previous);
                    SetError("Could not create the vault: " + ex.Message);
                }
                return true;
            });
        }
    }

    sealed class UnlockForm : DialogBase
    {
        readonly TextBox password;
        readonly Label fileLabel;
        public Vault Result { get; private set; }
        public int FailedAttempts { get; private set; }

        public UnlockForm(bool afterLock) : base(Ui.AppName, 380)
        {
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            ShowIcon = true;

            AddHeading(afterLock ? "Vault locked" : "Unlock your vault");
            AddText("Enter your master password to show your 2FA codes.", true);
            password = AddField("Master password", Ui.MakeTextBox(true));

            fileLabel = AddText("", true);
            fileLabel.Font = Ui.SmallFont;
            fileLabel.Margin = new Padding(0, 0, 0, Ui.Px(2));
            var change = AddControl(new LinkLabel
            {
                Text = "Use a different vault file…",
                AutoSize = true,
                Font = Ui.SmallFont,
                LinkColor = Ui.Accent,
                ActiveLinkColor = Ui.AccentDark,
                LinkBehavior = LinkBehavior.HoverUnderline
            }, Ui.Px(8));
            change.LinkClicked += delegate
            {
                string chosen = VaultFileDialogs.ChooseExisting(this, Vault.VaultPath);
                if (chosen == null) return;
                Vault.SetVaultPath(chosen);
                ShowFile();
                SetError(null);
                password.Focus();
            };
            ShowFile();

            AddError();
            Button exit;
            Button unlock = AddButtons("Unlock", afterLock ? "Close" : "Exit", out exit);
            AcceptButton = unlock;
            unlock.Click += delegate { Submit(); };
        }

        void ShowFile()
        {
            fileLabel.Text = "Vault file: " + Vault.VaultPath;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            password.Focus();
        }

        void Submit()
        {
            if (password.Text.Length == 0)
            {
                SetError("Enter your master password.");
                password.Focus();
                return;
            }
            SetError(null);
            Busy(delegate
            {
                try
                {
                    Result = Vault.Open(password.Text);
                    AppSettings.RememberVaultPath();
                    DialogResult = DialogResult.OK;
                }
                catch (WrongPasswordException)
                {
                    FailedAttempts++;
                    SetError("Incorrect password. Please try again.");
                }
                catch (FileNotFoundException)
                {
                    SetError("The vault file was not found. Use a different vault file, or check that its drive is connected.");
                }
                catch (DirectoryNotFoundException)
                {
                    SetError("The vault file was not found. Use a different vault file, or check that its drive is connected.");
                }
                catch (Exception ex)
                {
                    SetError("The vault file could not be read: " + ex.Message
                        + (File.Exists(Vault.BackupPath) ? "\r\nA previous copy exists: " + Vault.BackupPath : ""));
                }
                return true;
            });
            if (DialogResult != DialogResult.OK)
            {
                password.SelectAll();
                password.Focus();
            }
        }
    }
    sealed class WhatsNewForm : DialogBase
    {
        public WhatsNewForm(List<ReleaseNote> notes) : base("What's new – " + Ui.AppName, 480)
        {
            ShowIcon = true;
            AddHeading(Ui.AppName + " " + AppVersion.Display).Margin = Padding.Empty;
            AddText("by " + AppVersion.Publisher, true).Font = Ui.SmallFont;

            int textWidth = ContentWidth - SystemInformation.VerticalScrollBarWidth - Ui.Px(8);
            var scroll = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Height = Ui.Px(400),
                Padding = Padding.Empty,
                BackColor = Ui.Surface
            };

            AddTo(scroll, "What's new", Ui.TitleFont, Ui.Text, textWidth, Ui.Px(6));
            foreach (ReleaseNote note in notes)
            {
                AddTo(scroll, "Version " + note.Version + "  ·  " + note.Date, Ui.LabelFont, Ui.Accent, textWidth, Ui.Px(4));
                foreach (string change in note.Changes)
                    AddTo(scroll, "•  " + change, Ui.BaseFont, Ui.Text, textWidth, Ui.Px(4));
                scroll.Controls[scroll.Controls.Count - 1].Margin = new Padding(0, 0, 0, Ui.Px(12));
            }

            var divider = new Panel { Height = 1, Width = textWidth, BackColor = Ui.Border, Margin = new Padding(0, Ui.Px(4), 0, Ui.Px(14)) };
            scroll.Controls.Add(divider);
            AddTo(scroll, "What " + Ui.AppName + " does", Ui.TitleFont, Ui.Text, textWidth, Ui.Px(6));
            AddTo(scroll, ReleaseNotes.AboutText, Ui.BaseFont, Ui.Muted, textWidth, 0);

            AddControl(scroll, 0);
            Button close;
            AddButtons(null, "Got it", out close);
            close.DialogResult = DialogResult.OK;
            AcceptButton = close;
        }

        static void AddTo(FlowLayoutPanel panel, string text, Font font, Color color, int width, int bottom)
        {
            panel.Controls.Add(new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                AutoSize = true,
                MaximumSize = new Size(width, 0),
                Margin = new Padding(0, 0, 0, bottom)
            });
        }
    }
    sealed class ChangePasswordForm : DialogBase
    {
        readonly Vault vault;
        readonly TextBox current, password, confirm;

        public ChangePasswordForm(Vault vault) : base("Change master password", 360)
        {
            this.vault = vault;
            AddHeading("Change master password");
            current = AddField("Current password", Ui.MakeTextBox(true));
            password = AddField("New password", Ui.MakeTextBox(true));
            confirm = AddField("Confirm new password", Ui.MakeTextBox(true));
            AddError();
            Button cancel;
            Button change = AddButtons("Change password", "Cancel", out cancel);
            AcceptButton = change;
            change.Click += delegate { Submit(); };
        }

        void Submit()
        {
            string problem = current.Text.Length == 0 ? "Enter your current password." : CheckNewPassword(password.Text, confirm.Text);
            if (problem == null && password.Text == current.Text) problem = "The new password must be different from the current one.";
            if (problem != null)
            {
                SetError(problem);
                return;
            }
            SetError(null);
            Busy(delegate
            {
                if (!vault.CheckPassword(current.Text))
                {
                    SetError("The current password is incorrect.");
                    return false;
                }
                try
                {
                    vault.ChangePassword(password.Text);
                    DialogResult = DialogResult.OK;
                }
                catch (Exception ex)
                {
                    SetError("Could not change the password: " + ex.Message);
                }
                return true;
            });
        }
    }

    sealed class AddAccountForm : DialogBase
    {
        readonly IList<Account> existing;
        readonly TextBox secret, name;
        readonly CheckBox show;
        readonly ComboBox digits, period, algorithm;
        readonly Label preview;
        readonly Timer previewTimer;
        readonly LinkLabel advancedToggle;
        readonly TableLayoutPanel advanced;
        readonly Label advancedHint;
        bool updating;

        public Account Result { get; private set; }

        public AddAccountForm(IList<Account> existing) : base("Add account", 420)
        {
            this.existing = existing;

            AddHeading("Add account");
            AddText("Paste the secret key the website gave you when you turned on two-factor authentication. That is all that's needed.", true);

            secret = AddField("Secret key", Ui.MakeTextBox(true));
            secret.Margin = new Padding(0, 0, 0, Ui.Px(4));
            show = AddControl(new CheckBox { Text = "Show key while typing", AutoSize = true, Font = Ui.SmallFont, ForeColor = Ui.Muted }, Ui.Px(12));
            name = AddField("Name (optional)", Ui.MakeTextBox(false));
            Ui.SetPlaceholder(name, "e.g. GitHub, work email");

            digits = MakeCombo(ComboBoxStyle.DropDownList, "6", "7", "8");
            period = MakeCombo(ComboBoxStyle.DropDown, "30", "60");
            algorithm = MakeCombo(ComboBoxStyle.DropDownList, "SHA1", "SHA256", "SHA512");
            var advanced = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, Padding = Padding.Empty };
            for (int i = 0; i < 3; i++) advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            advanced.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            advanced.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            string[] captions = { "Digits", "Period (seconds)", "Algorithm" };
            ComboBox[] combos = { digits, period, algorithm };
            for (int i = 0; i < 3; i++)
            {
                var gap = new Padding(0, 0, i < 2 ? Ui.Px(10) : 0, 0);
                advanced.Controls.Add(new Label { Text = captions[i], Font = Ui.LabelFont, AutoSize = true, Margin = new Padding(0, 0, 0, Ui.Px(4)) }, i, 0);
                combos[i].Dock = DockStyle.Fill;
                combos[i].Margin = gap;
                advanced.Controls.Add(combos[i], i, 1);
            }
            advanced.Height = Ui.LabelFont.Height + Ui.Px(6) + digits.PreferredHeight + Ui.Px(2);

            // Websites almost always use the defaults, so these settings stay out of the way unless needed.
            advancedToggle = AddControl(new LinkLabel
            {
                AutoSize = true,
                Font = Ui.SmallFont,
                LinkColor = Ui.Accent,
                ActiveLinkColor = Ui.AccentDark,
                LinkBehavior = LinkBehavior.HoverUnderline
            }, Ui.Px(8));
            advancedToggle.LinkClicked += delegate { SetAdvancedVisible(!this.advanced.Visible); };
            this.advanced = AddControl(advanced, Ui.Px(4));
            advancedHint = AddText("Only change these if the website tells you to.", true);
            advancedHint.Font = Ui.SmallFont;
            SetAdvancedVisible(false);

            preview = AddControl(new Label { AutoSize = true, Font = Ui.TitleFont, MaximumSize = new Size(ContentWidth, 0) }, Ui.Px(4));
            AddError();

            Button cancel;
            Button add = AddButtons("Add account", "Cancel", out cancel);
            AcceptButton = add;
            add.Click += delegate { Submit(); };

            secret.TextChanged += delegate { OnSecretChanged(); };
            show.CheckedChanged += delegate { secret.UseSystemPasswordChar = !show.Checked; };
            digits.SelectedIndexChanged += delegate { UpdatePreview(); };
            period.TextChanged += delegate { UpdatePreview(); };
            algorithm.SelectedIndexChanged += delegate { UpdatePreview(); };

            previewTimer = new Timer { Interval = 1000 };
            previewTimer.Tick += delegate { UpdatePreview(); };
            previewTimer.Start();
            UpdatePreview();
        }

        static string LinkLabelText(OtpAuthInfo info)
        {
            if (string.IsNullOrEmpty(info.Issuer)) return info.Name ?? "";
            if (string.IsNullOrEmpty(info.Name) || info.Name == info.Issuer) return info.Issuer;
            return info.Issuer + " (" + info.Name + ")";
        }

        void SetAdvancedVisible(bool visible)
        {
            advanced.Visible = visible;
            advancedHint.Visible = visible;
            advancedToggle.Text = visible ? "▾ Hide advanced options" : "▸ Advanced options (digits, period, algorithm)";
        }

        static ComboBox MakeCombo(ComboBoxStyle style, params string[] items)
        {
            var combo = new ComboBox { DropDownStyle = style, Font = Ui.BaseFont, FlatStyle = FlatStyle.System };
            combo.Items.AddRange(items);
            combo.SelectedIndex = 0;
            return combo;
        }

        void OnSecretChanged()
        {
            if (updating) return;
            if (OtpAuthUri.IsUri(secret.Text))
            {
                OtpAuthInfo info;
                string error;
                if (OtpAuthUri.TryParse(secret.Text, out info, out error))
                {
                    updating = true;
                    secret.Text = info.Secret;
                    updating = false;
                    if (name.Text.Trim().Length == 0) name.Text = LinkLabelText(info);
                    digits.SelectedItem = info.Digits.ToString(CultureInfo.InvariantCulture);
                    period.Text = info.Period.ToString(CultureInfo.InvariantCulture);
                    algorithm.SelectedItem = info.Algorithm;
                    if (info.Digits != 6 || info.Period != 30 || info.Algorithm != "SHA1") SetAdvancedVisible(true);
                    SetError(null);
                }
                else
                {
                    SetError(error);
                }
            }
            else
            {
                SetError(null);
            }
            UpdatePreview();
        }

        bool ReadSettings(out int digitCount, out int periodSeconds, out string algorithmName)
        {
            digitCount = int.Parse((string)digits.SelectedItem, CultureInfo.InvariantCulture);
            algorithmName = (string)algorithm.SelectedItem;
            return int.TryParse(period.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out periodSeconds)
                && periodSeconds >= 1 && periodSeconds <= 3600;
        }

        void UpdatePreview()
        {
            string key = Base32.Normalize(secret.Text);
            if (key.Length == 0 || OtpAuthUri.IsUri(secret.Text))
            {
                preview.ForeColor = Ui.Muted;
                preview.Text = "A code preview appears here once you enter a key.";
                return;
            }
            byte[] bytes;
            try
            {
                bytes = Base32.Decode(key);
            }
            catch (FormatException ex)
            {
                preview.ForeColor = Ui.Danger;
                preview.Text = ex.Message;
                return;
            }
            int d, p;
            string alg;
            if (!ReadSettings(out d, out p, out alg))
            {
                preview.ForeColor = Ui.Danger;
                preview.Text = "The period must be a whole number from 1 to 3600.";
                return;
            }
            long now = Totp.UnixNow();
            string code = Totp.Generate(bytes, now, p, d, alg);
            Array.Clear(bytes, 0, bytes.Length);
            preview.ForeColor = Ui.Accent;
            preview.Text = "Current code: " + Totp.Format(code) + "   (" + (p - now % p) + "s left)";
        }

        void Submit()
        {
            string key = Base32.Normalize(secret.Text);
            if (key.Length == 0 || OtpAuthUri.IsUri(secret.Text))
            {
                SetError("Enter the secret key from the website.");
                secret.Focus();
                return;
            }
            try
            {
                byte[] bytes = Base32.Decode(key);
                Array.Clear(bytes, 0, bytes.Length);
            }
            catch (FormatException ex)
            {
                SetError(ex.Message);
                secret.Focus();
                return;
            }

            string nameText = name.Text.Trim();
            if (nameText.Length == 0) nameText = "Account " + (existing.Count + 1);

            int d, p;
            string alg;
            if (!ReadSettings(out d, out p, out alg))
            {
                SetError("The period must be a whole number from 1 to 3600.");
                period.Focus();
                return;
            }

            foreach (Account other in existing)
            {
                if (other.Secret != key) continue;
                string where = other.Hidden
                    ? "This key already belongs to the removed account \"" + other.DisplayName + "\". You can restore it from Removed instead.\r\n\r\nAdd it again anyway?"
                    : "This key is already registered as \"" + other.DisplayName + "\".\r\n\r\nAdd it again anyway?";
                if (MessageBox.Show(this, where, Ui.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                break;
            }

            Result = new Account
            {
                Id = Guid.NewGuid().ToString("N"),
                Issuer = "",
                Name = nameText,
                Secret = key,
                Digits = d,
                Period = p,
                Algorithm = alg,
                Created = Stamp.Now()
            };
            DialogResult = DialogResult.OK;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            previewTimer.Stop();
            secret.Text = "";
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) previewTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    sealed class RenameAccountForm : DialogBase
    {
        readonly TextBox name;

        public string NewName { get; private set; }

        public RenameAccountForm(string currentName) : base("Rename account", 380)
        {
            AddHeading("Rename account");
            name = AddField("Name", Ui.MakeTextBox(false));
            name.Text = currentName;
            AddError();

            Button cancel;
            Button save = AddButtons("Save", "Cancel", out cancel);
            AcceptButton = save;
            save.Click += delegate { Submit(); };
            Shown += delegate { name.Focus(); name.SelectAll(); };
        }

        void Submit()
        {
            string text = name.Text.Trim();
            if (text.Length == 0)
            {
                SetError("Enter a name.");
                name.Focus();
                return;
            }
            NewName = text;
            DialogResult = DialogResult.OK;
        }
    }

    sealed class RemovedAccountsForm : DialogBase
    {
        readonly Vault vault;
        readonly Func<bool> save;
        readonly ListView list;
        readonly Button restore;

        public bool Changed { get; private set; }

        public RemovedAccountsForm(Vault vault, Func<bool> save) : base("Removed accounts", 560)
        {
            this.vault = vault;
            this.save = save;

            AddHeading("Removed accounts");
            AddText("Removed accounts are never deleted. They stay in your encrypted vault and can be restored to the main list at any time.", true);
            list = AddControl(new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = true,
                BorderStyle = BorderStyle.FixedSingle,
                Height = Ui.Px(260)
            }, 0);
            list.Columns.Add("Name", ContentWidth - Ui.Px(170) - SystemInformation.VerticalScrollBarWidth - Ui.Px(6));
            list.Columns.Add("Removed on", Ui.Px(170));

            Button close;
            restore = AddButtons("Restore", "Close", out close);
            restore.Click += delegate { RestoreSelected(); };
            list.DoubleClick += delegate { RestoreSelected(); };
            list.SelectedIndexChanged += delegate { UpdateButton(); };
            Reload();
        }

        void Reload()
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (Account a in vault.Data.Accounts)
            {
                if (!a.Hidden) continue;
                var item = new ListViewItem(a.DisplayName) { Tag = a };
                item.SubItems.Add(Stamp.Display(a.HiddenAt));
                list.Items.Add(item);
            }
            if (list.Items.Count == 0)
                list.Items.Add(new ListViewItem("No removed accounts") { ForeColor = Ui.Muted });
            list.EndUpdate();
            UpdateButton();
        }

        void UpdateButton()
        {
            bool any = false;
            foreach (ListViewItem item in list.SelectedItems) if (item.Tag != null) any = true;
            restore.Enabled = any;
        }

        void RestoreSelected()
        {
            var accounts = new List<Account>();
            foreach (ListViewItem item in list.SelectedItems)
            {
                var a = item.Tag as Account;
                if (a != null) accounts.Add(a);
            }
            if (accounts.Count == 0) return;

            var entries = new List<HistoryEntry>();
            var previous = new List<string>();
            foreach (Account a in accounts)
            {
                previous.Add(a.HiddenAt);
                a.Hidden = false;
                a.HiddenAt = null;
                entries.Add(vault.Log("Account restored", a, null));
            }
            if (!save())
            {
                for (int i = 0; i < accounts.Count; i++)
                {
                    accounts[i].Hidden = true;
                    accounts[i].HiddenAt = previous[i];
                    vault.Data.History.Remove(entries[i]);
                }
                return;
            }
            Changed = true;
            Reload();
        }
    }

    sealed class HistoryForm : DialogBase
    {
        readonly Vault vault;
        readonly TextBox filter;
        readonly ListView list;
        readonly Label summary;

        public HistoryForm(Vault vault) : base("History", 700)
        {
            this.vault = vault;

            AddHeading("History");
            AddText("Every action is saved with a timestamp, newest first. Secret keys and codes are never recorded.", true);
            filter = AddControl(Ui.MakeTextBox(false), Ui.Px(8));
            Ui.SetPlaceholder(filter, "Filter by event, account or date");
            list = AddControl(new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                BorderStyle = BorderStyle.FixedSingle,
                Height = Ui.Px(380)
            }, Ui.Px(6));
            int timeWidth = Ui.Px(150), eventWidth = Ui.Px(160), accountWidth = Ui.Px(200);
            list.Columns.Add("Time", timeWidth);
            list.Columns.Add("Event", eventWidth);
            list.Columns.Add("Account", accountWidth);
            list.Columns.Add("Details", ContentWidth - timeWidth - eventWidth - accountWidth - SystemInformation.VerticalScrollBarWidth - Ui.Px(6));
            summary = AddControl(new Label { AutoSize = true, ForeColor = Ui.Muted, Font = Ui.SmallFont }, 0);

            Button close;
            AddButtons(null, "Close", out close);
            Button export = Ui.MakeButton("Export CSV…", false);
            export.Margin = new Padding(Ui.Px(8), 0, 0, 0);
            ButtonRow.Controls.Add(export);
            export.Click += delegate { ExportCsv(); };
            filter.TextChanged += delegate { Reload(); };
            Reload();
        }

        IEnumerable<HistoryEntry> Filtered()
        {
            string f = filter.Text.Trim();
            List<HistoryEntry> history = vault.Data.History;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                HistoryEntry h = history[i];
                if (f.Length == 0 || Contains(Stamp.Display(h.Timestamp), f) || Contains(h.Action, f)
                    || Contains(h.AccountName, f) || Contains(h.Details, f))
                    yield return h;
            }
        }

        static bool Contains(string text, string part)
        {
            return text != null && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void Reload()
        {
            var items = new List<ListViewItem>();
            foreach (HistoryEntry h in Filtered())
            {
                var item = new ListViewItem(Stamp.Display(h.Timestamp));
                item.SubItems.Add(h.Action ?? "");
                item.SubItems.Add(h.AccountName ?? "");
                item.SubItems.Add(h.Details ?? "");
                items.Add(item);
            }
            list.BeginUpdate();
            list.Items.Clear();
            list.Items.AddRange(items.ToArray());
            list.EndUpdate();
            int total = vault.Data.History.Count;
            summary.Text = items.Count == total
                ? total + (total == 1 ? " entry" : " entries")
                : items.Count + " of " + total + " entries";
        }

        void ExportCsv()
        {
            using (var dialog = new SaveFileDialog
            {
                Title = "Export history",
                FileName = "Offline2FA-history-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv",
                Filter = "CSV file (*.csv)|*.csv",
                OverwritePrompt = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var sb = new StringBuilder("Time,Event,Account,Details\r\n");
                foreach (HistoryEntry h in Filtered())
                {
                    sb.Append(Csv(Stamp.Display(h.Timestamp))).Append(',')
                      .Append(Csv(h.Action)).Append(',')
                      .Append(Csv(h.AccountName)).Append(',')
                      .Append(Csv(h.Details)).Append("\r\n");
                }
                try
                {
                    File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not export the history: " + ex.Message, Ui.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        static string Csv(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        }
    }
}
