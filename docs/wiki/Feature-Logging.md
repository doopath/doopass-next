# Feature: Logging

One append-only text log records what the application did, never what it stores.

## Location and format

| Property | Value |
| -------- | ----- |
| Path | `settings.local.log_file`, default `~/doopass/appdata/doopass.log` |
| Encoding | UTF-8 without BOM |
| Line format | `yyyy-MM-dd HH:mm:ss,fff LEVEL message` |
| Levels | `INFO`, `DEBUG`, `ERROR` |
| Rotation | at 5 MB → `doopass.log.1` (the previous archive is deleted) |
| Thread safety | a single static lock around append + rotation |
| Failure policy | every I/O exception is swallowed — logging never breaks the app |

Phone: `FileSystem.AppDataDirectory/doopass.log` in the Android app sandbox.

## What is recorded

| Area | Examples |
| ---- | -------- |
| Store | `Store initialized: <path>`, `Store opened: <path> (N entries)` |
| CRUD | `Property added: github`, `Property updated: github`, `Property removed: github` |
| Backups | `Backup created: 26_10_2026_store_1.enc.bak`, `Backup deleted: …`, `Store restored from backup: …` |
| Settings | `Setting changed: local.store_file`, `Settings restored to defaults`, `Dropped obsolete settings section: remote` |
| Host lifecycle | `Doopass web server starting (v5.0.0, bind localhost, port 7240)`, `Doopass web server stopped` |
| TLS | `TLS enabled; certificate SHA-256 fingerprint: <hex>` |
| Discovery | `UDP discovery announcing on port 7241`, bind/create failures |
| Errors | full exception text from UI handlers and background services |
| Phone | unhandled exceptions (both `AppDomain` and Android hooks), push connection failures, SecureStorage failures, discovery failures, remote call failures |

## What is never recorded

* master passwords;
* secret values (only key names);
* API session tokens;
* certificate passwords;
* HTTP request bodies.

`AppLoggerTests.StoreOperations_AreLogged_WithoutSecrets` opens a store, performs
CRUD and asserts the password and value never appear in the file.

## Practical use

```bash
tail -f ~/doopass/appdata/doopass.log
grep ERROR ~/doopass/appdata/doopass.log
grep -i discovery ~/doopass/appdata/doopass.log
```

| Symptom | What to look for |
| ------- | ---------------- |
| Phone cannot find the host | absence of "UDP discovery announcing"; a bind error |
| PWA/tls issues | `TLS enabled` fingerprint line |
| Login loop | `Store opened` vs `Store open failed (incorrect password)` (DEBUG) |
| Lost data | `Property removed` / `Store restored from backup` history |
| Backup restore surprises | `Backup created` right before `Store restored from backup` (the safety backup) |

## Limitations

* The log is not structured (no JSON) and has no correlation ids.
* No severity filtering, no log level configuration; `DEBUG` lines are rare and
  always written when present.
* Rotation keeps exactly one archive and does not compress.
* Several processes sharing one `DOOPASS_HOME` append to the same file; the lock
  only protects within a process, so interleaved lines are possible.
* There is no audit trail of who read which secret — by design, the log avoids
  that level of detail.

## Where the code lives

* `Doopass.Core/Diagnostics/AppLogger.cs` — writer, rotation, gate
* Call sites across `Cli`, `WebUi` pages, `Mobile` services and `Tui`
* `tests/Doopass.Core.Tests/AppLoggerTests.cs`
