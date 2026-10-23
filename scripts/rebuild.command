#!/usr/bin/env bash
set -euo pipefail

# rebuild: produce a fresh self-contained Release build and launch it. The macOS
# .app bundle is assembled and ad-hoc signed by the AssembleMacAppBundle target
# (Directory.Build.targets) as part of `dotnet publish`, so this launcher only
# publishes and opens the result — the same bundle the release workflow produces.
# Slow; run after changing source. run-built launches the existing bundle.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT_FILE="$REPO_DIR/src/DayNote/DayNote.csproj"
APP_BUNDLE="$REPO_DIR/publish/DayNote.app"
APP_EXECUTABLE="$APP_BUNDLE/Contents/MacOS/DayNote"
RUNTIME_TOKEN="rebuild-$$-$(date +%s)-$RANDOM"
source "$SCRIPT_DIR/launcher-runtime.sh"

# Apple Silicon only, matching the product that the release workflow ships.
ARCH="$(uname -m)"
if [[ "$ARCH" != "arm64" ]]; then
  echo "DayNote builds for Apple Silicon only; this host is $ARCH." >&2
  exit 1
fi
RID="osx-arm64"

pause_on_failure() {
  local status="$1"
  if [[ "$status" -ne 0 && ( "$status" -lt 128 || "$status" -gt 143 ) ]]; then
    echo
    echo "daynote rebuild failed with exit code $status."
    read -r -p "Press Enter to close..."
  fi
}

CANDIDATE="$REPO_DIR/publish-candidate-$RUNTIME_TOKEN"
PREVIOUS="$REPO_DIR/publish-previous-$RUNTIME_TOKEN"
cleanup() {
  local status="$?"
  trap - EXIT
  if [[ -d "$CANDIDATE" ]]; then rm -r "$CANDIDATE"; fi
  release_launcher_runtime "$RUNTIME_TOKEN" "$REPO_DIR"
  pause_on_failure "$status"
  exit "$status"
}
trap cleanup EXIT

cd "$REPO_DIR"

for required in dotnet open osascript; do
  command -v "$required" >/dev/null 2>&1 || { echo "Missing required command: $required" >&2; exit 1; }
done
claim_launcher_runtime "$RUNTIME_TOKEN" "$REPO_DIR"
printf '\n==> Publishing a clean candidate\n'
mkdir "$CANDIDATE"
dotnet publish "$PROJECT_FILE" -c Release -r "$RID" --self-contained true -o "$CANDIDATE"
[[ -x "$CANDIDATE/DayNote.app/Contents/MacOS/DayNote" ]] || { echo 'The candidate has no runnable DayNote app.' >&2; exit 1; }

# A cancelled quit leaves both the running app and its published files intact.
request_runtime_close "$REPO_DIR" "$APP_EXECUTABLE"
require_launcher_runtime_owner "$RUNTIME_TOKEN" "$REPO_DIR"
if [[ -d "$REPO_DIR/publish" ]]; then mv "$REPO_DIR/publish" "$PREVIOUS"; fi
if ! mv "$CANDIDATE" "$REPO_DIR/publish"; then
  if [[ -d "$PREVIOUS" ]]; then mv "$PREVIOUS" "$REPO_DIR/publish"; fi
  echo 'Could not publish the candidate; the previous build was retained.' >&2
  exit 1
fi
if [[ -d "$PREVIOUS" ]]; then rm -r "$PREVIOUS"; fi
require_launcher_runtime_owner "$RUNTIME_TOKEN" "$REPO_DIR"
open -n "$APP_BUNDLE"
wait_for_runtime "$REPO_DIR" "$APP_EXECUTABLE" built 30
