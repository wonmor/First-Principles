#!/bin/bash
# Archive + upload the Mac App Store build (Xcode project from BuildHelper.BuildMacAppStore).
# Uses Xcode cloud-managed signing through the App Store Connect API key, like the iOS upload,
# so no local distribution keys or keychain prompts are needed.
#   tools/mas_upload.sh            archive + upload
#   tools/mas_upload.sh --no-upload  archive + export a local .pkg only
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ_DIR="$ROOT/Builds/MAS/FirstPrinciples"
PBX="$PROJ_DIR/FirstPrinciples.xcodeproj/project.pbxproj"
OUT="$ROOT/Builds/MAS/out"
KEY=$(ls ~/.appstoreconnect/private_keys/AuthKey_*.p8 | head -1); KID=$(basename "$KEY" .p8 | sed 's/AuthKey_//')
ISSUER=953f41b8-a46a-403b-8a76-3fd6d8d8a9a5
AUTH=(-allowProvisioningUpdates -authenticationKeyPath "$KEY" -authenticationKeyID "$KID" -authenticationKeyIssuerID "$ISSUER")
mkdir -p "$OUT"

# Steamworks belongs to the Steam build only: drop the plug-in and its project references.
grep -v "steam_api.bundle" "$PBX" > "$PBX.tmp" && mv "$PBX.tmp" "$PBX"
rm -rf "$PROJ_DIR/First Principles/PlugIns/steam_api.bundle"

# Export compliance: the game uses no non-exempt encryption (same as iOS).
/usr/libexec/PlistBuddy -c "Add :ITSAppUsesNonExemptEncryption bool false" "$PROJ_DIR/First Principles/Info.plist" 2>/dev/null || true

cat > "$PROJ_DIR/MAS.entitlements" <<'PL'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>com.apple.security.app-sandbox</key><true/>
<key>com.apple.security.network.client</key><true/>
</dict></plist>
PL

xcodebuild -project "$PROJ_DIR/FirstPrinciples.xcodeproj" -scheme "First Principles" -configuration Release \
  -destination "generic/platform=macOS" -archivePath "$OUT/fp-mac.xcarchive" archive "${AUTH[@]}" \
  DEVELOPMENT_TEAM=Z64KRUX3W3 CODE_SIGN_STYLE=Automatic CODE_SIGN_IDENTITY="Apple Development" \
  CODE_SIGN_ENTITLEMENTS=MAS.entitlements ENABLE_HARDENED_RUNTIME=YES

DEST=upload; [ "${1:-}" = "--no-upload" ] && DEST=export
cat > "$OUT/export.plist" <<PL
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>method</key><string>app-store-connect</string>
<key>destination</key><string>$DEST</string>
<key>teamID</key><string>Z64KRUX3W3</string>
<key>signingStyle</key><string>automatic</string>
<key>manageAppVersionAndBuildNumber</key><false/>
</dict></plist>
PL
xcodebuild -exportArchive -archivePath "$OUT/fp-mac.xcarchive" -exportOptionsPlist "$OUT/export.plist" -exportPath "$OUT/export" "${AUTH[@]}"
