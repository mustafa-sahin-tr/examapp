#!/usr/bin/env bash
# Dev-only: soru bankasını sıfırlar — tüm sorular (ve onlara bağlı her şey), MinIO'daki soru/şık/paragraf
# görselleri, işaretleme ekranı kayıtları (LabelingPages/LabelingResults) ve bekleyen QuestionCreated outbox
# mesajları silinir. İsteğe bağlı olarak question-detector eğitim verisi (dataset/) de sıfırlanır; o zaman
# işaretleme ekranı her sayfayı yeniden "işaretlenmemiş" sayar.
#
# TRUNCATE CASCADE'in ulaştığı tablolar (FK zinciri, dev DB'de doğrulandı): Questions, Answers, Passage,
# QuestionSubTopics, TestQuestions, TestInstanceQuestions (öğrenci cevapları), DailyQuestionSetItems,
# PracticeSessionQuestions, WorksheetComments + WorksheetCommentReports (TÜM test yorumları), LabelingPages,
# LabelingResults. Testler (Worksheets), taksonomi, kullanıcılar, çalışma sayfaları ve LabelingPromptVersions kalır.
#
# ASLA prod'a karşı çalıştırma. Konteynerleri docker üzerinden bulur (Aspire: postgres-xxxx / minio-xxxx;
# docker-compose: pg_container_dev / minio); birden fazla aday varsa ya da emin değilse env ile seç:
#
#   PG_CONTAINER=postgres-jspmxngy MINIO_CONTAINER=minio-ntprfveq deploy/scripts/dev-reset-questions.sh
#
# Env (hepsi isteğe bağlı):
#   PG_CONTAINER / PG_DB (worksheet)        — kullanıcı/şifre konteynerin kendi POSTGRES_USER/POSTGRES_PASSWORD'ü
#   MINIO_CONTAINER / MINIO_BUCKET (exam-questions) — kimlik konteynerin MINIO_ROOT_USER/MINIO_ROOT_PASSWORD'ü
#   DATASET_DIR (question-detector/dataset)
#
# Bayraklar:
#   --wipe-dataset   question-detector/dataset içeriğini de sil (eğitim verisi! Drive arşivine bakmaz).
#   --yes            onay sorma.
#   --dry-run        ne yapılacağını göster, hiçbir şey silme.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

WIPE_DATASET=0
ASSUME_YES=0
DRY_RUN=0
for arg in "$@"; do
  case "$arg" in
    --wipe-dataset) WIPE_DATASET=1 ;;
    --yes) ASSUME_YES=1 ;;
    --dry-run) DRY_RUN=1 ;;
    -h|--help) sed -n '2,24p' "$0"; exit 0 ;;
    *) echo "Bilinmeyen argüman: $arg" >&2; exit 2 ;;
  esac
done

PG_DB="${PG_DB:-worksheet}"
MINIO_BUCKET="${MINIO_BUCKET:-exam-questions}"
DATASET_DIR="${DATASET_DIR:-$REPO_ROOT/question-detector/dataset}"

# Soru görsellerinin durduğu önekler (StorageAreaPolicy / upload yolları). books/ ve pages/ (çalışma sayfaları)
# ile worksheets/exams/student-avatars bucket'larına dokunulmaz.
MINIO_PREFIXES=(questions/ answers/ passages/ question-transfer/)

pick_container() {
  # $1: açıklama, $2: isim deseni (grep -E)
  local label="$1" pattern="$2"
  local matches
  matches="$(docker ps --format '{{.Names}}' | grep -E "$pattern" || true)"
  local count
  count="$(printf '%s\n' "$matches" | sed '/^$/d' | wc -l | tr -d ' ')"
  if [[ "$count" -eq 0 ]]; then
    echo "❌ Çalışan $label konteyneri bulunamadı (desen: $pattern)." >&2
    exit 1
  fi
  if [[ "$count" -gt 1 ]]; then
    echo "❌ Birden fazla $label konteyneri var, hangisini kullanacağımı env ile söyle:" >&2
    printf '   %s\n' $matches >&2
    exit 1
  fi
  printf '%s' "$matches"
}

PG_CONTAINER="${PG_CONTAINER:-$(pick_container "Postgres" '^(postgres-[a-z0-9]+|pg_container_dev)$')}"
MINIO_CONTAINER="${MINIO_CONTAINER:-$(pick_container "MinIO" '^(minio-[a-z0-9]+|minio)$')}"

psql_exec() {
  # Kimlik konteynerin kendi env'inden (Aspire ve compose ikisi de POSTGRES_USER/POSTGRES_PASSWORD verir).
  docker exec -i -e PG_DB="$PG_DB" "$PG_CONTAINER" sh -c \
    'export PGPASSWORD="$POSTGRES_PASSWORD"; exec psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$PG_DB" "$@"' psql "$@"
}

