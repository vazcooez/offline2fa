using System;
using System.Collections.Generic;

namespace Offline2FA
{
    static class AppVersion
    {
        // Single source of truth for the version; build.ps1 reads it to name the release file.
        public const string Number = "1.1.3";
        public const string Publisher = "Vazcooez LLC";
        public const string Copyright = "Copyright © 2026 Vazcooez LLC";

        public static string Display
        {
            get { return "v" + Number; }
        }

        public static Version Parse(string text)
        {
            Version value;
            return !string.IsNullOrEmpty(text) && Version.TryParse(text, out value) ? value : null;
        }
    }

    sealed class ReleaseNote
    {
        public string Version;
        public string Date;
        public string[] Changes;
    }

    static class ReleaseNotes
    {
        // Newest first. Add an entry here (and bump AppVersion.Number) for every release.
        public static readonly ReleaseNote[] All =
        {
            new ReleaseNote
            {
                Version = "1.1.3",
                Date = "2026-09-14",
                Changes = new[]
                {
                    "Codes in the main window are masked, showing only the last digit (for example ••• ••6).",
                    "Clicking a code or Copy copies it and shows the full code for 10 seconds."
                }
            },
            new ReleaseNote
            {
                Version = "1.1.2",
                Date = "2026-09-14",
                Changes = new[]
                {
                    "Offline 2FA is now published by Vazcooez LLC (shown in the file properties, About and this window)."
                }
            },
            new ReleaseNote
            {
                Version = "1.1.1",
                Date = "2026-09-14",
                Changes = new[]
                {
                    "The tray menu no longer shows codes. It shows each account with the time left, and clicking an account still copies its code."
                }
            },
            new ReleaseNote
            {
                Version = "1.1.0",
                Date = "2026-09-14",
                Changes = new[]
                {
                    "Choose where the vault file is stored during setup, open an existing vault file, or move it later (More → Move vault file…).",
                    "Closing the window keeps the app running in the notification area. Use Exit in the tray menu to quit.",
                    "The tray menu lists your accounts. Click one to copy its code without opening the window.",
                    "Adding an account only needs the secret key. The name is optional, and digits, period and algorithm are under Advanced options.",
                    "This \"What's new\" window, shown once after each update.",
                    "Fixed: the countdown timer overlapped the code."
                }
            },
            new ReleaseNote
            {
                Version = "1.0.0",
                Date = "2026-09-14",
                Changes = new[]
                {
                    "First release: offline 2FA codes, encrypted vault protected by a master password, remove and restore accounts, timestamped history, auto-lock and clipboard auto-clear."
                }
            }
        };

        public const string AboutText =
            "Offline 2FA generates the sign-in codes used for two-factor authentication (the same codes an authenticator app on your phone shows) without any internet connection.\r\n\r\n" +
            "•  Add account: paste the secret key a website gives you when you turn on two-factor authentication. A name is optional.\r\n" +
            "•  Codes are masked except for the last digit. Click a code, or Copy, to copy it and see it in full for 10 seconds, then paste it into the website. Codes refresh every 30 seconds, and the clipboard is cleared after 30 seconds.\r\n" +
            "•  Remove hides an account from the list without deleting it. Restore it at any time from Removed.\r\n" +
            "•  History lists everything that happened, with timestamps.\r\n" +
            "•  Closing the window keeps the app in the notification area. Right-click the tray icon to copy a code or to exit.\r\n\r\n" +
            "Your secret keys are encrypted with your master password (AES-256) and are never shown again after you save them. " +
            "The vault file can be stored wherever you like. The app locks after 10 minutes of inactivity and when Windows is locked. " +
            "There is no password recovery, so keep your master password safe.";

        /// <summary>True when this version's notes have not been shown yet (first run, or first run after an update).</summary>
        public static bool HasUnseenChanges(string lastSeenVersion)
        {
            Version seen = AppVersion.Parse(lastSeenVersion);
            return seen == null || seen < AppVersion.Parse(AppVersion.Number);
        }

        /// <summary>Notes for versions newer than the last one the user saw (just the current version on a first run).</summary>
        public static List<ReleaseNote> Since(string lastSeenVersion)
        {
            Version seen = AppVersion.Parse(lastSeenVersion);
            Version current = AppVersion.Parse(AppVersion.Number);
            var result = new List<ReleaseNote>();
            foreach (ReleaseNote note in All)
            {
                Version v = AppVersion.Parse(note.Version);
                if (v == null || v > current) continue;
                if (seen == null ? v == current : v > seen) result.Add(note);
            }
            return result;
        }
    }
}
