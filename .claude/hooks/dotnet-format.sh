#!/usr/bin/env bash
# PostToolUse(Edit|Write): duzenlenen .cs dosyasini bicimlendirir. Asla bloklamaz.
INPUT=$(cat)

# jq varsa jq, yoksa python3 ile JSON alani okur.
json_field() {
  if command -v jq >/dev/null 2>&1; then
    printf '%s' "$INPUT" | jq -r "$1"
  else
    printf '%s' "$INPUT" | python3 -c "$2" 2>/dev/null
  fi
}
FILE=$(json_field '.tool_input.file_path // empty' "import sys,json;d=json.load(sys.stdin);print(d.get('tool_input',{}).get('file_path','') or '')")
case "$FILE" in *.cs) ;; *) exit 0 ;; esac
command -v dotnet >/dev/null 2>&1 || exit 0
[ -f "$FILE" ] || exit 0

DIR=$(dirname "$FILE")
PROJ=""
while [ "$DIR" != "/" ] && [ -n "$DIR" ]; do
  FOUND=$(find "$DIR" -maxdepth 1 -name '*.csproj' 2>/dev/null | head -1)
  if [ -n "$FOUND" ]; then PROJ="$FOUND"; break; fi
  DIR=$(dirname "$DIR")
done
[ -z "$PROJ" ] && exit 0

dotnet format "$PROJ" --include "$FILE" --no-restore --verbosity quiet >/dev/null 2>&1
exit 0
