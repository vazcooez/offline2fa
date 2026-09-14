using System;
using System.IO;
using System.Runtime.Serialization;

namespace Offline2FA
{
    /// <summary>
    /// Small unencrypted settings file that remembers where the vault lives.
    /// Stored in %APPDATA%\Offline2FA, or in a "data" folder next to the exe when portable.txt exists there.
    /// </summary>
    [DataContract]
    sealed class AppSettings
    {
        [DataMember(Name = "vaultPath", Order = 0, EmitDefaultValue = false)] public string VaultPath;
        [DataMember(Name = "lastSeenVersion", Order = 1, EmitDefaultValue = false)] public string LastSeenVersionValue;

        static bool portable;
        static AppSettings stored = new AppSettings();

        /// <summary>The version whose "What's new" window was last closed, or null.</summary>
        public static string LastSeenVersion
        {
            get { return stored.LastSeenVersionValue; }
        }

        static string ExeDirectory
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string SettingsDirectory { get; private set; }

        static string SettingsFile
        {
            get { return Path.Combine(SettingsDirectory, "settings.json"); }
        }

        public static string DefaultVaultPath
        {
            get { return Path.Combine(SettingsDirectory, "vault.dat"); }
        }

        /// <summary>The custom vault path from the settings file, or null when the default location is used.</summary>
        public static string CustomVaultPath { get; private set; }

        public static void Initialize()
        {
            portable = File.Exists(Path.Combine(ExeDirectory, "portable.txt"));
            SettingsDirectory = portable
                ? Path.Combine(ExeDirectory, "data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Offline2FA");

            CustomVaultPath = null;
            stored = new AppSettings();
            try
            {
                if (File.Exists(SettingsFile))
                {
                    stored = Vault.FromJson<AppSettings>(File.ReadAllBytes(SettingsFile)) ?? new AppSettings();
                    // Relative paths (used in portable mode) are relative to the exe folder.
                    if (!string.IsNullOrEmpty(stored.VaultPath))
                        CustomVaultPath = Path.GetFullPath(Path.Combine(ExeDirectory, stored.VaultPath));
                }
            }
            catch (Exception)
            {
                stored = new AppSettings();
                CustomVaultPath = null;
            }
            Vault.SetVaultPath(CustomVaultPath ?? DefaultVaultPath);
        }

        /// <summary>Remembers the vault file currently in use for the next start.</summary>
        public static bool RememberVaultPath()
        {
            string path = Vault.VaultPath;
            bool isDefault = string.Equals(path, Path.GetFullPath(DefaultVaultPath), StringComparison.OrdinalIgnoreCase);
            string value = isDefault ? null : path;
            if (value != null && portable && value.StartsWith(ExeDirectory, StringComparison.OrdinalIgnoreCase))
                value = value.Substring(ExeDirectory.Length);

            string previous = stored.VaultPath;
            stored.VaultPath = value;
            if (!Save())
            {
                stored.VaultPath = previous;
                return false;
            }
            CustomVaultPath = isDefault ? null : path;
            return true;
        }

        public static bool MarkVersionSeen(string version)
        {
            stored.LastSeenVersionValue = version;
            return Save();
        }

        static bool Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                File.WriteAllBytes(SettingsFile, Vault.ToJson(stored));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
