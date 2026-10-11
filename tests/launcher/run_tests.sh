#!/usr/bin/env bash
# IPTraceX launcher behavior tests (bash, no framework).
# Portable: runs on Linux and Windows Git Bash/MSYS.
#
#   ./tests/launcher/run_tests.sh
#
# Network is used ONLY by the explicitly marked download tests.
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
LAUNCHER="$REPO/iptracex.sh"

PASS=0
FAIL=0

pass() { PASS=$((PASS + 1)); echo "PASS: $1"; }
fail() { FAIL=$((FAIL + 1)); echo "FAIL: $1"; }

is_windows_env() {
  if [[ "${OS:-}" == "Windows_NT" ]]; then return 0; fi
  case "$(uname -s 2>/dev/null || echo Unknown)" in
    MINGW*|MSYS*|CYGWIN*) return 0 ;;
  esac
  return 1
}

# --- 1. Missing binary fails immediately without downloading. ---
t_missing_binary_fast() {
  if is_windows_env; then
    echo "SKIP: missing binary (Windows takes platform path first)"
    return
  fi
  local dir; dir="$(mktemp -d)"
  cp "$LAUNCHER" "$dir/iptracex.sh"
  mkdir -p "$dir/fakebin"
  printf '#!/usr/bin/env bash\necho "NETWORK-USED" >&2\nexit 1\n' > "$dir/fakebin/curl"
  printf '#!/usr/bin/env bash\necho "NETWORK-USED" >&2\nexit 1\n' > "$dir/fakebin/wget"
  chmod +x "$dir/fakebin/curl" "$dir/fakebin/wget"
  local out code
  set +e
  out="$(PATH="$dir/fakebin:$PATH" "$dir/iptracex.sh" --version 2>&1)"; code=$?
  set -e
  if [[ "$code" -ne 0 && "$out" == *"binary is missing"* \
    && "$out" == *"Re-clone/re-download"* && "$out" != *"NETWORK-USED"* ]]; then
    pass "missing binary fails fast without network"
  else
    fail "missing binary (out='$out' code=$code)"
  fi
  rm -rf "$dir"
}

# --- 2. Shipped binary resolves immediately (sourced, offline). ---
t_shipped_binary_resolved() {
  # shellcheck disable=SC1090 # dynamic source of launcher under test
  source "$LAUNCHER" <<<"" >/dev/null 2>&1 || true
  local dir; dir="$(mktemp -d)"
  echo "x" > "$dir/IPTraceX"
  local saved_binary="$BINARY" got
  BINARY="$dir/IPTraceX"
  got="$(resolve_binary || true)"
  BINARY="$saved_binary"
  if [[ "$got" == "$dir/IPTraceX" ]]; then
    pass "shipped binary resolves without download"
  else
    fail "shipped binary resolution (got='$got')"
  fi
  rm -rf "$dir"
}

# --- 2b. ELF validation accepts real ELF, rejects scripts. ---
t_elf_validation() {
  # shellcheck disable=SC1090 # dynamic source of launcher under test
  source "$LAUNCHER" <<<"" >/dev/null 2>&1 || true
  local dir; dir="$(mktemp -d)"
  echo "not-elf" > "$dir/fake.txt"
  if is_elf_x86_64 "$dir/fake.txt" 2>/dev/null; then
    fail "elf check accepted text file"
    rm -rf "$dir"
    return
  else
    pass "elf check rejects non-ELF"
  fi
  if is_windows_env; then
    echo "SKIP: elf accept (no native ELF handy on Windows)"
    rm -rf "$dir"
    return
  fi
  if is_elf_x86_64 "/bin/ls" 2>/dev/null || is_elf_x86_64 "/usr/bin/ls" 2>/dev/null; then
    pass "elf check accepts real x86-64 binary"
  else
    fail "elf check rejected system binary"
  fi
  rm -rf "$dir"
}

# --- 3. Checksum verification unit (sourced functions, offline). ---
t_checksum_verify() {
  # shellcheck disable=SC1090 # dynamic source of launcher under test
  source "$LAUNCHER" <<<"" >/dev/null 2>&1 || true
  local dir; dir="$(mktemp -d)"
  echo "hello" > "$dir/a.txt"
  ( cd "$dir" && sha256sum a.txt > SHA256SUMS )
  if verify_archive "$dir/a.txt" "$dir/SHA256SUMS" "$dir"; then
    pass "sha256 verify accepts good file"
  else
    fail "sha256 verify rejected good file"
  fi
  echo "tampered" > "$dir/a.txt"
  if verify_archive "$dir/a.txt" "$dir/SHA256SUMS" "$dir" >/dev/null 2>&1; then
    fail "sha256 verify accepted tampered file"
  else
    pass "sha256 verify rejects tampered file"
  fi
  rm -rf "$dir"
}

