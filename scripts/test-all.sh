#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$project_root"

./plugins/paperformat-ai/scripts/dotnet.sh restore PaperFormat.sln --locked-mode
./plugins/paperformat-ai/scripts/dotnet.sh run \
  --project tools/PaperFormat.Fixtures \
  --configuration Release \
  --no-restore \
  -- \
  --output tests/fixtures/generated
./plugins/paperformat-ai/scripts/dotnet.sh build PaperFormat.sln \
  --configuration Release \
  --no-restore \
  -m:1 \
  --disable-build-servers
./plugins/paperformat-ai/scripts/dotnet.sh test PaperFormat.sln \
  --configuration Release \
  --no-build \
  -m:1 \
  --disable-build-servers
./plugins/paperformat-ai/scripts/dotnet.sh format PaperFormat.sln \
  --verify-no-changes \
  --no-restore

PYTHONPYCACHEPREFIX="${TMPDIR:-/tmp}/paperformat-pycache" \
  python3 -m unittest tests/agent_native_structure_test.py
PYTHONPYCACHEPREFIX="${TMPDIR:-/tmp}/paperformat-pycache" \
  python3 -m unittest tests/venue_catalog_test.py
PYTHONPYCACHEPREFIX="${TMPDIR:-/tmp}/paperformat-pycache" \
  python3 -m unittest tests/template_asset_test.py
