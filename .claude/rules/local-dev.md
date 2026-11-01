# Yerel geliştirme

Aspire (varsayılan): `cd AppHost && dotnet run` — ayrıntı `docs/local-development.md`. docker-compose yedek yoldur:

## Start everything (recommended)

```bash
cp .env.example .env   # one-time: infra credentials for docker-compose
docker-compose up -d
```

`.env` (git-ignored) holds the local Postgres/MinIO/RabbitMQ/Keycloak/Redis
credentials that `docker-compose.yml` reads via `${VAR}`. `.env.example` has the
dev defaults; never reuse them outside local dev.

## Sık tuzaklar (ayrıntı: `docs/dev-setup-notes.md`, issue numarasıyla ara)

- **Yeni secret / parametre:** `.env.example` + `AppHost/appsettings.json` `Parameters` birlikte; mevcut `.env`'e satırı elle ekle
  (`cp` geri kalanı ezer). Development dışında boş / `devOnly` değerler açılışta patlar.
- **Keycloak** `--import-realm` mevcut realm'i yeniden import etmez: realm export değişikliği çalışan Keycloak'ta elle yapılır.
  Prod realm `deploy/keycloak/import/` (boş kalır), dev `deploy/keycloak/dev-import/realm-export.json`.
- **RabbitMQ** kullanıcı / izinleri `rabbitmq/definitions.json` (tek kaynak, servis başına en az yetki, #279). Yeni event = izin satırı;
  compose'da `rabbitmq-permission-sync` uygular, sonra ilgili servisi `docker-compose restart`; Aspire'da `rabbitmq` kaynağını yeniden başlat.
- **Migration'lar** exam API açılışında uygulanır → Aspire'da exam API'yi yeniden başlat. Yeni kontrolcü / consumer → ilgili servisi yeniden başlat.
- **UI:** yalnız npm (`npm ci`; kilidi `npm install --package-lock-only` ile güncelle), Node 24, Angular 22. CSP üç yerde birlikte
  (`angular.json`, `deploy/nginx/angular-spa.conf.template`, `UiContentSecurityPolicyTests`); dev-server `allowedHosts` listesi.
- **Oturum okuma yolları (#684, #685):** oturum satırından okuyan sorgu `.WithRemovedWorksheetItems()` kullanır ve `(tiq.Order, tiq.Id)` ile sıralar.
- **Claude prompt'ları:** koddaki sabit kısım (sistem mesajı, şema) değişince ilgili `SchemaRevision`'ı artır (#578, #579);
  `textSelect` tokenizer kuralı değişince `shared/fixtures/text-tokenize.v1.json` + tip revizyonu birlikte (#582).
- **Dev sıfırlama:** `deploy/scripts/dev-reset-questions.sh --dry-run` önce.

## Özellik dokümanları (ilgili alana dokunmadan önce oku)

| Alan | Dosya |
|---|---|
| Kurulum / geçiş notları: Keycloak secret'ları (#238, #372), PKCE (#347), RabbitMQ (#279, #328), MinIO presign (#402), pepper (#419), marka (#409), CSP (#316), Angular (#488, #545), TLS (#553), npm, dev sıfırlama | `docs/dev-setup-notes.md` |
| Veli: çocuk hesabı (#438), davet / aktivasyon / grace (#440, #441) | `docs/parent-accounts.md` |
| İşaretleme + Claude (#578, #579, #582), QuestionTrainer, soru bankasına kayıt (#470, #472), büyük görsel (#597), tuvalde Claude (#473), kitaplar (#577), sınıflandırıcı (#469) | `docs/labeling.md` |
| Etkinlik tipleri ve oturumlar: yeniden çöz (#609), openText (#583), analiz (#584), görselsiz editör (#590), mazeTree (#585), crossword / wordSearch / logicGrid / şifre (#586), regionPaint (#587), numberLine / barChart / gridDraw / grafikler (#588), terazi (#632), saat (#634), madde listesi / sıra (#684, #685) | `docs/activities.md` |
| Profil sayıları (#501), öğretmen transferi (#539) | `docs/feature-notes.md` |

Yeni bir özellik notu bu dosyaya değil ilgili `docs/` dosyasına yazılır; burada yalnız tablo satırı / tuzak güncellenir.
Kod / config yorumlarındaki "bkz. local-dev.md" referanslarının (RabbitMQ parola hash'i, Keycloak, grace süresi …) ayrıntısı
artık yukarıdaki `docs/` dosyalarındadır; issue numarasıyla ara.

## Port map (host → container)

| Service | Host port | Notes |
|---|---|---|
| exam-dotnet-api | 5079 (HTTP), 8005 (HTTPS) | ana backend API |
| ocelot-gateway | 5678 | tüm client trafiği buraya gelir |
| auth-api | 6079 (HTTP, yalnız `127.0.0.1`, #100) | Keycloak yönetim — client trafiği gateway üzerinden |
| exam-badge-api | 5080 (HTTP), 8006 (HTTPS) | BadgeService / event handler |
| exam-outbox-publisher | 5081 (HTTP), 8007 (HTTPS) | outbox → RabbitMQ (worksheet DB) |
| identity-outbox-publisher | 5082 (HTTP), 8008 (HTTPS) | outbox → RabbitMQ (identity/auth-api DB) |
| badge-outbox-publisher | 5083 (HTTP), 8009 (HTTPS) | outbox → RabbitMQ (badge DB; puan senkronu #225) |
| angular-app | 4200 | ana UI |
| auth-ui | 4201 | Keycloak login akışı |
| keycloak | 8081 | admin console: http://localhost:8081 |
| PostgreSQL | 5433 | bağlantı: localhost:5433 |
| pgAdmin | 5051 | http://localhost:5051 |
| RabbitMQ AMQP | 5672 (`127.0.0.1` only, #279) | backend bağlantısı |
| RabbitMQ UI | 15672 (`127.0.0.1` only, #279) | http://localhost:15672 |
| Redis | 6379 | |
| MinIO API | 9000 | S3-compat storage |
| MinIO UI | 9001 | http://localhost:9001 |
| question-detector | 8080 | FastAPI (YOLO servisi) |
| jitsi-web | 8000 (HTTP, `127.0.0.1` only) | http://localhost:8000 — tarayıcı doğrudan buraya gider, gateway'den geçmez |
| jvb (Jitsi video bridge) | 10000/udp | medya trafiği |
| prosody / jicofo (Jitsi XMPP/focus) | dışa açık port yok | sadece `mynetwork` içinden erişilir |

## Run services locally (without Docker)

```bash
# Backend API
cd api/ExamApp.Api
dotnet run

# Angular UI
cd ui
ng serve

# question-detector (Python)
cd question-detector
python main.py
```

