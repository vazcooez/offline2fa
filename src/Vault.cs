using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace Offline2FA
{
    [DataContract]
    sealed class Account
    {
        [DataMember(Name = "id", Order = 0)] public string Id;
        [DataMember(Name = "issuer", Order = 1)] public string Issuer;
        [DataMember(Name = "name", Order = 2)] public string Name;
        [DataMember(Name = "secret", Order = 3)] public string Secret;
        [DataMember(Name = "digits", Order = 4)] public int Digits;
        [DataMember(Name = "period", Order = 5)] public int Period;
        [DataMember(Name = "algorithm", Order = 6)] public string Algorithm;
        [DataMember(Name = "created", Order = 7)] public string Created;
        [DataMember(Name = "hidden", Order = 8)] public bool Hidden;
        [DataMember(Name = "hiddenAt", Order = 9, EmitDefaultValue = false)] public string HiddenAt;

        public string Title
        {
            get { return !string.IsNullOrEmpty(Issuer) ? Issuer : (Name ?? ""); }
        }

        public string Subtitle
        {
            get
            {
                if (string.IsNullOrEmpty(Issuer) || string.IsNullOrEmpty(Name) || Name == Issuer) return null;
                return Name;
            }
        }

        public string DisplayName
        {
            get { return Subtitle == null ? Title : Title + " (" + Subtitle + ")"; }
        }

        public int SafeDigits { get { return Digits >= 6 && Digits <= 8 ? Digits : 6; } }
        public int SafePeriod { get { return Period >= 1 && Period <= 3600 ? Period : 30; } }
    }

    [DataContract]
    sealed class HistoryEntry
    {
        [DataMember(Name = "time", Order = 0)] public string Timestamp;
        [DataMember(Name = "event", Order = 1)] public string Action;
        [DataMember(Name = "account", Order = 2, EmitDefaultValue = false)] public string AccountName;
        [DataMember(Name = "details", Order = 3, EmitDefaultValue = false)] public string Details;
    }

    [DataContract]
    sealed class VaultData
    {
        [DataMember(Name = "accounts", Order = 0)] public List<Account> Accounts;
        [DataMember(Name = "history", Order = 1)] public List<HistoryEntry> History;

        public void EnsureLists()
        {
            if (Accounts == null) Accounts = new List<Account>();
            if (History == null) History = new List<HistoryEntry>();
        }
    }

    // On-disk envelope. Everything except the KDF parameters is encrypted.
    [DataContract]
    sealed class VaultFile
    {
        [DataMember(Name = "format", Order = 0)] public string Format;
        [DataMember(Name = "version", Order = 1)] public int Version;
        [DataMember(Name = "kdf", Order = 2)] public string Kdf;
        [DataMember(Name = "iterations", Order = 3)] public int Iterations;
        [DataMember(Name = "salt", Order = 4)] public string Salt;
        [DataMember(Name = "iv", Order = 5)] public string Iv;
        [DataMember(Name = "data", Order = 6)] public string Data;
        [DataMember(Name = "mac", Order = 7)] public string Mac;
    }

    sealed class WrongPasswordException : Exception
    {
        public WrongPasswordException() : base("Incorrect password.") { }
    }

    static class Stamp
    {
        public static string Now()
        {
            return DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
        }

        public static string Display(string iso)
        {
            DateTimeOffset value;
            if (!string.IsNullOrEmpty(iso) && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
                return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return iso ?? "";
        }
    }

    /// <summary>
    /// Encrypted vault: AES-256-CBC + HMAC-SHA256 (encrypt-then-MAC), keys derived from the
    /// master password with PBKDF2-HMAC-SHA256. Derived keys are kept in memory only while unlocked.
    /// </summary>
    sealed class Vault
    {
        const string FormatName = "offline-2fa-vault";
        const int FormatVersion = 1;
        const string KdfName = "PBKDF2-HMAC-SHA256";
        public const int DefaultIterations = 600000;
        public const int MinPasswordLength = 8;

        /// <summary>Full path of the vault file in use. Chosen by the user; see <see cref="AppSettings"/>.</summary>
        public static string VaultPath { get; private set; }
        public static string BackupPath { get { return VaultPath + ".bak"; } }

        public static void SetVaultPath(string path)
        {
            VaultPath = Path.GetFullPath(path);
        }

        public static bool Exists()
        {
            return File.Exists(VaultPath);
        }

        public VaultData Data { get; private set; }

        byte[] salt;
        int iterations;
        byte[] encKey;
        byte[] macKey;

        Vault() { }

        public static Vault Create(string password)
        {
            PreserveOrphanedBackup();
            var vault = new Vault();
            vault.Data = new VaultData();
            vault.Data.EnsureLists();
            vault.salt = RandomBytes(16);
            vault.iterations = DefaultIterations;
            vault.DeriveKeys(password);
            vault.Log("Vault created", null, "Master password set");
            vault.Save();
            return vault;
        }

        public static Vault Open(string password)
        {
            return Open(password, VaultPath);
        }

        internal static Vault Open(string password, string path)
        {
            VaultFile file = ReadVaultFile(path);
            byte[] iv, ciphertext, mac;
            var vault = new Vault();
            try
            {
                vault.salt = Convert.FromBase64String(file.Salt);
                iv = Convert.FromBase64String(file.Iv);
                ciphertext = Convert.FromBase64String(file.Data);
                mac = Convert.FromBase64String(file.Mac);
            }
            catch (FormatException)
            {
                throw new InvalidDataException("The vault file is damaged.");
            }
            vault.iterations = file.Iterations;
            vault.DeriveKeys(password);

            if (!FixedTimeEquals(vault.ComputeMac(iv, ciphertext), mac))
            {
                vault.Wipe();
                throw new WrongPasswordException();
            }

            byte[] plain = AesTransform(false, vault.encKey, iv, ciphertext);
            try
            {
                vault.Data = FromJson<VaultData>(plain);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
            if (vault.Data == null) throw new InvalidDataException("The vault contents are empty.");
            vault.Data.EnsureLists();
            return vault;
        }

        public HistoryEntry Log(string action, Account account, string details)
        {
            var entry = new HistoryEntry
            {
                Timestamp = Stamp.Now(),
                Action = action,
                AccountName = account == null ? null : account.DisplayName,
                Details = details
            };
            Data.History.Add(entry);
            return entry;
        }

        public void Save()
        {
            if (Data == null) throw new InvalidOperationException("The vault is locked.");

            byte[] plain = ToJson(Data);
            byte[] iv = RandomBytes(16);
            byte[] ciphertext;
            try
            {
                ciphertext = AesTransform(true, encKey, iv, plain);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }

            var file = new VaultFile
            {
                Format = FormatName,
                Version = FormatVersion,
                Kdf = KdfName,
                Iterations = iterations,
                Salt = Convert.ToBase64String(salt),
                Iv = Convert.ToBase64String(iv),
                Data = Convert.ToBase64String(ciphertext),
                Mac = Convert.ToBase64String(ComputeMac(iv, ciphertext))
            };
            WriteAtomically(VaultPath, ToJson(file));
        }

        public bool CheckPassword(string password)
        {
            byte[] enc, mac;
            Derive(password, salt, iterations, out enc, out mac);
            bool ok = FixedTimeEquals(mac, macKey);
            Clear(enc);
            Clear(mac);
            return ok;
        }

        public void ChangePassword(string newPassword)
        {
            byte[] oldSalt = salt, oldEnc = encKey, oldMac = macKey;
            int oldIterations = iterations;

            salt = RandomBytes(16);
            iterations = DefaultIterations;
            DeriveKeys(newPassword);
            HistoryEntry entry = Log("Master password changed", null, null);
            try
            {
                Save();
            }
            catch
            {
                Clear(encKey);
                Clear(macKey);
                salt = oldSalt;
                iterations = oldIterations;
                encKey = oldEnc;
                macKey = oldMac;
                Data.History.Remove(entry);
                throw;
            }
            Clear(oldEnc);
            Clear(oldMac);

            // Save again so the .bak copy is also protected by the new password.
            try { Save(); }
            catch (Exception)
            {
                try { File.Delete(BackupPath); } catch (Exception) { }
            }
        }

        /// <summary>Saves the vault to a new file, then removes the old file and its backup copy.</summary>
        public void MoveTo(string newPath)
        {
            newPath = Path.GetFullPath(newPath);
            string oldPath = VaultPath;
            if (string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(newPath)) throw new IOException("A file already exists at " + newPath + ".");

            HistoryEntry entry = Log("Vault file moved", null, oldPath + " → " + newPath);
            VaultPath = newPath;
            try
            {
                Save();
            }
            catch
            {
                VaultPath = oldPath;
                Data.History.Remove(entry);
                throw;
            }
            foreach (string leftover in new[] { oldPath, oldPath + ".bak", oldPath + ".tmp" })
            {
                try { File.Delete(leftover); } catch (Exception) { }
            }
        }

        public void Close()
        {
            Wipe();
            Data = null;
        }

        void Wipe()
        {
            Clear(encKey);
            Clear(macKey);
            encKey = null;
            macKey = null;
        }

        void DeriveKeys(string password)
        {
            Derive(password, salt, iterations, out encKey, out macKey);
        }

        static void Derive(string password, byte[] salt, int iterations, out byte[] enc, out byte[] mac)
        {
            byte[] pwd = Encoding.UTF8.GetBytes(password);
            byte[] master = Pbkdf2(pwd, salt, iterations, 32);
            Clear(pwd);
            using (var hmac = new HMACSHA256(master))
            {
                enc = hmac.ComputeHash(Encoding.ASCII.GetBytes("offline-2fa/encryption"));
                mac = hmac.ComputeHash(Encoding.ASCII.GetBytes("offline-2fa/authentication"));
            }
            Clear(master);
        }

        byte[] ComputeMac(byte[] iv, byte[] ciphertext)
        {
            byte[] header = Encoding.UTF8.GetBytes(FormatName + "|" + FormatVersion + "|" + KdfName + "|"
                + iterations.ToString(CultureInfo.InvariantCulture) + "|" + Convert.ToBase64String(salt) + "|");
            using (var hmac = new HMACSHA256(macKey))
            {
                hmac.TransformBlock(header, 0, header.Length, null, 0);
                hmac.TransformBlock(iv, 0, iv.Length, null, 0);
                hmac.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                return hmac.Hash;
            }
        }

        static VaultFile ReadVaultFile(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            VaultFile file;
            try
            {
                file = FromJson<VaultFile>(bytes);
            }
            catch (Exception)
            {
                throw new InvalidDataException("The vault file is damaged (unreadable format).");
            }
            if (file == null || file.Format != FormatName)
                throw new InvalidDataException("This is not an Offline 2FA vault file.");
            if (file.Version != FormatVersion)
                throw new InvalidDataException("Unsupported vault version " + file.Version + ".");
            if (file.Kdf != KdfName || file.Iterations < 10000 || file.Iterations > 50000000
                || file.Salt == null || file.Iv == null || file.Data == null || file.Mac == null)
                throw new InvalidDataException("The vault file header is damaged.");
            return file;
        }

        static void WriteAtomically(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }

            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }
            try
            {
                File.Replace(temp, path, path + ".bak", true);
            }
            catch (Exception)
            {
                // Some file systems (FAT, network shares) do not support File.Replace.
                File.Copy(path, path + ".bak", true);
                File.Copy(temp, path, true);
                File.Delete(temp);
            }
        }

        static void PreserveOrphanedBackup()
        {
            if (File.Exists(BackupPath) && !File.Exists(VaultPath))
                File.Move(BackupPath, BackupPath + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        }

        static byte[] AesTransform(bool encrypt, byte[] key, byte[] iv, byte[] input)
        {
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;
                using (ICryptoTransform transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor())
                    return transform.TransformFinalBlock(input, 0, input.Length);
            }
        }

        internal static byte[] Pbkdf2(byte[] password, byte[] salt, int iterations, int length)
        {
            try
            {
                return Pbkdf2Native(password, salt, iterations, length);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch (CryptographicException) { }
            return Pbkdf2Managed(password, salt, iterations, length);
        }

        // Windows CNG implementation: several times faster than the managed one, so we can afford more iterations.
        internal static byte[] Pbkdf2Native(byte[] password, byte[] salt, int iterations, int length)
        {
            IntPtr algorithm;
            int status = NativeMethods.BCryptOpenAlgorithmProvider(out algorithm, "SHA256", null, NativeMethods.BCRYPT_ALG_HANDLE_HMAC_FLAG);
            if (status != 0) throw new CryptographicException(status);
            try
            {
                var output = new byte[length];
                status = NativeMethods.BCryptDeriveKeyPBKDF2(algorithm, password, password.Length, salt, salt.Length,
                    (ulong)iterations, output, output.Length, 0);
                if (status != 0) throw new CryptographicException(status);
                return output;
            }
            finally
            {
                NativeMethods.BCryptCloseAlgorithmProvider(algorithm, 0);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static byte[] Pbkdf2Managed(byte[] password, byte[] salt, int iterations, int length)
        {
            using (var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
                return kdf.GetBytes(length);
        }

        static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return bytes;
        }

        static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        static void Clear(byte[] data)
        {
            if (data != null) Array.Clear(data, 0, data.Length);
        }

        internal static byte[] ToJson<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, value);
                return ms.ToArray();
            }
        }

        internal static T FromJson<T>(byte[] data)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(data))
                return (T)serializer.ReadObject(ms);
        }

        static class NativeMethods
        {
            public const int BCRYPT_ALG_HANDLE_HMAC_FLAG = 0x00000008;

            [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
            public static extern int BCryptOpenAlgorithmProvider(out IntPtr phAlgorithm, string pszAlgId, string pszImplementation, int dwFlags);

            [DllImport("bcrypt.dll")]
            public static extern int BCryptCloseAlgorithmProvider(IntPtr hAlgorithm, int dwFlags);

            [DllImport("bcrypt.dll")]
            public static extern int BCryptDeriveKeyPBKDF2(IntPtr hPrf, byte[] pbPassword, int cbPassword, byte[] pbSalt, int cbSalt,
                ulong cIterations, byte[] pbDerivedKey, int cbDerivedKey, int dwFlags);
        }
    }
}
