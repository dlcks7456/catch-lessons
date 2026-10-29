#!/bin/sh
set -eu
helper="$1"
model="$2"
fixture="$(mktemp -d "${TMPDIR:-/tmp}/catchlessons-reinstall-test.XXXXXX")"
trap 'rm -f "$fixture/models/ggml-large-v3-turbo-q5_0.bin" "$fixture/whisper-disabled.flag" "$fixture/settings.json" "$fixture/recording.txt"; rmdir "$fixture/models" "$fixture"' EXIT
. "$helper"
mkdir -p "$fixture/models"
printf 'settings preserved' > "$fixture/settings.json"
printf 'recording preserved' > "$fixture/recording.txt"
configure_existing_whisper "$fixture"
test -f "$fixture/whisper-disabled.flag"
printf 'truncated' > "$fixture/models/ggml-large-v3-turbo-q5_0.bin"
configure_existing_whisper "$fixture"
test -f "$fixture/whisper-disabled.flag"
rm "$fixture/models/ggml-large-v3-turbo-q5_0.bin"
ln "$model" "$fixture/models/ggml-large-v3-turbo-q5_0.bin" 2>/dev/null || cp "$model" "$fixture/models/ggml-large-v3-turbo-q5_0.bin"
configure_existing_whisper "$fixture"
test ! -e "$fixture/whisper-disabled.flag"
test "$(cat "$fixture/settings.json")" = 'settings preserved'
test "$(cat "$fixture/recording.txt")" = 'recording preserved'
echo 'Mac Whisper reinstall fixture passed'
