#!/usr/bin/env bash
# IPTraceX zero-setup launcher (Kali/Linux x86-64).
#
#   git clone https://github.com/t1-haaaa/IPTraceX.git
#   cd IPTraceX
#   chmod +x iptracex.sh
#   ./iptracex.sh
#
# The repository ships the prebuilt Linux x64 runtime next to this
# launcher (./IPTraceX). Normal startup NEVER downloads, compiles,
# installs, contacts GitHub, or requires the .NET SDK.
#
# Resolution: ./IPTraceX only. If it is missing, fail immediately —
# never download or build anything implicitly.
#
# The full branding header lives in the binary itself; the launcher prints
# it only for errors. All arguments are forwarded: exec "$BINARY" "$@".
#
# Explicit self update only (--update): refresh ./IPTraceX from the
# official GitHub Release (HTTPS + SHA256 verified).
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
APP_VERSION="2.3.0" # keep in sync with src/IPTraceX.Core/AppInfo.cs
ARCHIVE="iptracex-linux-x64.tar.gz"
BINARY="$SCRIPT_DIR/$APP"

# Official sources only. Never accept a download URL from the user.
# Used ONLY by the explicit --update command, never at startup.
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

is_elf_x86_64() {
  # is_elf_x86_64 <file> — magic + class + machine check, no dependencies.
  local file="$1" magic
  [[ -f "$file" ]] || return 1
  magic="$(od -An -tx1 -N20 "$file" 2>/dev/null | tr -d ' \n')" || return 1
  # 7f 45 4c 46 (ELF), 02 (64-bit), 01 (LE), 01 (version), 3e 00 (x86-64).
  [[ "${magic:0:8}" == "7f454c46" && "${magic:8:2}" == "02" && "${magic:36:4}" == "3e00" ]]
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
  # Used ONLY by the explicit --update command, never at startup.
  # Fails fast on HTTP 404/403; maps curl/wget failures to clear messages.
  local url="$1" dest="$2" downloader="$3"
  case "$url" in
    https://github.com/t1-haaaa/IPTraceX/*) ;;
    *)
      log_err "[ERROR] Refusing untrusted download URL."
      return 1
      ;;
  esac
  if [[ "$downloader" == "curl" ]]; then
    local err_file http_code curl_status connect_timeout max_time
    connect_timeout="${IPTRACEX_CONNECT_TIMEOUT:-15}"
    max_time="${IPTRACEX_DOWNLOAD_TIMEOUT:-180}"
    err_file="$(mktemp)" || {
      log_err "[ERROR] Cannot create temporary file."
      return 1
    }
    http_code="$(curl -fSL -sS --connect-timeout "$connect_timeout" --max-time "$max_time" --retry 2 \
      -o "$dest" -w '%{http_code}' "$url" 2>"$err_file")"
    curl_status=$?
    cat "$err_file" >&2
    rm -f "$err_file"
    if [[ "$curl_status" -eq 0 ]]; then
      return 0
    fi
    if [[ "$curl_status" -eq 28 ]]; then
      log_err "[ERROR] Download timed out after ${max_time}s: $(basename "$dest")."
      log_err "[!] Check connectivity and retry."
      return 1
    fi
    case "$http_code" in
      404)
        log_err "[ERROR] Release asset not found (HTTP 404): $(basename "$dest")."
        log_err "[!] The GitHub Release may not be published yet — check for v$APP_VERSION."
        ;;
      403)
        log_err "[ERROR] Download forbidden (HTTP 403): $(basename "$dest")."
        log_err "[!] Possible API rate limiting — wait and retry."
        ;;
      000)
        log_err "[ERROR] Network failure downloading $(basename "$dest")."
        log_err "[!] Check connectivity (connect timeout ${connect_timeout}s) and retry."
        ;;
      *)
        log_err "[ERROR] Download failed (HTTP ${http_code:-unknown}): $(basename "$dest")."
        ;;
    esac
    return 1
  elif [[ "$downloader" == "wget" ]]; then
    local connect_timeout
    connect_timeout="${IPTRACEX_CONNECT_TIMEOUT:-15}"
    wget -q --timeout="$connect_timeout" --tries=3 -O "$dest" "$url" 2>&1 || {
      local st
      st=$?
      if [[ "$st" -eq 8 ]]; then
        log_err "[ERROR] Release asset not found (HTTP 404): $(basename "$dest")."
        log_err "[!] The GitHub Release may not be published yet — check for v$APP_VERSION."
      elif [[ "$st" -eq 4 ]]; then
        log_err "[ERROR] Network failure downloading $(basename "$dest")."
        log_err "[!] Check connectivity and retry."
      else
        log_err "[ERROR] Download failed (wget exit $st): $(basename "$dest")."
      fi
      return 1
    }
    return 0
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
  # Explicit --update only: download + verify + atomic install next to
  # the launcher. Never deletes a working binary on failure.
  local downloader tmpdir archive sums arch
  downloader="$(detect_downloader)"
  if [[ -z "$downloader" ]]; then
    log_err "[ERROR] Unable to download IPTraceX."
    log_err "[!] Neither curl nor wget is installed."
    return 1
  fi
  arch="$(check_arch)" || return 1
  tmpdir="$(mktemp -d "$SCRIPT_DIR/tmp.XXXXXX")"
  if [[ -z "${tmpdir:-}" || ! -d "$tmpdir" ]]; then
    log_err "[ERROR] Cannot create temporary directory under $SCRIPT_DIR."
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
  if ! is_elf_x86_64 "$tmpdir/extract/$APP"; then
    log_err "[ERROR] Downloaded $APP is not a Linux x86-64 executable."
    rm -rf "$tmpdir"
    return 1
  fi
  chmod +x "$tmpdir/extract/$APP"
  mv -f "$tmpdir/extract/$APP" "$BINARY"
  chmod +x "$BINARY"
  rm -rf "$tmpdir"
  echo "$(c_green '[+]') IPTraceX is ready."
  return 0
}

