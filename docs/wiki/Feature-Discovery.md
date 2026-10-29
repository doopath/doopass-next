# Feature: LAN Discovery

Phones find the host without typing an address. A UDP broadcast probe is
answered by the host with a small JSON announcement.

## Protocol

| Property | Value |
| -------- | ----- |
| Transport | UDP over IPv4 |
| Port | `7241` (fixed, not configurable) |
| Probe | exactly the 16 ASCII bytes `DOOPASS_DISCOVER` |
| Reply | UTF-8 JSON (camelCase), sent to the probe's source address/port |
| Host enabled when | `--lan`, `--announce`, or `api.announce = "true"` |

Reply payload:

```json
{
  "service": "doopass",
  "name": "DESKTOP-7F3",
  "port": 7240,
  "tls": true,
  "fingerprint": "d3894b6e...c8e50f",
  "version": "5.0.0"
}
```

`name` is `Environment.MachineName` (fallback `doopass`), `port` is the web port,
`tls`/`fingerprint` describe the certificate in use (`fingerprint: null` on
plain HTTP).

## Host behaviour (`DiscoveryService`)

* The socket is created only when announcing is enabled; failures to create or
  bind it are **logged and non-fatal** — the host keeps serving HTTP/API.
* Binds `0.0.0.0:7241` and listens asynchronously.
* Answers only datagrams whose bytes equal the probe exactly (length and
  content); anything else is ignored.
* Send failures are ignored (the prober may be gone).
* Stopping cancels the listener and closes the socket.

Both the raw reply fields and the constant are exposed for tests
(`DiscoveryService.FormatProbe`, `DiscoveryService.Probe`).

## Phone behaviour (`HostDiscovery`)

1. Acquires an Android multicast lock (Wi-Fi broadcast replies are otherwise
   filtered).
2. Creates a `UdpClient` with `SO_BROADCAST`/`EnableBroadcast` and binds an
   ephemeral port.
3. Sends the probe to `255.255.255.255:7241` **and** to the directed broadcast
   address of every active Ethernet/Wi-Fi interface (`192.168.1.42/24` →
   `192.168.1.255`). VPNs, loopback and virtual bridges are skipped; `/32`
   interfaces have no broadcast address.
4. Receives for 1.5 s in a single outstanding `ReceiveAsync` loop (a second
   concurrent receive is rejected by `UdpClient`).
5. Accepts replies with `service == "doopass"` and `port in 1..65535`; the URL is
   built from the **sender address**, not from the payload, and de-duplicated by
   URL. Scheme is `https` when `tls` is true.

The reconnect window, probe text and port are constants in
`HostDiscovery`/`DiscoveryService`; they must stay in sync.

## Firewall

Discovery needs **inbound UDP 7241**; the API needs **inbound TCP 7240** (or the
custom `--port`). Example with `ufw`:

```bash
sudo ufw allow 7241/udp comment 'Doopass discovery'
sudo ufw allow 7240/tcp comment 'Doopass web/API'
```

A missing TCP rule for a **custom** port is the confusing case: the host answers
from the PC itself while the phone times out, and only that one address looks
broken.

The Windows autostart installer adds the equivalent rules.

## Troubleshooting

| Symptom | Cause / fix |
| ------- | ----------- |
| No hosts found | Different network/VLAN, firewall, or host started without `--lan`/`--announce` |
| Host found but login fails | Wrong scheme in Manual mode (a TLS host needs `https://`) or firewall blocking TCP |
| Emulator finds nothing | Emulator cannot receive LAN broadcasts; use `http://10.0.2.2:7240` |
| USB-only setup | `adb reverse tcp:7240 tcp:7240`, then `127.0.0.1:7240` (`https://…` for TLS) |
| Found host does not answer | A stale `doopass serve` still runs on that port; check `ss -ltnp \| grep doopass` |
| Discovery died but web works | Port 7241 already bound by another process — see the log: "UDP discovery disabled: port 7241 could not be bound" |

## Security notes

* Discovery is unauthenticated and reveals the machine name, the web port, the
  version and the TLS fingerprint to anyone on the LAN.
* It never reveals secrets or whether the vault is locked; `/api/vault/state`
  does report `storeExists` and `unlocked`.
* The announcement is only served when explicitly enabled; a loopback-only host
  (`doopass serve` without `--lan`) does not announce.

## Where the code lives

| Concern | File |
| ------- | ---- |
| Host announcer | `Cli/Api/DiscoveryService.cs` (hosted service), `DiscoveryAnnounce` record |
| Enablement + fingerprint | `Cli/Api/WebHostFactory.cs` |
| Phone scanner | `Mobile/Services/HostDiscovery.cs` |
| UI | `Mobile/Components/Connect.razor` |
| Tests | `tests/Doopass.Api.Tests/DiscoveryPayloadTests.cs` |
