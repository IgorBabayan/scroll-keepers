#!/usr/bin/env bash
# Installs Scroll Keeper for the current user: the application into ~/.local/share/ScrollKeeper,
# a launcher into the applications menu and the icon. Run it from the extracted folder:
#
#   ./install.sh            install or update
#   ./install.sh --remove   uninstall (settings in ~/.config/ScrollKeeper are kept)
set -euo pipefail

data="${XDG_DATA_HOME:-$HOME/.local/share}"
target="$data/ScrollKeeper"
desktop="$data/applications/scrollkeeper.desktop"
icon="$data/icons/hicolor/256x256/apps/scrollkeeper.png"
here="$(cd "$(dirname "$0")" && pwd)"

if [[ "${1:-}" == "--remove" ]]; then
    rm -rf "$target" "$desktop" "$icon"
    echo "Scroll Keeper was removed."
    exit 0
fi

rm -rf "$target"
mkdir -p "$target" "$(dirname "$desktop")" "$(dirname "$icon")"
cp -R "$here"/. "$target/"
chmod +x "$target/ScrollKeeper"
cp "$here/scrollkeeper.png" "$icon"
sed "s|^Exec=.*|Exec=\"$target/ScrollKeeper\"|" "$here/scrollkeeper.desktop" > "$desktop"

command -v update-desktop-database >/dev/null && update-desktop-database "$data/applications" || true
echo "Scroll Keeper was installed to $target. Start it from the applications menu."
