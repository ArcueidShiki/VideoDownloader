#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
# A local app uses tools already installed on this Mac; no Homebrew dylibs copied.
# Set OUTPUT_DIR outside the checkout to preserve the deliverable during cleanup.
OUT=${OUTPUT_DIR:-"$ROOT/release"}
mkdir -p "$OUT"
xcodebuild -project "$ROOT/macos/AnyDownloader.xcodeproj" -scheme AnyDownloader \
  -configuration Release -derivedDataPath "$ROOT/macos/.build-app" \
  CODE_SIGN_IDENTITY=- CODE_SIGN_STYLE=Manual DEVELOPMENT_TEAM= build
APP="$ROOT/macos/.build-app/Build/Products/Release/AnyDownloader.app"
EXECUTABLE=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist")
[ -x "$APP/Contents/MacOS/$EXECUTABLE" ]
[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundlePackageType' "$APP/Contents/Info.plist")" = APPL ]
codesign --verify --deep --strict "$APP"
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
DEST="$OUT/AnyDownloader-$STAMP.app"
[ ! -e "$DEST" ]
ditto "$APP" "$DEST"
ditto -c -k --sequesterRsrc --keepParent "$DEST" "$OUT/AnyDownloader-$STAMP.zip"
echo "Local ad-hoc signed app: $DEST"
echo 'External tools: brew install yt-dlp deno ffmpeg aria2'
