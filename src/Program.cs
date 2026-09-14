using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Offline2FA;

[assembly: AssemblyTitle("Offline 2FA")]
[assembly: AssemblyDescription("Offline two-factor authentication code generator")]
[assembly: AssemblyProduct("Offline 2FA")]
[assembly: AssemblyVersion(AppVersion.Number)]
[assembly: AssemblyFileVersion(AppVersion.Number)]
[assembly: AssemblyInformationalVersion(AppVersion.Number)]

namespace Offline2FA
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\Offline2FA-5C1E7B42-single-instance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(Ui.AppName + " is already running. Look for its icon in the notification area.",
                        Ui.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { ShowError(e.Exception); };

                AppSettings.Initialize();

                Vault vault = null;
                int failedAttempts = 0;
                bool isNew = false;
                while (vault == null)
                {
                    if (Vault.Exists())
                    {
                        using (var unlock = new UnlockForm(false))
                        {
                            if (unlock.ShowDialog() != DialogResult.OK) return;
                            vault = unlock.Result;
                            failedAttempts = unlock.FailedAttempts;
                        }
                    }
                    else
                    {
                        // A remembered custom location that no longer exists (moved file, disconnected drive).
                        string missing = AppSettings.CustomVaultPath != null ? Vault.VaultPath : null;
                        using (var setup = new SetupForm(missing))
                        {
                            DialogResult result = setup.ShowDialog();
                            if (result == DialogResult.OK)
                            {
                                vault = setup.Result;
                                isNew = true;
                            }
                            else if (result == DialogResult.Retry)
                            {
                                Vault.SetVaultPath(setup.ExistingVaultPath);
                            }
                            else
                            {
                                return;
                            }
                        }
                    }
                }

                Application.Run(new MainForm(vault, isNew, failedAttempts));
                GC.KeepAlive(mutex);
            }
        }

        static void ShowError(Exception ex)
        {
            MessageBox.Show("An unexpected error occurred:\r\n\r\n" + ex.Message, Ui.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
