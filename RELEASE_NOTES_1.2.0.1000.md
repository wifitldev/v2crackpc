# v1.2.0.1000 — Windows preview

This is a **pre-release** for testing the new Windows installer and portable package.

## What's new

- Added a per-user, self-contained Windows x64 installer; no separate .NET installation is intended to be required.
- The installer includes Xray, sing-box, geodata, Wintun and the DPI component. It preserves existing settings and subscription data during updates and asks you to close the application instead of force-stopping it.
- Added a portable ZIP: extract it to a folder and run `v2crackN.exe`.
- Improved update staging, validation, ordinary-error rollback and installer safety checks.
- Updated the Windows client, tray and settings persistence, and expanded automated tests.

## Testing and limitations

- 179 automated tests passed, including file-level checks using this portable ZIP. An earlier installer was used to update an existing installation while preserving its user files; **this exact installer has not yet been run**.
- Clean installation on a Windows machine without .NET, live connectivity, and uninstall/reinstall remain to be verified. Back up your settings before testing.
- Recovery after power loss or forced termination is not guaranteed. Installer registration and shortcut creation are not part of the file rollback transaction.
- The installer is unsigned and Windows may display an unknown-publisher warning. Verify the accompanying SHA-256 files before use.
- Xray and sing-box binaries were matched to official release binaries. Provenance, redistribution requirements and notices for all remaining components still need review; this preview does not yet include third-party license notices.

**Downloads:** `*-installer.exe` is the installer; `*.zip` is the portable build. SHA-256 checksum files accompany both.
