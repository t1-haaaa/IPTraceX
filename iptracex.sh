#!/usr/bin/env bash
# IPTraceX launcher — Linux-first, works from any working directory.
#
#   cd IPTraceX
#   ./iptracex.sh
#
# Resolves the project root from this script's own location (never relies
# on pwd), publishes the self-contained linux-x64 binary on first run when
# needed, then execs it with all arguments passed through.
set -euo pipefail

SCRIPT_PATH="${BASH_SOURCE[0]:-}"
if [[ -z "$SCRIPT_PATH" ]]; then
  SCRIPT_PATH="$0"
fi
SCRIPT_DIR="$(cd "$(dirname "$SCRIPT_PATH")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR"

export IPTRACEX_PROJECT_ROOT="$PROJECT_ROOT"
export DOTNET_CLI_TELEMETRY_OPTOUT="${DOTNET_CLI_TELEMETRY_OPTOUT:-1}"
export DOTNET_NOLOGO="${DOTNET_NOLOGO:-1}"

APP="iptracex"
PUBLISH_DIR="$PROJECT_ROOT/publish/linux-x64"
BIN="$PUBLISH_DIR/$APP"

if [[ ! -x "$BIN" ]]; then
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "[ERROR] Self-contained binary not found: $BIN" >&2
    echo "[ERROR] Install .NET 8 SDK once, then re-run this script:" >&2
    echo "        https://aka.ms/dotnet/download (or: sudo apt install dotnet-sdk-8.0)" >&2
    exit 5
  fi
  echo "[::] First run: publishing self-contained linux-x64 binary..." >&2
  dotnet publish "$PROJECT_ROOT/src/IPTraceX.CLI/IPTraceX.CLI.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o "$PUBLISH_DIR" >&2
  chmod +x "$BIN"
fi

exec "$BIN" "$@"
