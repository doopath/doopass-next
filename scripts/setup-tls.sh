#!/usr/bin/env bash
#
# Creates the local TLS certificate through the bundled executable.
#
# Usage:
#   ./setup-tls.sh                 # ~/doopass/doopass.pfx + random password
#   ./setup-tls.sh --force         # replace an existing certificate
#
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

# Release archive: the executable sits next to this script.
if [[ -x "$script_dir/doopass" ]]; then
    exec "$script_dir/doopass" setup-tls "$@"
fi

# Source checkout: build and run the CLI.
repo_root="$(cd -- "$script_dir/.." && pwd)"
exec dotnet run --project "$repo_root/src/Doopass.Cli/Doopass.Cli.csproj" -- setup-tls "$@"
