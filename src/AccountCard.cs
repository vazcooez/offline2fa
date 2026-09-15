using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Offline2FA
{
    /// <summary>
    /// One account in the main list. Shows the name and a masked code (all but the last digit hidden);
    /// the full code appears for a few seconds after it is copied. The secret is never shown.
    /// </summary>
    sealed class AccountCard : Control
    {
        enum Hot { None, Body, Copy, Remove }

        const int RevealSeconds = 10;

        public readonly Account Account;
        public event EventHandler CopyRequested;
        public event EventHandler RemoveRequested;

        readonly byte[] key;
        readonly ContextMenuStrip menu;
        string code = "";
        long counter = long.MinValue;
        int remaining = -1;
        Hot hot = Hot.None;
        DateTime copiedUntil = DateTime.MinValue;
        DateTime revealUntil = DateTime.MinValue;
        long revealedCounter = long.MinValue;
        Rectangle copyRect, removeRect;

        public AccountCard(Account account)
        {
            Account = account;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Ui.Window;
            Height = Ui.Px(84);
            Margin = new Padding(0, 0, 0, Ui.Px(10));
            Cursor = Cursors.Hand;

            try { key = Base32.Decode(account.Secret); }
            catch (FormatException) { key = null; }

            menu = new ContextMenuStrip { Font = Ui.BaseFont };
            menu.Items.Add("Copy code", null, delegate { Raise(CopyRequested); });
            menu.Items.Add("Remove from list…", null, delegate { Raise(RemoveRequested); });
            ContextMenuStrip = menu;
        }

        /// <summary>The code currently displayed, or null if the secret is invalid.</summary>
        public string CurrentCode
        {
            get { return key == null ? null : code; }
        }

        public void Tick(long unixNow)
        {
            bool changed = false;
            if (key != null)
            {
                int period = Account.SafePeriod;
                long c = unixNow / period;
                int rem = (int)(period - unixNow % period);
                if (c != counter)
                {
                    counter = c;
                    code = Totp.Hotp(key, c, Account.SafeDigits, Account.Algorithm);
                    changed = true;
                }
                if (rem != remaining)
                {
                    remaining = rem;
                    changed = true;
                }
            }
            if (copiedUntil != DateTime.MinValue && DateTime.UtcNow > copiedUntil)
            {
                copiedUntil = DateTime.MinValue;
                changed = true;
            }
            // Mask again when the reveal time is over or a new code has started.
            if (revealUntil != DateTime.MinValue && (DateTime.UtcNow > revealUntil || counter != revealedCounter))
            {
                revealUntil = DateTime.MinValue;
                changed = true;
            }
            if (changed) Invalidate();
        }

        /// <summary>Called after the code was copied: shows "Copied" and reveals the full code for a few seconds.</summary>
        public void FlashCopied()
        {
            copiedUntil = DateTime.UtcNow.AddSeconds(1.5);
            revealUntil = DateTime.UtcNow.AddSeconds(RevealSeconds);
            revealedCounter = counter;
            Invalidate();
        }

        static string Mask(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return new string('•', value.Length - 1) + value[value.Length - 1];
        }

        void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = Ui.Px(16);
            int h = Ui.Px(32);
            int y = (Height - h) / 2;
            removeRect = new Rectangle(Width - pad - Ui.Px(78), y, Ui.Px(78), h);
            copyRect = new Rectangle(removeRect.Left - Ui.Px(8) - Ui.Px(70), y, Ui.Px(70), h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            using (GraphicsPath card = Ui.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Ui.Px(10)))
            using (var fill = new SolidBrush(hot != Hot.None ? Ui.SurfaceHover : Ui.Surface))
            using (var pen = new Pen(hot != Hot.None ? Ui.BorderStrong : Ui.Border))
            {
                g.FillPath(fill, card);
                g.DrawPath(pen, card);
            }

            // Measure without EndEllipsis: combined with an unbounded size it reports a truncated width.
            const TextFormatFlags measure = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            const TextFormatFlags flags = measure | TextFormatFlags.EndEllipsis;
            var unbounded = new Size(int.MaxValue, int.MaxValue);
            int pad = Ui.Px(16);
            int textRight = copyRect.Left - Ui.Px(12);

            // Title and subtitle
            int titleY = Ui.Px(11);
            Size titleSize = TextRenderer.MeasureText(g, Account.Title, Ui.TitleFont, unbounded, measure);
            int titleWidth = Math.Min(titleSize.Width, Math.Max(0, textRight - pad));
            TextRenderer.DrawText(g, Account.Title, Ui.TitleFont, new Rectangle(pad, titleY, titleWidth, titleSize.Height), Ui.Text, flags);
            string subtitle = Account.Subtitle;
            int subX = pad + titleWidth + Ui.Px(8);
            if (subtitle != null && subX + Ui.Px(24) < textRight)
            {
                int smallHeight = TextRenderer.MeasureText(g, "Ag", Ui.SmallFont, unbounded, measure).Height;
                int subY = titleY + (titleSize.Height - smallHeight) / 2 + Ui.Px(1);
                TextRenderer.DrawText(g, subtitle, Ui.SmallFont, new Rectangle(subX, subY, textRight - subX, smallHeight), Ui.Muted, flags);
            }

            // Code and countdown
            int codeY = Ui.Px(33);
            if (key == null)
            {
                TextRenderer.DrawText(g, "Invalid secret key", Ui.TitleFont, new Point(pad, codeY + Ui.Px(8)), Ui.Danger, measure);
            }
            else
            {
                bool urgent = remaining <= 5;
                Color codeColor = urgent ? Ui.Danger : Ui.Accent;
                bool revealed = revealUntil != DateTime.MinValue;
                string shown = Totp.Format(revealed ? code : Mask(code));
                Size codeSize = TextRenderer.MeasureText(g, shown, Ui.CodeFont, unbounded, measure);
                TextRenderer.DrawText(g, shown, Ui.CodeFont, new Point(pad - Ui.Px(2), codeY), codeColor, measure);

                int d = Ui.Px(16);
                int cx = pad + codeSize.Width + Ui.Px(10);
                int cy = codeY + (codeSize.Height - d) / 2 + Ui.Px(1);
                if (cx + d + Ui.Px(28) < textRight)
                {
                    var ring = new Rectangle(cx, cy, d, d);
                    using (var track = new SolidBrush(Ui.Border))
                        g.FillEllipse(track, ring);
                    using (var arc = new SolidBrush(codeColor))
                        g.FillPie(arc, ring, -90, 360f * remaining / Account.SafePeriod);
                    string secs = remaining + "s";
                    Size secsSize = TextRenderer.MeasureText(g, secs, Ui.SmallFont, unbounded, measure);
                    TextRenderer.DrawText(g, secs, Ui.SmallFont, new Point(cx + d + Ui.Px(5), cy + (d - secsSize.Height) / 2), Ui.Muted, measure);
                }
            }

            // Buttons
            bool copied = copiedUntil != DateTime.MinValue;
            DrawButton(g, copyRect, copied ? "Copied" : "Copy", copied ? Ui.Success : Ui.Text, hot == Hot.Copy ? Ui.ButtonHover : Ui.Surface);
            DrawButton(g, removeRect, "Remove", Ui.Danger, hot == Hot.Remove ? Ui.DangerSoft : Ui.Surface);
        }

        static void DrawButton(Graphics g, Rectangle r, string text, Color fore, Color back)
        {
            using (GraphicsPath path = Ui.RoundedRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Ui.Px(6)))
            using (var fill = new SolidBrush(back))
            using (var pen = new Pen(Ui.Border))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, text, Ui.BaseFont, r, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Hot h = removeRect.Contains(e.Location) ? Hot.Remove : copyRect.Contains(e.Location) ? Hot.Copy : Hot.Body;
            if (h != hot)
            {
                hot = h;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hot = Hot.None;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            if (removeRect.Contains(e.Location)) Raise(RemoveRequested);
            else Raise(CopyRequested);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (key != null) Array.Clear(key, 0, key.Length);
                menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
