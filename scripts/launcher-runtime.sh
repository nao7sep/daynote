#!/usr/bin/env bash

claim_launcher_runtime() {
  local token="$1" repo_dir="$2"
  local owner_file="$repo_dir/.git/launcher-runtime-owner"
  local temporary_file="${owner_file}.$$.$RANDOM"
  printf '%s\n' "$token" > "$temporary_file"
  mv -f "$temporary_file" "$owner_file"
}

is_launcher_runtime_owner() {
  local token="$1" repo_dir="$2"
  local owner_file="$repo_dir/.git/launcher-runtime-owner"
  [[ -f "$owner_file" && "$(tr -d '\r\n' < "$owner_file")" == "$token" ]]
}

require_launcher_runtime_owner() {
  if ! is_launcher_runtime_owner "$1" "$2"; then
    echo "A newer DayNote launcher replaced this request. Run the desired launcher again." >&2
    return 1
  fi
}

release_launcher_runtime() {
  local token="$1" repo_dir="$2"
  if is_launcher_runtime_owner "$token" "$repo_dir"; then
    rm "$repo_dir/.git/launcher-runtime-owner"
  fi
}

runtime_paths() {
  local repo_dir="$1" built_executable="$2" mode="$3"
  if [[ "$mode" != dev ]]; then
    printf '%s\0' "$built_executable"
  fi
  if [[ "$mode" != built && -d "$repo_dir/src/DayNote/bin" ]]; then
    find "$repo_dir/src/DayNote/bin" -type f -name DayNote -print0
  fi
}

runtime_application_operation() {
  local operation="$1" repo_dir="$2" built_executable="$3" mode="$4" path paths_file
  local paths=()
  if ! command -v osascript >/dev/null 2>&1; then
    echo 'Missing required command: osascript' >&2
    return 1
  fi
  paths_file="$(mktemp "${TMPDIR:-/tmp}/daynote-launch-paths.XXXXXX")" || return
  if ! runtime_paths "$repo_dir" "$built_executable" "$mode" > "$paths_file"; then
    rm "$paths_file"
    return 1
  fi
  while IFS= read -r -d '' path; do paths+=("$path"); done < "$paths_file"
  rm "$paths_file"
  osascript -l JavaScript "$repo_dir/scripts/launcher-apps.js" "$operation" ${paths[@]+"${paths[@]}"}
}

request_runtime_close() {
  local repo_dir="$1" built_executable="$2"
  require_launcher_runtime_owner "$RUNTIME_TOKEN" "$repo_dir" || return
  echo 'Requesting ordinary quit for an existing DayNote app.'
  runtime_application_operation close "$repo_dir" "$built_executable" all >/dev/null
}

wait_for_runtime() {
  local repo_dir="$1" built_executable="$2" mode="$3" timeout_seconds="$4"
  local deadline=$((SECONDS + timeout_seconds)) state
  while (( SECONDS < deadline )); do
    require_launcher_runtime_owner "$RUNTIME_TOKEN" "$repo_dir" || return
    state="$(runtime_application_operation ready "$repo_dir" "$built_executable" "$mode")" || return
    [[ "$state" == ready ]] && return 0
    if [[ "$mode" == dev && -n "${DEV_PID:-}" ]] && ! kill -0 "$DEV_PID" 2>/dev/null; then
      echo 'The recorded DayNote development child exited before the app became ready.' >&2
      return 1
    fi
    sleep 0.1
  done
  echo "DayNote did not start within ${timeout_seconds}s." >&2
  return 1
}
