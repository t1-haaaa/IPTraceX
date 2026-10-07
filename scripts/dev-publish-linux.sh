#!/usr/bin/env bash
# Developer helper (NOT the production launcher).
# Publishes the self-contained linux-x64 binary and stages it next to
# iptracex.sh so the production launcher can exec it.
#
#   ./scripts/dev-publish-linux.sh
#
# Requires .NET 8 SDK. Never runs on end-user machines.
set -euo pipefail

SCRIPT_PATH="${BASH_SOURCE[0]:-}"
if [[ -z "$SCRIPT_PATH" ]]; then
  SCRIPT_PATH="$0"
fi
SCRIPT_DIR="$(cd "$(dirname "$SCRIPT_PATH")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

export DOTNET_CLI_TELEMETRY_OPTOUT="${DOTNET_CLI_TELEMETRY_OPTOUT:-1}"
export DOTNET_NOLOGO="${DOTNET_NOLOGO:-1}"

dotnet publish "$PROJECT_ROOT/src/IPTraceX.CLI/IPTraceX.CLI.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o "$PROJECT_ROOT/publish/linux-x64"

cp -f "$PROJECT_ROOT/publish/linux-x64/IPTraceX" "$PROJECT_ROOT/IPTraceX"
chmod +x "$PROJECT_ROOT/IPTraceX"
file "$PROJECT_ROOT/IPTraceX" || true
