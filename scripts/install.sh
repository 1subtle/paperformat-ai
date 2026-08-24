#!/usr/bin/env bash
set -euo pipefail

paperformat_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
install_prefix="${PAPERFORMAT_INSTALL_PREFIX:-${HOME}/.local}"
codex_root="${PAPERFORMAT_INSTALL_CODEX_HOME:-${CODEX_HOME:-${HOME}/.codex}}"
install_skill=true

usage() {
  printf '%s\n' \
    "Install PaperFormat from a cloned checkout." \
    "" \
    "Usage:" \
    "  ./scripts/install.sh [--prefix DIR] [--codex-home DIR] [--no-skill]" \
    "" \
    "Defaults:" \
    "  CLI link:    \$HOME/.local/bin/paperformat" \
    "  Codex Skill: \${CODEX_HOME:-\$HOME/.codex}/skills/paperformat" \
    "" \
    "The checkout remains the installation source. The installer never" \
    "writes credentials or modifies manuscript files."
}

# Install PaperFormat from a cloned checkout.
#
# Usage:
#   ./scripts/install.sh [--prefix DIR] [--codex-home DIR] [--no-skill]
#
# Defaults:
#   CLI link:   $HOME/.local/bin/paperformat
#   Codex Skill: ${CODEX_HOME:-$HOME/.codex}/skills/paperformat
#
# The checkout remains the installation source. Moving or deleting it breaks
# the installed links. The installer never writes credentials or modifies a
# manuscript.

while [[ $# -gt 0 ]]; do
  case "$1" in
    --prefix)
      [[ $# -ge 2 ]] || { echo "--prefix requires a directory." >&2; exit 2; }
      install_prefix="$2"
      shift 2
      ;;
    --codex-home)
      [[ $# -ge 2 ]] || { echo "--codex-home requires a directory." >&2; exit 2; }
      codex_root="$2"
      shift 2
      ;;
    --no-skill)
      install_skill=false
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if ! "$paperformat_root/scripts/dotnet.sh" --version >/dev/null 2>&1; then
  echo "Pinned .NET SDK 8.0.420 is unavailable; bootstrapping it locally."
  "$paperformat_root/scripts/bootstrap-dotnet.sh"
fi

"$paperformat_root/scripts/paperformat" --help >/dev/null

bin_dir="$install_prefix/bin"
launcher_link="$bin_dir/paperformat"
mkdir -p "$bin_dir"
if [[ -e "$launcher_link" && ! -L "$launcher_link" ]]; then
  echo "Refusing to replace non-symlink launcher: $launcher_link" >&2
  exit 2
fi
ln -sfn "$paperformat_root/scripts/paperformat" "$launcher_link"

skill_link=""
if [[ "$install_skill" == true ]]; then
  skill_dir="$codex_root/skills"
  skill_link="$skill_dir/paperformat"
  mkdir -p "$skill_dir"
  if [[ -e "$skill_link" && ! -L "$skill_link" ]]; then
    echo "Refusing to replace non-symlink Skill: $skill_link" >&2
    exit 2
  fi
  ln -sfn "$paperformat_root/skills/paperformat" "$skill_link"
  [[ -f "$skill_link/SKILL.md" ]]
fi

"$launcher_link" --help >/dev/null

echo "PaperFormat CLI installed: $launcher_link"
if [[ -n "$skill_link" ]]; then
  echo "PaperFormat Codex Skill installed: $skill_link"
fi
echo "Add $bin_dir to PATH if it is not already present."
