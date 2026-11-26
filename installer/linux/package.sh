#!/usr/bin/env bash
# Packs a `dotnet publish` folder for Linux into a .tar.gz with a launcher, the icon and install.sh.
# Used by .github/workflows/release-installer.yml.
#
#   installer/linux/package.sh <publish folder> <version> <runtime id> <output folder>
#   e.g. installer/linux/package.sh publish/linux-x64 0.1.12 linux-x64 dist
#
# Output: <output folder>/ScrollKeeper-<version>-<runtime id>.tar.gz (one ScrollKeeper folder inside)
set -euo pipefail

publish="$1"
version="$2"
rid="$3"
output="$4"

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
work="$(mktemp -d)"
folder="$work/ScrollKeeper"

mkdir -p "$folder" "$output"
cp -R "$publish"/. "$folder/"
cp "$here/scrollkeeper.desktop" "$here/install.sh" "$folder/"
cp "$root/ScrollKeeper/ScrollKeeper/Assets/App.png" "$folder/scrollkeeper.png"
cp "$root/LICENSE" "$folder/LICENSE.txt"
chmod +x "$folder/ScrollKeeper" "$folder/install.sh"

tar -C "$work" -czf "$output/ScrollKeeper-$version-$rid.tar.gz" ScrollKeeper
rm -rf "$work"
