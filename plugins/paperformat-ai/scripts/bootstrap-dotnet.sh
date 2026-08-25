#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
install_dir="$project_root/.tools/dotnet"
installer="${TMPDIR:-/tmp}/paperformat-dotnet-install.sh"

case "$(uname -m)" in
  arm64 | aarch64)
    architecture="arm64"
    ;;
  x86_64 | amd64)
    architecture="x64"
    ;;
  *)
    echo "Unsupported processor architecture: $(uname -m)" >&2
    exit 1
    ;;
esac

if [[ -x "$install_dir/dotnet" ]] &&
  [[ "$("$install_dir/dotnet" --version)" == "8.0.420" ]]; then
  echo ".NET SDK 8.0.420 is already available."
  exit 0
fi

mkdir -p "$install_dir"
curl --fail --silent --show-error --location \
  https://dot.net/v1/dotnet-install.sh \
  --output "$installer"
bash "$installer" \
  --version 8.0.420 \
  --architecture "$architecture" \
  --install-dir "$install_dir" \
  --no-path

"$install_dir/dotnet" --version
