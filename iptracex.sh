#!/usr/bin/env bash
# IPTraceX production launcher (Kali/Linux).
#
#   git clone https://github.com/t1-haaaa/IPTraceX.git
#   cd IPTraceX
#   chmod +x iptracex.sh
#   ./iptracex.sh
#
# Resolution order (no .NET SDK, no compilation, no first-run publish):
#   1. $SCRIPT_DIR/IPTraceX               (binary next to the launcher)
#   2. $SCRIPT_DIR/.iptracex/bin/IPTraceX (local cache)
#   3. Official GitHub Release download (HTTPS + SHA256 verified)
#
# The full branding header lives in the binary itself; the launcher prints
# it only while bootstrapping (binary missing), so it is never duplicated.
# All arguments are forwarded: exec "$BINARY" "$@".
#
# Diagnostic overrides (tests/scripting only, never required):
#   IPTraceX_ALWAYS_CLEAR=1  force terminal clear even when not a tty
#   IPTraceX_NO_CLEAR=1       never clear the terminal
set -euo pipefail

SCRIPT_PATH="${BASH_SOURCE[0]:-}"
if [[ -z "$SCRIPT_PATH" ]]; then
  SCRIPT_PATH="$0"
fi
SCRIPT_DIR="$(cd "$(dirname "$SCRIPT_PATH")" && pwd)"

APP="IPTraceX"
APP_VERSION="1.1.0" # keep in sync with src/IPTraceX.Core/AppInfo.cs
ARCHIVE="iptracex-linux-x64.tar.gz"

# Official sources only. Never accept a download URL from the user.
RELEASE_BASE="https://github.com/t1-haaaa/IPTraceX/releases/latest/download"
TARBALL_URL="$RELEASE_BASE/$ARCHIVE"
SHA_URL="$RELEASE_BASE/SHA256SUMS"

CACHE_ROOT="$SCRIPT_DIR/.iptracex"
CACHE_BIN="$CACHE_ROOT/bin/$APP"

log_err() { echo "$@" >&2; }

# --- Colors: tty + no --no-color/NO_COLOR only; never required. ---
USE_COLOR=0
color_setup() {
  USE_COLOR=0
  [[ -t 1 ]] || return 0
  [[ -n "${NO_COLOR:-}" || -n "${IPTraceX_NO_COLOR:-}" ]] && return 0
  local a
  for a in "$@"; do
    [[ "$a" == "--no-color" ]] && return 0
  done
  USE_COLOR=1
}
c_green() { if [[ "$USE_COLOR" -eq 1 ]]; then printf '\033[1;32m%s\033[0m' "$1"; else printf '%s' "$1"; fi; }
c_cyan() { if [[ "$USE_COLOR" -eq 1 ]]; then printf '\033[36m%s\033[0m' "$1"; else printf '%s' "$1"; fi; }
c_yellow() { if [[ "$USE_COLOR" -eq 1 ]]; then printf '\033[1;33m%s\033[0m' "$1"; else printf '%s' "$1"; fi; }

print_header() {
  # print_header <Ready|Preparing...> — ASCII only, 80-column safe.
  local status="$1"
  c_yellow ' ___ ____ _____                    __  __'; echo
  c_yellow '|_ _|  _ \_   _| __ __ _  ___  ___\ \/ /'; echo
  c_yellow $' | | | |_) || | | \'__/ _` |/ __|/ _ \\  /'; echo
  c_yellow ' | | |  __/ | | | | | (_| | (__  __/ /  '$'\x5c'; echo
  c_yellow '|___|_|    |_||_|  \__,_|\___\___/_/\_'$'\x5c'; echo
  echo
  echo 'MULTI-PROVIDER IP INTELLIGENCE & GEOLOCATION CLI'
  echo
  echo "$(c_cyan '[::]') Multi-provider IP intelligence & geolocation CLI"
  echo "$(c_cyan '[::]') Version $APP_VERSION"
  echo "$(c_cyan '[::]') Developer: t1_haaa"
  echo "$(c_green '[+]') Status: $status"
  echo
}

maybe_clear() {
  # Clear the screen only: never history, never dotfiles, never on pipes.
  if [[ -n "${IPTraceX_NO_CLEAR:-}" ]]; then
    return 0
  fi
  if [[ -z "${IPTraceX_ALWAYS_CLEAR:-}" ]] && ! [[ -t 1 ]]; then
    return 0
  fi
  if command -v clear >/dev/null 2>&1; then
    clear || true
  elif [[ "${TERM:-dumb}" != "dumb" ]]; then
    printf '\033[H\033[2J' || true
  fi
}

