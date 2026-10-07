#!/usr/bin/env bash
# IPTraceX production launcher (Kali/Linux).
#
#   git clone https://github.com/t1-haaaa/IPTraceX.git
#   cd IPTraceX
#   chmod +x iptracex.sh
#   ./iptracex.sh
#
# Resolution order (no .NET SDK, no compilation, no first-run publish):
#   1. $SCRIPT_DIR/IPTraceX            (binary next to the launcher)
#   2. $SCRIPT_DIR/.iptracex/bin/IPTraceX  (local cache)
#   3. Official GitHub Release download (HTTPS + SHA256 verified)
#
# All arguments are forwarded: exec "$BINARY" "$@".
set -euo pipefail

SCRIPT_PATH="${BASH_SOURCE[0]:-}"
if [[ -z "$SCRIPT_PATH" ]]; then
  SCRIPT_PATH="$0"
fi
SCRIPT_DIR="$(cd "$(dirname "$SCRIPT_PATH")" && pwd)"

APP="IPTraceX"
ARCHIVE="iptracex-linux-x64.tar.gz"

# Official sources only. Never accept a download URL from the user.
RELEASE_BASE="https://github.com/t1-haaaa/IPTraceX/releases/latest/download"
TARBALL_URL="$RELEASE_BASE/$ARCHIVE"
SHA_URL="$RELEASE_BASE/SHA256SUMS"

CACHE_ROOT="$SCRIPT_DIR/.iptracex"
CACHE_BIN="$CACHE_ROOT/bin/$APP"

log_err() { echo "$@" >&2; }

is_windows() {
  if [[ "${OS:-}" == "Windows_NT" ]]; then
    return 0
  fi
  case "$(uname -s 2>/dev/null || echo Unknown)" in
    MINGW*|MSYS*|CYGWIN*) return 0 ;;
  esac
  return 1
}

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
  # download_file <url> <dest> — HTTPS + official repo only, visible errors.
  local url="$1" dest="$2" downloader="$3"
  case "$url" in
    https://github.com/t1-haaaa/IPTraceX/*) ;;
    *)
      log_err "[ERROR] Refusing untrusted download URL."
      return 1
      ;;
  esac
  if [[ "$downloader" == "curl" ]]; then
    curl -fSL --connect-timeout 15 --max-time 600 --retry 2 -o "$dest" "$url"
  elif [[ "$downloader" == "wget" ]]; then
    wget --timeout=15 --tries=3 -O "$dest" "$url"
  else
    log_err "[ERROR] Unable to download IPTraceX."
    log_err "[!] Neither curl nor wget is installed."
    return 1
  fi
}

verify_archive() {
  # verify_archive <archive> <sums> <workdir> — exit 1 on any mismatch.
  # Compares hash fields only (separators differ: "  " vs " *").
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
    log_err "[!] expected: $expected"
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
  tmpdir="$(mktemp -d "$CACHE_ROOT/tmp.XXXXXX")"
  archive="$tmpdir/$ARCHIVE"
  sums="$tmpdir/SHA256SUMS"
  if ! download_file "$TARBALL_URL" "$archive" "$downloader"; then
    log_err "[ERROR] Unable to download IPTraceX."
    log_err "[!] Check your internet connection or use an offline binary."
    rm -rf "$tmpdir"
    return 1
  fi
  if ! download_file "$SHA_URL" "$sums" "$downloader"; then
    log_err "[ERROR] Unable to download IPTraceX checksums."
    rm -rf "$tmpdir"
    return 1
  fi
  if ! verify_archive "$archive" "$sums" "$tmpdir"; then
    log_err "[ERROR] SHA256 verification failed. Deleted untrusted files."
    rm -rf "$tmpdir"
    return 1
  fi
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
  echo "[::] Updating IPTraceX from the official release..."
  if ! install_binary; then
    return 1
  fi
  echo "[+] IPTraceX updated: $CACHE_BIN"
  return 0
}

main() {
  if is_windows; then
    log_err "[ERROR] IPTraceX Linux binary cannot run on Windows/Git Bash."
    log_err "[!] Please use Kali Linux/WSL/Linux, or run the .NET project directly for development."
    exit 1
  fi

  # --- Self update: refresh from the official latest release. ---
  if [[ "${1:-}" == "--update" ]]; then
    shift || true
    if ! run_update; then
      exit 1
    fi
    if [[ "$#" -eq 0 ]]; then
      "$CACHE_BIN" --version
      exit 0
    fi
    # fall through and execute the fresh binary with the remaining args
  fi

  local binary
  binary="$(resolve_binary || true)"
  if [[ -z "$binary" ]]; then
    echo "[::] IPTraceX binary not found locally. Downloading official release..."
    if ! install_binary; then
      log_err "[ERROR] IPTraceX binary is unavailable."
      log_err "[!] Platform: Linux $(uname -m 2>/dev/null || echo unknown)"
      log_err "[!] Attempted official release download."
      exit 1
    fi
    binary="$CACHE_BIN"
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
