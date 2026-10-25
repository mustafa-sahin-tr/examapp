#!/usr/bin/env bash
# Dev-only: soru bankasını sıfırlar — tüm sorular (ve onlara bağlı her şey), öğrencilerin test oturumları
# (TestInstances) ve bu cevaplardan türeyen puan/rozet verisi (exam DB + badge DB), MinIO'daki soru/şık/paragraf
# görselleri, işaretleme ekranı kayıtları (LabelingPages/LabelingResults) ve bekleyen QuestionCreated /
# StudentPointsChanged / StudentBadgeEarned outbox mesajları silinir. İsteğe bağlı olarak question-detector eğitim
# verisi (dataset/) de sıfırlanır; o zaman işaretleme ekranı her sayfayı yeniden "işaretlenmemiş" sayar.
#
# worksheet DB — TRUNCATE CASCADE'in ulaştığı tablolar (FK zinciri, dev DB'de doğrulandı): Questions, Answers, Passage,
# QuestionSubTopics, TestQuestions, TestInstanceQuestions (öğrenci cevapları), DailyQuestionSetItems,
# PracticeSessionQuestions, WorksheetComments + WorksheetCommentReports (TÜM test yorumları), LabelingPages,
# LabelingResults. Ayrıca açıkça: TestInstances (soruları silinen oturum "boş test yapılmış" gibi kalmasın — Questions'a
# FK ile bağlı değil, CASCADE ulaşmıyor) ve puan/rozet tabloları: StudentPoints, StudentPointHistories, StudentBadges,
# StudentRewards, StudentSpecialEvents, Leaderboards, StudentBadgeProjections, StudentDailyXps (#225/#422 projeksiyonları);
# Students.ProgressResetAtUtc NULL'lanır. Testler (Worksheets), taksonomi, kullanıcılar, çalışma sayfaları ve
# LabelingPromptVersions kalır.
#
# badge DB — öğrencinin kendi "ilerlemeyi sıfırla" ucunun (POST api/student/me/reset, #243, BadgeService
# UserResetService) sildiği tabloların TÜM öğrenciler için toplu hali: BadgeEarned, StudentBadgeProgresses,
# StudentDailyActivities, StudentQuestionAggregates, StudentSubjectAggregates, ProcessedAnswerSubmissions,
# AnswerPointAwards, UserResetMarkers; Notifications'tan yalnız Type='BadgeEarned' satırları; bekleyen
# StudentPointsChanged/StudentBadgeEarned outbox mesajları. BadgeDefinitions, diğer bildirimler, locale tercihleri kalır.
# (Uygulama içi uç öğrenci başına ve soft-delete'tir; burası dev'de her şeyi sert siler.)
#
# İsteğe bağlı gruplar (bayraklarla; CASCADE erişimi dev DB'nin FK grafiğinde doğrulandı):
#   --wipe-tests  Worksheets (testler) + DailyQuestionSets + PracticeSessions. CASCADE: TestQuestions, TestInstances,
#                 WorksheetAssignments, WorksheetReminders, WorksheetAccessRequests/Grants, WorksheetComments(+Reports),
#                 DailyQuestionSetItems, PracticeSessionQuestions. MinIO 'worksheets' (test arka planları) ve 'exams'
#                 bucket'ları boşalır. Books/BookTests KALIR: StudyItems (çalışma sayfaları) onlara FK ile bağlı,
#                 silmek çalışma sayfalarını ve program takvimlerini de götürürdü. TestPrototypes (kullanılmıyor) kalır.
#   --wipe-social Mesajlaşma (Conversations, DirectMessages, DirectMessageReports, DirectMessageBlocks), yorum
#                 moderasyon olayları (WorksheetCommentModerationEvents) ve badge DB'de TÜM bildirimler
#                 (Notifications, NotificationEventLogs, HiddenCommentTombstones).
#   --wipe-logs   İşlenmiş outbox mesajları (üç DB; bekleyenler ve hatalılar kalır), LoginEvents, AdminDataAccessLogs,
#                 AdminUserActionLogs, QuestionTransfer* işleri, badge ProcessedLoginAttempts; Hangfire'da yalnız
#                 bitmiş işler (Succeeded/Deleted/Failed) ve stats sayaçları — recurring/zamanlanmış işler kalır.
#   --all         üçü birden (dataset hariç).
# Hiçbir bayrakta yok (bilerek): veli bağları/davetleri (silinirse öğrenciler #441 grace süresine düşer), kullanıcılar
# (seed-teachers / seed-tutors ile yeniden üret), randevular, çalışma sayfaları, programlar, taksonomi, BadgeDefinitions.
#
# ASLA prod'a karşı çalıştırma. Konteynerleri docker üzerinden bulur (Aspire: postgres-xxxx / minio-xxxx;
# docker-compose: pg_container_dev / minio); birden fazla aday varsa ya da emin değilse env ile seç:
#
#   PG_CONTAINER=postgres-jspmxngy MINIO_CONTAINER=minio-ntprfveq deploy/scripts/dev-reset-questions.sh
#
# Env (hepsi isteğe bağlı):
#   PG_CONTAINER / PG_DB (worksheet) / BADGE_DB (badge) / IDENTITY_DB (identity) — kullanıcı/şifre konteynerin kendi POSTGRES_USER/POSTGRES_PASSWORD'ü
#   MINIO_CONTAINER / MINIO_BUCKET (exam-questions) — kimlik konteynerin MINIO_ROOT_USER/MINIO_ROOT_PASSWORD'ü
#   DATASET_DIR (question-detector/dataset)
#
# Bayraklar:
#   --wipe-dataset   question-detector/dataset içeriğini de sil (eğitim verisi! Drive arşivine bakmaz).
#   --wipe-tests / --wipe-social / --wipe-logs / --all   yukarıdaki isteğe bağlı gruplar.
#   --yes            onay sorma.
#   --dry-run        ne yapılacağını göster, hiçbir şey silme.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

