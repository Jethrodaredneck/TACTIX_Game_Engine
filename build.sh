#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT_DIR"

TFM="net9.0-macos15.0"
RID="osx-arm64"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: dotnet was not found in PATH." >&2
  echo "Install the .NET 9 SDK, then run ./build.sh again." >&2
  exit 127
fi

echo "Using: $(dotnet --version)"

rm -rf "$ROOT_DIR/bin" "$ROOT_DIR/obj"

dotnet publish "$ROOT_DIR/TACTIX.csproj" \
  -c Release \
  -f "$TFM" \
  -r "$RID" \
  -p:CreatePackage=false

APP_PATH="$(find "$ROOT_DIR/bin/Release" -type d -name 'TACTIX.app' -print -quit 2>/dev/null || true)"
if [[ -z "$APP_PATH" || ! -d "$APP_PATH" ]]; then
  echo "ERROR: Build completed but TACTIX.app was not found under bin/Release." >&2
  find "$ROOT_DIR/bin/Release" -maxdepth 8 -type f -name TACTIX -print 2>/dev/null || true
  exit 1
fi

/usr/bin/codesign --force --deep --sign - "$APP_PATH" >/dev/null 2>&1 || true
/usr/bin/xattr -dr com.apple.quarantine "$APP_PATH" >/dev/null 2>&1 || true

echo "Published: $APP_PATH"