# --- 4. Untrusted URL refusal (offline). ---
t_untrusted_url_refused() {
  # shellcheck disable=SC1090 # dynamic source of launcher under test
  source "$LAUNCHER" <<<"" >/dev/null 2>&1 || true
  if download_file "http://evil.example/x" "$HOME/nope" "curl" >/dev/null 2>&1; then
    fail "untrusted URL accepted"
  else
    pass "untrusted URL refused without network"
  fi
}

# --- 5. Header content: logo, version, developer (portable, no exec). ---
t_header_content() {
  # shellcheck disable=SC1090 # dynamic source of launcher under test
  source "$LAUNCHER" <<<"" >/dev/null 2>&1 || true
  local out
  out="$(print_header "Ready")"
  if [[ "$out" == *"MULTI-PROVIDER IP INTELLIGENCE"* \
    && "$out" == *"Version $APP_VERSION"* \
    && "$out" == *"Developer: t1_haaa"* \
    && "$out" == *"Status: Ready"* ]]; then
    pass "header has logo, version and developer"
  else
    fail "header content"
  fi
  local width
  width="$(printf '%s' "$out" | awk '{ if (length > m) m = length } END { print m + 0 }')"
  if [[ "$width" -le 80 ]]; then
    pass "header fits 80 columns"
  else
    fail "header too wide ($width)"
  fi
}

# --- 6. Quiet download flags: curl must stay silent on progress. ---
t_curl_quiet_flags() {
  # shellcheck disable=SC1090 # dynamic source of launcher under test
  source "$LAUNCHER" <<<"" >/dev/null 2>&1 || true
  local dir; dir="$(mktemp -d)"
  mkdir -p "$dir/fakebin"
  cat > "$dir/fakebin/curl" <<'EOF'
#!/usr/bin/env bash
echo "CURL-ARGS: $*" >> "$CURL_LOG"
echo "  % Total    % Received  (fake progress)"
: > "$CURL_DEST"
EOF
  chmod +x "$dir/fakebin/curl"
  export CURL_LOG="$dir/args.log" CURL_DEST="$dir/out.bin"
  PATH="$dir/fakebin:$PATH" download_file \
    "https://github.com/t1-haaaa/IPTraceX/releases/latest/download/x" \
    "$dir/out.bin" "curl" >/dev/null 2>&1 || true
  if grep -q "\-sS" "$dir/args.log" && ! grep -q "progress" "$dir/args.log"; then
    pass "curl invoked quiet (-sS, no progress flags)"
  else
    fail "curl flags ($(cat "$dir/args.log"))"
  fi
  rm -rf "$dir"
  unset CURL_LOG CURL_DEST
}

# --- 7. Download errors stay visible. ---
t_download_error_visible() {
  # shellcheck disable=SC1090 # dynamic source of launcher under test
  source "$LAUNCHER" <<<"" >/dev/null 2>&1 || true
  local dir; dir="$(mktemp -d)"
  mkdir -p "$dir/fakebin"
  printf '#!/usr/bin/env bash\necho "curl: (6) Could not resolve host" >&2\nexit 6\n' \
    > "$dir/fakebin/curl"
  chmod +x "$dir/fakebin/curl"
  local out code
  set +e
  out="$(PATH="$dir/fakebin:$PATH" download_file \
    "https://github.com/t1-haaaa/IPTraceX/releases/latest/download/x" \
    "$dir/out.bin" "curl" 2>&1)"; code=$?
  set -e
  if [[ "$code" -ne 0 && "$out" == *"Could not resolve host"* ]]; then
    pass "download errors remain visible"
  else
    fail "download error visibility (out='$out' code=$code)"
  fi
  rm -rf "$dir"
}