WIPE_DATASET=0
WIPE_TESTS=0
WIPE_SOCIAL=0
WIPE_LOGS=0
ASSUME_YES=0
DRY_RUN=0
for arg in "$@"; do
  case "$arg" in
    --wipe-dataset) WIPE_DATASET=1 ;;
    --wipe-tests) WIPE_TESTS=1 ;;
    --wipe-social) WIPE_SOCIAL=1 ;;
    --wipe-logs) WIPE_LOGS=1 ;;
    --all) WIPE_TESTS=1; WIPE_SOCIAL=1; WIPE_LOGS=1 ;;
    --yes) ASSUME_YES=1 ;;
    --dry-run) DRY_RUN=1 ;;
    -h|--help) sed -n '2,/^$/p' "$0"; exit 0 ;;
    *) echo "Bilinmeyen argüman: $arg" >&2; exit 2 ;;
  esac
done

PG_DB="${PG_DB:-worksheet}"
BADGE_DB="${BADGE_DB:-badge}"
IDENTITY_DB="${IDENTITY_DB:-identity}"
MINIO_BUCKET="${MINIO_BUCKET:-exam-questions}"
DATASET_DIR="${DATASET_DIR:-$REPO_ROOT/question-detector/dataset}"

# Soru görsellerinin durduğu önekler (StorageAreaPolicy / upload yolları). books/ ve pages/ (çalışma sayfaları)
# ile student-avatars/study-pages bucket'larına dokunulmaz; worksheets/exams yalnız --wipe-tests ile boşalır.
MINIO_PREFIXES=(questions/ answers/ passages/ question-transfer/)
TEST_BUCKETS=(worksheets exams)

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

psql_exec_db() {
  # $1: veritabanı adı. Kimlik konteynerin kendi env'inden (Aspire ve compose ikisi de POSTGRES_USER/POSTGRES_PASSWORD verir).
  local db="$1"; shift
  docker exec -i -e PG_DB="$db" "$PG_CONTAINER" sh -c \
    'export PGPASSWORD="$POSTGRES_PASSWORD"; exec psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$PG_DB" "$@"' psql "$@"
}
psql_exec()       { psql_exec_db "$PG_DB" "$@"; }
badge_psql_exec() { psql_exec_db "$BADGE_DB" "$@"; }
identity_psql_exec() { psql_exec_db "$IDENTITY_DB" "$@"; }

mc_exec() {
  # mc, minio/minio imajında var; alias'ı env ile veriyoruz (deploy/scripts/minio-presign-init.sh ile aynı yöntem).
  docker exec -i "$MINIO_CONTAINER" sh -c \
    'export MC_HOST_local="http://${MINIO_ROOT_USER}:${MINIO_ROOT_PASSWORD}@127.0.0.1:9000"; exec mc "$@"' mc "$@"
}

echo "Hedefler"
echo "  Postgres : $PG_CONTAINER  (db=$PG_DB + $BADGE_DB + $IDENTITY_DB)"
echo "  MinIO    : $MINIO_CONTAINER  (bucket=$MINIO_BUCKET önekler: ${MINIO_PREFIXES[*]})"
if [[ "$WIPE_DATASET" -eq 1 ]]; then
  echo "  Dataset  : $DATASET_DIR  (SİLİNECEK — question-detector eğitim verisi)"
