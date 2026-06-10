#!/usr/bin/env bash
# Download a Piper TTS voice model (sherpa-onnx bundle) for local TTS notifications.
# Usage:
#   ./download-tts-model.sh                          # default voice (en_US-lessac-medium)
#   ./download-tts-model.sh --voice en_US-amy-medium # specific voice
#   ./download-tts-model.sh --dir /path/to/models    # custom target directory
#   ./download-tts-model.sh --list                   # list known voices
#   ./download-tts-model.sh --force                  # re-download even if present

set -euo pipefail

# Resolve project root
if [[ -n "${CC_ROOT:-}" ]]; then
    PROJECT_ROOT="$CC_ROOT"
elif SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" 2>/dev/null && pwd)"; then
    PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
else
    PROJECT_ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || {
        echo "ERROR: Cannot determine project root. Set CC_ROOT env var."
        exit 1
    }
fi

VOICE="en_US-lessac-medium"
TARGET_DIR="$PROJECT_ROOT/models/tts"
FORCE=0
BASE_URL="https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models"

# Voices auto-assigned by Command Central (slot 1 → first, slot 2 → second, ...)
KNOWN_VOICES=(
    en_US-lessac-medium
    en_US-amy-medium
    en_US-arctic-medium
    en_US-danny-low
    en_US-joe-medium
    en_US-kathleen-low
    en_US-kusal-medium
    en_US-libritts_r-medium
    en_US-ryan-medium
)

while [[ $# -gt 0 ]]; do
    case "$1" in
        --voice) VOICE="$2"; shift 2 ;;
        --dir)   TARGET_DIR="$2"; shift 2 ;;
        --force) FORCE=1; shift ;;
        --list)
            echo "Voices auto-assigned per slot (download more for distinct per-instance voices):"
            printf '  %s\n' "${KNOWN_VOICES[@]}"
            echo ""
            echo "Full catalog: https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models (vits-piper-*)"
            exit 0
            ;;
        -h|--help)
            echo "Usage: $0 [--voice NAME] [--dir PATH] [--force] [--list]"
            echo ""
            echo "  --voice NAME  Piper voice to download (default: en_US-lessac-medium)"
            echo "  --dir PATH    Target models directory (default: <repo>/models/tts)"
            echo "  --force       Re-download even if the model is already present"
            echo "  --list        List known voice names"
            exit 0
            ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

BUNDLE="vits-piper-$VOICE"
DEST_DIR="$TARGET_DIR/$BUNDLE"
URL="$BASE_URL/$BUNDLE.tar.bz2"

if [[ -f "$DEST_DIR/$VOICE.onnx" && $FORCE -eq 0 ]]; then
    echo "Voice '$VOICE' already present at $DEST_DIR (use --force to re-download)"
    exit 0
fi

echo "Downloading $BUNDLE.tar.bz2 ..."
mkdir -p "$TARGET_DIR"
ARCHIVE="$(mktemp --suffix=.tar.bz2)"
trap 'rm -f "$ARCHIVE"' EXIT

curl -L --fail --progress-bar -o "$ARCHIVE" "$URL" || {
    echo "ERROR: Download failed: $URL"
    echo "Check the voice name with --list, or browse:"
    echo "  https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models"
    exit 1
}

echo "Extracting to $TARGET_DIR ..."
tar -xjf "$ARCHIVE" -C "$TARGET_DIR"

# Verify the expected layout
if [[ -f "$DEST_DIR/$VOICE.onnx" && -f "$DEST_DIR/tokens.txt" && -d "$DEST_DIR/espeak-ng-data" ]]; then
    echo ""
    echo "Done. Voice '$VOICE' installed:"
    echo "  $DEST_DIR"
    echo ""
    echo "The daemon picks it up on next start (Tts:NotificationEngine = SherpaOnnx)."
else
    echo "ERROR: Extracted bundle is missing expected files in $DEST_DIR"
    echo "Expected: $VOICE.onnx, tokens.txt, espeak-ng-data/"
    exit 1
fi
