#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT_DIR"

"$ROOT_DIR/build.sh"

APP_PATH="$(find "$ROOT_DIR/bin/Release" -type d -name 'TACTIX.app' -print -quit 2>/dev/null || true)"
if [[ -z "$APP_PATH" || ! -d "$APP_PATH" ]]; then
  echo "ERROR: TACTIX.app was not found after publishing." >&2
  exit 1
fi

rm -f /tmp/tactix_boottrace.txt
open "$APP_PATH"

echo "Launched: $APP_PATH"
echo "Boot trace: /tmp/tactix_boottrace.txt"