is_windows() {
  if [[ "${OS:-}" == "Windows_NT" ]]; then
    return 0
  fi
  case "$(uname -s 2>/dev/null || echo Unknown)" in
    MINGW*|MSYS*|CYGWIN*) return 0 ;;
  esac
  return 1
}

is_debug() {
  local a
  for a in "$@"; do
    [[ "$a" == "--debug" ]] && return 0
  done
  return 1
}

debug_line() { echo "[DEBUG] $1" >&2; }

check_arch() {
  # Echo the machine arch, or fail for unsupported ones.
  local arch
  arch="$(uname -m 2>/dev/null || echo unknown)"
  if [[ "$arch" != "x86_64" && "$arch" != "amd64" ]]; then
    log_err "[ERROR] Unsupported architecture: $arch"
    log_err "[!] This release currently provides linux-x64 only."
    return 1
  fi
  echo "$arch"
}

detect_downloader() {
  if command -v curl >/dev/null 2>&1; then
    echo "curl"
  elif command -v wget >/dev/null 2>&1; then
    echo "wget"
  else
    echo ""
  fi
}

download_file() {
  # download_file <url> <dest> <tool> — quiet progress, visible errors.
  local url="$1" dest="$2" downloader="$3"
  case "$url" in
    https://github.com/t1-haaaa/IPTraceX/*) ;;
    *)
      log_err "[ERROR] Refusing untrusted download URL."
      return 1
      ;;
  esac
  if [[ "$downloader" == "curl" ]]; then
    curl -fSL -sS --connect-timeout 15 --max-time 600 --retry 2 -o "$dest" "$url"
  elif [[ "$downloader" == "wget" ]]; then
    wget -q --timeout=15 --tries=3 -O "$dest" "$url"
  else
    log_err "[ERROR] Unable to download IPTraceX."
    log_err "[!] Neither curl nor wget is installed."
    return 1
  fi
}

verify_archive() {
  # verify_archive <archive> <sums> — exit 1 on any mismatch.
  local archive="$1" sums="$2" expected actual
  if ! command -v sha256sum >/dev/null 2>&1; then
    log_err "[ERROR] sha256sum is required to verify the download."
    return 1
  fi
  expected="$(grep -F "$(basename "$archive")" "$sums" 2>/dev/null | awk '{print $1}' | head -n 1)"
  if [[ -z "$expected" || ! "$expected" =~ ^[0-9a-fA-F]{64}$ ]]; then
    log_err "[ERROR] Checksum entry missing for $(basename "$archive")."
    return 1
  fi
  actual="$(sha256sum "$archive" | awk '{print $1}')"
  if [[ "$expected" != "$actual" ]]; then
    log_err "[ERROR] SHA256 mismatch for $(basename "$archive")."
    return 1
  fi
  return 0
}

install_binary() {
  # Download + verify + atomic install into the cache.
  # Never deletes a previously working binary on failure.
  local downloader tmpdir archive sums arch
  downloader="$(detect_downloader)"
  if [[ -z "$downloader" ]]; then
    log_err "[ERROR] Unable to download IPTraceX."
    log_err "[!] Neither curl nor wget is installed."
    return 1
  fi
  arch="$(check_arch)" || return 1
  mkdir -p "$CACHE_ROOT" || {
    log_err "[ERROR] Cannot create cache directory: $CACHE_ROOT"
    return 1
  }
  tmpdir="$(mktemp -d "$CACHE_ROOT/tmp.XXXXXX")"
  if [[ -z "${tmpdir:-}" || ! -d "$tmpdir" ]]; then
    log_err "[ERROR] Cannot create temporary directory under $CACHE_ROOT."
    return 1
  fi
  archive="$tmpdir/$ARCHIVE"
  sums="$tmpdir/SHA256SUMS"
  echo "$(c_cyan '[::]') Downloading official release..."
  if ! download_file "$TARBALL_URL" "$archive" "$downloader"; then
    log_err "[ERROR] Failed to download IPTraceX release."
    rm -rf "$tmpdir"
    return 1
  fi
  echo "$(c_green '[+]') Download complete."
  echo "$(c_cyan '[::]') Verifying release..."
  if ! download_file "$SHA_URL" "$sums" "$downloader"; then
    log_err "[ERROR] Unable to download IPTraceX checksums."
    rm -rf "$tmpdir"
    return 1
  fi
  if ! verify_archive "$archive" "$sums"; then
    log_err "[ERROR] SHA256 verification failed. Deleted untrusted files."
    rm -rf "$tmpdir"
    return 1
  fi
  echo "$(c_green '[+]') SHA256 verified."
  echo "$(c_cyan '[::]') Installing..."
  mkdir -p "$tmpdir/extract"
  tar -xzf "$archive" -C "$tmpdir/extract"
  if [[ ! -f "$tmpdir/extract/$APP" ]]; then
    log_err "[ERROR] Release archive does not contain $APP."
    rm -rf "$tmpdir"
    return 1
  fi
  chmod +x "$tmpdir/extract/$APP"
  mkdir -p "$CACHE_ROOT/bin"
  mv -f "$tmpdir/extract/$APP" "$CACHE_BIN"
  chmod +x "$CACHE_BIN"
  rm -rf "$tmpdir"
  echo "$(c_green '[+]') IPTraceX is ready."
  return 0
}

