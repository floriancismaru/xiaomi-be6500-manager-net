#!/usr/bin/env bash
# Installs Xiaomi BE6500 Manager for the current user (no sudo): app, icon set and a menu/dock launcher.
# Run after `scripts/publish.sh linux-x64`. Remove everything with: packaging/linux/install.sh --uninstall
set -euo pipefail

APP_ID=com.florin.xiaomi_be6500_manager
HERE="$(cd "$(dirname "$0")" && pwd)"
BUNDLE="$HERE/../../artifacts/linux-x64"
DEST="$HOME/.local/opt/xiaomi-be6500-manager"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"

refresh() {
  update-desktop-database "$DATA/applications" 2>/dev/null || true
  gtk-update-icon-cache -q -t "$DATA/icons/hicolor" 2>/dev/null || true
}

if [[ "${1:-}" == "--uninstall" ]]; then
  rm -rf "$DEST" "$DATA/applications/$APP_ID.desktop" "$DATA/icons/hicolor/scalable/apps/$APP_ID.svg"
  for s in 48 64 128 256 512; do rm -f "$DATA/icons/hicolor/${s}x${s}/apps/$APP_ID.png"; done
  refresh; echo "Uninstalled."; exit 0
fi

[[ -x "$BUNDLE/xiaomi-be6500-manager" ]] || { echo "Build first: scripts/publish.sh linux-x64" >&2; exit 1; }

rm -rf "$DEST"; mkdir -p "$DEST"
cp -r "$BUNDLE"/. "$DEST"/

for s in 48 64 128 256 512; do
  install -Dm644 "$HERE/icons/$s.png" "$DATA/icons/hicolor/${s}x${s}/apps/$APP_ID.png"
done
install -Dm644 "$HERE/icons/app_icon.svg" "$DATA/icons/hicolor/scalable/apps/$APP_ID.svg"

mkdir -p "$DATA/applications"
sed "s|@EXEC@|$DEST/xiaomi-be6500-manager|" "$HERE/$APP_ID.desktop" > "$DATA/applications/$APP_ID.desktop"
chmod 644 "$DATA/applications/$APP_ID.desktop"

refresh
echo "Installed. Find 'Xiaomi BE6500 Manager' in your application menu."
