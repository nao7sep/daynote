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

trap 'pause_on_failure $?' EXIT

cd "$REPO_DIR"

# Clear stale output, then publish. `dotnet publish` runs the bundling target,
# leaving publish/ holding only the signed .app.
rm -rf "$REPO_DIR/publish"
dotnet publish "$PROJECT_FILE" -c Release -r "$RID" --self-contained true -o "$REPO_DIR/publish"

claim_launcher_runtime "$RUNTIME_TOKEN" "$REPO_DIR"
stop_owned_runtime dotnet "DayNote" "$REPO_DIR" "$PROJECT_FILE" "DayNote" "$APP_EXECUTABLE"
open -n "$APP_BUNDLE"
wait_for_owned_runtime dotnet "DayNote" "$REPO_DIR" "$PROJECT_FILE" "DayNote" "$APP_EXECUTABLE" 30