# --- 8. Clear behavior: called when forced, silent when unavailable. ---
t_clear_behavior() {
  # shellcheck disable=SC1090 # dynamic source of launcher under test
  source "$LAUNCHER" <<<"" >/dev/null 2>&1 || true
  local dir; dir="$(mktemp -d)"
  mkdir -p "$dir/fakebin"
  # shellcheck disable=SC2016 # $CLEAR_MARKER must expand when the fake runs, not now
  printf '#!/usr/bin/env bash\ntouch "$CLEAR_MARKER"\n' > "$dir/fakebin/clear"
  chmod +x "$dir/fakebin/clear"
  export CLEAR_MARKER="$dir/cleared"
  if IPTraceX_ALWAYS_CLEAR=1 PATH="$dir/fakebin:$PATH" maybe_clear \
    && [[ -f "$dir/cleared" ]]; then
    pass "clear invoked when forced"
  else
    fail "clear not invoked"
  fi
  rm -f "$dir/cleared"
  if IPTraceX_NO_CLEAR=1 IPTraceX_ALWAYS_CLEAR=1 PATH="$dir/fakebin:$PATH" maybe_clear \
    && [[ ! -f "$dir/cleared" ]]; then
    pass "IPTraceX_NO_CLEAR suppresses clear"
  else
    fail "no-clear override"
  fi
  unset CLEAR_MARKER
  rm -rf "$dir"
}

# --- 9. Arch gate via fake uname (Linux only). ---
t_arch_gate() {
  if is_windows_env; then
    echo "SKIP: arch gate (Windows takes platform path)"
    return
  fi
  local dir fakebin; dir="$(mktemp -d)"; fakebin="$dir/fakebin"
  mkdir -p "$fakebin"
  printf '#!/usr/bin/env bash\necho aarch64\n' > "$fakebin/uname"
  chmod +x "$fakebin/uname"
  # shellcheck disable=SC1090 # dynamic source of launcher under test
  source "$LAUNCHER" <<<"" >/dev/null 2>&1 || true
  local out
  out="$(PATH="$fakebin:$PATH" check_arch 2>&1)" && code=0 || code=$?
  if [[ "$code" -ne 0 && "$out" == *"Unsupported architecture: aarch64"* ]]; then
    pass "arch gate rejects aarch64"
  else
    fail "arch gate (out='$out' code=$code)"
  fi
  rm -rf "$dir"
}

# --- 10. Windows platform message (Windows only). ---
t_windows_message() {
  if ! is_windows_env; then
    echo "SKIP: windows message (not on Windows)"
    return
  fi
  local dir; dir="$(mktemp -d)"
  cp "$LAUNCHER" "$dir/iptracex.sh"
  local out code
  set +e
  out="$("$dir/iptracex.sh" --version 2>&1)"; code=$?
  set -e
  if [[ "$code" -ne 0 && "$out" == *"cannot run on Windows/Git Bash"* ]]; then
    pass "windows platform message, no Exec format error"
  else
    fail "windows message (out='$out' code=$code)"
  fi
  rm -rf "$dir"
}

# --- 11. Explicit --update downloads → verifies → installs (Linux, network). ---
# Skipped automatically when the release assets are unpublished.
t_update_flow() {
  if is_windows_env; then
    echo "SKIP: update flow (Windows cannot exec ELF)"
    return
  fi
  local dir; dir="$(mktemp -d)"
  cp "$LAUNCHER" "$dir/iptracex.sh"
  local out code
  set +e
  out="$("$dir/iptracex.sh" --update 2>&1)"; code=$?
  set -e
  if [[ "$code" -eq 0 && -x "$dir/IPTraceX" ]]; then
    # shellcheck disable=SC1090 # dynamic source of launcher under test
    source "$LAUNCHER" <<<"" >/dev/null 2>&1 || true
    if is_elf_x86_64 "$dir/IPTraceX" 2>/dev/null; then
      pass "update downloads+verifies+installs ELF"
    else
      fail "update installed non-ELF"
    fi
  elif [[ "$out" == *"HTTP 404"* ]]; then
    echo "SKIP: update flow (release assets unpublished)"
  else
    fail "update flow (out='$out' code=$code)"
  fi
  rm -rf "$dir"
}

