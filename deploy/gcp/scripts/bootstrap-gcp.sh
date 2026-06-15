#!/usr/bin/env bash
set -euo pipefail

PROJECT_ID="${PROJECT_ID:-}"
REGION="${REGION:-europe-west1}"
ZONE="${ZONE:-europe-west1-b}"
CLUSTER_NAME="${CLUSTER_NAME:-examapp-gke}"
ARTIFACT_REPO="${ARTIFACT_REPO:-examapp}"
NODE_COUNT="${NODE_COUNT:-1}"
MACHINE_TYPE="${MACHINE_TYPE:-e2-medium}"
DISK_SIZE_GB="${DISK_SIZE_GB:-30}"
USE_SPOT_NODES="${USE_SPOT_NODES:-false}"

if [[ -z "$PROJECT_ID" ]]; then
  echo "PROJECT_ID is required. Example: PROJECT_ID=my-gcp-project" >&2
  exit 1
fi

echo "[1/7] Setting active project"
gcloud config set project "$PROJECT_ID"

echo "[2/7] Enabling required APIs"
gcloud services enable \
  artifactregistry.googleapis.com \
  container.googleapis.com \
  cloudbuild.googleapis.com \
  compute.googleapis.com \
  iamcredentials.googleapis.com

echo "[3/7] Creating Artifact Registry repository if missing"
if ! gcloud artifacts repositories describe "$ARTIFACT_REPO" --location="$REGION" >/dev/null 2>&1; then
  gcloud artifacts repositories create "$ARTIFACT_REPO" \
    --repository-format=docker \
    --location="$REGION" \
    --description="ExamApp Docker images"
else
  echo "Artifact Registry repo already exists: $ARTIFACT_REPO"
fi

echo "[4/7] Creating GKE cluster if missing"
if ! gcloud container clusters describe "$CLUSTER_NAME" --zone "$ZONE" >/dev/null 2>&1; then
  create_args=(
    "$CLUSTER_NAME"
    --zone "$ZONE"
    --num-nodes "$NODE_COUNT"
    --machine-type "$MACHINE_TYPE"
    --disk-size "$DISK_SIZE_GB"
    --enable-ip-alias
  )

  if [[ "$USE_SPOT_NODES" == "true" ]]; then
    create_args+=(--spot)
  fi

  gcloud container clusters create "${create_args[@]}"
else
  echo "Cluster already exists: $CLUSTER_NAME"
fi

echo "[5/7] Getting kubectl credentials"
gcloud container clusters get-credentials "$CLUSTER_NAME" --zone "$ZONE"

echo "[6/7] Creating namespace if missing"
kubectl get namespace examapp >/dev/null 2>&1 || kubectl create namespace examapp

echo "[7/7] Done"
echo "Artifact Registry: ${REGION}-docker.pkg.dev/${PROJECT_ID}/${ARTIFACT_REPO}"
echo "Cluster: ${CLUSTER_NAME} (${ZONE})"
echo "Node profile: count=${NODE_COUNT}, machine=${MACHINE_TYPE}, disk=${DISK_SIZE_GB}GB, spot=${USE_SPOT_NODES}"
