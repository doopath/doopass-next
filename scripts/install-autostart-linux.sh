#!/usr/bin/env bash
#
# Installs the Doopass web server as a user systemd service (starts at login).
#
# Usage:
#   ./install-autostart-linux.sh /path/to/doopass [local|lan] [--linger]
#
#   PORT (env) overrides the HTTP port (default 7240).
#   --linger keeps the service running even when no session is active.
#
set -euo pipefail

EXE_PATH="${1:-}"
BIND="${2:-local}"
PORT="${PORT:-7240}"

usage() {
    echo "Usage: $0 /path/to/doopass [local|lan] [--linger]"
    echo "       PORT env var overrides the HTTP port (default 7240)."
    exit 1
}

[[ -n "$EXE_PATH" && -f "$EXE_PATH" ]] || usage

LAN=0
case "$BIND" in
    local|localhost) LAN=0 ;;
    lan) LAN=1 ;;
    *) usage ;;
esac

LINGER=0
[[ "${3:-}" == "--linger" ]] && LINGER=1

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

URL_HOST="localhost"
if [[ "$LAN" == "1" ]]; then
    URL_HOST="$(hostname -I 2>/dev/null | awk '{print $1}')"
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

    echo "NOTE: lan mode is plain HTTP; pair it with --tls for phone clients."
fi

echo "Installed. Web interface: http://$URL_HOST:$PORT"