resolve_binary() {
  # Zero-setup: the shipped ./IPTraceX only. No cache, no download.
  if [[ -f "$BINARY" ]]; then
    echo "$BINARY"
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

fail_missing_binary() {
  log_err "[!] IPTraceX binary is missing."
  log_err "[!] Re-clone/re-download the official repository."
  log_err "[!] Expected: $BINARY"
  exit 1
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

  # --- Explicit self update only. Normal startup never downloads. ---
  if [[ "${1:-}" == "--update" ]]; then
    shift || true
    maybe_clear
    if ! run_update; then
      exit 1
    fi
    if [[ "$#" -eq 0 ]]; then
      "$BINARY" --version
      exit 0
    fi
    # fall through and execute the fresh binary with the remaining args
  fi

  check_arch >/dev/null || exit 1

  # Zero-setup resolution: shipped binary or immediate failure.
  local binary
  binary="$(resolve_binary || true)"
  if [[ -z "$binary" ]]; then
    fail_missing_binary
  fi
  if ! is_elf_x86_64 "$binary"; then
    log_err "[ERROR] $binary is not a Linux x86-64 executable."
    fail_missing_binary
  fi

  if [[ "$#" -eq 0 ]]; then
    maybe_clear
    print_header "Ready"
  elif [[ "$debug" -eq 1 ]]; then
    debug_line "Platform: Linux"
    debug_line "Architecture: $(uname -m 2>/dev/null || echo unknown)"
    debug_line "Binary: $binary"
  fi
  if [[ ! -x "$binary" ]]; then
    chmod +x "$binary"
  fi
  export IPTRACEX_PROJECT_ROOT="$SCRIPT_DIR"
  export IPTraceX_LAUNCHER_UI=1
  if [[ "$#" -eq 0 ]]; then
    exec "$binary"
  fi
  exec "$binary" "$@"
}

# Allow tests to source this file for its functions without executing.
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
  main "$@"
fi
