# Operations Runbook

Day-two tasks and failure recovery. Commands assume the default root
`~/doopass`; substitute your `DOOPASS_HOME`.

## Start / stop

| Mode | Command |
| ---- | ------- |
| Terminal UI | `doopass` |
| Web + API (loopback) | `doopass serve` |
| Web + API (LAN, plain HTTP) | `doopass serve --lan` |
| Web + API (LAN, TLS) | `doopass serve --lan --tls ~/doopass/doopass.pfx --tls-pass …` |
| Background (Linux) | `systemctl --user start doopass` (installed by the autostart script) |
| Stop a foreground host | `Ctrl-C` (graceful; logs "web server stopped") |

Check what is listening:

```bash
ss -ltnp | grep doopass      # TCP (web/API)
ss -lunp | grep doopass      # UDP (discovery)
```

Two host processes on the same directory are unsupported: each keeps its own
in-memory copy and the last writer wins.

## Health checks

```bash
curl -s http://localhost:7240/api/vault/state | jq
```

| Field | Meaning |
| ----- | ------- |
| `storeExists` | the vault file is present |
| `unlocked` | some API session currently holds the decrypted vault |
| `serverVersion` | running build |
| `certFingerprint` | TLS fingerprint, `null` on HTTP |

## Firewall

```bash
sudo ufw allow 7241/udp comment 'Doopass discovery'
sudo ufw allow 7240/tcp comment 'Doopass web/API'
```

A custom `--port` needs its own TCP rule. Windows: allow the ports in Defender
Firewall (the autostart installer does this).

## Common failures

| Symptom | Diagnosis | Fix |
| ------- | --------- | --- |
| Phone finds no host | `grep -i discovery ~/doopass/appdata/doopass.log` | Start with `--lan`/`--announce`; check UDP 7241; same subnet; not the emulator |
| "UDP discovery disabled: port 7241 could not be bound" | another process owns the port | Stop it or start the host without announce |
| Phone finds the host, login times out | TCP port blocked or wrong scheme | Allow TCP 7240; use `https://` for a TLS host |
| "Host did not answer" on the phone after a network change | host IP changed | **Change host** on the login screen; discovery again |
| Login always "Incorrect password" | wrong keyboard layout or caps | use the TUI's `layout:` hint; try the TUI to isolate |
| PWA will not install | insecure origin (plain HTTP LAN / untrusted cert) | use `http://localhost` or a trusted certificate |
| TLS certificate rejected by the phone | certificate rotated without re-pairing | re-pair through **Change host** |
| Web UI loads but actions fail | stale asset cache | hard-reload; text assets are `no-cache` but an installed PWA may keep the old worker (`sw.js` cache version) |
| Port already in use | a previous host is still running | `ss -ltnp | grep 7240`, stop the old process |

## Recovering a vault

1. **Restore a snapshot** — Backups page (web), `r` (TUI), or
   `POST /api/backups/restore?name=<name>`. A safety backup of the current state
   is written first.
2. **Manual restore** — stop the host, then:
   ```bash
   cd ~/doopass/appdata
   cp store.enc store.enc.broken
   cp backups/26_10_2026_store_1.enc.bak store.enc
   ```
   Restart; unlock with the password that was valid **at snapshot time**.
3. **Corrupted `store.enc`** — a truncated or partially written file fails the
   MAC check and is indistinguishable from a wrong password. Try a backup; if
   none exists, the data is unrecoverable by design.
4. **`settings.json` unreadable** — stop the host, move it aside
   (`mv settings.json settings.json.bak`), restart: defaults are regenerated.
   Paths (not data) are lost; a custom `[api]` section must be re-added.

## TLS certificate rotation

```bash
# create a new .pfx (see Feature: TLS and Pinning), then
systemctl --user restart doopass        # or restart `doopass serve`
```

* The startup log prints the new fingerprint.
* Every phone must re-pair through **Change host**; until then it refuses the
  host — that is the intended protection.
* Browsers only need a reload.

## Backups and retention

Nothing is pruned automatically. Inspect and clean up:

```bash
ls -lh ~/doopass/appdata/backups
# delete old snapshots through the UI/API so the change is logged,
# or remove files manually while no host is running
```

Copy snapshots off the machine if they matter; they are encrypted with the same
master password and are useless without it.

## Upgrading

1. Stop the running host/TUI.
2. Replace the binary (single-file build, no install step).
3. Start again. Settings and vault formats did not change across 5.x.
4. If the PWA behaves oddly after an upgrade, close and reinstall it so the
   service worker picks up the new assets.
5. Phones need no action unless the API changed — `serverVersion` in
   `/api/vault/state` tells you what the host runs.

## Security hygiene

* Keep `store.enc`, `doopass.pfx`, `doopass.key` out of version control and
  off shared folders.
* Use a long master password: it is the only protection for a copied vault.
* Prefer `--tls` on shared networks; plain `--lan` exposes tokens and secrets.
* Lower `api.session_ttl_minutes` if a phone may be lost.
* Log out of the web UI when leaving a shared machine — a circuit stays unlocked
  until logout or disconnect.

## Logs for support

```bash
tail -n 200 ~/doopass/appdata/doopass.log
grep -E "ERROR|TLS|discovery|restored" ~/doopass/appdata/doopass.log
```

The log contains paths, key names and exception text — never secret values,
passwords or tokens. It is safe to attach to an issue after a quick skim.
