#!/usr/bin/env bash
# PreToolUse(Edit|Write): dosyaya gomulu credential yazilmasini ve .env'e dokunulmasini engeller.
# Heuristiktir: yanlis alarm verirse asagidaki pattern'i daralt.
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
BODY=$(json_field '[.tool_input.content, .tool_input.new_string] | map(select(. != null)) | join("\n")' "import sys,json;d=json.load(sys.stdin).get('tool_input',{});print('\\n'.join([str(x) for x in [d.get('content'),d.get('new_string')] if x]))")
[ -z "$FILE" ] && exit 0

# 1) .env dosyasina yazma (.env.example serbest)
case "$(basename "$FILE")" in
  .env|.env.local|.env.production)
    echo "Bloklandi: '$FILE' gercek credential tutar ve agent tarafindan yazilmaz." >&2
    echo "Yeni degisken gerekiyorsa .env.example icine placeholder ile ekle ve bana soyle." >&2
    exit 2
    ;;
esac

[ -z "$BODY" ] && exit 0

# 2) Ozel anahtar blogu
if printf '%s' "$BODY" | grep -qE 'BEGIN (RSA |EC |OPENSSH |PGP )?PRIVATE KEY'; then
  echo "Bloklandi: icerikte private key blogu var." >&2
  exit 2
fi

# 3) Gomulu credential ("password": "abc123" / ApiKey=... / client_secret: ...)
#    Placeholder iceren satirlar (${VAR}, <...>, CHANGEME, example, your-) muaf.
CANDIDATES=$(printf '%s' "$BODY" \
  | grep -iEn '(password|passwd|pwd|secret|api[_-]?key|client[_-]?secret|access[_-]?token|connectionstring)[^A-Za-z0-9]{0,4}[:=][[:space:]]*["'"'"']?[^"'"'"'[:space:]]{8,}' \
  | grep -viE '\$\{|\$\(|<[^>]+>|changeme|example|placeholder|your[-_]|xxx|\*\*\*|env\.|configuration\[|getenv|builder\.|options\.' || true)

if [ -n "$CANDIDATES" ]; then
  echo "Bloklandi: '$FILE' icinde gomulu credential gorunuyor:" >&2
  printf '%s\n' "$CANDIDATES" | head -3 >&2
  echo "Degeri konfigurasyondan oku (.env / appsettings + Configuration binding). Yanlis alarmsa .claude/hooks/secret-guard.sh'i daralt." >&2
  exit 2
fi
exit 0
