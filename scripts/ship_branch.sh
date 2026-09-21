#!/usr/bin/env bash
# ship-branch.sh — commit + push + PR + merge + update local master, tek komutla.
#
# Kullanım:
#   scripts/ship-branch.sh [-y|--yes] [branch-name] [commit-message]
#
# branch-name verilmezse, script çalıştırıldığı anda checkout'ta olan branch kullanılır.
# -y/--yes verilirse tüm onay sorularına otomatik "evet" denir (dikkatli kullan).
#
# Örnek:
#   scripts/ship-branch.sh issue-127-vortex-magnet-tile "feat: spawn Vortex tile (#127)"
#   scripts/ship-branch.sh                                # mevcut branch'i ship eder
#   scripts/ship-branch.sh -y                             # mevcut branch'i, sorusuz ship eder
#
# commit-message verilmezse, git commit mesajı için varsayılan editörü açar.
# PR başlığı/gövdesi otomatik olarak branch'teki commit'lerden (gh pr create --fill) türetilir.
#
# Adımlar:
#   1. Verilen branch'e geç (yoksa hata verir — branch'i önce sen oluşturmalısın)
#   2. Değişiklikleri gözden geçirip onay alır, sonra stage eder
#   3. Commit eder
#   4. origin'e push eder (-u ile upstream ayarlanır)
#   5. Pull request açar (gh pr create --fill)
#   6. Onay alıp PR'ı merge eder ve remote branch'i siler
#   7. Local master'e geçip origin/master'i çeker

set -euo pipefail

AUTO_YES=0
ARGS=()
for arg in "$@"; do
  case "$arg" in
    -y|--yes)
      AUTO_YES=1
      ;;
    *)
      ARGS+=("$arg")
      ;;
  esac
done

BRANCH="${ARGS[0]:-}"
COMMIT_MESSAGE="${ARGS[1]:-}"

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

if [[ -z "$BRANCH" ]]; then
  BRANCH="$(git branch --show-current)"
  if [[ -z "$BRANCH" ]]; then
    echo "Hata: mevcut branch tespit edilemedi (detached HEAD olabilir). Kullanım: $0 <branch-name> [commit-message]" >&2
    exit 1
  fi
  echo "-> branch belirtilmedi, mevcut branch kullanılıyor: $BRANCH"
fi

confirm() {
  local prompt="$1"
  if [[ "$AUTO_YES" -eq 1 ]]; then
    echo "$prompt [y/N] -> y (--yes)"
    return 0
  fi
  read -r -p "$prompt [y/N] " reply
  [[ "$reply" =~ ^[Yy]$ ]]
}

CURRENT_BRANCH="$(git branch --show-current)"
if [[ "$CURRENT_BRANCH" != "$BRANCH" ]]; then
  if ! git show-ref --verify --quiet "refs/heads/$BRANCH"; then
    echo "Hata: '$BRANCH' local'de yok. Önce oluştur: git checkout -b $BRANCH" >&2
    exit 1
  fi
  echo "-> '$BRANCH' branch'ine geçiliyor"
  git checkout "$BRANCH"
fi

# Uncommitted değişiklik yoksa commit adımını atla
if [[ -z "$(git status --porcelain)" ]]; then
  echo "-> Stage edilecek değişiklik yok, commit adımı atlanıyor."
else
  echo "-> Değişiklikler:"
  git status --short
  echo
  if ! confirm "Bu değişiklikleri stage edip commit'leyeyim mi? (git add -A)"; then
    echo "İptal edildi." >&2
    exit 1
  fi

  git add -A
  echo
  echo "-> Stage edilenler (secret/credential görünen dosya var mı kontrol et):"
  git status --short

  if [[ -n "$COMMIT_MESSAGE" ]]; then
    git commit -m "$COMMIT_MESSAGE"
  else
    git commit
  fi
fi

echo
if ! confirm "'$BRANCH' origin'e push edilsin mi?"; then
  echo "Push atlandı, script burada duruyor." >&2
  exit 0
fi
git push -u origin "$BRANCH"

echo
if ! confirm "Pull request açılsın mı? (gh pr create --fill)"; then
  echo "PR açma atlandı, script burada duruyor." >&2
  exit 0
fi
PR_URL="$(gh pr create --fill)"
echo "$PR_URL"
PR_NUMBER="$(echo "$PR_URL" | grep -oE '[0-9]+$')"

# Branch adından issue numarasını çıkar (örn. issue-127-vortex-magnet-tile -> 127)
# ve PR gövdesinde "Closes #<issue>" yoksa ekle, böylece merge'de issue otomatik kapanır.
ISSUE_NUMBER="$(echo "$BRANCH" | grep -oE '^issue-[0-9]+' | grep -oE '[0-9]+' || true)"
if [[ -n "$ISSUE_NUMBER" ]]; then
  PR_BODY="$(gh pr view "$PR_NUMBER" --json body --jq .body)"
  if ! echo "$PR_BODY" | grep -qiE '(close[sd]?|fix(e[sd])?|resolve[sd]?) #'"$ISSUE_NUMBER"'\b'; then
    printf '%s\n\nCloses #%s\n' "$PR_BODY" "$ISSUE_NUMBER" | gh pr edit "$PR_NUMBER" --body-file -
    echo "-> PR gövdesine 'Closes #$ISSUE_NUMBER' eklendi."
  fi
fi

echo
if ! confirm "PR #$PR_NUMBER merge edilip remote branch silinsin mi?"; then
  echo "Merge atlandı, script burada duruyor." >&2
  exit 0
fi
gh pr merge "$PR_NUMBER" --merge --delete-branch

echo
echo "-> master'e geçilip origin/master çekiliyor"
git checkout master
git pull origin master

echo
echo "Tamamlandı: PR #$PR_NUMBER merge edildi, local master güncel."