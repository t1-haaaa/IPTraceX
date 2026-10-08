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

# --- 1. Fake binary: resolution order + argument forwarding + exit code. ---
t_fake_binary_exec() {
  if is_windows_env; then
    echo "SKIP: fake binary exec (Windows cannot exec; platform guard covers it)"
    return
  fi
  local dir; dir="$(mktemp -d)"
  cp "$LAUNCHER" "$dir/iptracex.sh"
  cat > "$dir/IPTraceX" <<'EOF'
#!/usr/bin/env bash
echo "FAKE-EXEC args: $*"
exit 7
EOF
  chmod +x "$dir/IPTraceX"
  local out code
  set +e
  out="$("$dir/iptracex.sh" --version foo 2>&1)"; code=$?
  set -e
  if [[ "$out" == "FAKE-EXEC args: --version foo" && "$code" -eq 7 ]]; then
    pass "fake binary exec + args + exit code"
  else
    fail "fake binary exec (out='$out' code=$code)"
  fi
  rm -rf "$dir"
}

# --- 2. Cache precedence over download. ---
t_cache_precedence() {
  if is_windows_env; then
    echo "SKIP: cache precedence (Windows cannot exec; platform guard covers it)"
    return
  fi
  local dir; dir="$(mktemp -d)"
  cp "$LAUNCHER" "$dir/iptracex.sh"
  mkdir -p "$dir/.iptracex/bin"
  cat > "$dir/.iptracex/bin/IPTraceX" <<'EOF'
#!/usr/bin/env bash
echo "FAKE-CACHE"
exit 0
EOF
  chmod +x "$dir/.iptracex/bin/IPTraceX"
  local out
  set +e
  out="$("$dir/iptracex.sh" --version 2>&1)"; code=$?
  set -e
  if [[ "$out" == "FAKE-CACHE" ]]; then
    pass "cache used when root binary missing (no download)"
  else
    fail "cache precedence (out='$out')"
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
    && "$out" == *"Version 2.1.0"* \
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

# --- 11. Real download → verify → cache → execute (Linux only, network). ---
t_real_download_flow() {
  if is_windows_env; then
    echo "SKIP: real download (Windows cannot exec ELF)"
    return
  fi
  local dir; dir="$(mktemp -d)"
  cp "$LAUNCHER" "$dir/iptracex.sh"
  local out code
  set +e
  out="$("$dir/iptracex.sh" --version 2>&1)"; code=$?
  set -e
  if [[ "$code" -eq 0 && "$out" == *"IPTraceX 2.1.0"* && -x "$dir/.iptracex/bin/IPTraceX" ]]; then
    pass "download+verify+cache+exec"
  else
    fail "download flow (out='$out' code=$code)"
    rm -rf "$dir"
    return
  fi
  local before after
  before="$(stat -c %Y "$dir/.iptracex/bin/IPTraceX")"
  sleep 1
  set +e
  out="$("$dir/iptracex.sh" --version 2>&1)"; code=$?
  set -e
  after="$(stat -c %Y "$dir/.iptracex/bin/IPTraceX")"
  if [[ "$code" -eq 0 && "$before" == "$after" && "$out" == *"IPTraceX 2.1.0"* ]]; then
    pass "second run uses cache (no re-download)"
  else
    fail "cache reuse (out='$out' code=$code)"
  fi
  rm -rf "$dir"
}

t_fake_binary_exec
t_cache_precedence
t_checksum_verify
t_untrusted_url_refused
t_header_content
t_curl_quiet_flags
t_download_error_visible
t_clear_behavior
t_arch_gate
t_windows_message
t_real_download_flow

echo ""
echo "Launcher tests: PASS=$PASS FAIL=$FAIL"
[[ "$FAIL" -eq 0 ]]
