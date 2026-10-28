# Doopass Wiki

Doopass is a cross-platform password manager for the **terminal**, the
**browser** and **Android phones**. Everything is stored in a single encrypted
`store.enc` file; three front-ends share it, and the on-disk format is
byte-for-byte compatible with the original Python implementation.

This wiki is the reference documentation for the `doopass-next` code base
(version 5.0.0, GPL-3.0). It describes **what the product does**, **how each
application is put together**, and **how the code is organised**.

The pages are maintained in the repository under `docs/wiki/` and published to
the GitHub wiki with `scripts/publish-wiki.sh`.

## Start here

| Page | What you get |
| ---- | ------------ |
| [Overview](Overview.md) | What Doopass is, who it is for, the guarantees it makes |
| [Architecture](Architecture.md) | Components, process model, data flow, dependency rules |
| [Business Rules](Business-Rules.md) | Every rule the vault, backups, settings and sessions enforce |
| [Data Formats](Data-Formats.md) | `store.enc`, `settings.json`, backup names, wire formats |
| [Security Model](Security-Model.md) | Cryptography, threat model, trust boundaries, what is never logged |

## Applications

| Page | Application |
| ---- | ----------- |
| [Application: Core](Application-Core.md) | `Doopass.Core` — storage, crypto, settings, backups, logging |
| [Application: CLI Host](Application-CLI-Host.md) | `Doopass.Cli` — the single `doopass` entry point and web host |
| [Application: Web UI](Application-Web-UI.md) | `Doopass.WebUi` — Blazor components, themes, PWA shell |
| [Application: REST API](Application-REST-API.md) | `/api` + `/hubs/vault` contract for thin clients |
| [Application: TUI](Application-TUI.md) | `Doopass.Tui` — Terminal.Gui interface |
| [Application: Android Client](Application-Android-Client.md) | `Doopass.Mobile` — MAUI thin client |

## Features

| Page | Feature |
| ---- | ------- |
| [Feature: Store](Feature-Store.md) | Add, edit, delete, reveal, copy, search pairs |
| [Feature: Backups](Feature-Backups.md) | Dated snapshots, safety backup on restore, delete |
| [Feature: Settings](Feature-Settings.md) | `settings.json` editing, defaults, PWA install card |
| [Feature: Sessions and Locking](Feature-Sessions-and-Locking.md) | Unlock, logout, sliding TTL, automatic relock |
| [Feature: Real-time Sync](Feature-Realtime-Sync.md) | SignalR push between all clients |
| [Feature: LAN Discovery](Feature-Discovery.md) | UDP broadcast so phones find the host |
| [Feature: TLS and Pinning](Feature-TLS-Pinning.md) | HTTPS host, trust-on-first-use certificate pinning |
| [Feature: PWA](Feature-PWA.md) | Installable web app, offline shell, icons |
| [Feature: Logging](Feature-Logging.md) | Log file, rotation, redaction rules |
| [Feature: Packaging](Feature-Packaging.md) | Single-file publishes, releases, autostart, Android APK |

## Reference

| Page | Contents |
| ---- | -------- |
| [Configuration](Configuration.md) | `settings.json` keys, environment variables, CLI flags |
| [API Reference](API-Reference.md) | REST endpoints, status codes, hub events, discovery payload |
| [Generated API Docs](API-Reference-Core.md) | XML-doc reference for every public type (per assembly) |
| [Testing](Testing.md) | Test projects, what is covered, how to extend |
| [Development](Development.md) | Build, run, conventions, adding a feature |
| [Operations](Operations.md) | Runbook: firewall, discovery failures, TLS rotation, recovery |
| [Known Limitations](Known-Limitations.md) | Honest list of current constraints and sharp edges |
| [Glossary](Glossary.md) | Terms used across the code base and this wiki |

## About this documentation

* Written in English, kept in the repository, and reviewed like code.
* Every statement about behaviour is derived from the source in `src/`; when the
  code and the README disagree, the code wins and the README is fixed.
* API reference pages under `API-Reference-*.md` are generated from the XML
  documentation that the compiler emits (`dotnet build` with
  `GenerateDocumentationFile=true`) — see
  [Development](Development.md#generating-api-documentation).
