#!/usr/bin/env bash
#
# Builds self-contained, single-file portable executables for Doopass.
#
# Produces one executable per runtime identifier (default: win-x64 and linux-x64).
# All managed assemblies, the .NET runtime and the web static assets are bundled
# into a single file (extracted to a temp folder at runtime).
#
# Usage:
#   ./publish-portable.sh
#   ./publish-portable.sh -r linux-x64
#   ./publish-portable.sh -c Debug -r linux-x64,win-x64 -o /tmp/portable
#
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
PROJECT="$REPO_ROOT/src/Doopass.Cli/Doopass.Cli.csproj"

CONFIGURATION="Release"
OUTPUT_ROOT=""
RIDS=()
ORIGINAL_ARGS=("$@")

usage() {
    cat <<EOF
Usage: $(basename "$0") [options]

  -c, --configuration <name>   Build configuration (default: Release)
  -r, --rid <rid[,rid]>        Runtime identifier(s), repeatable
                               (default: win-x64,linux-x64)
  -o, --output <dir>           Output root (default: dist/portable)
  -h, --help                   Show this help
EOF
}

die() {
    echo "error: $*" >&2
    exit 1
}

# NETSDK1226 means the SDK has no ASP.NET Core targeting pack, so it cannot
# resolve prune data for Microsoft.AspNetCore.App (distro SDKs ship ASP.NET Core
# as a separate package). Explain the fix instead of dumping the raw MSBuild error.
hint_missing_aspnet_pack() {
    local sdk_version
    sdk_version="$(dotnet --version 2>/dev/null | tail -1)"

    cat >&2 <<EOF

  The .NET SDK ${sdk_version} cannot find the ASP.NET Core targeting pack.
  Install the package matching your SDK, then re-run this script:

    Arch / Omarchy    sudo pacman -S aspnet-runtime aspnet-targeting-pack
    Debian / Ubuntu   sudo apt install aspnetcore-targeting-pack-10.0 aspnetcore-runtime-10.0
    Fedora            sudo dnf install aspnetcore-targeting-pack-10.0 aspnetcore-runtime-10.0

  No root? Let restore fetch the reference pack from NuGet instead:

    AllowMissingPrunePackageData=true $0 ${ORIGINAL_ARGS[*]}

EOF
}

# Appends comma-separated runtime identifiers to RIDS.
add_rids() {
    local value="$1" rid
    IFS=',' read -ra parts <<< "$value"
    for rid in "${parts[@]}"; do
        [[ -n "$rid" ]] && RIDS+=("$rid")
    done
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -c|--configuration)
            [[ $# -ge 2 ]] || die "$1 needs a value"
            CONFIGURATION="$2"
            shift 2
            ;;
        -c=*|--configuration=*)
            CONFIGURATION="${1#*=}"
            shift
            ;;
        -r|--rid|--runtime-identifier)
            [[ $# -ge 2 ]] || die "$1 needs a value"
            add_rids "$2"
            shift 2
            ;;
        -r=*|--rid=*|--runtime-identifier=*)
            add_rids "${1#*=}"
            shift
            ;;
        -o|--output)
            [[ $# -ge 2 ]] || die "$1 needs a value"
            OUTPUT_ROOT="$2"
            shift 2
            ;;
        -o=*|--output=*)
            OUTPUT_ROOT="${1#*=}"
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            usage >&2
            die "unknown argument: $1"
            ;;
    esac
done

[[ ${#RIDS[@]} -gt 0 ]] || RIDS=('win-x64' 'linux-x64')
[[ -n "$OUTPUT_ROOT" ]] || OUTPUT_ROOT="$REPO_ROOT/dist/portable"

command -v dotnet >/dev/null 2>&1 || die "dotnet SDK not found in PATH"

if [[ -t 1 ]]; then
    C_CYAN=$'\033[36m'
    C_GREEN=$'\033[32m'
    C_DIM=$'\033[2m'
    C_RESET=$'\033[0m'
else
    C_CYAN=""
    C_GREEN=""
    C_DIM=""
    C_RESET=""
fi

mkdir -p "$OUTPUT_ROOT"
OUTPUT_ROOT="$(cd -- "$OUTPUT_ROOT" && pwd)"

for rid in "${RIDS[@]}"; do
    out_dir="$OUTPUT_ROOT/$rid"
    [[ -d "$out_dir" ]] && rm -rf -- "$out_dir"

    echo
    echo "${C_CYAN}Publishing $rid -> $out_dir${C_RESET}"

    log_file="$(mktemp)"
    if ! dotnet publish "$PROJECT" \
        -c "$CONFIGURATION" \
        -r "$rid" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:IncludeAllContentForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -p:DebugType=None \
        -p:DebugSymbols=false \
        -o "$out_dir" 2>&1 | tee "$log_file"
    then
        if grep -q 'NETSDK1226' "$log_file"; then
            hint_missing_aspnet_pack
        fi
        rm -f -- "$log_file"
        die "dotnet publish failed for $rid"
    fi
    rm -f -- "$log_file"

    # Cross-publishing does not set the executable bit on Linux binaries.
    if [[ "$rid" == linux-* && -f "$out_dir/doopass" ]]; then
        chmod +x "$out_dir/doopass"
    fi
done

echo
echo "${C_GREEN}Done. Portable executables:${C_RESET}"
while IFS= read -r -d '' file; do
    size_mb="$(awk -v bytes="$(stat -c %s -- "$file")" 'BEGIN { printf "%.1f", bytes / 1048576 }')"
    echo "  $file  ($size_mb MB)"
done < <(find "$OUTPUT_ROOT" -type f \( -name 'doopass' -o -name 'doopass.exe' \) -print0 | sort -z)

echo
echo "${C_DIM}On Linux run: chmod +x ./doopass${C_RESET}"
