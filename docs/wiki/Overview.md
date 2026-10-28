# Overview

Doopass is a **local-first password manager**. It stores a flat list of
`key = value` secrets in one encrypted file and offers several ways to reach
that file:

* a **terminal UI** (Terminal.Gui v2) that mirrors the original Textual
  interface;
* a **web UI** (Blazor Server) that is also installable as a PWA;
* a **private REST API** plus a **SignalR change channel** served by the same
  process;
* an **Android thin client** (MAUI + Blazor) that holds no vault of its own and
  talks to a host over the REST API.

The project is a from-scratch .NET 10 rewrite of
[Doopass](https://github.com/doopath/PasswordManager/) (Python + Textual). The
file format did not change, so an existing `~/doopass` directory opens without
migration.

## Product goals

| Goal | How it is met |
| ---- | ------------- |
| **One vault, many front-ends** | A single `store.enc`; every UI is a view over the same data model (`Store`) |
| **No cloud, no server dependency** | Everything runs on the user's machine; the phone only attaches to a host the user runs |
| **Drop-in compatibility** | Fernet token, PBKDF2 parameters, plaintext layout and backup names match the Python version exactly |
| **Small operational surface** | One self-contained executable, embedded web assets, no database, no runtime install |
| **Honest security posture** | Local file encryption, explicit unlock, certificate pinning for the phone, secrets never logged |

## What a user can do

1. **Create a vault** with a master password (`/init` in the web UI, `Init store`
   in the TUI) or point Doopass at an existing `store.enc`.
2. **Unlock it** with the master password. The password is only kept in memory
   while the vault is open.
3. **Manage secrets** — add, rename, edit, delete, search, reveal and copy
   individual values.
4. **Snapshot the vault** into dated backups, restore one (a safety backup is
   written first) and delete snapshots.
5. **Change application settings** — storage paths and the optional API keys
   (`api.tls_pfx`, `api.tls_pass`, `api.session_ttl_minutes`,
   `api.announce`) — and restore the embedded defaults.
6. **Serve the vault to other devices** on the LAN, optionally over HTTPS with a
   self-signed certificate that the Android client pins.
7. **Use the phone as a remote control** of the PC vault, with live updates from
   every other client.

## Non-goals

These are deliberate decisions, not missing features:

* **No multi-vault / multi-user support.** One store file, one master password.
* **No password generator, TOTP, attachments or browser extension.**
* **No cloud sync.** Devices reach the vault only through a running host.
* **No password recovery.** Forgetting the master password means losing the
  data; backups are encrypted with the same password.
* **No migration tooling.** Compatibility is achieved by keeping the format, not
  by transforming files.

## Compatibility contract

The following are treated as stable contracts and are protected by tests:

| Contract | Where it is enforced |
| -------- | -------------------- |
| `store.enc` byte layout and plaintext grammar | `Store`, `Fernet`, `Pbkdf2`, `Base64Url`; `FernetCompatibilityTests` |
| `settings.json` shape and `APPDATA/` placeholder | `Settings`; `SettingsTests` |
| Backup file naming (`dd_MM_yyyy_<store>_<n>.enc.bak`) | `StoreBackup` |
| REST paths, payload shapes and status codes | `VaultEndpoints`; `Doopass.Api.Tests` |
| Discovery probe text and reply JSON | `DiscoveryService`, `DiscoveryPayloadTests` |
| UTF-8 without BOM for every written file | `TextFile`; store and settings tests |

Changing any of them is a breaking change for existing users and for the
Android client. See [Data Formats](Data-Formats.md) for the exact
specifications.

## Platforms

| Platform | What runs | Notes |
| -------- | --------- | ----- |
| Linux | TUI, web host | primary development target |
| Windows | TUI, web host | startup shortcut installer |
| Android | thin client | API 23+, no local vault |
| macOS | TUI, web host (untested in CI) | no packaged build script |

Requirements: .NET SDK 10 to build; a published binary needs no runtime.

## Version and license

* Current version: **5.0.0** (`Doopass.Core.Constants.Version`, mirrored in every
  `.csproj`).
* License: **GPL-3.0**, inherited from the original project.

Continue with [Architecture](Architecture.md) for the component view, or
[Business Rules](Business-Rules.md) for the behaviour contract.
