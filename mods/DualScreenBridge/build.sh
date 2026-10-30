#!/usr/bin/env bash
# Usage: build.sh MANAGED_DIR MELONLOADER_DIR [OUT_DIR]
# MANAGED_DIR is the game's DQ8MR_Data/Managed folder, MELONLOADER_DIR its MelonLoader folder.
set -euo pipefail
managed=${1:?game Managed directory}
melon=${2:?MelonLoader directory}
out=${3:-.}
here=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$out"
mcs -target:library -nostdlib -noconfig -nowarn:1701,1702 -out:"$out/DualScreenBridge.dll" \
  -r:"$managed/mscorlib.dll" -r:"$managed/System.dll" -r:"$managed/System.Core.dll" \
  -r:"$managed/UnityEngine.dll" -r:"$managed/UnityEngine.CoreModule.dll" \
  -r:"$managed/UnityEngine.ImageConversionModule.dll" -r:"$managed/UnityEngine.ScreenCaptureModule.dll" \
  -r:"$melon/MelonLoader.dll" -r:"$melon/0Harmony.dll" \
  "$here/DualScreenBridge.cs"
