# `azure-vm-acr-deploy.yml` — Komut Bazlı Tam Açıklama

Bu döküman, `azure-vm-acr-deploy.yml` workflow dosyasındaki **her bir komutu** sırasıyla, hiçbirini atlamadan açıklamaktadır.

---

## Genel Yapı

| Alan | Değer |
|---|---|
| Workflow adı | `Build & Deploy (Azure VM + ACR)` |
| Tetikleyici | Yalnızca `workflow_dispatch` (elle çalıştırma) |
| Toplam job sayısı | 5 (`build_and_push`, `deploy_after_build`, `deploy_only`, `bootstrap_minimal`, `diagnose_tls`) |

---

## Tetikleyici Girdiler (`on.workflow_dispatch.inputs`)

```yaml
on:
  workflow_dispatch:
    inputs:
      mode: ...
      target: ...
      service: ...
      image_tag: ...
```

| Girdi | Açıklama |
|---|---|
| `mode` | Workflow'un hangi modda çalışacağını belirtir: `build_and_deploy`, `build_only`, `deploy_only`, `bootstrap_minimal`, `diagnose_tls` |
| `target` | Hangi ortama deploy edileceğini belirtir: `staging` veya `prod` |
| `service` | Hangi servisin build edilip push edileceğini belirtir. `all` seçilirse tüm servisler build edilir |
| `image_tag` | Docker imajına verilecek tag. Boş bırakılırsa varsayılan olarak `git sha` (commit hash) kullanılır |

---

## İzinler (`permissions`)

```yaml
permissions:
  contents: read
  id-token: write
```

- `contents: read` → Repo içeriğini okuma izni (checkout için gerekli)
- `id-token: write` → Azure OIDC (Federated Identity) ile şifresiz kimlik doğrulama için gerekli JWT token üretme izni

---

## Eşzamanlılık Kontrolü (`concurrency`)

```yaml
concurrency:
  group: examapp-${{ ... }}
  cancel-in-progress: true
```

- Aynı anda birden fazla workflow çalışmasını engeller.
- Aynı `group` adıyla yeni bir run başlarsa, **çalışmakta olan run iptal edilir**.
- `group` adı; `workflow_dispatch` ise seçilen `target`, tag push ise `prod`, diğer durumlarda `staging` olarak belirlenir.

---

## Ortam Değişkenleri (`env`)

```yaml
env:
  TARGET: ...
  SERVICE: ...
  IMAGE_TAG: ...
  IMAGE_TAG_INPUT: ...
```

| Değişken | Açıklama |
|---|---|
| `TARGET` | `staging` veya `prod`. workflow_dispatch ise `inputs.target`, tag push ise `prod`, diğerlerinde `staging` |
| `SERVICE` | Hangi servisin build edileceği. workflow_dispatch ise `inputs.service`, diğerlerinde `all` |
| `IMAGE_TAG` | Image tag'i. `inputs.image_tag` doluysa o kullanılır, boşsa `github.sha` (commit hash) kullanılır |
| `IMAGE_TAG_INPUT` | Kullanıcının girdiği ham tag değeri. Boşsa boş string olur |

---

## JOB 1: `build_and_push`

Bu job Docker image'larını build edip Azure Container Registry'e (ACR) push eder.

### Koşul

```yaml
if: ${{ github.event_name != 'workflow_dispatch' || (inputs.mode != 'deploy_only' && inputs.mode != 'bootstrap_minimal' && inputs.mode != 'diagnose_tls') }}
```

`deploy_only`, `bootstrap_minimal` veya `diagnose_tls` modlarında bu job **çalışmaz**.

---

### Step 1 — `Checkout`

```yaml
- name: Checkout
  uses: actions/checkout@v4
  with:
    fetch-depth: 0
```