else
  echo "  Dataset  : dokunulmayacak (--wipe-dataset ile silinir)"
fi
onoff() { [[ "$1" -eq 1 ]] && echo "SİLİNECEK" || echo "dokunulmayacak ($2 ile silinir)"; }
echo "  Testler  : $(onoff "$WIPE_TESTS" --wipe-tests)"
echo "  Sosyal   : $(onoff "$WIPE_SOCIAL" --wipe-social)"
echo "  Loglar   : $(onoff "$WIPE_LOGS" --wipe-logs)"
echo

echo "Mevcut sayılar:"
psql_exec -At -c "
  SELECT '  Questions=' || (SELECT count(*) FROM \"Questions\")
      || '  Answers=' || (SELECT count(*) FROM \"Answers\")
      || '  Passage=' || (SELECT count(*) FROM \"Passage\")
      || '  LabelingPages=' || (SELECT count(*) FROM \"LabelingPages\")
      || '  QuestionCreated outbox (bekleyen)=' || (SELECT count(*) FROM \"OutboxMessages\" WHERE \"Type\" LIKE '%QuestionCreated%' AND \"ProcessedAt\" IS NULL)
      || E'\n  TestInstances=' || (SELECT count(*) FROM \"TestInstances\")
      || '  StudentPoints=' || (SELECT count(*) FROM \"StudentPoints\")
      || '  StudentBadges=' || (SELECT count(*) FROM \"StudentBadges\")
      || '  StudentDailyXps=' || (SELECT count(*) FROM \"StudentDailyXps\");"
badge_psql_exec -At -c "
  SELECT '  [badge] BadgeEarned=' || (SELECT count(*) FROM \"BadgeEarned\")
      || '  AnswerPointAwards=' || (SELECT count(*) FROM \"AnswerPointAwards\")
      || '  StudentDailyActivities=' || (SELECT count(*) FROM \"StudentDailyActivities\")
      || '  BadgeEarned bildirimi=' || (SELECT count(*) FROM \"Notifications\" WHERE \"Type\" = 'BadgeEarned');"
echo "MinIO nesneleri:"
for p in "${MINIO_PREFIXES[@]}"; do
  n="$(mc_exec ls --recursive "local/${MINIO_BUCKET}/${p}" 2>/dev/null | wc -l | tr -d ' ')"
  echo "  ${MINIO_BUCKET}/${p} → $n nesne"
done
if [[ "$WIPE_TESTS" -eq 1 ]]; then
  psql_exec -At -c "
    SELECT '  [tests] Worksheets=' || (SELECT count(*) FROM \"Worksheets\")
        || '  WorksheetAssignments=' || (SELECT count(*) FROM \"WorksheetAssignments\")
        || '  DailyQuestionSets=' || (SELECT count(*) FROM \"DailyQuestionSets\")
        || '  PracticeSessions=' || (SELECT count(*) FROM \"PracticeSessions\");"
  for b in "${TEST_BUCKETS[@]}"; do
    echo "  [tests] $b → $(mc_exec ls --recursive "local/${b}" 2>/dev/null | wc -l | tr -d ' ') nesne"
  done
fi
if [[ "$WIPE_SOCIAL" -eq 1 ]]; then
  psql_exec -At -c "
    SELECT '  [social] Conversations=' || (SELECT count(*) FROM \"Conversations\")
        || '  DirectMessages=' || (SELECT count(*) FROM \"DirectMessages\")
        || '  WorksheetCommentModerationEvents=' || (SELECT count(*) FROM \"WorksheetCommentModerationEvents\");"
  badge_psql_exec -At -c "
    SELECT '  [social] Notifications=' || (SELECT count(*) FROM \"Notifications\")
        || '  NotificationEventLogs=' || (SELECT count(*) FROM \"NotificationEventLogs\");"
fi
if [[ "$WIPE_LOGS" -eq 1 ]]; then
  psql_exec -At -c "
    SELECT '  [logs] işlenmiş outbox=' || (SELECT count(*) FROM \"OutboxMessages\" WHERE \"ProcessedAt\" IS NOT NULL)
        || '  LoginEvents=' || (SELECT count(*) FROM \"LoginEvents\")
        || '  Admin*Logs=' || ((SELECT count(*) FROM \"AdminDataAccessLogs\") + (SELECT count(*) FROM \"AdminUserActionLogs\"))
        || '  QuestionTransferJobs=' || (SELECT count(*) FROM \"QuestionTransferJobs\")
        || '  hangfire bitmiş iş=' || (SELECT count(*) FROM hangfire.job WHERE statename IN ('Succeeded','Deleted','Failed'));"
  badge_psql_exec -At -c "
    SELECT '  [logs] badge işlenmiş outbox=' || (SELECT count(*) FROM \"OutboxMessages\" WHERE \"ProcessedAt\" IS NOT NULL)
        || '  ProcessedLoginAttempts=' || (SELECT count(*) FROM \"ProcessedLoginAttempts\");"
  identity_psql_exec -At -c "
    SELECT '  [logs] identity işlenmiş outbox=' || (SELECT count(*) FROM \"OutboxMessages\" WHERE \"ProcessedAt\" IS NOT NULL);"
fi
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

echo "▶ Postgres ($PG_DB): soru, oturum ve puan/rozet tabloları boşaltılıyor (CASCADE bağlı tabloları da boşaltır, NOTICE satırlarında listelenir)…"
# TRUNCATE ... CASCADE: Questions'a FK ile bağlı her tablo (TestQuestions, TestInstanceQuestions, QuestionSubTopics,
# DailyQuestionSetItems, PracticeSessionQuestions, QuestionTransfer*Maps, WorksheetComments…) otomatik boşalır.
# Passage ve Labeling* ayrıca sayılır (Questions'a bağlı değiller / ters yönde bağlılar). TestInstances'a yalnız
# TestInstanceQuestions bağlı; Questions'tan ulaşılmadığı için açıkça listede. Puan/rozet tabloları (StudentResetJob'un
# soft-delete ettiği set + #422 projeksiyonları) başka tabloya bağlı değil.
psql_exec <<'SQL'
BEGIN;
TRUNCATE TABLE "Questions", "Answers", "Passage", "LabelingResults", "LabelingPages",
  "TestInstances",
  "StudentPoints", "StudentPointHistories", "StudentBadges", "StudentRewards", "StudentSpecialEvents", "Leaderboards",
  "StudentBadgeProjections", "StudentDailyXps"
  RESTART IDENTITY CASCADE;
UPDATE "Students" SET "ProgressResetAtUtc" = NULL WHERE "ProgressResetAtUtc" IS NOT NULL;
DELETE FROM "OutboxMessages" WHERE "Type" LIKE '%QuestionCreated%' AND "ProcessedAt" IS NULL;
COMMIT;
SQL

echo "▶ Postgres ($BADGE_DB): puan/rozet hesap tabloları boşaltılıyor…"
# BadgeService UserResetService'in öğrenci başına sildiği tabloların toplu hali. BadgeDefinitions ve BadgeEarned dışı
# bildirimler kalır. Notifications'ta (UserId, SourceBadgeDefinitionId) unique index var: BadgeEarned bildirimleri
# silinmezse rozet yeniden kazanılınca yeni bildirim yazılamaz (#146).
badge_psql_exec <<'SQL'
BEGIN;
TRUNCATE TABLE "BadgeEarned", "StudentBadgeProgresses", "StudentDailyActivities", "StudentQuestionAggregates",
  "StudentSubjectAggregates", "ProcessedAnswerSubmissions", "AnswerPointAwards", "UserResetMarkers"
  RESTART IDENTITY CASCADE;
DELETE FROM "Notifications" WHERE "Type" = 'BadgeEarned';
DELETE FROM "OutboxMessages" WHERE ("Type" LIKE '%StudentPointsChanged%' OR "Type" LIKE '%StudentBadgeEarned%') AND "ProcessedAt" IS NULL;
COMMIT;
SQL

if [[ "$WIPE_TESTS" -eq 1 ]]; then
  echo "▶ Postgres ($PG_DB): testler, günlük soru setleri ve pratik oturumları boşaltılıyor…"
  # Books/BookTests bilerek listede değil: StudyItems -> Books/BookTests FK'sı CASCADE ile çalışma sayfalarını götürür.
  psql_exec <<'SQL'
BEGIN;
TRUNCATE TABLE "Worksheets", "DailyQuestionSets", "PracticeSessions" RESTART IDENTITY CASCADE;
COMMIT;
SQL
fi

if [[ "$WIPE_SOCIAL" -eq 1 ]]; then
  echo "▶ Postgres ($PG_DB + $BADGE_DB): mesajlar ve bildirimler boşaltılıyor…"
  psql_exec <<'SQL'
BEGIN;
TRUNCATE TABLE "Conversations", "DirectMessages", "DirectMessageReports", "DirectMessageBlocks",
  "WorksheetCommentModerationEvents" RESTART IDENTITY CASCADE;
COMMIT;
SQL
  badge_psql_exec <<'SQL'
BEGIN;
TRUNCATE TABLE "Notifications", "NotificationEventLogs", "HiddenCommentTombstones" RESTART IDENTITY CASCADE;
COMMIT;
SQL
fi

if [[ "$WIPE_LOGS" -eq 1 ]]; then
  echo "▶ Postgres: loglar, işlenmiş outbox mesajları ve bitmiş Hangfire işleri siliniyor…"
  # Outbox: yalnız ProcessedAt dolu satırlar; bekleyen/hatalı mesajlar yayınlanmayı beklesin.
  # Hangfire: hangfire.state/jobparameter job'a ON DELETE CASCADE bağlı. hash/set'teki recurring tanımlarına ve
  # zamanlanmış (Scheduled/Enqueued/Processing) işlere dokunulmaz.
  psql_exec <<'SQL'
BEGIN;
DELETE FROM "OutboxMessages" WHERE "ProcessedAt" IS NOT NULL;
TRUNCATE TABLE "LoginEvents", "AdminDataAccessLogs", "AdminUserActionLogs",
  "QuestionTransferJobs", "QuestionTransferExportBundles", "QuestionTransferExportMaps", "QuestionTransferImportMaps"
  RESTART IDENTITY CASCADE;
DELETE FROM hangfire.job WHERE statename IN ('Succeeded', 'Deleted', 'Failed');
DELETE FROM hangfire.list WHERE key IN ('succeeded', 'deleted');
DELETE FROM hangfire.counter WHERE key LIKE 'stats:%';
DELETE FROM hangfire.aggregatedcounter WHERE key LIKE 'stats:%';
COMMIT;
SQL
  badge_psql_exec <<'SQL'
BEGIN;
DELETE FROM "OutboxMessages" WHERE "ProcessedAt" IS NOT NULL;
TRUNCATE TABLE "ProcessedLoginAttempts" RESTART IDENTITY;
COMMIT;
SQL
  identity_psql_exec -c 'DELETE FROM "OutboxMessages" WHERE "ProcessedAt" IS NOT NULL;'
fi

echo "▶ MinIO: görseller siliniyor…"
for p in "${MINIO_PREFIXES[@]}"; do
  # Önek boşsa mc rm hata verir; sessizce geç.
  mc_exec rm --recursive --force "local/${MINIO_BUCKET}/${p}" >/dev/null 2>&1 || true
  echo "  ${MINIO_BUCKET}/${p} temiz"
done
if [[ "$WIPE_TESTS" -eq 1 ]]; then
  for b in "${TEST_BUCKETS[@]}"; do
    # Bucket'ın kendisi ve politikası kalsın, yalnız içi boşalsın.
    mc_exec rm --recursive --force "local/${b}" >/dev/null 2>&1 || true
    echo "  ${b} temiz"
  done
fi

if [[ "$WIPE_DATASET" -eq 1 ]]; then
  echo "▶ question-detector dataset siliniyor: $DATASET_DIR"
  if [[ -d "$DATASET_DIR" ]]; then
    # Klasörün kendisi kalsın (konteyner bind mount'u /app/dataset olarak görüyor), içi boşalsın.
    find "$DATASET_DIR" -mindepth 1 -delete
  fi
  echo "  işaretleme ekranı artık hiçbir sayfayı işaretlenmiş saymaz (GET /labeled-pages boş döner)."
fi

echo
echo "✅ Bitti. Çalışan exam API / BadgeService'i yeniden başlatman gerekmez; bellek önbellekleri soru/puan içermez."
echo "   Öğrencilerin test geçmişi, puanı, rozetleri ve liderlik tablosu da sıfırlandı (tüm öğrenciler)."
[[ "$WIPE_TESTS" -eq 1 ]] && echo "   Testler, atamalar, günlük soru setleri ve pratik oturumları silindi (kitaplar ve çalışma sayfaları duruyor)."
[[ "$WIPE_SOCIAL" -eq 1 ]] && echo "   Mesajlar ve tüm bildirimler silindi."
[[ "$WIPE_LOGS" -eq 1 ]] && echo "   Loglar, işlenmiş outbox mesajları ve bitmiş Hangfire işleri silindi."
echo "   Tarayıcıda açık işaretleme sayfasını yenile (işaretlenmiş sayfa listesi açılışta çekilir)."
