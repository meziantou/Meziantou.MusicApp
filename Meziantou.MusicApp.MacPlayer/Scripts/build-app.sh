#!/bin/sh
# Builds "Meziantou Music.app" into the .build directory.
# Usage: Scripts/build-app.sh [debug|release]
# Optional environment variables:
# - APP_VERSION: CFBundleShortVersionString (for example 1.2.0)
# - APP_BUILD: CFBundleVersion (defaults to the short commit hash)
set -eu

configuration="${1:-release}"
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

swift build --configuration "$configuration" --product MeziantouMusic
binary_dir="$(swift build --configuration "$configuration" --show-bin-path)"

app="$root/.build/Meziantou Music.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp "$binary_dir/MeziantouMusic" "$app/Contents/MacOS/MeziantouMusic"
cp "Sources/MusicPlayerMac/Resources/Info.plist" "$app/Contents/Info.plist"
cp "Sources/MusicPlayerMac/Resources/AppIcon.icns" "$app/Contents/Resources/AppIcon.icns"

# Use the commit as the build number by default, like the web player shows its commit hash
build="${APP_BUILD:-$(git rev-parse --short HEAD 2>/dev/null || echo dev)}"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $build" "$app/Contents/Info.plist"
if [ -n "${APP_VERSION:-}" ]; then
  /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $APP_VERSION" "$app/Contents/Info.plist"
fi

# Ad-hoc signature so the app can be launched locally
codesign --force --sign - "$app"

echo "Built $app"
