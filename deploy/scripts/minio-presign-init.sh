#!/bin/sh
# Issue #402 (O1) — dedicated MinIO account used ONLY to presign browser image URLs.
#
# Presigned /img URLs carry the signing access key in X-Amz-Credential. Signing with the MinIO root user leaked the
# root access key to every signed-in user; this one-shot job creates a separate user whose only right is s3:GetObject
# on the image prefixes the exam API is allowed to sign (StorageAreaPolicy allowlist). The exam API reads it from
# MinioConfig:PresignAccessKey / MinioConfig:PresignSecretKey.
#
# Runs in the minio/minio image (it ships `mc`; there is no sed/grep/awk in it) with entrypoint sh, as root or as any
# non-root uid (only a writable TMPDIR is needed).
# Used by: docker-compose.yml (minio-presign-init), deploy/docker-compose.prod.yml (minio-presign-init),
# AppHost (minio-presign-init container), and inlined in deploy/gcp/k8s/stateful-services.yaml (minio-presign-init Job).
# MinioPresignAccountTests checks that the k8s copy is identical to this file and that the policy resources match
# StorageAreaPolicy — change all of them together.
#
# Secrets never appear in argv (visible in /proc/*/cmdline and `ps`): the root connection goes through the
# MC_HOST_<alias> environment variable, the presign secret through stdin. mc's config dir is a private temp dir
# removed on exit.
#
# Idempotent (safe on every start/deploy): `policy create` overwrites the policy, `user add` on an existing user
# updates its secret (picks up a rotated MINIO_PRESIGN_SECRET_KEY), attach tolerates "already attached". Afterwards
# the user must have EXACTLY this one policy and no group memberships, otherwise the job fails.
# Changing MINIO_PRESIGN_ACCESS_KEY creates a NEW user; remove the old one by hand (deploy/README.md, #402).
#
# Env: MINIO_URL (default http://minio:9000), MINIO_ROOT_USER, MINIO_ROOT_PASSWORD, MINIO_PRESIGN_ACCESS_KEY,
#      MINIO_PRESIGN_SECRET_KEY, MINIO_QUESTIONS_BUCKET (default exam-questions; same value as the exam API's
#      MinioConfig:BucketName).
set -eu

MINIO_URL="${MINIO_URL:-http://minio:9000}"
QB="${MINIO_QUESTIONS_BUCKET:-exam-questions}"
POLICY_NAME="exam-presign-getobject"

: "${MINIO_ROOT_USER:?MINIO_ROOT_USER is required}"
: "${MINIO_ROOT_PASSWORD:?MINIO_ROOT_PASSWORD is required}"
: "${MINIO_PRESIGN_ACCESS_KEY:?MINIO_PRESIGN_ACCESS_KEY is required}"
: "${MINIO_PRESIGN_SECRET_KEY:?MINIO_PRESIGN_SECRET_KEY is required}"

if [ "$MINIO_PRESIGN_ACCESS_KEY" = "$MINIO_ROOT_USER" ]; then
  echo "[minio-presign-init] MINIO_PRESIGN_ACCESS_KEY must differ from MINIO_ROOT_USER (the whole point is not to expose root)." >&2
  exit 1
fi

WORK_DIR="$(mktemp -d)"
export MC_CONFIG_DIR="$WORK_DIR/mc"
trap 'rm -rf "$WORK_DIR"' EXIT INT TERM

# mc does NOT URL-decode MC_HOST_* and splits the credentials on ':' (session token) — a ':' in the root password
# cannot be expressed there. In that case only, fall back to `mc alias set` with the keys read from stdin (still not
# argv; the alias lands in the private MC_CONFIG_DIR above, deleted on exit).
case "$MINIO_URL" in
  http://*) scheme="http://"; hostport="${MINIO_URL#http://}" ;;
  https://*) scheme="https://"; hostport="${MINIO_URL#https://}" ;;
  *) echo "[minio-presign-init] MINIO_URL must start with http:// or https://" >&2; exit 1 ;;
esac
case "$MINIO_ROOT_PASSWORD" in
  *:*) USE_ALIAS=1 ;;
  *) USE_ALIAS=0; export MC_HOST_exam="${scheme}${MINIO_ROOT_USER}:${MINIO_ROOT_PASSWORD}@${hostport}" ;;
esac

connect() {
  if [ "$USE_ALIAS" = 1 ]; then
    printf '%s\n%s\n' "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" | mc alias set exam "$MINIO_URL" >/dev/null 2>&1 || return 1
  fi
  mc admin info exam >/dev/null 2>&1
}

# MinIO has no healthcheck in the compose files; wait until the admin API accepts the root credentials.
attempt=0
until connect; do
  attempt=$((attempt + 1))
  if [ "$attempt" -ge 60 ]; then
    echo "[minio-presign-init] MinIO at $MINIO_URL not reachable with root credentials after 60 attempts." >&2
    exit 1
  fi
  sleep 2
done

POLICY_FILE="$WORK_DIR/policy.json"
cat > "$POLICY_FILE" <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["s3:GetObject"],
      "Resource": [
        "arn:aws:s3:::${QB}/questions/*",
        "arn:aws:s3:::${QB}/answers/*",
        "arn:aws:s3:::${QB}/passages/*",
        "arn:aws:s3:::worksheets/*",
        "arn:aws:s3:::exams/*",
        "arn:aws:s3:::study-pages/books/*",
        "arn:aws:s3:::study-pages/pages/*"
      ]
    }
  ]
}
EOF

mc admin policy create exam "$POLICY_NAME" "$POLICY_FILE" >/dev/null
# Secret via stdin: `mc admin user add TARGET ACCESSKEY` prompts for the secret key when it is not an argument.
printf '%s\n' "$MINIO_PRESIGN_SECRET_KEY" | mc admin user add exam "$MINIO_PRESIGN_ACCESS_KEY" >/dev/null
# Re-runs: attaching an already attached policy is an error in newer mc — the verification below decides.
mc admin policy attach exam "$POLICY_NAME" --user "$MINIO_PRESIGN_ACCESS_KEY" >/dev/null 2>&1 || true

# The user must have exactly this policy (policyName is a comma-separated list) and belong to no group (a group
# policy would widen its rights). Matched with `case` because the image has no grep/jq.
info="$(mc admin user info exam "$MINIO_PRESIGN_ACCESS_KEY" --json)"
case "$info" in
  *'"status":"success"'*) ;;
  *) echo "[minio-presign-init] could not read user $MINIO_PRESIGN_ACCESS_KEY: $info" >&2; exit 1 ;;
esac
case "$info" in
  *"\"policyName\":\"$POLICY_NAME\""*) ;;
  *) echo "[minio-presign-init] $MINIO_PRESIGN_ACCESS_KEY must have exactly the $POLICY_NAME policy: $info" >&2; exit 1 ;;
esac
case "$info" in
  *'"memberOf"'*) echo "[minio-presign-init] $MINIO_PRESIGN_ACCESS_KEY must not be a member of any group: $info" >&2; exit 1 ;;
esac

echo "[minio-presign-init] presign user $MINIO_PRESIGN_ACCESS_KEY ready (only policy $POLICY_NAME: s3:GetObject on image prefixes, no groups)."