- Repo kodunu GitHub Actions runner'ına klonlar.
- `fetch-depth: 0` → Tüm git geçmişini çeker (tüm branch ve tag'ler dahil). Commit hash'e dayalı tag oluşturma ve değişiklik tespiti için gereklidir.

---

### Step 2 — `Detect changed services`

```yaml
- name: Detect changed services
  id: changes
  shell: bash
  run: |
    set -euo pipefail
    svc="${SERVICE:-all}"
    if [[ -z "$svc" ]]; then
      svc="all"
    fi
    echo "services=${svc}" >> "$GITHUB_OUTPUT"
```

| Komut | Açıklama |
|---|---|
| `set -euo pipefail` | Bash güvenli mod: hata olursa dur (`-e`), tanımsız değişkende hata ver (`-u`), pipe'ta hata yakala (`-o pipefail`) |
| `svc="${SERVICE:-all}"` | `SERVICE` değişkeni boşsa `all` olarak ata |
| `echo "services=${svc}" >> "$GITHUB_OUTPUT"` | Tespit edilen servis adını bir sonraki step'e aktarmak için GitHub Actions output'una yazar |

---

### Step 3 — `Free disk space on runner`

```yaml
if: ${{ steps.changes.outputs.services != '__none__' }}
```

Yalnızca build edecek servis varsa çalışır.

```bash
echo "Disk before cleanup:" && df -h
```
Temizlikten önce disk kullanımını gösterir.

```bash
sudo rm -rf /usr/share/dotnet /opt/ghc /usr/local/lib/android || true
```
GitHub hosted runner'larda önceden yüklü gelen büyük toolchain'leri siler (.NET SDK, GHC, Android SDK). Bu workflow Docker içinde build yapacağı için bunlara ihtiyaç yoktur. `|| true` ile hata olsa da devam eder.

```bash
sudo apt-get clean || true
```
APT paket cache'ini temizler, disk alanı açar.

```bash
docker system prune -af || true
docker builder prune -af || true
```
- `docker system prune -af` → Kullanılmayan tüm container, image, network ve volume'ları siler (`-a` = tüm image'lar, `-f` = onay sormadan)
- `docker builder prune -af` → BuildKit cache'ini tamamen temizler

```bash
echo "Disk after cleanup:" && df -h
```
Temizlik sonrası disk kullanımını gösterir.

---

### Step 4 — `Validate Azure secrets present`

Gerekli secret'ların tanımlı olup olmadığını kontrol eder. Eksik olan her secret için `::error::` annotation'ı yazdırır ve `missing=1` ile işaretler. Son satırda `exit $missing` ile eksik varsa workflow'u durdurur.

```bash
set -euo pipefail
missing=0
```
Güvenli mod ve sayaç başlatma.

```bash
if [[ -z "${{ secrets.AZURE_CLIENT_ID }}" ]]; then ... fi
if [[ -z "${{ secrets.AZURE_TENANT_ID }}" ]]; then ... fi
if [[ -z "${{ secrets.AZURE_SUBSCRIPTION_ID }}" ]]; then ... fi
```
Azure OIDC için zorunlu üç secret'ı kontrol eder.

```bash
if [[ -z "${{ secrets.ACR_NAME }}" && -z "${{ secrets.ACR_LOGIN_SERVER }}" ]]; then ... fi
```
ACR için en az birinin tanımlı olması gerekir: `ACR_NAME` (Azure CLI yolu) veya `ACR_LOGIN_SERVER` (Docker doğrudan login yolu).

```bash
acr_ls=$(printf '%s' "${{ secrets.ACR_LOGIN_SERVER }}" | tr -d ' \t\r\n' | sed -E 's#^https?://##' | sed -E 's#/*$##' | tr '[:upper:]' '[:lower:]')
if ! echo "$acr_ls" | grep -Eq '^[a-z0-9][a-z0-9-]*\.azurecr\.io$'; then
```
`ACR_LOGIN_SERVER` değeri varsa formatını doğrular: boşlukları çıkar, scheme'i (`https://`) sil, küçük harfe çevir, `.azurecr.io` ile bittiğini regex ile kontrol et.

```bash
if [[ -n "${{ secrets.ACR_USERNAME }}" || -n "${{ secrets.ACR_PASSWORD }}" ]]; then
  if [[ -z "${{ secrets.ACR_USERNAME }}" || -z "${{ secrets.ACR_PASSWORD }}" ]]; then ...
  if [[ -z "${{ secrets.ACR_LOGIN_SERVER }}" ]]; then ...
fi
```
Admin credentials kullanılıyorsa hem `ACR_USERNAME` hem `ACR_PASSWORD` birlikte tanımlı olmalı ve `ACR_LOGIN_SERVER` zorunlu olmalı.

```bash
exit $missing
```
En az bir eksik varsa (`missing=1`) workflow hata vererek durur.

---

### Step 5 — `Azure login (OIDC)`

```yaml
- name: Azure login (OIDC)
  uses: azure/login@v2
  with:
    client-id: ${{ secrets.AZURE_CLIENT_ID }}
    tenant-id: ${{ secrets.AZURE_TENANT_ID }}
    subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
```

OIDC (Federated Identity) ile Azure'a bağlanır. Uzun ömürlü şifre/secret gerektirmez; GitHub Actions kısa ömürlü OIDC token üretir ve Azure bunu doğrular. Bu adımdan sonra `az` CLI komutları kimlik doğrulamasız çalışır.

---

### Step 6 — `ACR login`

Docker image push edebilmek için ACR'ye login olur. İki farklı yolu destekler:

