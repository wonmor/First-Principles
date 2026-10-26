#!/bin/bash
# Sign + notarize the macOS Steam build with a Developer ID Application certificate.
# Needs the Developer ID identity in this Mac's keychain (create or export it as the Apple
# Account Holder: developer.apple.com > Certificates > Developer ID Application).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP="$ROOT/Builds/Steam/macOS/First Principles.app"
ID="${DEVID_IDENTITY:-Developer ID Application: Wonmo Seong (Z64KRUX3W3)}"
KEY=$(ls ~/.appstoreconnect/private_keys/AuthKey_*.p8 | head -1)
KID=$(basename "$KEY" .p8 | sed 's/AuthKey_//')
ISSUER=953f41b8-a46a-403b-8a76-3fd6d8d8a9a5
security find-identity -v -p codesigning | grep -q "$ID" || { echo "Missing identity: $ID"; exit 1; }
cat > /tmp/fp-steam.entitlements <<'PL'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>com.apple.security.cs.disable-library-validation</key><true/>
<key>com.apple.security.cs.allow-dyld-environment-variables</key><true/>
</dict></plist>
PL
find "$APP/Contents" \( -name "*.dylib" -o -name "*.bundle" -o -name "*.so" \) -print0 | xargs -0 -I{} codesign --force --timestamp --options runtime -s "$ID" "{}"
codesign --force --deep --timestamp --options runtime --entitlements /tmp/fp-steam.entitlements -s "$ID" "$APP"
ditto -c -k --keepParent "$APP" /tmp/fp-steam-mac.zip
xcrun notarytool submit /tmp/fp-steam-mac.zip --key "$KEY" --key-id "$KID" --issuer "$ISSUER" --wait
xcrun stapler staple "$APP"
spctl -a -vv "$APP"
