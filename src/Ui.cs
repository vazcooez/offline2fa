using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Offline2FA
{
    static class Ui
    {
        public const string AppName = "Offline 2FA";

        public static readonly float Scale = ReadScale();

        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentDark = Color.FromArgb(29, 78, 216);
        public static readonly Color Window = Color.FromArgb(241, 245, 249);
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfaceHover = Color.FromArgb(248, 250, 252);
        public static readonly Color ButtonHover = Color.FromArgb(241, 245, 249);
        public static readonly Color Border = Color.FromArgb(226, 232, 240);
        public static readonly Color BorderStrong = Color.FromArgb(203, 213, 225);
        public static readonly Color Text = Color.FromArgb(15, 23, 42);
        public static readonly Color Muted = Color.FromArgb(100, 116, 139);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color DangerSoft = Color.FromArgb(254, 242, 242);
        public static readonly Color Success = Color.FromArgb(22, 163, 74);

        public static readonly Font BaseFont = new Font("Segoe UI", 9.75f);
        public static readonly Font SmallFont = new Font("Segoe UI", 8.75f);
        public static readonly Font LabelFont = new Font("Segoe UI Semibold", 9f);
        public static readonly Font InputFont = new Font("Segoe UI", 10.5f);
        public static readonly Font TitleFont = new Font("Segoe UI Semibold", 10.5f);
        public static readonly Font HeadingFont = new Font("Segoe UI Semibold", 14f);
        public static readonly Font CodeFont = new Font("Segoe UI Semibold", 21f);

        static float ReadScale()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    return g.DpiX / 96f;
            }
            catch (Exception)
            {
                return 1f;
            }
        }

        public static int Px(float value)
        {
            return (int)Math.Round(value * Scale);
        }

        static Icon appIcon;

        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try
                    {
                        using (Stream stream = typeof(Ui).Assembly.GetManifestResourceStream("Offline2FA.app.ico"))
                            appIcon = stream != null ? new Icon(stream) : Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                    }
                    catch (Exception)
                    {
                        appIcon = SystemIcons.Application;
                    }
                }
                return appIcon;
            }
        }

        public static Button MakeButton(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                Font = BaseFont,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(Px(88), Px(34)),
                Padding = new Padding(Px(10), 0, Px(10), 0),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 1;
            if (primary)
            {
                button.BackColor = Accent;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Accent;
                button.FlatAppearance.MouseOverBackColor = AccentDark;
                button.FlatAppearance.MouseDownBackColor = AccentDark;
            }
            else
            {
                button.BackColor = Surface;
                button.ForeColor = Text;
                button.FlatAppearance.BorderColor = BorderStrong;
                button.FlatAppearance.MouseOverBackColor = ButtonHover;
                button.FlatAppearance.MouseDownBackColor = Border;
            }
            return button;
        }

        public static Button MakeToolbarButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Font = BaseFont,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(0, Px(32)),
                Padding = new Padding(Px(4), 0, Px(4), 0),
                Margin = new Padding(Px(2), 0, 0, 0),
                BackColor = Surface,
                ForeColor = Text,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                TabStop = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ButtonHover;
            button.FlatAppearance.MouseDownBackColor = Border;
            return button;
        }

        public static TextBox MakeTextBox(bool password)
        {
            return new TextBox
            {
                Font = InputFont,
                BorderStyle = BorderStyle.FixedSingle,
                UseSystemPasswordChar = password
            };
        }

        const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public static void SetPlaceholder(TextBox box, string text)
        {
            box.HandleCreated += delegate { SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text); };
            if (box.IsHandleCreated) SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
