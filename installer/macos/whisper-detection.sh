#!/bin/sh
has_existing_whisper() {
  folder="$1"
  for item in \
    "ggml-large-v3-turbo-q5_0.bin:394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2" \
    "ggml-small-q5_1.bin:ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb" \
    "ggml-large-v3-q5_0.bin:d75795ecff3f83b5faa89d1900604ad8c780abd5739fae406de19f23ecd98ad1"
  do
    name=${item%%:*}; expected=${item#*:}; file="$folder/$name"
    if [ -f "$file" ] && [ "$(/usr/bin/shasum -a 256 "$file" | /usr/bin/awk '{print $1}')" = "$expected" ]; then return 0; fi
  done
  return 1
}
configure_existing_whisper() {
  root="$1"
  /bin/mkdir -p "$root"
  if has_existing_whisper "$root/models"; then /bin/rm -f "$root/whisper-disabled.flag"; else /usr/bin/touch "$root/whisper-disabled.flag"; fi
}
