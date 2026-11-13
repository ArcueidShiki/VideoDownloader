#!/usr/bin/env bash
# One-click release build for AnyDownloader (run on macOS).
#
#   scripts/build-release.sh                 # macOS tests + universal .app/.zip, then Windows via CI
#   scripts/build-release.sh --skip-windows  # macOS only
#   scripts/build-release.sh --skip-tests    # skip `swift test`
#   scripts/build-release.sh --allow-unpushed  # let CI build origin/<branch> despite local edits
#
# Outputs:
#   release/AnyDownloader-<timestamp>.app / .zip      (macOS, universal, ad-hoc signed)
#   release/windows/AnyDownloader-<ver>-x64-setup.exe  (Windows installer)
#   release/windows/AnyDownloader-<ver>-x64-portable.zip
#
# Windows cannot be compiled on macOS (the WinUI XAML compiler is a Windows-only
# .NET Framework tool), so this script dispatches .github/workflows/windows-release.yml
# on a GitHub-hosted Windows runner and downloads the result. CI builds the *pushed*
# branch, not uncommitted local edits. On a Windows PC run scripts/build-windows.ps1.
set -euo pipefail

ROOT=$(cd -- "$(dirname -- "$0")/.." && pwd)
WORKFLOW=windows-release.yml
ARTIFACT=AnyDownloader-Windows-x64
WIN_OUT="$ROOT/release/windows"
DISPATCH_WAIT_SECONDS=90

skip_windows=false
skip_tests=false
allow_unpushed=false
for arg in "$@"; do
  case "$arg" in
    --skip-windows)   skip_windows=true ;;
    --skip-tests)     skip_tests=true ;;
    --allow-unpushed) allow_unpushed=true ;;
    -h|--help)        awk 'NR > 1 && /^#/ { print; next } NR > 1 { exit }' "$0"; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

step() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
fail() { printf '\n\033[1;31mFAILED: %s\033[0m\n' "$*" >&2; exit 1; }

build_macos() {
  if ! $skip_tests; then
    step "macOS: swift test"
    swift test --package-path "$ROOT/macos" || fail "macOS tests"
  fi
  step "macOS: Release build (universal arm64 + x86_64)"
  local log
  log=$("$ROOT/scripts/package-macos.sh" 2>&1) || { echo "$log" | tail -40; fail "macOS build"; }
  local app
  app=$(echo "$log" | sed -n 's/^Local ad-hoc signed app: //p')
  [ -d "$app" ] || fail "packaged app not found"
  local archs
  archs=$(lipo -archs "$app/Contents/MacOS/$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$app/Contents/Info.plist")")
  echo "    $app  [$archs]"
  echo "    ${app%.app}.zip"
}

# CI builds origin/<branch>. A mismatch would ship a Windows build that differs from
# the macOS build made from the working tree, so it is an error unless overridden.
require_pushed_head() {
  local branch=$1
  git -C "$ROOT" fetch --quiet origin "$branch" 2>/dev/null || fail "branch '$branch' is not on origin — push it first"
  $allow_unpushed && return 0
  [ -z "$(git -C "$ROOT" status --porcelain --untracked-files=no)" ] \
    || fail "uncommitted changes would be missing from the Windows build — commit & push (or --allow-unpushed)"
  [ "$(git -C "$ROOT" rev-parse HEAD)" = "$(git -C "$ROOT" rev-parse "origin/$branch")" ] \
    || fail "local HEAD differs from origin/$branch — push first (or --allow-unpushed)"
}

run_ids() {  # newline-separated ids of recent dispatch runs for branch $1
  gh run list --workflow "$WORKFLOW" --branch "$1" --event workflow_dispatch --limit 20 \
    --json databaseId --jq '.[].databaseId'
}

build_windows() {
  step "Windows: GitHub Actions ($WORKFLOW)"
  command -v gh >/dev/null || fail "GitHub CLI 'gh' not found (brew install gh), or run scripts/build-windows.ps1 on Windows"
  gh auth status >/dev/null 2>&1 || fail "gh is not logged in — run: gh auth login"
  local branch before run_id=""
  branch=$(git -C "$ROOT" rev-parse --abbrev-ref HEAD)
  require_pushed_head "$branch"

  # Identify our run as the one id that did not exist before dispatch
  # (comparing timestamps would depend on the local clock matching GitHub's).
  before=$(run_ids "$branch")
  gh workflow run "$WORKFLOW" --ref "$branch" \
    || fail "could not dispatch $WORKFLOW (workflow_dispatch needs the file on the default branch)"
  for _ in $(seq 1 $((DISPATCH_WAIT_SECONDS / 3))); do
    run_id=$(run_ids "$branch" | grep -vxF -f <(printf '%s\n' "$before") | head -1 || true)
    [ -n "$run_id" ] && break
    sleep 3
  done
  [ -n "$run_id" ] || fail "dispatched run did not appear within ${DISPATCH_WAIT_SECONDS}s"
  echo "    run $run_id: $(gh run view "$run_id" --json url --jq .url)"

  gh run watch "$run_id" --exit-status --interval 20 || fail "Windows CI build (gh run view $run_id --log-failed)"
  rm -rf "$WIN_OUT" && mkdir -p "$WIN_OUT"
  gh run download "$run_id" --name "$ARTIFACT" --dir "$WIN_OUT" || fail "artifact download"
  ls -lh "$WIN_OUT"
}

build_macos
$skip_windows || build_windows
step "Release build complete → $ROOT/release"
