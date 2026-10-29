#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
version="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$root/Directory.Build.props")"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'Invalid version' >&2; exit 1; }
arch="$(uname -m)"
case "$arch" in arm64) rid=osx-arm64 ;; x86_64) rid=osx-x64 ;; *) echo "Unsupported architecture: $arch"; exit 1 ;; esac
app="$root/artifacts/CatchLessons.app"
out="$app/Contents/MacOS"
mkdir -p "$out/native" "$app/Contents/Resources"
icon_tmp="$(mktemp -d "${TMPDIR:-/tmp}/catchlessons-icon.XXXXXX")"
trap 'rm -rf "$icon_tmp"' EXIT
mkdir "$icon_tmp/App.iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$root/Assets/app-icon.png" --out "$icon_tmp/App.iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size*2)); sips -z "$double" "$double" "$root/Assets/app-icon.png" --out "$icon_tmp/App.iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$icon_tmp/App.iconset" -o "$app/Contents/Resources/app.icns"
dotnet publish src/CatchLessons.Desktop/CatchLessons.Desktop.csproj -c Release -r "$rid" --self-contained true -o "$out"
xcrun swiftc -O -parse-as-library -target "$arch-apple-macos14.0" native/mac/AudioCapture.swift -framework ScreenCaptureKit -framework CoreMedia -framework AudioToolbox -o "$out/native/caption-audio"
cat > "$app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>local.catchlessons.desktop</string>
<key>CFBundleName</key><string>CatchLessons</string>
<key>CFBundleIconFile</key><string>app.icns</string>
<key>CFBundleExecutable</key><string>CatchLessons.Desktop</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleVersion</key><string>1</string>
<key>CFBundleShortVersionString</key><string>1.0</string>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>NSHighResolutionCapable</key><true/>
<key>NSScreenCaptureUsageDescription</key><string>재생 중인 시스템 오디오를 자막으로 표시합니다.</string>
</dict></plist>
PLIST
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $version" "$app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $version" "$app/Contents/Info.plist"
cat > "$root/artifacts/mac-entitlements.plist" <<'PLIST'
<?xml version="1.0"?><plist version="1.0"><dict><key>com.apple.security.cs.allow-jit</key><true/><key>com.apple.security.cs.disable-library-validation</key><true/></dict></plist>
PLIST
codesign --force --sign - "$out/native/caption-audio"
codesign --force --deep --sign "${CAPTION_APP_SIGN_IDENTITY:--}" --options runtime --entitlements "$root/artifacts/mac-entitlements.plist" "$app"
echo "Built: $app"
