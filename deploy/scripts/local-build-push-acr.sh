#!/usr/bin/env bash
set -euo pipefail

ACR_LOGIN_SERVER="${1:-}"
IMAGE_TAG="${2:-}"
PLATFORM="${3:-linux/amd64}"

if [[ -z "${ACR_LOGIN_SERVER}" || -z "${IMAGE_TAG}" ]]; then
  echo "Usage: $0 <acr_login_server> <image_tag> [platform]" >&2
  echo "Example: $0 sorukutusuacr.azurecr.io manual-20260209-amd64-1 linux/amd64" >&2
  exit 2
fi

echo "ACR_LOGIN_SERVER=${ACR_LOGIN_SERVER}"
echo "IMAGE_TAG=${IMAGE_TAG}"
echo "PLATFORM=${PLATFORM}"

build() {
  echo
  echo "=== $*"
  "$@"
}

build docker buildx build --push \
  --platform "${PLATFORM}" \
  -f deploy/dockerfiles/dotnet-web.Dockerfile \
  --build-arg PROJECT_PATH=api/ExamApp.Api/ExamApp.Api.csproj \
  --build-arg APP_DLL=ExamApp.Api.dll \
  --build-arg EXPOSE_PORT=5079 \
  -t "${ACR_LOGIN_SERVER}/exam-dotnet-api:${IMAGE_TAG}" \
  .

build docker buildx build --push \
  --platform "${PLATFORM}" \
  -f deploy/dockerfiles/dotnet-web.Dockerfile \
  --build-arg PROJECT_PATH=auth-api/ExamApp.Api.csproj \
  --build-arg APP_DLL=ExamApp.Api.dll \
  --build-arg EXPOSE_PORT=5079 \
  -t "${ACR_LOGIN_SERVER}/auth-api:${IMAGE_TAG}" \
  .

build docker buildx build --push \
  --platform "${PLATFORM}" \
  -f deploy/dockerfiles/dotnet-web.Dockerfile \
  --build-arg PROJECT_PATH=Services/BadgeService/BadgeService.csproj \
  --build-arg APP_DLL=BadgeService.dll \
  --build-arg EXPOSE_PORT=8006 \
  -t "${ACR_LOGIN_SERVER}/exam-badge-api:${IMAGE_TAG}" \
  .

build docker buildx build --push \
  --platform "${PLATFORM}" \
  -f deploy/dockerfiles/dotnet-web.Dockerfile \
  --build-arg PROJECT_PATH=Services/Gateway/Gateway.csproj \
  --build-arg APP_DLL=Gateway.dll \
  --build-arg EXPOSE_PORT=5678 \
  -t "${ACR_LOGIN_SERVER}/ocelot-gateway:${IMAGE_TAG}" \
  .

build docker buildx build --push \
  --platform "${PLATFORM}" \
  -f deploy/dockerfiles/dotnet-worker-net10-preview.Dockerfile \
  --build-arg PROJECT_PATH=Services/OutboxPublisher/OutboxPublisherService.csproj \
  --build-arg APP_DLL=OutboxPublisherService.dll \
  -t "${ACR_LOGIN_SERVER}/exam-outbox-publisher:${IMAGE_TAG}" \
  .

build docker buildx build --push \
  --platform "${PLATFORM}" \
  -f deploy/dockerfiles/angular-spa.Dockerfile \
  --build-arg APP_DIR=ui \
  -t "${ACR_LOGIN_SERVER}/ui:${IMAGE_TAG}" \
  .

build docker buildx build --push \
  --platform "${PLATFORM}" \
  -f deploy/dockerfiles/angular-spa.Dockerfile \
  --build-arg APP_DIR=auth-ui \
  -t "${ACR_LOGIN_SERVER}/auth-ui:${IMAGE_TAG}" \
  .

build docker buildx build --push \
  --platform "${PLATFORM}" \
  -f deploy/dockerfiles/question-detector.Dockerfile \
  -t "${ACR_LOGIN_SERVER}/exam-question-detector:${IMAGE_TAG}" \
  .

echo
echo "Done. Pushed tag: ${IMAGE_TAG}"
