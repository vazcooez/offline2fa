using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Offline2FA
{
    static class Base32
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        // Uppercases and strips the spaces, dashes and padding that websites add for readability.
        public static string Normalize(string input)
        {
            if (input == null) return "";
            var sb = new StringBuilder(input.Length);
            foreach (char ch in input)
            {
                if (ch == '-' || ch == '=' || char.IsWhiteSpace(ch)) continue;
                sb.Append(char.ToUpperInvariant(ch));
            }
            return sb.ToString();
        }

        public static byte[] Decode(string input)
        {
            string s = Normalize(input);
            if (s.Length == 0) throw new FormatException("The secret key is empty.");

            var output = new byte[s.Length * 5 / 8];
            if (output.Length == 0) throw new FormatException("The secret key is too short.");

            int buffer = 0, bits = 0, index = 0;
            foreach (char ch in s)
            {
                int value = Alphabet.IndexOf(ch);
                if (value < 0)
                    throw new FormatException("The secret key contains an invalid character '" + ch + "'. Only letters A-Z and digits 2-7 are allowed.");
                buffer = (buffer << 5) | value;
                bits += 5;
                if (bits >= 8)
                {
                    bits -= 8;
                    if (index < output.Length) output[index++] = (byte)(buffer >> bits);
                    buffer &= (1 << bits) - 1;
                }
            }
            return output;
        }
    }

    static class Totp
    {
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static long UnixNow()
        {
            return (long)(DateTime.UtcNow - Epoch).TotalSeconds;
        }

        public static string Generate(byte[] key, long unixSeconds, int period, int digits, string algorithm)
        {
            return Hotp(key, unixSeconds / period, digits, algorithm);
        }

        public static string Hotp(byte[] key, long counter, int digits, string algorithm)
        {
            var message = new byte[8];
            for (int i = 7; i >= 0; i--)
            {
                message[i] = (byte)(counter & 0xff);
                counter >>= 8;
            }

            byte[] hash;
            using (HMAC hmac = CreateHmac(algorithm, key))
                hash = hmac.ComputeHash(message);

            int offset = hash[hash.Length - 1] & 0x0f;
            int binary = ((hash[offset] & 0x7f) << 24)
                       | (hash[offset + 1] << 16)
                       | (hash[offset + 2] << 8)
                       | hash[offset + 3];

            int modulus = 1;
            for (int i = 0; i < digits; i++) modulus *= 10;
            return (binary % modulus).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
        }

        static HMAC CreateHmac(string algorithm, byte[] key)
        {
            switch ((algorithm ?? "").ToUpperInvariant())
            {
                case "SHA256": return new HMACSHA256(key);
                case "SHA512": return new HMACSHA512(key);
                default: return new HMACSHA1(key);
            }
        }

        // "123456" -> "123 456", "12345678" -> "1234 5678"
        public static string Format(string code)
        {
            if (code == null || code.Length < 6) return code;
            int split = code.Length / 2;
            return code.Substring(0, split) + " " + code.Substring(split);
        }
    }

    sealed class OtpAuthInfo
    {
        public string Secret;
        public string Issuer;
        public string Name;
        public int Digits = 6;
        public int Period = 30;
        public string Algorithm = "SHA1";
    }

    // Parses otpauth://totp/Issuer:account?secret=...&issuer=...&digits=6&period=30&algorithm=SHA1
    static class OtpAuthUri
    {
        const string Prefix = "otpauth://";

        public static bool IsUri(string text)
        {
            return text != null && text.Trim().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryParse(string text, out OtpAuthInfo info, out string error)
        {
            info = null;
            error = null;
            if (!IsUri(text)) { error = "Not an otpauth:// link."; return false; }

            string rest = text.Trim().Substring(Prefix.Length);
            int slash = rest.IndexOf('/');
            if (slash < 0) { error = "The otpauth:// link is incomplete."; return false; }
            if (!rest.Substring(0, slash).Equals("totp", StringComparison.OrdinalIgnoreCase))
            {
                error = "Only time-based (TOTP) links are supported.";
                return false;
            }
            rest = rest.Substring(slash + 1);

            string label = rest, query = "";
            int q = rest.IndexOf('?');
            if (q >= 0) { label = rest.Substring(0, q); query = rest.Substring(q + 1); }

            var result = new OtpAuthInfo();
            label = Unescape(label);
            int colon = label.IndexOf(':');
            if (colon >= 0)
            {
                result.Issuer = label.Substring(0, colon).Trim();
                result.Name = label.Substring(colon + 1).Trim();
            }
            else
            {
                result.Name = label.Trim();
            }

            foreach (string part in query.Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq).ToLowerInvariant();
                string value = Unescape(part.Substring(eq + 1)).Trim();
                int number;
                switch (key)
                {
                    case "secret":
                        result.Secret = Base32.Normalize(value);
                        break;
                    case "issuer":
                        if (value.Length > 0) result.Issuer = value;
                        break;
                    case "digits":
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) || number < 6 || number > 8)
                        { error = "Unsupported number of digits: " + value; return false; }
                        result.Digits = number;
                        break;
                    case "period":
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) || number < 1 || number > 3600)
                        { error = "Unsupported period: " + value; return false; }
                        result.Period = number;
                        break;
                    case "algorithm":
                        string alg = value.ToUpperInvariant().Replace("-", "");
                        if (alg != "SHA1" && alg != "SHA256" && alg != "SHA512")
                        { error = "Unsupported algorithm: " + value; return false; }
                        result.Algorithm = alg;
                        break;
                }
            }

            if (string.IsNullOrEmpty(result.Secret)) { error = "The otpauth:// link does not contain a secret."; return false; }
            try { Base32.Decode(result.Secret); }
            catch (FormatException ex) { error = ex.Message; return false; }

            info = result;
            return true;
        }

        static string Unescape(string s)
        {
            try { return Uri.UnescapeDataString(s.Replace('+', ' ')); }
            catch (UriFormatException) { return s; }
        }
    }
}