mc_exec() {
  # mc, minio/minio imajında var; alias'ı env ile veriyoruz (deploy/scripts/minio-presign-init.sh ile aynı yöntem).
  docker exec -i "$MINIO_CONTAINER" sh -c \
    'export MC_HOST_local="http://${MINIO_ROOT_USER}:${MINIO_ROOT_PASSWORD}@127.0.0.1:9000"; exec mc "$@"' mc "$@"
}

echo "Hedefler"
echo "  Postgres : $PG_CONTAINER  (db=$PG_DB)"
echo "  MinIO    : $MINIO_CONTAINER  (bucket=$MINIO_BUCKET önekler: ${MINIO_PREFIXES[*]})"
if [[ "$WIPE_DATASET" -eq 1 ]]; then
  echo "  Dataset  : $DATASET_DIR  (SİLİNECEK — question-detector eğitim verisi)"
else
  echo "  Dataset  : dokunulmayacak (--wipe-dataset ile silinir)"
fi
echo

echo "Mevcut sayılar:"
psql_exec -At -c "
  SELECT '  Questions=' || (SELECT count(*) FROM \"Questions\")
      || '  Answers=' || (SELECT count(*) FROM \"Answers\")
      || '  Passage=' || (SELECT count(*) FROM \"Passage\")
      || '  LabelingPages=' || (SELECT count(*) FROM \"LabelingPages\")
      || '  QuestionCreated outbox (bekleyen)=' || (SELECT count(*) FROM \"OutboxMessages\" WHERE \"Type\" LIKE '%QuestionCreated%' AND \"ProcessedAt\" IS NULL);"
echo "MinIO nesneleri:"
for p in "${MINIO_PREFIXES[@]}"; do
  n="$(mc_exec ls --recursive "local/${MINIO_BUCKET}/${p}" 2>/dev/null | wc -l | tr -d ' ')"
  echo "  ${MINIO_BUCKET}/${p} → $n nesne"
done
if [[ "$WIPE_DATASET" -eq 1 && -d "$DATASET_DIR" ]]; then
  echo "Dataset: $(find "$DATASET_DIR" -type f | wc -l | tr -d ' ') dosya, $(grep -c . "$DATASET_DIR/manifest.jsonl" 2>/dev/null || echo 0) manifest satırı"
fi
echo

if [[ "$DRY_RUN" -eq 1 ]]; then
  echo "--dry-run: hiçbir şey silinmedi."
  exit 0
fi

if [[ "$ASSUME_YES" -ne 1 ]]; then
  read -r -p "Bunların hepsi geri alınamaz şekilde silinecek. Devam? [yes/N] " answer
  [[ "$answer" == "yes" ]] || { echo "Vazgeçildi."; exit 0; }
fi

echo "▶ Postgres: soru tabloları boşaltılıyor (CASCADE bağlı tabloları da boşaltır, NOTICE satırlarında listelenir)…"
# TRUNCATE ... CASCADE: Questions'a FK ile bağlı her tablo (TestQuestions, TestInstanceQuestions, QuestionSubTopics,
# DailyQuestionSetItems, PracticeSessionQuestions, QuestionTransfer*Maps, WorksheetComments…) otomatik boşalır.
# Passage ve Labeling* ayrıca sayılır (Questions'a bağlı değiller / ters yönde bağlılar).
psql_exec <<'SQL'
BEGIN;
TRUNCATE TABLE "Questions", "Answers", "Passage", "LabelingResults", "LabelingPages" RESTART IDENTITY CASCADE;
DELETE FROM "OutboxMessages" WHERE "Type" LIKE '%QuestionCreated%' AND "ProcessedAt" IS NULL;
COMMIT;
SQL

echo "▶ MinIO: görseller siliniyor…"
for p in "${MINIO_PREFIXES[@]}"; do
  # Önek boşsa mc rm hata verir; sessizce geç.
  mc_exec rm --recursive --force "local/${MINIO_BUCKET}/${p}" >/dev/null 2>&1 || true
  echo "  ${MINIO_BUCKET}/${p} temiz"
done

if [[ "$WIPE_DATASET" -eq 1 ]]; then
  echo "▶ question-detector dataset siliniyor: $DATASET_DIR"
  if [[ -d "$DATASET_DIR" ]]; then
    # Klasörün kendisi kalsın (konteyner bind mount'u /app/dataset olarak görüyor), içi boşalsın.
    find "$DATASET_DIR" -mindepth 1 -delete
  fi
  echo "  işaretleme ekranı artık hiçbir sayfayı işaretlenmiş saymaz (GET /labeled-pages boş döner)."
fi

echo
echo "✅ Bitti. Çalışan exam API / BadgeService'i yeniden başlatman gerekmez; bellek önbellekleri soru içermez."
echo "   Tarayıcıda açık işaretleme sayfasını yenile (işaretlenmiş sayfa listesi açılışta çekilir)."