```bash
if [[ -n "$ACR_LOGIN_SERVER" && -n "$ACR_USERNAME" && -n "$ACR_PASSWORD" ]]; then
  echo "$ACR_PASSWORD" | docker login "$ACR_LOGIN_SERVER" -u "$ACR_USERNAME" --password-stdin
  exit 0
fi
```
**Yol A (Doğrudan Docker login):** Admin kullanıcı adı/şifre ile login. Şifre `--password-stdin` ile stdin'den okunur (shell history'ye düşmez). Başarılı olursa `exit 0` ile diğer adımları atlar.

```bash
if [[ -z "$ACR_NAME" && -n "$ACR_LOGIN_SERVER" ]]; then
  ACR_NAME="${ACR_LOGIN_SERVER%%.azurecr.io}"
fi
```
`ACR_NAME` boşsa `ACR_LOGIN_SERVER`'dan türetir: `sorukutusuacr.azurecr.io` → `sorukutusuacr`

```bash
az acr login --name "$ACR_NAME"
```
**Yol B (Azure CLI login):** Önceki OIDC oturumunu kullanarak ACR'ye login olur. `docker login` yapmak için gerekli token'ı Azure CLI üretir.

---

### Step 7 — `Set ACR login server`

```bash
if [[ -n "$ACR_LOGIN_SERVER" ]]; then
  LOGIN_SERVER="$ACR_LOGIN_SERVER"
else
  LOGIN_SERVER=$(az acr show -n "$ACR_NAME" --query loginServer -o tsv)
fi
```
`ACR_LOGIN_SERVER` tanımlıysa onu kullan, yoksa Azure CLI ile ACR bilgilerini sorgula ve `loginServer` değerini al.

```bash
LOGIN_SERVER=$(printf '%s' "$LOGIN_SERVER" | tr -d ' \t\r\n' | sed -E 's#^https?://##' | sed -E 's#/*$##' | tr '[:upper:]' '[:lower:]')
```
Login server değerini normalize et: tüm boşluk/newline kaldır, `https://` varsa çıkar, sondaki `/` kaldır, küçük harfe çevir.

Aşağıdaki 4 kontrol sırasıyla:
```bash
if echo "$LOGIN_SERVER" | grep -Eq '[A-Z]'; then ... fi      # Büyük harf kontrolü
if echo "$LOGIN_SERVER" | grep -Eq '/'; then ... fi           # Path içerip içermediği kontrolü
if echo "$LOGIN_SERVER" | grep -Eq '^https?://'; then ... fi  # Scheme içerip içermediği kontrolü
if ! echo "$LOGIN_SERVER" | grep -Eq '^[a-z0-9][a-z0-9-]*\.azurecr\.io$'; then ... fi  # Format kontrolü
```

```bash
echo "login_server=$LOGIN_SERVER" >> $GITHUB_OUTPUT
```
Doğrulanan login server adresini sonraki job'ların kullanabilmesi için output'a yazar.

---

### Step 8 — `Setup Docker Buildx`

```yaml
- name: Setup Docker Buildx
  uses: docker/setup-buildx-action@v3
```

`docker buildx` build aracını ve BuildKit backend'ini aktif eder. Multi-platform build desteği ve katman cache optimizasyonu sağlar.

---

### Step 9 — `Build & push images`

`should_build` fonksiyonu tanımlanır:

```bash
should_build() {
  local name="$1"
  if [[ "$SERVICES" == "all" ]]; then
    return 0
  fi
  [[ " $SERVICES " == *" $name "* ]]
}
```
`SERVICES` değişkeni `all` ise her servis için `true` döner. Değilse verilen servis adı listede aranır.

Her servis için ayrı `docker buildx build --push` komutu çalışır:

#### `exam-dotnet-api`
```bash
docker buildx build --push \
  -f deploy/dockerfiles/dotnet-web.Dockerfile \
  --build-arg PROJECT_PATH=api/ExamApp.Api/ExamApp.Api.csproj \
  --build-arg APP_DLL=ExamApp.Api.dll \
  --build-arg EXPOSE_PORT=5079 \
  -t "$ACR/exam-dotnet-api:$TAG" \
  .
```
Ana .NET exam API'sini build eder.

#### `auth-api`
```bash
docker buildx build --push \
  -f deploy/dockerfiles/dotnet-web.Dockerfile \
  --build-arg PROJECT_PATH=auth-api/ExamApp.Api.csproj \
  --build-arg APP_DLL=ExamApp.Api.dll \
  --build-arg EXPOSE_PORT=5079 \
  -t "$ACR/auth-api:$TAG" \
  .
```
Kimlik doğrulama API'sini build eder.

