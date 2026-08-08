#!/bin/bash
set -e
cd "$(dirname "$0")"
exec python3 Tools/Ollama/tactix_ollama_agent.py "$@"
