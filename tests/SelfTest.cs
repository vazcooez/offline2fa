using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Offline2FA
{
    static class SelfTest
    {
        static int failures;

        static void Check(bool condition, string name)
        {
            Console.WriteLine((condition ? "  PASS  " : "  FAIL  ") + name);
            if (!condition) failures++;
        }

        static bool Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return true; }
            catch (Exception) { return false; }
            return false;
        }

        static int Main()
        {
            Console.WriteLine("TOTP (RFC 6238 test vectors)");
            byte[] key1 = Encoding.ASCII.GetBytes("12345678901234567890");
            byte[] key256 = Encoding.ASCII.GetBytes("12345678901234567890123456789012");
            byte[] key512 = Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234");
            long[] times = { 59, 1111111109, 1111111111, 1234567890, 2000000000, 20000000000 };
            string[] sha1 = { "94287082", "07081804", "14050471", "89005924", "69279037", "65353130" };
            string[] sha256 = { "46119246", "68084774", "67062674", "91819424", "90698825", "77737706" };
            string[] sha512 = { "90693936", "25091201", "99943326", "93441116", "38618901", "47863826" };
            for (int i = 0; i < times.Length; i++)
            {
                Check(Totp.Generate(key1, times[i], 30, 8, "SHA1") == sha1[i], "SHA1   T=" + times[i]);
                Check(Totp.Generate(key256, times[i], 30, 8, "SHA256") == sha256[i], "SHA256 T=" + times[i]);
                Check(Totp.Generate(key512, times[i], 30, 8, "SHA512") == sha512[i], "SHA512 T=" + times[i]);
            }
            Check(Totp.Generate(key1, 59, 30, 6, "SHA1") == "287082", "6-digit truncation");
            Check(Totp.Format("287082") == "287 082" && Totp.Format("94287082") == "9428 7082", "code formatting");

            Console.WriteLine("Base32");
            Check(Encoding.ASCII.GetString(Base32.Decode("MZXW6YTBOI")) == "foobar", "decode foobar");
            Check(Encoding.ASCII.GetString(Base32.Decode("mzxw 6ytb-oi======")) == "foobar", "lowercase, spaces, dashes, padding");
            Check(Throws<FormatException>(delegate { Base32.Decode("ABC1DEFG"); }), "rejects invalid characters");
            Check(Throws<FormatException>(delegate { Base32.Decode("   "); }), "rejects empty key");

            Console.WriteLine("otpauth:// links");
            OtpAuthInfo info;
            string error;
            bool parsed = OtpAuthUri.TryParse("otpauth://totp/ACME%20Co:john.doe@email.com?secret=HXDMVJECJJWSRB3HWIZR4IFUGFTMXBOZ&issuer=ACME%20Co&algorithm=SHA256&digits=8&period=60", out info, out error);
            Check(parsed && info.Issuer == "ACME Co" && info.Name == "john.doe@email.com"
                && info.Secret == "HXDMVJECJJWSRB3HWIZR4IFUGFTMXBOZ" && info.Digits == 8 && info.Period == 60 && info.Algorithm == "SHA256",
                "full link");
            parsed = OtpAuthUri.TryParse("otpauth://totp/alice?secret=JBSWY3DPEHPK3PXP", out info, out error);
            Check(parsed && info.Name == "alice" && info.Issuer == null && info.Digits == 6 && info.Period == 30 && info.Algorithm == "SHA1", "minimal link uses defaults");
            Check(!OtpAuthUri.TryParse("otpauth://hotp/x?secret=JBSWY3DPEHPK3PXP&counter=1", out info, out error), "rejects HOTP links");
            Check(!OtpAuthUri.TryParse("otpauth://totp/x?issuer=y", out info, out error), "rejects link without secret");

            Console.WriteLine("Key derivation");
            byte[] pwd = Encoding.UTF8.GetBytes("password");
            byte[] salt = Encoding.UTF8.GetBytes("saltsaltsaltsalt");
            Check(Convert.ToBase64String(Vault.Pbkdf2Native(pwd, salt, 1000, 32)) == Convert.ToBase64String(Vault.Pbkdf2Managed(pwd, salt, 1000, 32)),
                "native PBKDF2 matches managed implementation");
            var sw = Stopwatch.StartNew();
            Vault.Pbkdf2(pwd, salt, Vault.DefaultIterations, 32);
            Console.WriteLine("  info  " + Vault.DefaultIterations + " iterations took " + sw.ElapsedMilliseconds + " ms");

            Console.WriteLine("Vault");
            string dir = Path.Combine(Path.GetTempPath(), "offline2fa-test-" + Guid.NewGuid().ToString("N"));
            Vault.SetVaultPath(Path.Combine(dir, "vault.dat"));
            try
            {
                Vault vault = Vault.Create("correct horse");
                vault.Data.Accounts.Add(new Account { Id = "1", Issuer = "Test", Name = "me", Secret = "JBSWY3DPEHPK3PXP", Digits = 6, Period = 30, Algorithm = "SHA1", Created = Stamp.Now() });
                vault.Log("Account added", vault.Data.Accounts[0], null);
                vault.Save();
                vault.Close();

                string raw = File.ReadAllText(Vault.VaultPath);
                Check(!raw.Contains("JBSWY3DPEHPK3PXP") && !raw.Contains("Test"), "file contains no plaintext secrets or names");
                Check(Throws<WrongPasswordException>(delegate { Vault.Open("wrong password"); }), "wrong password rejected");

                vault = Vault.Open("correct horse");
                Check(vault.Data.Accounts.Count == 1 && vault.Data.Accounts[0].Secret == "JBSWY3DPEHPK3PXP", "accounts survive round trip");
                Check(vault.Data.History.Count == 2 && vault.Data.History[1].AccountName == "Test (me)", "history survives round trip");
                Check(vault.CheckPassword("correct horse") && !vault.CheckPassword("nope"), "CheckPassword");

                vault.ChangePassword("new password 123");
                vault.Close();
                Check(Throws<WrongPasswordException>(delegate { Vault.Open("correct horse"); }), "old password rejected after change");
                Check(Throws<WrongPasswordException>(delegate { Vault.Open("correct horse", Vault.BackupPath); }), "backup copy does not accept old password");
                vault = Vault.Open("new password 123");
                Check(vault.Data.Accounts.Count == 1 && vault.Data.History[vault.Data.History.Count - 1].Action == "Master password changed", "new password opens vault");

                string oldPath = Vault.VaultPath;
                string movedPath = Path.Combine(dir, "moved folder", "my 2fa.vault");
                vault.MoveTo(movedPath);
                Check(Vault.VaultPath == movedPath && File.Exists(movedPath) && !File.Exists(oldPath) && !File.Exists(oldPath + ".bak"),
                    "MoveTo writes the new file and removes the old ones");
                Check(Throws<IOException>(delegate { vault.MoveTo(movedPath + ".other"); File.WriteAllText(movedPath, "x"); vault.MoveTo(movedPath); }),
                    "MoveTo refuses to overwrite an existing file");
                vault.Close();
                Check(Vault.VaultPath == movedPath + ".other" && Vault.Open("new password 123").Data.History.Exists(h => h.Action == "Vault file moved"),
                    "moved vault opens and logs the move");

                VaultFile file = Vault.FromJson<VaultFile>(File.ReadAllBytes(Vault.VaultPath));
                byte[] data = Convert.FromBase64String(file.Data);
                data[data.Length / 2] ^= 1;
                file.Data = Convert.ToBase64String(data);
                File.WriteAllBytes(Vault.VaultPath, Vault.ToJson(file));
                Check(Throws<WrongPasswordException>(delegate { Vault.Open("new password 123"); }), "tampered vault is rejected");

                File.WriteAllText(Vault.VaultPath, "garbage");
                Check(Throws<InvalidDataException>(delegate { Vault.Open("new password 123"); }), "corrupt file reported as damaged");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }

            Console.WriteLine(failures == 0 ? "All tests passed." : failures + " test(s) FAILED.");
            return failures == 0 ? 0 : 1;
        }
    }
}
