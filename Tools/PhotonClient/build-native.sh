#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
bundle="Assets/Plugins/macOS/PhotonWebView.bundle"
mkdir -p "$bundle/Contents/MacOS"
/usr/bin/clang -arch arm64 -arch x86_64 -mmacosx-version-min=12.0 -fobjc-arc -O2 -bundle \
  -framework AppKit -framework WebKit -framework QuartzCore "Tools/PhotonClient/PhotonWebView.m" \
  -o "$bundle/Contents/MacOS/PhotonWebView"
cat > "$bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>PhotonWebView</string>
<key>CFBundleIdentifier</key><string>com.photonfox.unity.webview</string>
<key>CFBundlePackageType</key><string>BNDL</string>
</dict></plist>
PLIST
/usr/bin/codesign --force --sign - "$bundle"
