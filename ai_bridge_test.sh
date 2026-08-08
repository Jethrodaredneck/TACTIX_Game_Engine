#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MANIFEST="$ROOT_DIR/.tactix/ai-bridge.json"
if [[ ! -f "$MANIFEST" ]]; then
  echo "TACTIX AI Bridge manifest not found. Start TACTIX first." >&2
  exit 1
fi
python3 - "$MANIFEST" <<'PY'
import json, sys, urllib.request
m=json.load(open(sys.argv[1]))
req=urllib.request.Request(m['url'].rstrip('/')+'/health',headers={'Authorization':'Bearer '+m['token']})
print(urllib.request.urlopen(req,timeout=5).read().decode())
PY
