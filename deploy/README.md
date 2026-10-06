# ExamApp Production Deploy (Docker Compose)

Bu klasör, mevcut dev compose akışını bozmadan prod için **build + run** edilebilir bir Compose seti sağlar.

Azure VM için ek notlar: [deploy/AZURE.md](AZURE.md)

Google Cloud (GKE + Artifact Registry) notları: [deploy/gcp/README.md](gcp/README.md)

## 0) Ön Koşullar

- Bir Linux VM (Ubuntu 22.04 önerilir). Başlangıç için: **2 vCPU / 4–8 GB RAM / 60+ GB disk**.
- Bir domain: ör. `exam.example.com` ve DNS A kaydı VM public IP’ye yönlendirilmiş olmalı.
- Firewall/Security Group:
  - Inbound: `80/tcp`, `443/tcp` (zorunlu)
  - Inbound: `22/tcp` (SSH, mümkünse sadece kendi IP’n)

> Prod compose sadece 80/443’ü dışarı açar. DB/MinIO/RabbitMQ/Redis dış dünyaya kapalı kalır.

## 1) VM Hazırlığı

Ubuntu örneği:

```bash
sudo apt-get update
sudo apt-get install -y ca-certificates curl gnupg

# Docker
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo $VERSION_CODENAME) stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# (Opsiyonel) docker komutları için sudo’suz kullanım
sudo usermod -aG docker $USER
# yeniden login ol
```

## 2) Kod / Deploy Dosyaları

İki seçenek:

### Seçenek A — Repo’yu VM’ye clone et

```bash
git clone <REPO_URL>
cd examapp/deploy
```

### Seçenek B — Sadece `deploy/` klasörünü kopyala

Yine de Docker build context için repo kökü gerekli (compose `context: ..` kullanıyor). En pratik yöntem repo clone.

## 3) Prod Ortam Değişkenleri

`deploy/.env.prod.example` dosyasını kopyala:

```bash
cd examapp/deploy
cp .env.prod.example .env.prod
nano .env.prod
```

Doldurman gereken kritik alanlar:

- `DOMAIN` (örn: `exam.example.com`)
- `PUBLIC_BASE_URL` (örn: `https://exam.example.com`)
- Postgres/Redis/Rabbit/MinIO/Keycloak şifreleri (hepsi güçlü olmalı)
- `JWT_KEY` (32+ karakter)
- Keycloak client secret’lar (`KEYCLOAK_CLIENT_SECRET`, `KEYCLOAK_ADMIN_CLIENT_SECRET`, `KEYCLOAK_SERVICE_CLIENT_SECRET`)
- BadgeService AI analyzer kontrolü: `BADGE_AI_ACTIVE=true|false`

## 4) İlk Kurulum (Up)

```bash
cd examapp/deploy
docker compose --env-file .env.prod -f docker-compose.prod.yml up -d --build
```

Log bakmak için:

```bash
docker compose --env-file .env.prod -f docker-compose.prod.yml logs -f --tail=200
```

## 5) Keycloak Realm / Client Ayarı

Bu deploy seti Keycloak realm import’u destekler.

Realm export JSON’unu (client/roles/groups dahil olabilir) şu klasöre koy:

- `deploy/keycloak/import/*.json`

Sonra Keycloak container’ı **recreate** et ki import startup’ta çalışsın:

```bash
docker compose --env-file .env.prod -f docker-compose.prod.yml up -d --force-recreate keycloak
```

İlk kurulumda (import kullanmıyorsan) manuel olarak:

1. Keycloak container ayağa kalkınca admin console’a geçici erişim ver:
   - Kısa süreli debug için host’ta port publish ekleyebilirsin (prod compose’da yok).
   - Alternatif: VM’ye SSH ile girip `docker exec -it exam-keycloak ...` ile yönet.
