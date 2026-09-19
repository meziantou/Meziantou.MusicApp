#!/bin/sh
# Builds "Meziantou Music.app" into the .build directory.
# Usage: Scripts/build-app.sh [debug|release]
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

# Use the commit as the build number, like the web player shows its commit hash
commit="$(git rev-parse --short HEAD 2>/dev/null || echo dev)"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $commit" "$app/Contents/Info.plist"

# Ad-hoc signature so the app can be launched locally
codesign --force --sign - "$app"

echo "Built $app"
