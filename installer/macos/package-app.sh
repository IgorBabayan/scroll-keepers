#!/usr/bin/env bash
# Builds "Scroll Keeper.app" from a `dotnet publish` folder and zips it.
# Used by .github/workflows/release-installer.yml; runs on macOS (needs codesign and ditto).
#
#   installer/macos/package-app.sh <publish folder> <version> <runtime id> <output folder>
#   e.g. installer/macos/package-app.sh publish/osx-arm64 0.1.12 osx-arm64 dist
#
# Output: <output folder>/ScrollKeeper-<version>-<runtime id>.zip
set -euo pipefail

publish="$1"
version="$2"
rid="$3"
output="$4"

here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
app="$work/Scroll Keeper.app"

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$output"
cp -R "$publish"/. "$app/Contents/MacOS/"
cp "$here/ScrollKeeper.icns" "$app/Contents/Resources/ScrollKeeper.icns"
sed "s/__VERSION__/$version/g" "$here/Info.plist" > "$app/Contents/Info.plist"
chmod +x "$app/Contents/MacOS/ScrollKeeper"

# Apple Silicon only runs signed code: sign the bundle ad hoc (no Developer ID; Gatekeeper still asks on first start).
codesign --force --deep --sign - "$app"

# ditto keeps the bundle layout, permissions and signature in the zip.
ditto -c -k --sequesterRsrc --keepParent "$app" "$output/ScrollKeeper-$version-$rid.zip"
rm -rf "$work"
