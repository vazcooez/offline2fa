# Offline 2FA

A small Windows desktop app that generates two-factor authentication codes (TOTP) completely offline.

Published by **Vazcooez LLC**.

- Add as many accounts as you like. Paste the secret key the website gives you; a name is optional.
- Secret keys are never shown after they are saved. Codes are masked except for the last digit (`••• ••6`); copying a code shows it in full for 10 seconds.
- On first launch you create a master password. It encrypts everything, and it is required each time the app opens.
- You choose where the vault file is stored, and you can move it later.
- **Remove** only hides an account. Nothing is ever deleted, and hidden accounts can be restored from **Removed**.
- **History** records every action with a timestamp (added, removed, restored, code copied, unlocked, locked, and so on). It can be filtered and exported to CSV. Secret keys and codes are never recorded.
- Closing the window keeps the app in the notification area (tray). Right-click the tray icon to see your accounts and click one to copy its code, or choose **Exit** to quit.
- After each update, a **What's new** window lists the changes once.

## Download

Get `Offline2FA-vX.Y.Z.exe` from the [Releases](https://github.com/vazcooez/offline2fa/releases) page. It is a single file with no installer and no dependencies. It needs .NET Framework 4.7.2 or later, which is built into Windows 10 and 11.

## Use

Click a code (or **Copy**) to copy it and reveal it for 10 seconds. The clipboard is cleared after 30 seconds, and codes are kept out of Windows clipboard history.

| Shortcut | Action |
|---|---|
| Ctrl+N | Add account |
| Ctrl+F | Search (Enter copies the first result, Esc clears) |
| Ctrl+H | History |
| Ctrl+L | Lock |

The app locks itself after 10 minutes of inactivity and when Windows is locked. If the window was hidden in the tray, it stays locked until you open it again.

Codes are based on the computer's clock. If codes are rejected, make sure the Windows time is correct.

## Where data is stored

- **Vault file:** wherever you chose during setup. The default is `%APPDATA%\Offline2FA\vault.dat`, and the previous version is kept next to it as `.bak`.
  - Move it with **More → Move vault file…**.
  - Open a different vault file with **Use a different vault file…** on the unlock screen.
- **Settings:** `%APPDATA%\Offline2FA\settings.json` remembers the vault location and the last version whose What's new window you closed. It contains no secrets.
- **Portable mode:** put an empty file named `portable.txt` next to the exe. Settings (and the default vault) then live in a `data` folder beside the exe, and vault paths inside that folder are stored as relative paths, so the app keeps working when a USB drive gets a different letter.
- **Backup:** use **More → Back up vault file…**. To restore a backup, exit the app and copy the backup over the vault file. A backup opens with the master password that was in use when it was made.

There is no password recovery. If you forget the master password, the secrets cannot be decrypted.

## Security

- The vault is encrypted with AES-256-CBC and protected by HMAC-SHA256 (encrypt-then-MAC), so a wrong password or a modified file is detected.
- Keys are derived from the master password with PBKDF2-HMAC-SHA256 using 600,000 iterations and a random salt.
- Account names and history are encrypted too. Only the key-derivation settings are stored in plain text.
- Only one copy of the app can run at a time, so the vault file cannot be written by two copies at once.

## Build

Run `build.bat` (or `build.ps1`). It uses the C# compiler that ships with .NET Framework (`csc.exe`), so no SDK is needed. The build:

1. Generates `assets\app.ico` if it is missing.
2. Compiles and runs `tests\SelfTest.cs` (RFC 6238 test vectors, Base32, otpauth links, vault encryption, wrong-password and tampering checks, moving the vault).
3. Builds `dist\Offline2FA.exe` and copies it to `release\Offline2FA-v<version>.exe`.

To make a release, bump `AppVersion.Number` and add an entry to `ReleaseNotes.All` in `src\Version.cs`.

```
src\Program.cs       startup: settings, setup / unlock, single instance
src\MainForm.cs      main window and tray: list, search, copy, remove, lock, move, backup
src\AccountCard.cs   one account row (name, code, countdown)
src\Dialogs.cs       setup, unlock, add account, removed accounts, history, what's new, change password
src\Settings.cs      settings.json (vault location, last seen version, portable mode)
src\Version.cs       version number, release notes, app description
src\Vault.cs         data model, encryption, atomic file saving
src\Totp.cs          TOTP / Base32 / otpauth:// parsing
```
