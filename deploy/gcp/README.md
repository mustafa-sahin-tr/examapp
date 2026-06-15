# ExamApp GCP Deploy (GKE + Artifact Registry)

Bu klasor Azure VM + ACR akisini, Google Cloud uzerinde GKE + Artifact Registry modeline tasir.

## Mimari

- Container image registry: Artifact Registry
- Orkestrasyon: GKE (Standard veya Autopilot)
- CI/CD: GitHub Actions OIDC (`.github/workflows/gcp-gke-deploy.yml`)
- Kubernetes namespace: `examapp`

Not: Bu ilk gecis paketinde stateful servisler (Postgres/Redis/RabbitMQ/MinIO/Keycloak) icin iki yol var:

1. GKE icinde mevcut servisleri ayri manifestlerle calistirmak
2. Managed servisler kullanmak (onerilen)

Bu set, uygulama deploymentlarini (api, ui, gateway, worker) GKE'ye tasir ve baglanti bilgilerini `configmap`/`secret` ile disaridan alir.

## 1) GCP kaynaklarini hazirla

`gcloud` kurulu bir makinede:

```bash
cd deploy/gcp/scripts
chmod +x bootstrap-gcp.sh
PROJECT_ID=<your-project-id> REGION=europe-west1 ZONE=europe-west1-b CLUSTER_NAME=examapp-gke ARTIFACT_REPO=examapp ./bootstrap-gcp.sh
```

En ucuz ogrenme/staging profili (onerilen baslangic):

- `NODE_COUNT=1`
- `MACHINE_TYPE=e2-medium`
- `DISK_SIZE_GB=30`
- `USE_SPOT_NODES=false` (daha stabil)

Komut:

```bash
cd deploy/gcp/scripts
PROJECT_ID=<your-project-id> \
REGION=europe-west1 \
ZONE=europe-west1-b \
CLUSTER_NAME=examapp-staging \
ARTIFACT_REPO=examapp \
NODE_COUNT=1 \
MACHINE_TYPE=e2-medium \
DISK_SIZE_GB=30 \
USE_SPOT_NODES=false \
./bootstrap-gcp.sh
```

Not: Daha da ucuz deneme icin `USE_SPOT_NODES=true` kullanabilirsin; node aniden kapanabilir.

## 2) Kubernetes config dosyalarini olustur

```bash
cd /path/to/examapp
chmod +x deploy/gcp/scripts/prepare-config.sh
./deploy/gcp/scripts/prepare-config.sh
```

Sonra su dosyalari gercek degerlerle doldur:

- `deploy/gcp/k8s/configmap.yaml`
- `deploy/gcp/k8s/secret.yaml`
- `deploy/gcp/k8s/managed-certificate.yaml`
- `deploy/gcp/k8s/ingress.yaml`

## 3) Ilk manuel image build + push (opsiyonel hizli test)

```bash
gcloud auth configure-docker europe-west1-docker.pkg.dev --quiet
chmod +x deploy/gcp/scripts/local-build-push-gar.sh
./deploy/gcp/scripts/local-build-push-gar.sh europe-west1-docker.pkg.dev/<project-id>/examapp test-001 linux/amd64
```

## 4) Ilk manuel GKE deploy

```bash
gcloud container clusters get-credentials examapp-gke --zone europe-west1-b --project <project-id>
chmod +x deploy/gcp/scripts/deploy-gke.sh
GAR_REPO=europe-west1-docker.pkg.dev/<project-id>/examapp IMAGE_TAG=test-001 NAMESPACE=examapp ./deploy/gcp/scripts/deploy-gke.sh
```

Ingress IP kontrol:

```bash
kubectl -n examapp get ingress examapp-ingress
```

## 5) GitHub Actions OIDC kurulumu

GCP tarafinda:

- Workload Identity Pool + Provider olustur
- GitHub workflow service account olustur
- Service account'a roller ver:
  - `roles/container.developer`
  - `roles/artifactregistry.writer`
  - `roles/iam.workloadIdentityUser`

Repo Secrets (staging/prod environment bazli)

- `GCP_PROJECT_ID`
- `GCP_WORKLOAD_IDENTITY_PROVIDER`
- `GCP_SERVICE_ACCOUNT`
- `GCP_REGION` (ornek: `europe-west1`)
- `GCP_ARTIFACT_REPO` (ornek: `examapp`)
- `GKE_CLUSTER` (ornek: `examapp-gke`)
- `GKE_LOCATION` (zone veya region, ornek: `europe-west1-b`)
- `GKE_NAMESPACE` (ornek: `examapp`)

## 6) Pipeline calistirma

Workflow: `.github/workflows/gcp-gke-deploy.yml`

`workflow_dispatch` parametreleri:

- `mode`: `build_and_deploy` | `build_only` | `deploy_only`
- `target`: `staging` | `prod`
- `service`: `all` veya tek servis
- `image_tag`: bos birakilirsa commit SHA kullanilir

## 7) Uretim gecisi icin oneri

1. Once staging namespace/cluster'ta dogrula.
2. Cloud SQL (Postgres) private IP ile baglan.
3. Keycloak'i ya ayrica GKE'de calistir ya da managed IdP'ye gecis planla.
4. MinIO ihtiyaci devam ediyorsa StatefulSet veya GCS entegrasyonu planla.
5. DNS'i Ingress static IP'ye cevir.
