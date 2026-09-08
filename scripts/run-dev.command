#!/usr/bin/env bash
set -euo pipefail

# run-dev: run the app from source with live reload, in its loosest configuration.
# For active coding and debugging. The strict, production-faithful launchers are
# run-built (launch the existing packaged app bundle without rebuilding) and
# rebuild (build and package a fresh bundle, then launch).
#
# On macOS, features gated by TCC (access to protected folders) need the
# ad-hoc-signed bundle from rebuild/run-built — `dotnet run` has no bundle
# identity, so those prompts won't fire here.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT_FILE="$REPO_DIR/src/DayNote/DayNote.csproj"
APP_EXECUTABLE="$REPO_DIR/publish/DayNote.app/Contents/MacOS/DayNote"
RUNTIME_TOKEN="run-dev-$$-$(date +%s)-$RANDOM"
source "$SCRIPT_DIR/launcher-runtime.sh"

log_step() {
  printf '\n==> %s\n' "$1"
}

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

pause_on_failure() {
  local status="$1"
  if [[ "$status" -ne 0 && ( "$status" -lt 128 || "$status" -gt 143 ) ]]; then
    echo
    echo "daynote run-dev failed with exit code $status."
    read -r -p "Press Enter to close..."
  fi
}

cleanup() {
  local status="$?"
  trap - EXIT
  if is_launcher_runtime_owner "$RUNTIME_TOKEN" "$REPO_DIR"; then
    stop_owned_runtime dotnet "DayNote" "$REPO_DIR" "$PROJECT_FILE" "DayNote" "$APP_EXECUTABLE" >/dev/null 2>&1 || true
    release_launcher_runtime "$RUNTIME_TOKEN" "$REPO_DIR"
  else
    status=0
  fi
  pause_on_failure "$status"
  exit "$status"
}

trap cleanup EXIT

require_command dotnet

cd "$REPO_DIR"

log_step "Replacing any existing DayNote runtime"
claim_launcher_runtime "$RUNTIME_TOKEN" "$REPO_DIR"
stop_owned_runtime dotnet "DayNote" "$REPO_DIR" "$PROJECT_FILE" "DayNote" "$APP_EXECUTABLE"

log_step "Restoring packages required for launch"
dotnet restore "$PROJECT_FILE"

log_step "Starting DayNote (Debug, from source)"
dotnet run --project "$PROJECT_FILE" &
DEV_PID=$!
wait_for_owned_runtime dotnet "DayNote" "$REPO_DIR" "" "DayNote" "$APP_EXECUTABLE" 120
log_step "DayNote is ready"
wait "$DEV_PID"
