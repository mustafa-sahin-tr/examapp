# ExamApp Azure VM Deploy (Docker Compose)

Bu doküman, mevcut [deploy/README.md](README.md) akışını **Azure VM** üzerinde birebir uygulamak için Azure’a özel adımları özetler.

## 1) Azure Kaynakları (önerilen)

- **Resource Group**: `examapp-rg`
- **VM**: Ubuntu 22.04 LTS, başlangıç için `Standard_B2s` (2 vCPU / 4GB)
- **Disk**: en az 64GB (yoğun MinIO/DB kullanımı varsa 128GB+)
- **Network Security Group (NSG)** inbound:
  - `22/tcp` (SSH) sadece kendi IP’n
  - `80/tcp`, `443/tcp` (public)

### Region seçimi (pratik öneri)

- Kullanıcıların çoğu Türkiye’deyse: **Germany West Central** veya **West Europe** genelde iyi default.
- Min. latency istiyorsan: kullanıcı kitlesine en yakın region (çoğu zaman Frankfurt/Amsterdam hattı).
- Maliyet odaklıysan: Azure fiyatları region’a göre değişir; VM + disk + outbound’u kıyasla.

> Prod compose sadece 80/443’ü publish ediyor. Diğer servisler private kalır.

## 2) Azure Portal ile hızlı kurulum (en pratik)

1. Azure Portal → **Virtual machines** → Create
2. Image: **Ubuntu 22.04 LTS**
3. Size: `Standard_B2s` (minimum)
4. Authentication: SSH key
5. Networking:
   - Public IP: enabled
   - NSG inbound rules: 22 (My IP), 80/443 (Any)
6. Create

VM hazır olunca public IP’yi not al.

## 3) DNS

Domain’inde bir **A kaydı** oluştur:
- `exam.example.com` → `<VM_PUBLIC_IP>`

Let’s Encrypt için DNS’in doğru çözüldüğünden emin ol:

```bash
nslookup exam.example.com
```

## 4) VM üzerinde Docker kurulumu

VM’ye SSH ile bağlan ve [deploy/README.md](README.md) içindeki Docker kurulum bölümünü aynen uygula.

## 5) Uygulamayı ayağa kaldırma

```bash
git clone <REPO_URL>
cd examapp/deploy
cp .env.prod.example .env.prod
nano .env.prod

docker compose --env-file .env.prod -f docker-compose.prod.yml up -d --build
```

Log:

```bash
docker compose --env-file .env.prod -f docker-compose.prod.yml logs -f --tail=200
```

## 6) Azure üzerinde güvenlik önerileri

- SSH portunu mümkünse **sadece kendi IP’ne** kısıtla (NSG).
- VM üzerinde `ufw` kullanacaksan:
  - allow: 22 (kısıtlı), 80, 443
  - diğerlerini deny
- `.env.prod` dosyasını **repoya koyma** (zaten `.gitignore`’da).
- Backup:
  - Postgres için `pg_dump` (günlük) + Azure Storage’a (Blob) offsite.
  - MinIO dataları için periyodik disk snapshot veya rsync + blob.

## 7) (Opsiyonel) Azure Container Registry (ACR) ile image publish

Bu repo şu an VM üzerinde `docker compose ... --build` yaklaşımına uygun. CI/CD istersen:
- GitHub Actions ile image’ları ACR’a push
- VM’de `docker compose pull && docker compose up -d` ile güncelle

### GitHub Actions ile otomatik deploy (önerilen)

Repo içinde hazır workflow var: [ .github/workflows/azure-vm-acr-deploy.yml ](../.github/workflows/azure-vm-acr-deploy.yml)

Kurulum özeti:
1) Azure’da bir ACR oluştur (VM ile aynı region önerilir)
2) GitHub → Repo → Settings → Secrets and variables → Actions → Secrets:
  - `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (OIDC)
  - `ACR_NAME` (örn: `examappacr`)
  - `ACR_USERNAME`, `ACR_PASSWORD` (VM tarafında `docker login` için; ACR admin user veya token)
  - `VM_HOST`, `VM_USER`, `VM_SSH_KEY`
  - `VM_DEPLOY_DIR` (örn: `/opt/examapp/deploy`)
  - `VM_ENV_FILE` (örn: `.env.prod`)
3) VM’de deploy klasörünü hazırla:

```bash
sudo mkdir -p /opt/examapp
sudo chown -R $USER:$USER /opt/examapp

git clone <REPO_URL> /opt/examapp
cd /opt/examapp/deploy
cp .env.prod.example .env.prod
nano .env.prod

# İlk kez çalıştırmak istersen (build ile)
docker compose --env-file .env.prod -f docker-compose.prod.yml up -d --build
```

CI deploy akışı:
- GitHub Actions image’ları ACR’a push eder.
- Sonra VM’ye SSH ile bağlanıp `deploy/scripts/vm-update.sh` script’ini çalıştırır.

## 8) Staging (önerilen kurulum)

Staging’i en problemsiz şekilde yapmak için **2 ayrı VM** öneriyorum:

- **Prod VM**: `exam.<domain>` (veya kök domain)
- **Staging VM**: `staging.<domain>`

Avantajlar:
- Prod verisi ile staging verisi karışmaz (Postgres/MinIO/RabbitMQ)
- Riskli değişiklikler prod’u etkilemez
- Ölçek/maliyet bağımsız yönetilir

GitHub tarafı:
- GitHub Environments oluştur: `staging` ve `production`
- Workflow bu environment’lara göre deploy eder.
  - `push` (main/master) → staging
  - `prod-*` tag → production
  - `workflow_dispatch` ile manuel hedef seçilebilir

Not:
- Staging Keycloak için ayrı realm önerilir: `exam-realm-staging` (veya ayrı Keycloak instance zaten ayrı VM’de olur)

İstersen bir sonraki adımda ACR + GitHub Actions pipeline’ını ekleyebilirim.