#### `exam-badge-api`
```bash
docker buildx build --push \
  -f deploy/dockerfiles/dotnet-web.Dockerfile \
  --build-arg PROJECT_PATH=Services/BadgeService/BadgeService.csproj \
  --build-arg APP_DLL=BadgeService.dll \
  --build-arg EXPOSE_PORT=8006 \
  -t "$ACR/exam-badge-api:$TAG" \
  .
```
Rozet servisi API'sini build eder.

#### `ocelot-gateway`
```bash
docker buildx build --push \
  -f deploy/dockerfiles/dotnet-web.Dockerfile \
  --build-arg PROJECT_PATH=Services/Gateway/Gateway.csproj \
  --build-arg APP_DLL=Gateway.dll \
  --build-arg EXPOSE_PORT=5678 \
  -t "$ACR/ocelot-gateway:$TAG" \
  .
```
Ocelot API Gateway'i build eder.

#### `exam-outbox-publisher`
```bash
docker buildx build --push \
  -f deploy/dockerfiles/dotnet-worker-net10-preview.Dockerfile \
  --build-arg PROJECT_PATH=Services/OutboxPublisher/OutboxPublisherService.csproj \
  --build-arg APP_DLL=OutboxPublisherService.dll \
  -t "$ACR/exam-outbox-publisher:$TAG" \
  .
```
RabbitMQ outbox publisher worker servisini build eder. .NET 10 preview Dockerfile kullanır.

#### `angular-app`
```bash
docker buildx build --push \
  -f deploy/dockerfiles/angular-spa.Dockerfile \
  --build-arg APP_DIR=ui \
  -t "$ACR/ui:$TAG" \
  .
```
Ana Angular SPA'yı (`ui/`) build eder.

#### `auth-ui`
```bash
docker buildx build --push \
  -f deploy/dockerfiles/angular-spa.Dockerfile \
  --build-arg APP_DIR=auth-ui \
  -t "$ACR/auth-ui:$TAG" \
  .
```
Auth Angular SPA'yı (`auth-ui/`) build eder.

#### `question-detector`
```bash
docker buildx build --push \
  -f deploy/dockerfiles/question-detector.Dockerfile \
  -t "$ACR/exam-question-detector:$TAG" \
  .
```
YOLOv8 tabanlı AI soru dedektörünü build eder.

Tüm build komutlarında ortak parametreler:
| Parametre | Açıklama |
|---|---|
| `--push` | Build bittikten hemen sonra ACR'ye push et |
| `-f <Dockerfile>` | Kullanılacak Dockerfile yolu |
| `--build-arg` | Dockerfile içindeki ARG değişkenlerine dışarıdan değer geç |
| `-t "$ACR/<image>:$TAG"` | Image'a full path:tag ver |
| `.` | Build context'i olarak repo kökünü kullan |

---

## JOB 2: `deploy_after_build`

Build tamamlandıktan sonra VM'ye deploy eder.

### Koşul

```yaml
needs: [build_and_push]
if: ${{ (github.event_name != 'workflow_dispatch' || inputs.mode == 'build_and_deploy') && needs.build_and_push.outputs.services_to_deploy != '__none__' }}
```

Yalnızca `build_and_deploy` modunda ve build job'ı başarıyla tamamlandığında çalışır.

---

### Step 1 — `Checkout`

```yaml
uses: actions/checkout@v4
```
Repo içeriğini VM'ye kopyalanacak deploy dosyalarını çekebilmek için klonlar.

---

### Step 2 — `Validate VM deploy secrets present`

`build_and_push` job'undaki secret doğrulamasına benzer şekilde VM secret'larını kontrol eder:

| Secret | Açıklama |
|---|---|
| `VM_HOST` | VM'nin IP adresi veya hostname'i |
| `VM_USER` | SSH kullanıcı adı |
| `VM_SSH_KEY` | SSH özel anahtarı |
| `VM_DEPLOY_DIR` | VM üzerindeki deploy klasörü |
| `VM_ENV_FILE` | VM üzerindeki `.env` dosyasının yolu |

---

### Step 3 — `Ensure VM deploy dirs exist`

```yaml
uses: appleboy/ssh-action@v1.0.3
```
SSH ile VM'e bağlanır ve aşağıdaki komutları çalıştırır:

```bash
mkdir -p "${{ secrets.VM_DEPLOY_DIR }}" "${{ secrets.VM_DEPLOY_DIR }}/scripts" "${{ secrets.VM_DEPLOY_DIR }}/postgres/init"
```
Deploy klasörünü ve alt klasörlerini oluşturur. Zaten varsa hata vermez (`-p`).

```bash
sudo chown -R "${{ secrets.VM_USER }}":"${{ secrets.VM_USER }}" "${{ secrets.VM_DEPLOY_DIR }}" || true
sudo chmod -R u+rwX,go+rX "${{ secrets.VM_DEPLOY_DIR }}" || true
```
Deploy klasörünün sahibini SSH kullanıcısı yapar ve izinleri ayarlar. Böylece `scp-action` dosyaları yazabilir.

