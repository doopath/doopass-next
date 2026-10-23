#!/usr/bin/env bash
#
# Removes the Doopass systemd service and (optionally) the installed binary.
#
# Usage:
#   ./uninstall-autostart-linux.sh [--keep-files]
#
set -euo pipefail

KEEP=0
[[ "${1:-}" == "--keep-files" ]] && KEEP=1

systemctl --user disable --now doopass.service 2>/dev/null || true
rm -f "$HOME/.config/systemd/user/doopass.service"
systemctl --user daemon-reload 2>/dev/null || true

if [[ "$KEEP" == "0" ]]; then
    rm -f "$HOME/.local/bin/doopass"
fi

echo "Removed."
