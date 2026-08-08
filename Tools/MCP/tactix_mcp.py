#!/usr/bin/env python3
"""Minimal stdio MCP adapter for the running TACTIX AI Bridge. No third-party packages."""
import json, os, sys, urllib.request, urllib.parse
from pathlib import Path

PROJECT = Path(os.environ.get("TACTIX_PROJECT_ROOT", os.getcwd())).resolve()
MANIFEST = PROJECT / ".tactix" / "ai-bridge.json"

def bridge():
    data = json.loads(MANIFEST.read_text())
    return data["url"].rstrip("/"), data["token"]

def request(method, path, payload=None):
    base, token = bridge()
    data = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(base + path, data=data, method=method,
        headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=10) as r:
        return json.loads(r.read().decode())

def tools():
    return [
      {"name":"tactix_status","description":"Check whether the running TACTIX editor AI bridge is available.","inputSchema":{"type":"object","properties":{}}},
      {"name":"tactix_context","description":"Read live TACTIX editor/project context.","inputSchema":{"type":"object","properties":{}}},
      {"name":"tactix_list_files","description":"List files/directories inside the TACTIX project.","inputSchema":{"type":"object","properties":{"path":{"type":"string"}}}},
      {"name":"tactix_read_file","description":"Read a text file inside the TACTIX project.","inputSchema":{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}},
      {"name":"tactix_write_file","description":"Write a text file inside the TACTIX project. TACTIX must have Allow edits enabled.","inputSchema":{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}},
      {"name":"tactix_console","description":"Read TACTIX startup/diagnostic console trace.","inputSchema":{"type":"object","properties":{}}},
    ]

def call(name, a):
    if name == "tactix_status": return request("GET","/health")
    if name == "tactix_context": return request("GET","/context")
    if name == "tactix_list_files": return request("GET","/project/list?path="+urllib.parse.quote(a.get("path","")))
    if name == "tactix_read_file": return request("GET","/project/read?path="+urllib.parse.quote(a["path"]))
    if name == "tactix_write_file": return request("POST","/project/write",{"path":a["path"],"content":a["content"]})
    if name == "tactix_console": return request("GET","/console")
    raise ValueError("unknown tool")

def send(obj):
    sys.stdout.write(json.dumps(obj,separators=(",",":"))+"\n"); sys.stdout.flush()

for line in sys.stdin:
    try:
        msg=json.loads(line); mid=msg.get("id"); method=msg.get("method")
        if method == "initialize":
            send({"jsonrpc":"2.0","id":mid,"result":{"protocolVersion":"2025-06-18","capabilities":{"tools":{}},"serverInfo":{"name":"TACTIX","version":"0.1"}}})
        elif method == "notifications/initialized":
            pass
        elif method == "tools/list":
            send({"jsonrpc":"2.0","id":mid,"result":{"tools":tools()}})
        elif method == "tools/call":
            p=msg.get("params",{}); result=call(p.get("name"),p.get("arguments") or {})
            send({"jsonrpc":"2.0","id":mid,"result":{"content":[{"type":"text","text":json.dumps(result,indent=2)}]}})
        elif method == "ping":
            send({"jsonrpc":"2.0","id":mid,"result":{}})
        elif mid is not None:
            send({"jsonrpc":"2.0","id":mid,"error":{"code":-32601,"message":"Method not found"}})
    except Exception as e:
        if 'mid' in locals() and mid is not None:
            send({"jsonrpc":"2.0","id":mid,"error":{"code":-32000,"message":str(e)}})
