#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
local_dotnet="$project_root/.tools/dotnet/dotnet"

export DOTNET_CLI_HOME="$project_root/.tools/dotnet-home"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export NUGET_PACKAGES="$project_root/.nuget/packages"

if [[ -x "$local_dotnet" ]]; then
  exec "$local_dotnet" "$@"
fi

if command -v dotnet >/dev/null 2>&1; then
  exec dotnet "$@"
fi

echo "PaperFormat AI requires .NET SDK 8.0.420." >&2
echo "Run scripts/bootstrap-dotnet.sh, then retry." >&2
exit 127
