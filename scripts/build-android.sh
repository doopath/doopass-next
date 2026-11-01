#!/usr/bin/env bash
# Build helper for the Android thin client (Linux/macOS counterpart of
# build-android.ps1). Requires the Android SDK and a JDK 17+:
#   ANDROID_HOME (or ANDROID_SDK_ROOT)  e.g. ~/Android/Sdk
#                                       with platforms;android-36, build-tools;36.0.0
#   JAVA_HOME                           e.g. ~/.local/opt/jdk-21
# Usage:
#   scripts/build-android.sh                 # Debug APK
#   scripts/build-android.sh -c Release      # Release APK
#   scripts/build-android.sh -c Release -f aab
set -euo pipefail

configuration='Debug'
package_format='apk'

usage() {
    sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'
}

while getopts ':c:f:h' opt; do
    case "$opt" in
        c) configuration=$OPTARG ;;
        f) package_format=$OPTARG ;;
        h) usage; exit 0 ;;
        *) echo "Unknown option: -$OPTARG" >&2; usage >&2; exit 2 ;;
    esac
done

case "$package_format" in
    apk | aab) ;;
    *) echo "-f must be 'apk' or 'aab'" >&2; exit 2 ;;
esac

if [[ -z "${ANDROID_HOME:-}${ANDROID_SDK_ROOT:-}" ]]; then
    echo "Set ANDROID_HOME (or ANDROID_SDK_ROOT), e.g. export ANDROID_HOME=\"\$HOME/Android/Sdk\"." >&2
    exit 1
fi
if [[ -z "${JAVA_HOME:-}" ]]; then
    echo "Set JAVA_HOME to a JDK 17+ install (e.g. \"\$HOME/.local/opt/jdk-21\")." >&2
    exit 1
fi

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
project="$repo_root/src/Doopass.Mobile/Doopass.Mobile.csproj"

exec dotnet build "$project" -c "$configuration" -f net10.0-android \
    "-p:AndroidPackageFormat=$package_format" -p:AndroidEnableMultiDex=true
