#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
# A local app uses tools already installed on this Mac; no Homebrew dylibs copied.
# Set OUTPUT_DIR outside the checkout to preserve the deliverable during cleanup.
OUT=${OUTPUT_DIR:-"$ROOT/release"}
# Universal (Apple silicon + Intel) by default; MACOS_ARCHS=arm64 for a faster local build.
ARCHS=${MACOS_ARCHS:-"arm64 x86_64"}
mkdir -p "$OUT"
xcodebuild -project "$ROOT/macos/AnyDownloader.xcodeproj" -scheme AnyDownloader \
  -configuration Release -derivedDataPath "$ROOT/macos/.build-app" \
  ARCHS="$ARCHS" ONLY_ACTIVE_ARCH=NO \
  CODE_SIGN_IDENTITY=- CODE_SIGN_STYLE=Manual DEVELOPMENT_TEAM= build
APP_NAME=VideoDownloader
APP="$ROOT/macos/.build-app/Build/Products/Release/$APP_NAME.app"
EXECUTABLE=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist")
[ -x "$APP/Contents/MacOS/$EXECUTABLE" ]
[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundlePackageType' "$APP/Contents/Info.plist")" = APPL ]
codesign --verify --deep --strict "$APP"
VERSION=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$APP/Contents/Info.plist")
# Fixed names: each build replaces the previous one, so release/ only holds the latest.
DEST="$OUT/$APP_NAME.app"
ZIP="$OUT/$APP_NAME-$VERSION-macOS.zip"
rm -rf "$DEST" "$ZIP"
ditto "$APP" "$DEST"
ditto -c -k --sequesterRsrc --keepParent "$DEST" "$ZIP"
echo "Local ad-hoc signed app: $DEST"
echo "Zip: $ZIP"
echo 'External tools: brew install yt-dlp deno ffmpeg aria2'