resolve_binary() {
  if [[ -x "$SCRIPT_DIR/$APP" ]]; then
    echo "$SCRIPT_DIR/$APP"
    return 0
  fi
  if [[ -x "$CACHE_BIN" ]]; then
    echo "$CACHE_BIN"
    return 0
  fi
  return 1
}

run_update() {
  echo "$(c_cyan '[::]') Updating IPTraceX..."
  if ! install_binary; then
    return 1
  fi
  echo "$(c_green '[+]') Update installed successfully."
  return 0
}

main() {
  if is_windows; then
    log_err "[ERROR] IPTraceX Linux binary cannot run on Windows/Git Bash."
    log_err "[!] Please use Kali Linux/WSL/Linux, or run the .NET project directly for development."
    exit 1
  fi

  local debug=0
  if is_debug "$@"; then debug=1; fi
  color_setup "$@"

  # --- Self update: refresh from the official latest release. ---
  if [[ "${1:-}" == "--update" ]]; then
    shift || true
    maybe_clear
    if ! run_update; then
      exit 1
    fi
    if [[ "$#" -eq 0 ]]; then
      "$CACHE_BIN" --version
      exit 0
    fi
    # fall through and execute the fresh binary with the remaining args
  fi

  # Interactive (no arguments): clean branded start.
  if [[ "$#" -eq 0 ]]; then
    maybe_clear
    local binary
    binary="$(resolve_binary || true)"
    if [[ -z "$binary" ]]; then
      print_header "Preparing..."
      if ! install_binary; then
        log_err "[ERROR] IPTraceX binary is unavailable."
        log_err "[!] Platform: Linux $(uname -m 2>/dev/null || echo unknown)"
        log_err "[!] Attempted official release download."
        exit 1
      fi
      binary="$CACHE_BIN"
    else
      print_header "Ready"
    fi
    if [[ "$debug" -eq 1 ]]; then
      debug_line "Platform: Linux"
      debug_line "Architecture: $(uname -m 2>/dev/null || echo unknown)"
      debug_line "Binary: $binary"
      debug_line "Cache: $CACHE_BIN"
    fi
    if [[ ! -x "$binary" ]]; then
      chmod +x "$binary"
    fi
    export IPTRACEX_PROJECT_ROOT="$SCRIPT_DIR"
    export IPTraceX_LAUNCHER_UI=1
    exec "$binary"
  fi

  # Non-interactive: silent resolve, full forwarding (JSON-safe, no extras).
  if [[ "$debug" -eq 1 ]]; then
    debug_line "Platform: Linux"
    debug_line "Architecture: $(uname -m 2>/dev/null || echo unknown)"
    debug_line "Release URL: $TARBALL_URL"
  fi
  local binary
  binary="$(resolve_binary || true)"
  if [[ -z "$binary" ]]; then
    log_err "[ERROR] IPTraceX binary is unavailable."
    log_err "[!] Platform: Linux $(uname -m 2>/dev/null || echo unknown)"
    log_err "[!] Attempted official release download."
    if ! install_binary; then
      exit 1
    fi
    binary="$CACHE_BIN"
    if [[ "$debug" -eq 1 ]]; then
      debug_line "SHA256: verified"
    fi
  fi
  if [[ ! -x "$binary" ]]; then
    chmod +x "$binary"
  fi
  export IPTRACEX_PROJECT_ROOT="$SCRIPT_DIR"
  exec "$binary" "$@"
}

# Allow tests to source this file for its functions without executing.
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
  main "$@"
fi
