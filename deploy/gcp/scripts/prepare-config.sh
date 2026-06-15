#!/usr/bin/env bash
set -euo pipefail

K8S_DIR="${K8S_DIR:-deploy/gcp/k8s}"

if [[ -f "$K8S_DIR/configmap.yaml" || -f "$K8S_DIR/secret.yaml" ]]; then
  echo "configmap.yaml or secret.yaml already exists. Aborting to avoid overwrite." >&2
  exit 1
fi

cp "$K8S_DIR/configmap.example.yaml" "$K8S_DIR/configmap.yaml"
cp "$K8S_DIR/secret.example.yaml" "$K8S_DIR/secret.yaml"
cp "$K8S_DIR/managed-certificate.example.yaml" "$K8S_DIR/managed-certificate.yaml"
cp "$K8S_DIR/ingress.example.yaml" "$K8S_DIR/ingress.yaml"

echo "Created editable files:"
echo "- $K8S_DIR/configmap.yaml"
echo "- $K8S_DIR/secret.yaml"
echo "- $K8S_DIR/managed-certificate.yaml"
echo "- $K8S_DIR/ingress.yaml"