---

### Step 4 — `Upload deploy assets to VM`

```yaml
uses: appleboy/scp-action@v0.1.7
with:
  source: 'deploy/Caddyfile,deploy/docker-compose.prod.yml,deploy/docker-compose.prod.images.yml,deploy/docker-compose.pgadmin.yml'
  target: '${{ secrets.VM_DEPLOY_DIR }}'
  strip_components: 1
  overwrite: true
```

SCP ile deploy dosyalarını VM'e kopyalar:
- `source` → Kopyalanacak dosyalar (virgülle ayrılmış liste)
- `strip_components: 1` → İlk klasör bileşenini kaldır (yani `deploy/` prefix'ini çıkar, dosyalar doğrudan `VM_DEPLOY_DIR/` altına gider)
- `overwrite: true` → Varolan dosyaların üzerine yaz

---

### Step 5 — `Upload VM update script`

```yaml
source: 'deploy/scripts/vm-update.sh'
target: '${{ secrets.VM_DEPLOY_DIR }}/scripts'
strip_components: 2
```

`vm-update.sh` scriptini VM'e kopyalar. `strip_components: 2` → `deploy/scripts/` prefix'ini çıkarır, script doğrudan `scripts/` klasörüne gider.

---

### Step 6 — `Deploy on VM (SSH)`

SSH ile VM'e bağlanır ve deploy işlemini gerçekleştirir:

```bash
cd "${{ secrets.VM_DEPLOY_DIR }}"
echo "VM deploy dir: $(pwd)"
```
Deploy klasörüne geçer, çalışma dizinini loglar.

```bash
grep -n -E "@keycloak|handle @keycloak|X-Forwarded-Proto|reverse_proxy keycloak" ./Caddyfile || true
```
Debug amaçlı: Caddyfile'daki Keycloak ilgili satırları gösterir.

```bash
ACR_LOGIN_SERVER_EFFECTIVE="${{ secrets.ACR_LOGIN_SERVER }}"
if [ -z "$ACR_LOGIN_SERVER_EFFECTIVE" ]; then
  ACR_LOGIN_SERVER_EFFECTIVE="${{ needs.build_and_push.outputs.acr_login_server }}"
fi
```
ACR login server'ı belirler: secret tanımlıysa onu kullan, değilse build job'ının output'undan al.

```bash
ACR_LOGIN_SERVER_NORM=$(printf '%s' "$ACR_LOGIN_SERVER_EFFECTIVE" | tr -d ' \t\r\n' | sed -E 's#^https?://##' | sed -E 's#/*$##' | tr '[:upper:]' '[:lower:]')
export ACR_LOGIN_SERVER="${ACR_LOGIN_SERVER_NORM}"
export ACR_USERNAME="${{ secrets.ACR_USERNAME }}"
export ACR_PASSWORD="${{ secrets.ACR_PASSWORD }}"
export IMAGE_TAG="${{ env.IMAGE_TAG }}"
export SERVICES_TO_DEPLOY="${{ needs.build_and_push.outputs.services_to_deploy }}"
export ENV_FILE="${{ secrets.VM_ENV_FILE }}"
```
Tüm ortam değişkenlerini export eder. `vm-update.sh` scripti bu değişkenleri okur.

```bash
sh ./scripts/vm-update.sh $SERVICES_TO_DEPLOY
```
`vm-update.sh` scriptini çalıştırır. Deploy edilecek servis adlarını argüman olarak geçer. Bu script imajları pull edip container'ları yeniden başlatır.

---

## JOB 3: `deploy_only`

Build yapmadan, sadece zaten ACR'de olan image'larla VM'yi günceller.

### Koşul

```yaml
if: ${{ github.event_name == 'workflow_dispatch' && inputs.mode == 'deploy_only' }}
```

Yalnızca `deploy_only` modu seçildiğinde çalışır.

---

### Step 1–4

`deploy_after_build` job'undaki **Step 1–4** ile tamamen aynıdır (Checkout, secret doğrulama, klasör oluşturma, deploy assets upload).

---

### Step 5 — `Deploy config on VM (SSH, no build)`

Build yapmadan yalnızca yapılandırma değişikliklerini uygular:

```bash
grep -n -E "@keycloak|handle @keycloak|X-Forwarded-Proto|reverse_proxy keycloak" ./Caddyfile || true
```
Caddyfile içindeki Keycloak routing'ini debug amaçlı loglar.

```bash
docker compose --env-file "$ENV_FILE" \
  -f docker-compose.prod.yml \
  -f docker-compose.prod.images.yml \
  up -d --no-build --force-recreate --no-deps caddy keycloak
```

| Parametre | Açıklama |
|---|---|
| `--env-file "$ENV_FILE"` | `.env` dosyasından değişkenleri yükle |
| `-f docker-compose.prod.yml -f docker-compose.prod.images.yml` | Birden fazla compose dosyasını birleştir |
| `up -d` | Container'ları arka planda başlat |
| `--no-build` | Image build etme, var olanı kullan |
| `--force-recreate` | Container'ları değişip değişmediğine bakmaksızın yeniden oluştur |
| `--no-deps` | Bağımlı servisleri (depends_on) başlatma — diğer servisler tetiklenmez |
| `caddy keycloak` | Yalnızca bu iki servisi yeniden başlat |

---

## JOB 4: `bootstrap_minimal`

Sunucuyu sıfırdan hazırlar: veritabanı, cache, message broker, Keycloak ve Caddy'yi başlatır.

### Koşul

```yaml
if: ${{ github.event_name == 'workflow_dispatch' && inputs.mode == 'bootstrap_minimal' }}
```

---

### Step 1–2

Checkout ve secret doğrulama (`deploy_after_build` ile aynı).

---

### Step 3 — `Ensure VM deploy dirs exist`

`deploy_after_build`'e ek olarak daha fazla klasör oluşturur:

```bash
mkdir -p \
  "${{ secrets.VM_DEPLOY_DIR }}" \
  "${{ secrets.VM_DEPLOY_DIR }}/scripts" \
  "${{ secrets.VM_DEPLOY_DIR }}/keycloak/import" \
  "${{ secrets.VM_DEPLOY_DIR }}/keycloak/keycloak-themes" \
  "${{ secrets.VM_DEPLOY_DIR }}/pgadmin"
```
Keycloak realm import ve pgAdmin klasörleri de oluşturulur.

---

### Step 4 — `Upload deploy assets to VM`

`deploy_after_build` ile aynı (Caddyfile + compose dosyaları).

---

### Step 5 — `Upload Postgres init scripts to VM`

```yaml
source: 'deploy/postgres/init/*'
target: '${{ secrets.VM_DEPLOY_DIR }}/postgres/init'
strip_components: 3
```
PostgreSQL başlangıç SQL scriptlerini VM'e kopyalar. `strip_components: 3` → `deploy/postgres/init/` prefix'ini çıkarır.

---

### Step 6 — `Upload Keycloak realm import to VM`

```yaml
if: ${{ hashFiles('deploy/keycloak/import/**') != '' }}
```
Keycloak import dosyaları varsa:

```yaml
source: 'deploy/keycloak/import/*'
target: '${{ secrets.VM_DEPLOY_DIR }}/keycloak/import'
strip_components: 3
```
Keycloak realm/client JSON dosyalarını VM'e kopyalar.

---

### Step 7 — `Bootstrap minimal stack on VM (SSH)`

En kapsamlı SSH adımı. Sırasıyla:

```bash
docker compose ... down --remove-orphans
```
Çalışan tüm compose container'larını ve tanımsız (orphan) container'ları durdurur ve siler.

```bash
PROJECT="${COMPOSE_PROJECT_NAME:-$(basename "$(pwd)")}"
docker volume ls -q --filter "label=com.docker.compose.project=$PROJECT" | while read -r vol; do
  vkey=$(docker volume inspect "$vol" --format ...)
  if [ "$vkey" = "caddy_data" ] || [ "$vkey" = "caddy_config" ] || \
     [ "$vkey" = "postgres_data" ] || [ "$vkey" = "redis_data" ] || [ "$vkey" = "rabbitmq_data" ]; then
    echo "Keeping volume: $vol ($vkey)"
    continue
  fi
  docker volume rm "$vol" ...
done
```
Compose projesine ait tüm volume'ları listeler. TLS sertifikaları (`caddy_data/caddy_config`) ve kalıcı veri volume'larını (`postgres_data`, `redis_data`, `rabbitmq_data`) **korur**, diğer tüm volume'ları siler.

```bash
docker system prune -af
docker builder prune -af || true
```
Tüm kullanılmayan Docker imaj ve cache'ini temizler.

```bash
docker compose ... up -d --no-build postgres redis rabbitmq keycloak
```
Yalnızca altyapı servislerini başlatır (uygulama servisleri henüz başlamaz).

```bash
if grep -nE '^[A-Za-z_][A-Za-z0-9_]*=[^#"\x27]*[[:space:]][^#]*$' "$ENV_FILE"; then
  echo "ERROR: ..."
  exit 1
fi
set -a
. "$ENV_FILE"
set +a
```
`.env` dosyasını syntax kontrolünden geçirir: tırnaksız boşluk içeren değerler hata üretir. Geçerliyse `set -a` ile tüm değişkenleri export eder (`.` = source), `set +a` ile export modunu kapatır.

#### pgAdmin kurulumu:

```bash
printf '%s\n' '{"Servers":{"1":{"Name":"exam-postgres",...}}}' > ./pgadmin/servers.json
```
pgAdmin'e Postgres server'ını önceden kaydetmek için `servers.json` oluşturur.

```bash
printf '%s\n' "postgres:5432:*:${POSTGRES_USER}:${POSTGRES_PASSWORD}" > ./pgadmin/pgpass
chmod 600 ./pgadmin/pgpass || true
```
pgAdmin'in şifresiz bağlantı kurabilmesi için `.pgpass` dosyası oluşturur. `chmod 600` → Dosyayı yalnızca sahibi okuyabilir (güvenlik gereği).

```bash
docker compose ... up -d --no-build pgadmin
```
pgAdmin container'ını başlatır.

```bash
if ! docker ps --format '{{.Names}}' | grep -qx 'exam-pgadmin'; then
  docker logs --tail 200 exam-pgadmin || true
  exit 1
fi
```
pgAdmin'in gerçekten ayağa kalkıp kalkmadığını doğrular. Kalkmadıysa logları gösterir ve hata verir.

#### Keycloak hazır olana kadar bekle:

```bash
i=0
until docker exec exam-keycloak /opt/keycloak/bin/kcadm.sh config credentials \
  --server http://localhost:8080 \
  --realm master \
  --user "$KEYCLOAK_ADMIN" \
  --password "$KEYCLOAK_ADMIN_PASSWORD" >/dev/null 2>&1
do
  i=$((i+1))
  if [ "$i" -ge 60 ]; then
    docker logs --tail 200 exam-keycloak || true
    exit 1
  fi
  sleep 2
done
```
Keycloak admin API'si yanıt verinceye kadar 2 saniye arayla en fazla 60 kez (2 dakika) dener. Zaman aşımında logları gösterir.

#### Keycloak realm ve client güncelleme:

```bash
docker exec exam-keycloak /opt/keycloak/bin/kcadm.sh update realms/exam-realm \
  -s "attributes.frontendUrl=$PUBLIC_BASE_URL"
```
`exam-realm`'in Frontend URL'sini ayarlar. Keycloak bu URL'yi redirect ve email linklerinde kullanır.

```bash
CLIENT_INTERNAL_ID=$(docker exec exam-keycloak /opt/keycloak/bin/kcadm.sh get clients \
  -r exam-realm \
  -q clientId=exam-client \
  --fields id | tr -d '\r' | sed -n 's/.*"id"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1)
```
`exam-client`'ın Keycloak internal UUID'sini sorgular. JSON çıktısından regex ile `id` değerini çekar.

```bash
docker exec exam-keycloak /opt/keycloak/bin/kcadm.sh update "clients/$CLIENT_INTERNAL_ID" \
  -r exam-realm \
  -s "redirectUris=[\"$PUBLIC_BASE_URL/app/*\",\"$PUBLIC_BASE_URL/*\"]" \
  -s "webOrigins=[\"$PUBLIC_BASE_URL\"]"
```
`exam-client`'ın izin verilen redirect URI'larını ve web origin'lerini public URL ile günceller.

```bash
docker compose ... up -d --no-build --no-deps caddy
```
Caddy reverse proxy'yi başlatır. `--no-deps` ile `ocelot-gateway` gibi bağımlı servisler başlatılmaz.

```bash
docker compose ... ps
```
Tüm servislerin durumunu gösterir.

---

## JOB 5: `diagnose_tls`

TLS ve Caddy sorunlarını teşhis eder.

### Koşul

```yaml
if: ${{ github.event_name == 'workflow_dispatch' && inputs.mode == 'diagnose_tls' }}
```

---

### Step 1 — `Validate VM deploy secrets present`

Diğer job'larla aynı secret doğrulaması.

---

### Step 2 — `Diagnose TLS on VM (SSH)`

```bash
echo "VM: $(hostname)"
echo "Deploy dir: $(pwd)"
echo "Env file: $ENV_FILE"
```
Temel VM bilgilerini loglar.

```bash
if grep -nE '^[A-Za-z_][A-Za-z0-9_]*=[^#"\x27]*[[:space:]][^#]*$' "$ENV_FILE"; then
  echo "WARNING: ..."
else
  set -a; . "$ENV_FILE"; set +a
fi
```
`.env` dosyasını yükler. Syntax hatası olsa bile teşhis devam eder (hata yerine uyarı verir).

```bash
echo "DOMAIN=${DOMAIN:-<empty>}"
echo "PUBLIC_BASE_URL=${PUBLIC_BASE_URL:-<empty>}"
```
Alan adı değişkenlerini loglar.

```bash
sudo ss -lntp | grep -E ':(80|443)\b' || true
```
80 ve 443 portlarını dinleyen process'leri gösterir (`ss` = socket statistics).

```bash
docker ps --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}' | sed -n '1p;/exam-caddy/p;/exam-keycloak/p;/ocelot-gateway/p' || true
```
Tüm container'ları tablo formatında listeler, yalnızca ilgili satırları (`exam-caddy`, `exam-keycloak`, `ocelot-gateway`) gösterir.

```bash
docker logs --tail 250 exam-caddy || true
```
Caddy container'ının son 250 satır logunu gösterir.

```bash
docker inspect exam-caddy --format '{{json .HostConfig.PortBindings}}' || true
docker inspect exam-caddy --format '{{json .Mounts}}' || true
```
Caddy container'ının port mapping'lerini ve mount noktalarını JSON formatında gösterir.

```bash
sed -n '1,120p' ./Caddyfile
```
VM'deki Caddyfile'ın ilk 120 satırını gösterir.

```bash
docker exec exam-caddy caddy validate --config /etc/caddy/Caddyfile || true
```
Caddy config dosyasını container içinde validate eder. Syntax veya semantik hataları raporlar.

```bash
dom="${DOMAIN:-staging.hedefokul.com}"
docker run --rm --network host alpine:3.20 sh -lc \
  "apk add --no-cache openssl curl >/dev/null && echo | openssl s_client -connect 127.0.0.1:443 -servername $dom -tls1_2 -brief 2>&1 | sed -n '1,80p'" || true
```
Geçici bir Alpine Linux container başlatır, `openssl s_client` ile TLS handshake yapar. Sertifika, cipher suite ve bağlantı durumunu gösterir.

```bash
docker run --rm --network host curlimages/curl:8.6.0 \
  -vkI --resolve "$dom:443:127.0.0.1" "https://$dom/" 2>&1 | sed -n '1,120p' || true
```
`curlimages/curl` container'ı ile HTTPS isteği atar. 
- `-v` → Verbose (tüm TLS müzakeresi loglanır)
- `-k` → Sertifika hatalarını yoksay
- `-I` → Yalnızca HTTP header'larını al
- `--resolve` → DNS olmadan domain'i 127.0.0.1'e yönlendir

```bash
docker compose --env-file "$ENV_FILE" -f docker-compose.prod.yml -f docker-compose.prod.images.yml ps || true
```
Tüm compose servislerinin mevcut durumunu gösterir.

---

## Özet: Job → Mode Eşleştirmesi

| Mode | `build_and_push` | `deploy_after_build` | `deploy_only` | `bootstrap_minimal` | `diagnose_tls` |
|---|:---:|:---:|:---:|:---:|:---:|
| `build_and_deploy` | ✅ | ✅ | ❌ | ❌ | ❌ |
| `build_only` | ✅ | ❌ | ❌ | ❌ | ❌ |
| `deploy_only` | ❌ | ❌ | ✅ | ❌ | ❌ |
| `bootstrap_minimal` | ❌ | ❌ | ❌ | ✅ | ❌ |
| `diagnose_tls` | ❌ | ❌ | ❌ | ❌ | ✅ |

---

## Gerekli Repository Secrets

| Secret | Kullanıldığı Job | Açıklama |
|---|---|---|
| `AZURE_CLIENT_ID` | `build_and_push` | Azure Service Principal Client ID (OIDC) |
| `AZURE_TENANT_ID` | `build_and_push` | Azure Tenant ID (OIDC) |
| `AZURE_SUBSCRIPTION_ID` | `build_and_push` | Azure Subscription ID (OIDC) |
| `ACR_NAME` | `build_and_push` | Azure Container Registry adı (örn: `sorukutusuacr`) |
| `ACR_LOGIN_SERVER` | `build_and_push`, deploy jobları | ACR login server (örn: `sorukutusuacr.azurecr.io`) |
| `ACR_USERNAME` | `build_and_push` | ACR admin kullanıcı adı (opsiyonel) |
| `ACR_PASSWORD` | `build_and_push` | ACR admin şifresi (opsiyonel) |
| `VM_HOST` | Tüm deploy jobları | VM IP veya hostname |
| `VM_USER` | Tüm deploy jobları | VM SSH kullanıcı adı |
| `VM_SSH_KEY` | Tüm deploy jobları | VM SSH private key |
| `VM_DEPLOY_DIR` | Tüm deploy jobları | VM üzerindeki deploy klasörü |
| `VM_ENV_FILE` | Tüm deploy jobları | VM üzerindeki `.env` dosyasının yolu |