2. `exam-realm` realm’ini oluştur.
3. `exam-client`, `exam-admin` ve `exam-service` client’larını oluştur (issue #372):
   - `exam-admin`: confidential, service accounts açık; service account yalnız `realm-management`
     `manage-users`, `view-users`, `query-users`, `view-realm` rollerini taşır (auth-api / exam API Keycloak
     admin REST). `manage-realm` ve `exam-service` rolü VERİLMEZ.
   - `exam-service`: confidential, service accounts açık, standard/direct-grant kapalı; service account
     yalnız `exam-service` realm rolünü taşır (realm-management rolü yok). Servisler arası
     (`BadgeService` ↔ exam API ↔ auth-api) `client_credentials` token'ı bununla alınır.
   - Mevcut prod realm'ini güncellerken sıra: önce `exam-service` client + rol + secret'ı oluştur ve
     `KEYCLOAK_SERVICE_CLIENT_ID/SECRET` deploy secret'larını (docker-compose `.env.prod` / k8s
     `examapp-secrets`) set edip exam API + BadgeService'i yeniden başlat; servisler arası çağrının
     çalıştığını doğrula; SON olarak `service-account-exam-admin`'den `manage-realm` ve `exam-service`
     rolünü kaldır.
4. `redirect URI` olarak `https://<DOMAIN>/app/*` tanımla.
5. `.env.prod` içine client secret’ları gir.
6. Ardından:

```bash
docker compose --env-file .env.prod -f docker-compose.prod.yml up -d
```

### Keycloak brute-force koruması (#368)

Repoda prod realm dosyası yok (`deploy/keycloak/import/` boş/gitignored); dev realm'indeki
(`dev-import/realm-export.json`) ayar prod realm'ine **elle** uygulanmalı. Admin konsolu →
`exam-realm` → Realm settings → Security defenses → Brute force detection:

- Enabled: **ON**, Mode: *Lockout temporarily* (kalıcı kilit KAPALI — saldırganın bilinen kullanıcıları kalıcı kilitlemesini önler)
- Max login failures: **5**
- Wait increment: **1 minute**, Max wait: **5 minutes**
- Minimum quick login wait: **1 minute**, Quick login check milliseconds: **1000**
- Failure reset time: **1 hour**

Gateway ayrıca IP başına rate limit uygular (anti-spray/flood; kullanıcı bazlı brute-force'un birincil
kontrolü yukarıdaki Keycloak kilididir). Sayılan: `/api/auth/login`, `/token` ve `/realms/**` ile
`/auth/realms/**` altındaki **her POST** (login-actions/authenticate, registration, reset-credentials,
required-action, token, introspect, revoke...). Muaf (allowlist): `grant_type=refresh_token` ve
`login-actions/restart`; GET'ler (sayfa/statik kaynak) ve `/api/auth/refresh-token` hiç sayılmaz.
Aşımda 429 + `Retry-After`. Path önce kanonikleştirilir (`;x`, `%2F`, büyük/küçük harf, sondaki `/`).
Gövdesi 16 KB'ı aşan/uzunluğu bilinmeyen/okunamayan "refresh" istekleri sayılır (fail-closed).

- Varsayılan **100 istek/dk/IP** (IPv6 için /64 önek tek istemci sayılır). Okul NAT'ı arkasında çok sayıda
  öğrenci tek IP görünür; sabah yoğun girişte 429 görülürse `RateLimiting__LoginEntry__PermitLimit` /
  `RateLimiting__LoginEntry__WindowSeconds` (env) ile artırın.
- **Gerçek istemci IP'si:** prod'da gateway Caddy arkasındadır; varsayılan olarak `X-Forwarded-For`
  güvenilmez ve tüm kullanıcılar Caddy'nin IP'si altında tek bucket olur. Caddy servisine compose'ta
  sabit `ipv4_address` verin ve yalnız onu tanımlayın: `ForwardedHeaders__KnownProxies__0=<caddy-ipv4_address>`
  (/16 alt ağ yerine; ağdaki başka bir container XFF sahteleyemesin). `KnownNetworks` yalnız zorunluysa ve
  dar CIDR ile kullanılır; `0.0.0.0/0` ve `::/0` başlangıçta reddedilir. Caddy'nin `X-Forwarded-For`'u istemci
  IP'siyle gönderdiğini doğrulayın. Boşsa `RemoteIpAddress` kullanılır.
  Gateway downstream'e her zaman çözülmüş IP'yi `X-Forwarded-For` olarak iletir.

