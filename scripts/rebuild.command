#!/usr/bin/env bash
set -euo pipefail

# rebuild: build the app in its release configuration and launch it. On macOS that
# means publishing a self-contained Release build, assembling an unsigned .app,
# ad-hoc signing it, and launching via Launch Services. Slow; run after changing
# source. run-built launches the existing bundle without rebuilding.
#
# The .app assembly itself lives in build-macos-app.sh, the single shared
# implementation also called by the release.yml CI workflow, so a local rebuild
# and a CI release produce the same bundle. This launcher adds only the local
# concerns: a failure pause (so a double-clicked window doesn't vanish on error)
# and launching the freshly built bundle.
#
# No Apple Developer identity is used.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
BUILD_SCRIPT="$SCRIPT_DIR/build-macos-app.sh"
APP_BUNDLE="$REPO_DIR/publish/DayNote.app"

log_step() {
  printf '\n==> %s\n' "$1"
}

pause_on_failure() {
  local status="$1"
  if [[ "$status" -ne 0 && "$status" -ne 130 ]]; then
    echo
    echo "daynote rebuild failed with exit code $status."
    read -r -p "Press Enter to close..."
  fi
}

trap 'pause_on_failure $?' EXIT

cd "$REPO_DIR"

# Assemble the bundle (publish, lay out Contents, copy icon/catalog, ad-hoc sign).
# Same script CI runs, so the local and released bundles match.
bash "$BUILD_SCRIPT"

log_step "Launching"
# `open` routes through Launch Services, which is what registers the app's
# bundle identity with TCC and triggers permission prompts on first access.
open "$APP_BUNDLE"
