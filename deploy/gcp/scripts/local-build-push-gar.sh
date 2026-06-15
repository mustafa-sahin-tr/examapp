#!/usr/bin/env bash
set -euo pipefail

GAR_REPO="${1:-}"
IMAGE_TAG="${2:-}"
PLATFORM="${3:-linux/amd64}"

if [[ -z "${GAR_REPO}" || -z "${IMAGE_TAG}" ]]; then
  echo "Usage: $0 <gar_repo> <image_tag> [platform]" >&2
  echo "Example: $0 europe-west1-docker.pkg.dev/my-project/examapp 20260610-1 linux/amd64" >&2
  exit 2
fi

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
  -t "${GAR_REPO}/exam-dotnet-api:${IMAGE_TAG}" \
  .

build docker buildx build --push \
  --platform "${PLATFORM}" \
  -f deploy/dockerfiles/dotnet-web.Dockerfile \
  --build-arg PROJECT_PATH=auth-api/ExamApp.Api.csproj \
  --build-arg APP_DLL=ExamApp.Api.dll \
  --build-arg EXPOSE_PORT=5079 \
  -t "${GAR_REPO}/auth-api:${IMAGE_TAG}" \
  .

build docker buildx build --push \
  --platform "${PLATFORM}" \
  -f deploy/dockerfiles/dotnet-web.Dockerfile \
  --build-arg PROJECT_PATH=Services/BadgeService/BadgeService.csproj \
  --build-arg APP_DLL=BadgeService.dll \
  --build-arg EXPOSE_PORT=8006 \
  -t "${GAR_REPO}/exam-badge-api:${IMAGE_TAG}" \
  .

build docker buildx build --push \
  --platform "${PLATFORM}" \
  -f deploy/dockerfiles/gateway.Dockerfile \
  --build-arg PROJECT_PATH=Services/Gateway/Gateway.csproj \
  --build-arg OCELOT_ENVIRONMENT=Production \
  --build-arg APP_DLL=Gateway.dll \
  --build-arg EXPOSE_PORT=5678 \
  -t "${GAR_REPO}/ocelot-gateway:${IMAGE_TAG}" \
  .

build docker buildx build --push \
  --platform "${PLATFORM}" \
  -f deploy/dockerfiles/dotnet-worker-net10-preview.Dockerfile \
  --build-arg PROJECT_PATH=Services/OutboxPublisher/OutboxPublisherService.csproj \
  --build-arg APP_DLL=OutboxPublisherService.dll \
  -t "${GAR_REPO}/exam-outbox-publisher:${IMAGE_TAG}" \
  .

build docker buildx build --push \
  --platform "${PLATFORM}" \
  -f deploy/dockerfiles/angular-spa.Dockerfile \
  --build-arg APP_DIR=ui \
  -t "${GAR_REPO}/ui:${IMAGE_TAG}" \
  .

build docker buildx build --push \
  --platform "${PLATFORM}" \
  -f deploy/dockerfiles/angular-spa.Dockerfile \
  --build-arg APP_DIR=auth-ui \
  -t "${GAR_REPO}/auth-ui:${IMAGE_TAG}" \
  .

build docker buildx build --push \
  --platform "${PLATFORM}" \
  -f deploy/dockerfiles/question-detector.Dockerfile \
  -t "${GAR_REPO}/exam-question-detector:${IMAGE_TAG}" \
  .

echo
echo "Done. Pushed tag: ${IMAGE_TAG}"
