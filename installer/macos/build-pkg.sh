#!/bin/bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"
version="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$root/Directory.Build.props")"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'Invalid version' >&2; exit 1; }
case "$(uname -m)" in arm64) arch=arm64 ;; x86_64) arch=x64 ;; *) echo 'Unsupported Mac architecture' >&2; exit 1;; esac
bash "$root/build-macos.sh"
app="$root/artifacts/CatchLessons.app"
tmp="$(mktemp -d "${TMPDIR:-/tmp}/catchlessons-pkg.XXXXXX")"
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/app-scripts" "$tmp/model-scripts" "$tmp/root/Library/Application Support/CatchLessons/models" "$tmp/resources" "$root/artifacts/installers"
cp "$root/installer/macos/app-postinstall" "$tmp/app-scripts/postinstall"
cp "$root/installer/macos/model-postinstall" "$tmp/model-scripts/postinstall"
chmod 755 "$tmp/app-scripts/postinstall" "$tmp/model-scripts/postinstall"
models="$tmp/root/Library/Application Support/CatchLessons/models"
fetch_model() {
 local name="$1" repo="$2" hash="$3"
 if [ -f "$root/artifacts/app/models/$name" ]; then cp "$root/artifacts/app/models/$name" "$models/$name"; else curl -fL --retry 2 "https://huggingface.co/$repo/resolve/main/$name" -o "$models/$name"; fi
 echo "$hash  $models/$name" | shasum -a 256 -c -
}
fetch_model ggml-large-v3-turbo-q5_0.bin ggerganov/whisper.cpp 394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2
fetch_model ggml-silero-v6.2.0.bin ggml-org/whisper-vad 2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987
cp "$root/installer/macos/welcome.html" "$root/Assets/installer-art.png" "$tmp/resources/"
pkgbuild --component "$app" --install-location /Applications --scripts "$tmp/app-scripts" --identifier local.catchlessons.app --version "$version" "$tmp/app.pkg"
pkgbuild --root "$tmp/root" --install-location / --scripts "$tmp/model-scripts" --identifier local.catchlessons.models --version "$version" "$tmp/models.pkg"
sed "s/@VERSION@/$version/g" "$root/installer/macos/distribution.xml" > "$tmp/distribution.xml"
sign=()
if [ -n "${CAPTION_INSTALLER_SIGN_IDENTITY:-}" ]; then sign=(--sign "$CAPTION_INSTALLER_SIGN_IDENTITY"); fi
productbuild --distribution "$tmp/distribution.xml" --resources "$tmp/resources" --package-path "$tmp" "${sign[@]}" "$root/artifacts/installers/CatchLessons-macOS-$arch-$version.pkg"
echo "Built package: $root/artifacts/installers/CatchLessons-macOS-$arch-$version.pkg"
