#!/usr/bin/env bash
# IPTraceX production launcher (Kali/Linux).
#
#   cd IPTraceX
#   ./iptracex.sh
#
# Runs the packaged self-contained Linux binary next to this script.
# No .NET SDK, no dotnet CLI, no compilation, no first-run publish.
# The release archive already contains the ready-to-run binary.
set -euo pipefail

SCRIPT_PATH="${BASH_SOURCE[0]:-}"
if [[ -z "$SCRIPT_PATH" ]]; then
  SCRIPT_PATH="$0"
fi
SCRIPT_DIR="$(cd "$(dirname "$SCRIPT_PATH")" && pwd)"

# --- Platform guard: never execute the Linux ELF binary on Windows. ---
IS_WINDOWS=0
if [[ "${OS:-}" == "Windows_NT" ]]; then
  IS_WINDOWS=1
else
  case "$(uname -s 2>/dev/null || echo Unknown)" in
    MINGW*|MSYS*|CYGWIN*) IS_WINDOWS=1 ;;
  esac
fi

if [[ "$IS_WINDOWS" -eq 1 ]]; then
  echo "[!] IPTraceX Linux launcher detected on Windows." >&2
  echo "[!] The bundled binary targets Linux (linux-x64)." >&2
  echo "[!] Run IPTraceX on Kali/Linux, or use the Windows .NET build." >&2
  exit 1
fi
# --- End platform guard. ---

BINARY="$SCRIPT_DIR/IPTraceX"

if [[ ! -f "$BINARY" ]]; then
  echo "[ERROR] IPTraceX Linux binary not found." >&2
  echo "[!] Download the latest Linux release or build it on Linux." >&2
  exit 1
fi

if [[ ! -x "$BINARY" ]]; then
  chmod +x "$BINARY"
fi

export IPTRACEX_PROJECT_ROOT="$SCRIPT_DIR"

exec "$BINARY" "$@"