# --- 13. HTTP 404 fails fast with a clear message (stubbed curl). ---
t_http_404_fast() {
  # shellcheck disable=SC1090 # dynamic source of launcher under test
  source "$LAUNCHER" <<<"" >/dev/null 2>&1 || true
  local dir; dir="$(mktemp -d)"
  mkdir -p "$dir/fakebin"
  printf '#!/usr/bin/env bash\nprintf "404"\necho "curl: (22) The requested URL returned error: 404" >&2\nexit 22\n' \
    > "$dir/fakebin/curl"
  chmod +x "$dir/fakebin/curl"
  local out code
  set +e
  out="$(PATH="$dir/fakebin:$PATH" download_file \
    "https://github.com/t1-haaaa/IPTraceX/releases/latest/download/x" \
    "$dir/out.bin" "curl" 2>&1)"; code=$?
  set -e
  if [[ "$code" -ne 0 && "$out" == *"HTTP 404"* && "$out" == *"may not be published"* ]]; then
    pass "HTTP 404 fails fast with clear message"
  else
    fail "http 404 handling (out='$out' code=$code)"
  fi
  rm -rf "$dir"
}

# --- 14. Timeout fails fast with a clear message (stubbed curl). ---
t_timeout_fast() {
  # shellcheck disable=SC1090 # dynamic source of launcher under test
  source "$LAUNCHER" <<<"" >/dev/null 2>&1 || true
  local dir; dir="$(mktemp -d)"
  mkdir -p "$dir/fakebin"
  printf '#!/usr/bin/env bash\nprintf "000"\necho "curl: (28) Operation timed out" >&2\nexit 28\n' \
    > "$dir/fakebin/curl"
  chmod +x "$dir/fakebin/curl"
  local out code
  set +e
  out="$(IPTRACEX_DOWNLOAD_TIMEOUT=7 PATH="$dir/fakebin:$PATH" download_file \
    "https://github.com/t1-haaaa/IPTraceX/releases/latest/download/x" \
    "$dir/out.bin" "curl" 2>&1)"; code=$?
  set -e
  if [[ "$code" -ne 0 && "$out" == *"timed out"* ]]; then
    pass "timeout fails fast with clear message"
  else
    fail "timeout handling (out='$out' code=$code)"
  fi
  rm -rf "$dir"
}

# --- 15. Network failure fails fast (stubbed curl, exit 7). ---
t_network_failure_fast() {
  # shellcheck disable=SC1090 # dynamic source of launcher under test
  source "$LAUNCHER" <<<"" >/dev/null 2>&1 || true
  local dir; dir="$(mktemp -d)"
  mkdir -p "$dir/fakebin"
  printf '#!/usr/bin/env bash\nprintf "000"\necho "curl: (7) Failed to connect" >&2\nexit 7\n' \
    > "$dir/fakebin/curl"
  chmod +x "$dir/fakebin/curl"
  local out code
  set +e
  out="$(PATH="$dir/fakebin:$PATH" download_file \
    "https://github.com/t1-haaaa/IPTraceX/releases/latest/download/x" \
    "$dir/out.bin" "curl" 2>&1)"; code=$?
  set -e
  if [[ "$code" -ne 0 && "$out" == *"Network failure"* ]]; then
    pass "network failure fails fast with clear message"
  else
    fail "network failure handling (out='$out' code=$code)"
  fi
  rm -rf "$dir"
}

# --- 16. Checksum mismatch message is explicit. ---
t_checksum_mismatch_message() {
  # shellcheck disable=SC1090 # dynamic source of launcher under test
  source "$LAUNCHER" <<<"" >/dev/null 2>&1 || true
  local dir; dir="$(mktemp -d)"
  echo "hello" > "$dir/a.txt"
  ( cd "$dir" && sha256sum a.txt > SHA256SUMS )
  echo "tampered" > "$dir/a.txt"
  local out code
  set +e
  out="$(verify_archive "$dir/a.txt" "$dir/SHA256SUMS" "$dir" 2>&1)"; code=$?
  set -e
  if [[ "$code" -ne 0 && "$out" == *"mismatch"* ]]; then
    pass "checksum mismatch is explicit"
  else
    fail "checksum mismatch message (out='$out' code=$code)"
  fi
  rm -rf "$dir"
}

t_missing_binary_fast
t_shipped_binary_resolved
t_elf_validation
t_checksum_verify
t_untrusted_url_refused
t_header_content
t_curl_quiet_flags
t_download_error_visible
t_clear_behavior
t_arch_gate
t_windows_message
t_update_flow
t_http_404_fast
t_timeout_fast
t_network_failure_fast
t_checksum_mismatch_message

echo ""
echo "Launcher tests: PASS=$PASS FAIL=$FAIL"
[[ "$FAIL" -eq 0 ]]
