#!/usr/bin/env bash
#
# Installs the Doopass web server as a user systemd service and starts it now.
#
# Usage:
#   ./install-autostart-linux.sh [path/to/doopass] [local|lan] [--linger]
#   ./install-autostart-linux.sh [local|lan] [--linger]
#
# Without a path the executable next to this script is installed, so the release
# archive works as unpacked. Without a bind mode it picks lan when setup-tls has
# stored a certificate, local otherwise. PORT (env) overrides the port (7240).
# --linger keeps the service running even when no session is active.
#
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PORT="${PORT:-7240}"
EXE_PATH=""
BIND=""
LINGER=0

usage() {
    echo "Usage: $0 [path/to/doopass] [local|lan] [--linger]"
    echo "       PORT env var overrides the HTTP port (default 7240)."
    exit 1
}

for arg in "$@"; do
    case "$arg" in
        --linger) LINGER=1 ;;
        --help|-h) usage ;;
        local|localhost|lan) BIND="$arg" ;;
        *) EXE_PATH="$arg" ;;
    esac
done

[[ -n "$EXE_PATH" ]] || EXE_PATH="$SCRIPT_DIR/doopass"
[[ -f "$EXE_PATH" ]] || usage

# setup-tls stores the certificate in settings.json; the host then serves HTTPS
# without --tls on the command line.
SETTINGS_FILE="${DOOPASS_HOME:-$HOME/doopass}/appdata/settings.json"
HAS_TLS=0
if [[ -f "$SETTINGS_FILE" ]] && grep -q '"tls_pfx"[[:space:]]*:' "$SETTINGS_FILE"; then
    HAS_TLS=1
fi

if [[ -z "$BIND" ]]; then
    if [[ "$HAS_TLS" == "1" ]]; then
        BIND="lan"
    else
        BIND="local"
    fi
fi

LAN=0
case "$BIND" in
    local|localhost) LAN=0 ;;
    lan) LAN=1 ;;
    *) usage ;;
esac

BIN_DIR="$HOME/.local/bin"
TARGET="$BIN_DIR/doopass"
UNIT_DIR="$HOME/.config/systemd/user"
UNIT="$UNIT_DIR/doopass.service"

mkdir -p "$BIN_DIR" "$UNIT_DIR" "$HOME/.cache/doopass"

# Stop any running instance before replacing the binary.
systemctl --user stop doopass.service 2>/dev/null || true

install -m 0755 "$EXE_PATH" "$TARGET"

EXTRA=""
[[ "$LAN" == "1" ]] && EXTRA=" --lan"

cat > "$UNIT" <<EOF
[Unit]
Description=Doopass web server
After=network.target

[Service]
Type=simple
ExecStart=$TARGET serve --port $PORT --no-browser$EXTRA
Environment=DOTNET_BUNDLE_EXTRACT_BASE_DIR=%h/.cache/doopass
Restart=on-failure
RestartSec=3

[Install]
WantedBy=default.target
EOF

systemctl --user daemon-reload
systemctl --user enable --now doopass.service

if [[ "$LINGER" == "1" ]]; then
    loginctl enable-linger "$USER" 2>/dev/null || true
fi

SCHEME="http"
[[ "$HAS_TLS" == "1" ]] && SCHEME="https"

URL_HOST="localhost"
if [[ "$LAN" == "1" ]]; then
    # Some minimal hosts ship a hostname(1) without -I; prefer `ip` and fall back.
    if command -v ip >/dev/null 2>&1; then
        URL_HOST="$(ip -4 -o addr show scope global 2>/dev/null \
            | awk 'NR == 1 { sub(/\/.*/, "", $4); print $4 }' || true)"
    else
        URL_HOST="$(hostname -I 2>/dev/null | awk '{print $1}' || true)"
    fi
    [[ -z "$URL_HOST" ]] && URL_HOST="localhost"

    # Discovery rides an inbound UDP broadcast on 7241; ufw needs both rules.
    if command -v ufw >/dev/null 2>&1; then
        sudo -n ufw allow "7241/udp" comment 'Doopass discovery' 2>/dev/null \
            && sudo -n ufw allow "$PORT/tcp" comment 'Doopass web/API' 2>/dev/null \
            && echo "Firewall rules added: UDP 7241 (discovery) + TCP $PORT (web/API)." \
            || echo "NOTE: allow inbound UDP 7241 (discovery) and TCP $PORT (web/API) in your firewall."
    else
        echo "NOTE: allow inbound UDP 7241 (discovery) and TCP $PORT (web/API) in your firewall if one is active."
    fi
fi

if [[ "$LAN" == "1" && "$HAS_TLS" == "0" ]]; then
    echo "WARNING: lan mode without TLS - the master password and all secrets travel in clear text." >&2
    echo "         Run ./setup-tls.sh first and install again to serve HTTPS." >&2
fi

echo "Installed and started. Web interface: $SCHEME://$URL_HOST:$PORT"
