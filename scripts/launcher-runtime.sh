#!/usr/bin/env bash

# Shared by run-dev, run-built and rebuild: one DayNote per repository. The launcher that starts DayNote
# records its process ID and executable under .git; the next launcher asks that process to quit only
# while it still runs that executable, and stops with a message rather than force-closing it when it
# stays open, for example while it asks about unsaved work. A DayNote started any other way is left to
# its single-instance behavior, which brings its window forward instead of opening a second one.
# The caller sets REPO_DIR.

RUNTIME_RECORD="$REPO_DIR/.git/launcher-runtime-process"
SINGLE_INSTANCE_NOTE='If DayNote was already running outside these launchers, its window is brought forward instead.'

# The executable a process runs, or nothing when no such process exists.
executable_of() {
  ps -p "$1" -o comm= 2>/dev/null || true
}

record_runtime() {
  printf '%s\n%s\n' "$1" "$2" > "$RUNTIME_RECORD"
}

# The recorded process ID, printed only while that process still runs the recorded executable.
recorded_runtime_pid() {
  local pid="" executable=""
  [[ -f "$RUNTIME_RECORD" ]] || return 0
  { read -r pid; read -r executable; } < "$RUNTIME_RECORD" || true
  [[ "$pid" =~ ^[0-9]+$ && -n "$executable" ]] || return 0
  if [[ "$(executable_of "$pid")" == "$executable" ]]; then
    printf '%s\n' "$pid"
  fi
}

# Asks the recorded DayNote to quit as a user would, so it can still finish or ask about unsaved work.
stop_recorded_runtime() {
  local pid deadline
  pid="$(recorded_runtime_pid)"
  [[ -n "$pid" ]] || return 0
  echo "Asking the running DayNote (pid $pid) to quit."
  osascript -l JavaScript -e 'ObjC.import("AppKit");
function run(args) {
  var application = $.NSRunningApplication.runningApplicationWithProcessIdentifier(Number(args[0]));
  if (application) application.terminate;
}' "$pid" >/dev/null
  deadline=$((SECONDS + 5))
  while (( SECONDS < deadline )); do
    [[ "$(recorded_runtime_pid)" == "$pid" ]] || return 0
    sleep 0.1
  done
  echo 'DayNote is still open, perhaps finishing or asking about unsaved work. Close it, then run this launcher again.' >&2
  return 1
}

# Opens a new instance of the bundle through Launch Services, which gives it its bundle identity, and
# records it.
launch_bundle() {
  local pid
  pid="$(osascript -l JavaScript -e 'ObjC.import("AppKit");
function run(args) {
  var application = $.NSWorkspace.sharedWorkspace.launchApplicationAtURLOptionsConfigurationError(
    $.NSURL.fileURLWithPath(args[0]), $.NSWorkspaceLaunchNewInstance, $({}), null);
  return application.isNil() ? "" : String(application.processIdentifier);
}' "$1")"
  if [[ ! "$pid" =~ ^[0-9]+$ ]]; then
    echo 'DayNote could not be opened.' >&2
    return 1
  fi
  record_runtime "$pid" "$(executable_of "$pid")"
  echo "$SINGLE_INSTANCE_NOTE"
}

# Records the DayNote that `dotnet run` (process $1) starts from the development build, once it
# appears. Fails when the wrapper ends first, as it does when the build fails.
record_wrapped_runtime() {
  local wrapper="$1" development_bin="$REPO_DIR/src/DayNote/bin/" processes descendants pid parent executable grown
  while true; do
    if ! kill -0 "$wrapper" 2>/dev/null; then
      echo 'The DayNote development runtime ended before the app started.' >&2
      return 1
    fi
    processes="$(ps -axo pid=,ppid=,comm=)"
    descendants=" $wrapper "
    grown=1
    while (( grown )); do
      grown=0
      while read -r pid parent executable; do
        if [[ "$descendants" == *" $parent "* && "$descendants" != *" $pid "* ]]; then
          descendants+="$pid "
          grown=1
        fi
      done <<< "$processes"
    done
    while read -r pid parent executable; do
      if [[ "$descendants" == *" $pid "* && "$executable" == "$development_bin"*/DayNote ]]; then
        record_runtime "$pid" "$executable"
        echo "$SINGLE_INSTANCE_NOTE"
        return 0
      fi
    done <<< "$processes"
    sleep 0.2
  done
}
