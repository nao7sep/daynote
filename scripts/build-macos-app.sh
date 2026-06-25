#!/usr/bin/env bash
set -euo pipefail

# build-macos-app: assemble DayNote's macOS .app bundle from source. This is the
# single shared implementation of the .app assembly, called by BOTH the local
# rebuild.command launcher and the release.yml CI workflow, so a CI-built release
# ships the same bundle a local rebuild produces — icon, Liquid Glass catalog, and
# ad-hoc signature included.
#
# It publishes a self-contained Release build for the mac RID, lays out
# Contents/{MacOS,Resources} + Info.plist, copies in the classic icon (icon.icns)
# and the Tahoe Liquid Glass catalog (Assets.car, if present), then ad-hoc signs
# the whole bundle. No Apple Developer identity is used.
#
# Output: <repo>/publish/DayNote.app — the same path run-built.command launches.
#
# Idempotent: the bundle is cleaned and recreated on every run, and the publish
# output is removed first so a file deleted since the last build can't linger.
#
# Self-locating: the repo root is resolved from this script's own path (not the
# caller's working directory), so CI and a double-clicked launcher both work.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT_FILE="$REPO_DIR/src/DayNote.Desktop/DayNote.Desktop.csproj"
APP_BUNDLE="$REPO_DIR/publish/DayNote.app"
INFO_PLIST="$REPO_DIR/macOS/Info.plist"

# Map the host CPU to a .NET runtime identifier so the same script works on
# Apple Silicon and Intel Macs (and on the GitHub macOS runner) without a flag.
ARCH="$(uname -m)"
case "$ARCH" in
  arm64)  RID="osx-arm64" ;;
  x86_64) RID="osx-x64"   ;;
  *)
    echo "Unsupported macOS architecture: $ARCH (expected arm64 or x86_64)." >&2
    exit 1
    ;;
esac

PUBLISH_DIR="$REPO_DIR/bin/Release/net10.0/$RID/publish"

log_step() {
  printf '\n==> %s\n' "$1"
}

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

require_command dotnet
require_command codesign

cd "$REPO_DIR"

# Remove stale publish output so a file deleted since the last build can't linger
# and get copied into the bundle (the Contents/MacOS reset below only clears the
# copy target, not the publish source).
log_step "Cleaning previous publish output"
rm -rf "$PUBLISH_DIR"

log_step "Publishing self-contained $RID build (host arch $ARCH)"
dotnet publish "$PROJECT_FILE" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -o "$PUBLISH_DIR"

log_step "Assembling app bundle"
# Recreate the bundle from scratch so stale assemblies from a previous build can't
# linger and so the layout is identical whether or not a bundle already existed.
rm -rf "${APP_BUNDLE:?}"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

# Copy the publish output (binary + native dylibs + managed DLLs) into MacOS/.
cp -R "$PUBLISH_DIR/." "$APP_BUNDLE/Contents/MacOS/"

# Drop in the Info.plist so TCC has a bundle identity and usage strings.
cp "$INFO_PLIST" "$APP_BUNDLE/Contents/Info.plist"

# Drop in the app icon. Info.plist's CFBundleIconFile points to "icon" → icon.icns
# here; macOS reads it for the Dock/Finder tile. publish/ is gitignored, so the
# committed source is macOS/icon.icns, copied in fresh on every build.
cp "$REPO_DIR/macOS/icon.icns" "$APP_BUNDLE/Contents/Resources/icon.icns"

# Drop in the Liquid Glass icon catalog (Tahoe). Info.plist's CFBundleIconName names it,
# so macOS 26 renders it as the Liquid Glass tile; older macOS falls back to the icon.icns
# above. macOS/Assets.car is the committed source (staged by the liquid-glass-icon deploy);
# copied in only if present, so a classic-only build still works.
if [[ -f "$REPO_DIR/macOS/Assets.car" ]]; then
  cp "$REPO_DIR/macOS/Assets.car" "$APP_BUNDLE/Contents/Resources/Assets.car"
fi

log_step "Ad-hoc signing bundle"
# `--sign -` is the ad-hoc identity. --force overwrites prior signatures (each
# build produces a new cdhash). --deep recursively re-signs nested bundles
# (Avalonia's native dylibs ship pre-signed with Avalonia's identity; we replace
# those with our ad-hoc signature so the whole bundle has one consistent identity).
codesign --force --deep --sign - "$APP_BUNDLE"

# Verify the signature attached cleanly. `codesign --verify` exits non-zero if
# the bundle isn't recognized as signed code.
codesign --verify --verbose=1 "$APP_BUNDLE"

log_step "Built $APP_BUNDLE"
