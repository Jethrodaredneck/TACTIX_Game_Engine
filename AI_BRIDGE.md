# TACTIX AI Bridge

TACTIX now exposes a provider-neutral local bridge while the editor is running.

## Security model
- Binds only to `127.0.0.1`.
- A random bearer token is generated each run.
- Connection details are written to `.tactix/ai-bridge.json` and removed on shutdown.
- Read access is enabled while TACTIX runs.
- Writes are rejected until **Allow edits** is enabled in the AI Bridge editor panel.
- All file access is restricted to the TACTIX project root. `bin`, `obj`, and `.git` are excluded.
- Existing files receive a `.tactix-ai.bak` backup before bridge writes.

## HTTP endpoints
- `GET /health`
- `GET /context`
- `GET /project/list?path=...`
- `GET /project/read?path=...`
- `POST /project/write` JSON: `{ "path": "...", "content": "..." }`
- `GET /console`
- `GET /tools`

All calls require `Authorization: Bearer <token>`.

## MCP adapter
`Tools/MCP/tactix_mcp.py` is a dependency-free stdio MCP adapter. Start TACTIX first and configure an MCP-capable coding agent to launch that script with its working directory (or `TACTIX_PROJECT_ROOT`) set to this project.

This is the foundation hook. Future passes can expose scene selection, entity/property edits, assets, build commands, viewport capture, undoable editor commands, and provider-specific UI without changing the bridge contract.

## Quick test
With TACTIX running:

```bash
./ai_bridge_test.sh
```

A healthy bridge returns JSON containing `"ok": true`.
