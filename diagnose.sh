#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT_DIR"

echo "== TACTIX diagnostics =="
echo "Root: $ROOT_DIR"
echo "macOS: $(sw_vers -productVersion 2>/dev/null || echo unknown)"
echo "Arch: $(uname -m)"
if command -v dotnet >/dev/null 2>&1; then
  echo "dotnet: $(dotnet --version)"
  echo "Installed SDKs:"
  dotnet --list-sdks
else
  echo "dotnet: NOT FOUND"
fi

echo
echo "Boot trace:"
if [[ -f /tmp/tactix_boottrace.txt ]]; then
  cat /tmp/tactix_boottrace.txt
else
  echo "(none yet)"
fi
