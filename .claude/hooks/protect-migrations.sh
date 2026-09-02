#!/usr/bin/env bash
# PreToolUse(Edit|Write): EF Core tarafından üretilmiş migration dosyalarının elle düzenlenmesini engeller.
# Doğru yol: dotnet ef migrations remove -> entity'yi düzelt -> dotnet ef migrations add
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
[ -z "$FILE" ] && exit 0

case "$FILE" in
  */Migrations/*.cs)
    echo "Bloklandi: '$FILE' EF Core tarafindan uretilen bir migration dosyasi ve elle duzenlenmez." >&2
    echo "Bunun yerine: cd api/ExamApp.Api && dotnet ef migrations remove, entity'yi duzelt, sonra migrations add." >&2
    exit 2
    ;;
esac
exit 0