### Keycloak Theme (login)

Custom theme mount’ı prod compose’da aktiftir:

- Host: `deploy/keycloak/keycloak-themes/my-theme/`
- Container: `/opt/keycloak/themes/my-theme`

Realm import dosyan `my-theme`’i referans ediyorsa, Keycloak recreate sonrası otomatik kullanılabilir.

## 6) Veri Kalıcılığı ve Backup

Compose named volume’ler kullanır:

- `postgres_data`, `minio_data`, `rabbitmq_data`, `redis_data`

Backup önerisi:

- Postgres: günlük `pg_dump` + offsite storage
- MinIO: bucket replication veya periyodik volume backup

## 7) Güncelleme (Deploy Yeni Versiyon)

Repo güncelle:

```bash
cd examapp
git pull
cd deploy
```

İmajları rebuild + restart:

```bash
docker compose --env-file .env.prod -f docker-compose.prod.yml up -d --build
```

Eski image temizliği:

```bash
docker image prune -f
```

## Notlar / Bilinen Noktalar

- Prod stack **tek giriş noktası** olarak Caddy (80/443) kullanır ve tüm trafiği `ocelot-gateway`’e iletir.
- DB’ler `deploy/postgres/init/01-create-databases.sql` ile **ilk boot’ta** oluşur. Eğer `postgres_data` doluysa init script tekrar çalışmaz.
- Tüm .NET servisleri (`exam-dotnet-api`, `auth-api`, `exam-badge-api`, `ocelot-gateway`, `exam-outbox-publisher`, `finance-api`, `CatalogService`) `net10.0` (GA) hedefliyor; prod dockerfile'ları `DOTNET_VERSION=10.0` default'u ile stable SDK/runtime image'larını kullanır.
- **Görsel URL imzası (issue #365 S2):** exam API, `[StorageUrl]` alanlarını kısa ömürlü imzalı `/img/{bucket}/{key}?X-Amz-...` URL'si olarak döner. SigV4 imzası `host` başlığını kapsar ve Ocelot `/img` isteğini Host'u downstream adrese çevirerek MinIO'ya iletir; bu yüzden exam API'nin `MinioConfig:PresignEndpoint` değeri (boşsa `MinioConfig:Endpoint`) gateway'in `/img/{everything}` route'undaki `DownstreamHostAndPorts` ile **birebir aynı host:port** olmalıdır (prod: `ocelot.Production.json` → `exam-minio:9000`; docker-compose dev: `minio:9000`; Aspire: `localhost:9000`, AppHost iki tarafa da aynı değeri verir). Uyuşmazsa MinIO imzayı reddeder ve bucket herkese açık olsa bile **tüm görseller 403** alır. Açılışta API logu bağlanılan host'u yazar (`[MinIO] Presigned image URLs are bound to host ...`). Acil durumda `MinioConfig__PresignImageUrls=false` imzalamayı kapatır (bucket'lar özel olana kadar, S4). Ayar yalnız açılışta okunur: değiştirdikten sonra exam API **yeniden başlatılmalıdır** (container/pod restart; ortam değişkeni değişikliği çalışan süreci etkilemez). İmzalı URL'ler 4 saat geçerlidir; kapatma, daha önce verilmiş URL'leri geri almaz. Gateway route'unun host'unu değiştirirseniz `MinioConfig__PresignEndpoint`'i (`docker-compose.prod.yml`, `gcp/k8s/apps.yaml`) de güncelleyin.
