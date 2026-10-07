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
  # shellcheck disable=SC1091
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
  # shellcheck disable=SC1091
  source "$LAUNCHER" <<<"" >/dev/null 2>&1 || true
  if download_file "http://evil.example/x" "$HOME/nope" "curl" >/dev/null 2>&1; then
    fail "untrusted URL accepted"
  else
    pass "untrusted URL refused without network"
  fi
}

# --- 5. Arch gate via fake uname (Linux only). ---
t_arch_gate() {
  if is_windows_env; then
    echo "SKIP: arch gate (Windows takes platform path)"
    return
  fi
  local dir fakebin; dir="$(mktemp -d)"; fakebin="$dir/fakebin"
  mkdir -p "$fakebin"
  printf '#!/usr/bin/env bash\necho aarch64\n' > "$fakebin/uname"
  chmod +x "$fakebin/uname"
  # shellcheck disable=SC1091
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

# --- 6. Windows platform message (Windows only). ---
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

# --- 7. Real download → verify → cache → execute (Linux only, network). ---
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
  if [[ "$code" -eq 0 && "$out" == *"IPTraceX 1.0.0"* && -x "$dir/.iptracex/bin/IPTraceX" ]]; then
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
  if [[ "$code" -eq 0 && "$before" == "$after" && "$out" == *"IPTraceX 1.0.0"* ]]; then
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
t_arch_gate
t_windows_message
t_real_download_flow

echo ""
echo "Launcher tests: PASS=$PASS FAIL=$FAIL"
[[ "$FAIL" -eq 0 ]]
