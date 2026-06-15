#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${NAMESPACE:-examapp}"
GAR_REPO="${GAR_REPO:-}"
IMAGE_TAG="${IMAGE_TAG:-}"
SERVICES="${SERVICES:-all}"
K8S_DIR="${K8S_DIR:-deploy/gcp/k8s}"

if [[ -z "$GAR_REPO" || -z "$IMAGE_TAG" ]]; then
  echo "GAR_REPO and IMAGE_TAG are required." >&2
  echo "Example: GAR_REPO=europe-west1-docker.pkg.dev/my-project/examapp IMAGE_TAG=20260610-1 $0" >&2
  exit 1
fi

kubectl apply -f "$K8S_DIR/namespace.yaml"

if [[ -f "$K8S_DIR/stateful-services.yaml" ]]; then
  kubectl apply -f "$K8S_DIR/stateful-services.yaml"
else
  echo "WARN: $K8S_DIR/stateful-services.yaml not found"
fi

# These are intentionally not committed with real values.
if [[ -f "$K8S_DIR/configmap.yaml" ]]; then
  kubectl apply -f "$K8S_DIR/configmap.yaml"
else
  echo "WARN: $K8S_DIR/configmap.yaml not found (copy from configmap.example.yaml)"
fi

if [[ -f "$K8S_DIR/secret.yaml" ]]; then
  kubectl apply -f "$K8S_DIR/secret.yaml"
else
  echo "WARN: $K8S_DIR/secret.yaml not found (copy from secret.example.yaml)"
fi

kubectl apply -f "$K8S_DIR/apps.yaml"

if [[ -f "$K8S_DIR/managed-certificate.yaml" ]]; then
  kubectl apply -f "$K8S_DIR/managed-certificate.yaml"
fi

if [[ -f "$K8S_DIR/ingress.yaml" ]]; then
  kubectl apply -f "$K8S_DIR/ingress.yaml"
fi

set_image() {
  local deployment="$1"
  local container="$2"
  local image="$3"
  kubectl -n "$NAMESPACE" set image deployment/"$deployment" "$container"="$image"
}

should_deploy() {
  local name="$1"
  if [[ "$SERVICES" == "all" ]]; then
    return 0
  fi
  [[ " $SERVICES " == *" $name "* ]]
}

if should_deploy "exam-dotnet-api"; then
  set_image exam-dotnet-api exam-dotnet-api "$GAR_REPO/exam-dotnet-api:$IMAGE_TAG"
fi
if should_deploy "auth-api"; then
  set_image auth-api auth-api "$GAR_REPO/auth-api:$IMAGE_TAG"
fi
if should_deploy "exam-badge-api"; then
  set_image exam-badge-api exam-badge-api "$GAR_REPO/exam-badge-api:$IMAGE_TAG"
fi
if should_deploy "exam-outbox-publisher"; then
  set_image exam-outbox-publisher exam-outbox-publisher "$GAR_REPO/exam-outbox-publisher:$IMAGE_TAG"
fi
if should_deploy "question-detector"; then
  set_image question-detector question-detector "$GAR_REPO/exam-question-detector:$IMAGE_TAG"
fi
if should_deploy "angular-app"; then
  set_image angular-app angular-app "$GAR_REPO/ui:$IMAGE_TAG"
fi
if should_deploy "auth-ui"; then
  set_image auth-ui auth-ui "$GAR_REPO/auth-ui:$IMAGE_TAG"
fi
if should_deploy "ocelot-gateway"; then
  set_image ocelot-gateway ocelot-gateway "$GAR_REPO/ocelot-gateway:$IMAGE_TAG"
fi

for d in exam-dotnet-api auth-api exam-badge-api exam-outbox-publisher n8n question-detector angular-app auth-ui ocelot-gateway; do
  kubectl -n "$NAMESPACE" rollout status deployment/"$d" --timeout=300s || true
done

echo "Deployment completed for tag: $IMAGE_TAG"
