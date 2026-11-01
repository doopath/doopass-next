#!/usr/bin/env bash
#
# Builds everything that belongs in a GitHub release:
#   dist/github-release/doopass-<version>-win-x64.zip
#   dist/github-release/doopass-<version>-linux-x64.tar.gz
#   dist/github-release/doopass-<version>-android.apk
#   dist/github-release/SHA256SUMS
#
# Each portable archive contains the self-contained executable, setup-tls,
# install-autostart, uninstall-autostart and a short README.
#
# Usage:
#   ./publish-release.sh
#   ./publish-release.sh -c Debug
#   ./publish-release.sh --skip-android
#
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
CONFIGURATION="Release"
BUILD_ANDROID=1

usage() {
    cat <<EOF
Usage: $(basename "$0") [options]

  -c, --configuration <name>   Build configuration (default: Release)
      --skip-android           Do not build the APK
  -h, --help                   Show this help
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -c|--configuration) CONFIGURATION="$2"; shift 2 ;;
        -c=*|--configuration=*) CONFIGURATION="${1#*=}"; shift ;;
        --skip-android) BUILD_ANDROID=0; shift ;;
        -h|--help) usage; exit 0 ;;
        *) usage >&2; echo "error: unknown argument: $1" >&2; exit 2 ;;
    esac
done

VERSION="$(sed -n 's/.*Version = "\([^"]*\)".*/\1/p' "$REPO_ROOT/src/Doopass.Core/Constants.cs" | head -1)"
[[ -n "$VERSION" ]] || { echo "error: could not read the version from Constants.cs" >&2; exit 1; }

PORTABLE_ROOT="$REPO_ROOT/dist/portable"
RELEASE_ROOT="$REPO_ROOT/dist/github-release"

command -v dotnet >/dev/null 2>&1 || { echo "error: dotnet SDK not found in PATH" >&2; exit 1; }
command -v tar >/dev/null 2>&1 || { echo "error: tar not found" >&2; exit 1; }

zip_dir() {
    local source="$1" destination="$2"
    rm -f -- "$destination"

    if command -v zip >/dev/null 2>&1; then
        (cd "$source" && zip -qr "$destination" .)
    elif command -v python3 >/dev/null 2>&1; then
        (cd "$source" && python3 -m zipfile -c "$destination" .)
    else
        echo "error: zip or python3 is required to build the Windows archive" >&2
        exit 1
    fi
}

rm -rf -- "$RELEASE_ROOT"
mkdir -p "$RELEASE_ROOT"

echo
echo "Building Doopass $VERSION ($CONFIGURATION)"
"$SCRIPT_DIR/publish-portable.sh" -c "$CONFIGURATION" -o "$PORTABLE_ROOT"

if [[ "$BUILD_ANDROID" == "1" ]]; then
    echo
    echo "Building the Android APK"
    "$SCRIPT_DIR/build-android.sh" -c "$CONFIGURATION"

    apk="$REPO_ROOT/src/Doopass.Mobile/bin/$CONFIGURATION/net10.0-android/io.github.doopath.doopass-Signed.apk"
    [[ -f "$apk" ]] || { echo "error: APK not found at $apk" >&2; exit 1; }
    cp -- "$apk" "$RELEASE_ROOT/doopass-$VERSION-android.apk"
fi

stage_root="$(mktemp -d)"
trap 'rm -rf -- "$stage_root"' EXIT

# --- Windows archive -------------------------------------------------------
win_stage="$stage_root/win-x64"
mkdir -p "$win_stage"
cp "$PORTABLE_ROOT/win-x64/doopass.exe" "$win_stage/doopass.exe"
cp "$SCRIPT_DIR/setup-tls.ps1" "$win_stage/"
cp "$SCRIPT_DIR/install-autostart-win.ps1" "$win_stage/"
cp "$SCRIPT_DIR/uninstall-autostart-win.ps1" "$win_stage/"

cat > "$win_stage/README.txt" <<EOF
Doopass $VERSION (win-x64) - portable, self-contained
=====================================================

Quick start
-----------
1. Run setup-tls.ps1 (creates the HTTPS certificate and stores it in settings):
     powershell -ExecutionPolicy Bypass -File .\setup-tls.ps1

2. Install the background service and start it immediately:
     powershell -ExecutionPolicy Bypass -File .\install-autostart-win.ps1

   With the certificate from step 1 the service serves HTTPS on the LAN and
   starts at every sign-in. The command prints the web address.

3. Open that address, accept the self-signed certificate and initialize the
   vault.

Android phone (optional)
------------------------
Install the APK from the same GitHub release. Keep the phone on the same home
Wi-Fi, pick the discovered host, compare the SHA-256 fingerprint with the
startup log, then tap "Trust and connect".

Manual run
----------
    .\doopass.exe                      # terminal UI
    .\doopass.exe serve --port 7240    # http://localhost:7240
    .\doopass.exe serve --port 7240 --lan --tls mycert.pfx --tls-pass mypass

Remove the service
------------------
    powershell -ExecutionPolicy Bypass -File .\uninstall-autostart-win.ps1

Security
--------
Only run the host on your own trusted home network. Do not use public, cafe or
hotel Wi-Fi, a shared/corporate LAN, or forward the port from the internet.
EOF

zip_dir "$win_stage" "$RELEASE_ROOT/doopass-$VERSION-win-x64.zip"

# --- Linux archive ---------------------------------------------------------
linux_stage="$stage_root/linux-x64"
mkdir -p "$linux_stage"
cp "$PORTABLE_ROOT/linux-x64/doopass" "$linux_stage/doopass"
cp "$SCRIPT_DIR/setup-tls.sh" "$linux_stage/"
cp "$SCRIPT_DIR/install-autostart-linux.sh" "$linux_stage/"
cp "$SCRIPT_DIR/uninstall-autostart-linux.sh" "$linux_stage/"
chmod +x "$linux_stage/doopass" "$linux_stage"/*.sh

cat > "$linux_stage/README.txt" <<EOF
Doopass $VERSION (linux-x64) - portable, self-contained
=======================================================

Quick start
-----------
1. Run setup-tls.sh (creates the HTTPS certificate and stores it in settings):
     ./setup-tls.sh

2. Install the systemd user service and start it immediately:
     ./install-autostart-linux.sh

   With the certificate from step 1 the service serves HTTPS on the LAN and
   starts at login. The command prints the web address.

3. Open that address, accept the self-signed certificate and initialize the
   vault.

Android phone (optional)
------------------------
Install the APK from the same GitHub release. Keep the phone on the same home
Wi-Fi, pick the discovered host, compare the SHA-256 fingerprint with the
startup log, then tap "Trust and connect".

Manual run
----------
    ./doopass                          # terminal UI
    ./doopass serve --port 7240        # http://localhost:7240
    ./doopass serve --port 7240 --lan --tls mycert.pfx --tls-pass mypass

Remove the service
------------------
    ./uninstall-autostart-linux.sh

Security
--------
Only run the host on your own trusted home network. Do not use public, cafe or
hotel Wi-Fi, a shared/corporate LAN, or forward the port from the internet.
EOF

tar -czf "$RELEASE_ROOT/doopass-$VERSION-linux-x64.tar.gz" -C "$linux_stage" .

# --- Checksums -------------------------------------------------------------
(
    cd "$RELEASE_ROOT"
    sha256sum doopass-* > SHA256SUMS
)

echo
echo "Release artifacts in $RELEASE_ROOT:"
ls -lh "$RELEASE_ROOT"
