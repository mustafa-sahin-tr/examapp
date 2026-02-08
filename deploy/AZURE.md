# ExamApp Azure VM Deploy (Docker Compose)

Bu doküman, mevcut [deploy/README.md](README.md) akışını **Azure VM** üzerinde birebir uygulamak için Azure’a özel adımları özetler.

## 1) Azure Kaynakları (önerilen)

- **Resource Group**: `examapp-rg`
- **VM**: Ubuntu 22.04 LTS, başlangıç için `Standard_B2s` (2 vCPU / 4GB)
- **Disk**: en az 64GB (yoğun MinIO/DB kullanımı varsa 128GB+)
- **Network Security Group (NSG)** inbound:
  - `22/tcp` (SSH) sadece kendi IP’n
  - `80/tcp`, `443/tcp` (public)

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

İstersen bir sonraki adımda ACR + GitHub Actions pipeline’ını ekleyebilirim.
