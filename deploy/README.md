# ExamApp Production Deploy (Docker Compose)

Bu klasör, mevcut dev compose akışını bozmadan prod için **build + run** edilebilir bir Compose seti sağlar.

Azure VM için ek notlar: [deploy/AZURE.md](AZURE.md)

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
- Keycloak client secret’lar (`KEYCLOAK_CLIENT_SECRET`, `KEYCLOAK_ADMIN_CLIENT_SECRET`)

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
3. `exam-client` ve `exam-admin` client’larını oluştur.
4. `redirect URI` olarak `https://<DOMAIN>/app/*` tanımla.
5. `.env.prod` içine client secret’ları gir.
6. Ardından:

```bash
docker compose --env-file .env.prod -f docker-compose.prod.yml up -d
```

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
- `Services/OutboxPublisher` şu an `net10.0` preview target’lıyor; prod compose bunu çalıştırmak için preview SDK image kullanır. İstersen bunu `net8.0`’a çekip daha “stable” hale getirebiliriz.
