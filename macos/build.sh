#!/bin/zsh
set -eu
cd "${0:A:h}"

cache="$PWD/.build"
destination="$PWD/Codex Battery.app"
mkdir -p "$cache"
stage=$(mktemp -d "$cache/release.XXXXXXXX")
trap 'rm -rf -- "$stage"' EXIT
bundle="$stage/Codex Battery.app"
mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"

export CLANG_MODULE_CACHE_PATH="$cache/clang"
xcrun swiftc -swift-version 5 -module-cache-path "$cache/modules" \
    Tools/MakeIcon.swift Sources/{PetAppIconArt,PetIconFile}.swift \
    -o "$stage/MakeIcon" -framework Cocoa
"$stage/MakeIcon" "$bundle/Contents/Resources/AppIcon.icns"

for arch in arm64 x86_64; do
    xcrun swiftc -O -target "$arch-apple-macos13.0" -swift-version 5 \
        -module-name CodexBattery -module-cache-path "$cache/modules" \
        Sources/*.swift -o "$stage/CodexBattery-$arch" -framework Cocoa
done
executable="$bundle/Contents/MacOS/CodexBattery"
xcrun lipo -create "$stage/CodexBattery-arm64" "$stage/CodexBattery-x86_64" -output "$executable"
xcrun lipo "$executable" -verify_arch arm64 x86_64
cp Info.plist "$bundle/Contents/Info.plist"
codesign --force --sign - --timestamp=none "$bundle"
codesign --verify --strict --all-architectures "$bundle"

# Exchange complete bundles on the same volume. A failed build keeps the previous
# product intact, and old personal resources cannot carry over into a new release.
python3 - "$bundle" "$destination" <<'PY'
import ctypes
import os
import plistlib
import sys

source, target = sys.argv[1:]
if not os.path.lexists(target):
    os.rename(source, target)
else:
    if os.path.islink(target) or not os.path.isdir(target):
        raise SystemExit("The output path is not a Codex Battery app directory.")
    with open(os.path.join(target, "Contents", "Info.plist"), "rb") as f:
        if plistlib.load(f).get("CFBundleIdentifier") != "app.codexbattery.macos":
            raise SystemExit("The output path belongs to a different app.")
    swap = ctypes.CDLL(None, use_errno=True).renamex_np
    swap.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_uint]
    if swap(os.fsencode(source), os.fsencode(target), 0x00000002):  # RENAME_SWAP
        err = ctypes.get_errno()
        raise OSError(err, os.strerror(err), target)
PY
echo "$destination"
