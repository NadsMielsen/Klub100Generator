#!/bin/bash
# Publish Klub100Generator as a .app bundle for macOS (MacCatalyst).
# Output: bin/Release/net9.0-maccatalyst/maccatalyst-x64/publish/
#
# Requirements on the recipient's Mac:
#   brew install yt-dlp ffmpeg
#
# The .app bundle does not bundle yt-dlp or ffmpeg — the app expects them
# on the system PATH (installed via Homebrew).

set -e

CONFIGURATION="Release"
RUNTIME_ARCHS="maccatalyst-x64 maccatalyst-arm64"

for RUNTIME in $RUNTIME_ARCHS; do
    echo "Publishing for $RUNTIME ($CONFIGURATION)..."
    dotnet publish Klub100Generator.csproj \
        -c "$CONFIGURATION" \
        -r "$RUNTIME"
done

echo ""
echo "Publish succeeded!"
echo "Output: bin/$CONFIGURATION/net9.0-maccatalyst/"

for RUNTIME in $RUNTIME_ARCHS; do
    APP_DIR="bin/$CONFIGURATION/net9.0-maccatalyst/$RUNTIME/publish/Klub100Generator.app"
    if [ -d "$APP_DIR" ]; then
        SIZE=$(du -sh "$APP_DIR" 2>/dev/null | cut -f1)
        echo "  $RUNTIME: $APP_DIR ($SIZE)"
    fi
done

echo ""
echo "To distribute: zip the .app bundle for each architecture."
echo "Without notarization, the recipient must right-click -> Open the first time."
