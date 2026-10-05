# `finance-*` ve `graft` — kısa özet

Bu dosya, ExamApp reposunda duran ama sınav platformuyla işlevsel bağı olmayan kişisel finans takip uygulamasını (`finance-api/`, `finance-app/`, `finance-ios/`) ve repoya bağlı geliştirici aracı **graft**'ı (`.claude/skills/graft`, `.claude/helpers/graft-*.cjs`) kısaca anlatır. Issue #352 kapsam gereği bunlar için derin anlatım yapılmaz; yalnız ne oldukları, ürünün parçası olup olmadıkları (kanıtlarıyla), teknolojileri ve nasıl çalıştırılabilecekleri yazılır.

## İçindekiler

1. [Kısa cevap](#1-kısa-cevap)
2. [finance-api](#2-finance-api)
3. [finance-app](#3-finance-app)
4. [finance-ios](#4-finance-ios)
5. [Ürünün parçası mı? Kanıtlar](#5-ürünün-parçası-mı-kanıtlar)
6. [Nasıl çalıştırılır](#6-nasıl-çalıştırılır)
7. [graft (geliştirici aracı)](#7-graft-geliştirici-aracı)
8. [Doğrulanmadı](#8-doğrulanmadı)
9. [Ayrı issue adayları](#9-ayrı-issue-adayları)

---

## 1. Kısa cevap

| Bileşen | Ne | Teknoloji | ExamApp ürününün parçası mı? |
|---|---|---|---|
| `finance-api/` | Varlık/işlem/portföy takibi, fiyat çekme (Yahoo Finance, scraping), kâr/zarar geçmişi, e-posta | ASP.NET Core `net10.0`, EF Core + Npgsql, SignalR, Swagger | **Hayır** — orkestrasyonda, gateway'de ve solution'da referansı yok (§5) |
| `finance-app/` | Yukarıdaki API'nin Angular arayüzü (BIST100, ABD hisseleri, fon, kripto, değerli metal, vadeli mevduat sayfaları) | Angular `^19.2` standalone, ngx-charts, SignalR | **Hayır** |
| `finance-ios/` | `finance-app`'in SwiftUI karşılığı için iskelet + ayrı repo submodule'ü | SwiftUI / SwiftData (iOS 17+) | **Hayır** |
| `graft` | Repo kodunu bir grafa indeksleyip Claude Code oturumlarında bağlam sağlayan üçüncü taraf geliştirici aracı (`@nanonets/graft`) | Node.js hook'ları | **Hayır** — yalnız geliştirme ortamı aracı |

**Görüş (açık soru için):** `finance-*` aynı repoda duran **ayrı bir kişisel projedir**, ExamApp ürününün parçası değildir. Gerekçe §5'te. Devralan ekip için öneri: ürün kapsamından çıkarılması (ayrı repoya taşınması) veya en azından "bakımsız / kapsam dışı" olarak işaretlenmesi; bu bir ürün kararıdır, bu doküman karar vermez.

## 2. finance-api

- Proje: `finance-api/finance-api/finance-api.csproj` — `net10.0`, `RootNamespace=FinanceApi`; paketler HtmlAgilityPack, Swashbuckle, EF Core 10 + Npgsql, SignalR (`finance-api/finance-api/finance-api.csproj:3-18`). Kendi solution'ları: `finance-api/app.sln`, `finance-api/finance-api/finance-api.sln`.
- `Program.cs` (`finance-api/finance-api/Program.cs`):
  - Kestrel portu `Kestrel:Port`, varsayılan **8005** (`:10-15`).
  - DbContext `FinanceDbContext`, `ConnectionStrings:DefaultConnection` (`:36-37`); başlangıçta `Database.MigrateAsync()` (hata yutulur, yalnız konsola yazılır) (`:121-133`).
  - Servisler: `AssetService`, `TransactionService`, `PortfolioService`, `PortfolioPriceUpdateService`, `YahooFinanceService` (`IRealTimeDataService`), `WebScrapingService`, `AllowedCryptoService`, `ExchangeRateService`, `EmailService` (`:40-48`); arka plan servisleri `PriceUpdateBackgroundService`, `ProfitLossHistoryService` (`:75-76`).
  - SignalR hub `/priceUpdateHub` (`:118`).
  - CORS: `AllowAll` (tanımlı, kullanılmıyor) ve kullanılan `SignalRCors` (varsayılan `localhost:4200/5678/3000`, credentials) (`:79-100`, `:115`).
  - **Kimlik doğrulama/yetkilendirme yok**: `UseAuthentication`/`UseAuthorization` yok, controller'larda `[Authorize]` yok (grep).
- Controller'lar (`finance-api/finance-api/Controllers/`, hepsi `api/[controller]`): `AllowedCryptos`, `AssetPrices`, `Assets`, `ExchangeRate`, `FundTaxRates`, `Portfolio`, `ProfitLoss`, `Transactions`, `Test` (`seed-assets`, `assets`, `clear-assets` — `TestController.cs:21`, `:136`, `:151`).
- Veri: `finance_db` (Postgres). `FinanceDbContext` DbSet'leri: `Assets`, `AllowedCryptos`, `Transactions`, `Portfolios`, `ProfitLossHistories`, `AssetTypeProfitLosses`, `AssetProfitLosses`, `ExchangeRates`, `UserCurrencyPreferences`, `FundTaxRates` (`finance-api/finance-api/Data/FinanceDbContext.cs:13-22`). Migration'lar `finance-api/finance-api/Migrations/` (2025-07 → 2026-02).
- Konfigürasyon: `appsettings.json` gitignore'da (`.gitignore:79`); şablon `finance-api/finance-api/appsettings.json.example` (connection string, `EmailSettings` — değerler yer tutucu; parola anahtarı `ConnectionStrings:DefaultConnection` ve `EmailSettings:Password` altında).
- `StartupConfigDump.cs` beş kopyadan biri; ExamApp.Foundation'ı referans etmediği için ortak kütüphaneye taşınmamış (`finance-api/finance-api/StartupConfigDump.cs:7-11`; testi `tests/ExamApp.Api.Tests/Helpers/StartupConfigDumpTests.cs`). Bu, ExamApp ile **tek kod düzeyi temas noktasıdır** (gizli değer maskeleme kuralının kopyası, issue #228).
- Dev Dockerfile: `dockerfiles/finance-api/Dockerfile.finance-api` (SDK 10 + dotnet-ef vb. araçlar; yalnız geliştirme kabı).

## 3. finance-app

- Angular CLI 19.2 ile üretilmiş (`finance-app/README.md:3`), `@angular/core ^19.2.0` (`finance-app/package.json:15`) — `ui`/`auth-ui`'den (19.1.4) farklı sürüm hattı.
- Rotalar (`finance-app/src/app/app.routes.ts:15-30`): `dashboard`, `bist100`, `us-stocks`, `precious-metals`, `funds`, `crypto`, `fixed-deposits`/`futures`, `add-transaction`, `add-asset`, `home`, `detailpage`. Guard/kimlik yok.
- API adresi: `environment.apiUrl = 'http://localhost:5678/api/finance'` (`finance-app/src/environments/environment.ts:3`, `environment.prod.ts:3`); `ApiService` varsayılanı `http://localhost:5678` (`finance-app/src/app/services/api.service.ts:9`); SignalR `'/api/finance/priceUpdateHub'` (`finance-app/src/app/services/signalr.service.ts:35`). Yani uygulama ExamApp gateway'inin (5678) `/api/finance` önekli bir route sunmasını bekliyor; **böyle bir route yok** (§5).
- Dev Dockerfile: `dockerfiles/finance-ui/Dockerfile.finance-ui` (`sleep infinity` kabı, ui ile aynı desen).
- `automation/n8n-finance-sheets/` bu uygulamaya ait bir n8n + Google Sheets otomasyon notudur; fiyat kaynağı olarak `finance-api`'nin `POST /api/AssetPrices/bulk` ucunu kullanır (`automation/n8n-finance-sheets/README.md:1-8`). n8n ExamApp orkestrasyonundan kaldırılmıştır (`docs/aspire-migration-decisions.md:44`).

## 4. finance-ios

- `finance-ios/README.md`: `finance-app`'in SwiftUI (iOS 17+, SwiftData) karşılığı için iskelet; veri kaynakları backend'siz (Google Finance / TEFAS scraping, CoinGecko, Frankfurter) (`finance-ios/README.md:1-30`).
- `finance-ios/FinanceTracker26` bir **git submodule**'üdür: `https://github.com/mtafasahin/finance-ios.git` (`.gitmodules:1-3`). Bu worktree'de başlatılmamış (`git submodule status` → `-e27e45d…`, klasör boş). İçeriği görmek için `git submodule update --init finance-ios/FinanceTracker26`.
- ExamApp ile hiçbir kod/konfig bağı yok.

## 5. Ürünün parçası mı? Kanıtlar

| Kontrol | Sonuç | Kaynak |
|---|---|---|
| `docker-compose.yml` / `docker-compose.override.yml` | `finance` geçmiyor (grep -i) | — |
| `deploy/docker-compose.prod.yml` | `finance` servisi yok; yalnız `deploy/README.md` .NET 10 listesinde adı geçiyor | `deploy/README.md:167` |
| Aspire AppHost | `finance` geçmiyor; migration kararında bilinçli olarak dışarıda bırakıldı (auth yok → ürün/güvenlik kararı gerektirir) | `AppHost/AppHost.cs` (grep), `docs/aspire-migration-decisions.md:42`, `:54` |
| Ocelot (`ocelot.json`, `.Development`, `.Production`) | `/api/finance` route'u yok | `Services/Gateway/ocelot*.json` (grep) |
| `ExamApp.slnx` | finance projesi yok | `ExamApp.slnx` (grep) |
| Veritabanı | Yalnız prod Postgres init script'i `finance_db`'yi oluşturuyor (boş kalıyor; onu kullanan servis prod'da tanımlı değil) | `deploy/postgres/init/01-create-databases.sql:25-27` |
| Kod bağı | ExamApp projelerinden finance'a referans yok; finance-api ExamApp.Foundation'ı referans etmiyor | `finance-api/finance-api/StartupConfigDump.cs:7-11` |
| Kimlik | Keycloak/JWT entegrasyonu yok | `finance-api/finance-api/Program.cs` |
| Alan | Kişisel yatırım portföyü (BIST, kripto, fon, altın) — sınav/eğitim alanıyla ilgisiz | Controller ve model adları |
| Geçmiş | İlk commit'ler 2025-07-04 ("finance initial", "finance app"); son anlamlı geliştirme 2026-02 (submodule, precious-metal migration). 2026-08/09 commit'leri yalnız repo genelindeki toplu değişiklikler (net10 göçü, satır sonları, #228 parola maskeleme) | `git log -- finance-api finance-app finance-ios` |
| Diğer dokümanlar | `.github/copilot-instructions.md:7-8` finance'ı "ExamApp" bileşeni gibi listeliyor (eski/yanıltıcı); `architect.md:40`, `:48` "ölü rota" ve "aynı repoda" diye not düşüyor | — |

Sonuç: **ayrı proje**. Aynı geliştiricinin kişisel finans uygulaması, sınav platformuyla aynı repoda (ve geçmişte aynı compose dosyalarında, `docs/aspire-migration-decisions.md:42` bunlardan "comment'li" diye söz ediyor; güncel compose dosyalarında artık izi yok) tutulmuş. Gateway ortak `5678` portunu ve `/api/finance` önekini beklediği için bir dönem ExamApp gateway'inin arkasında çalıştırılmış olabilir — **Doğrulanmadı**.

## 6. Nasıl çalıştırılır

Orkestrasyonda tanımı olmadığı için elle:

1. Postgres'te `finance_db` oluşturun (dev Postgres init'inde yok; prod init script'i referans: `deploy/postgres/init/01-create-databases.sql:25-27`).
2. `finance-api/finance-api/appsettings.json.example` → `appsettings.json` kopyalayıp connection string'i ve (gerekiyorsa) `EmailSettings`'i doldurun. Değerleri repoya koymayın (dosya gitignore'da).
3. `cd finance-api/finance-api && dotnet run` — port `Kestrel:Port` (varsayılan 8005; `launchSettings.json` profilleri 5049/7255 gösterir ama `ConfigureKestrel` `ListenAnyIP(8005)` ile bunları ezer — **Doğrulanmadı**). Development'ta Swagger UI kök yolda (`Program.cs:105-113`). Migration'lar açılışta otomatik uygulanır.
4. `cd finance-app && npm install && npx ng serve` (4200). Uygulama `http://localhost:5678/api/finance` bekler; doğrudan çalıştırmak için `environment.ts`'yi API'nin adresine (ör. `http://localhost:8005/api`) çevirmek ya da bir proxy/gateway route'u eklemek gerekir. **Port 4200 ExamApp `ui` ile çakışır** — ikisini aynı anda çalıştırmayın ya da `--port` verin.
5. iOS: submodule'ü init edip Xcode'da açın (`finance-ios/README.md` "Kurulum").

## 7. graft (geliştirici aracı)

- Ne: `@nanonets/graft` npm paketi. Repoyu `graft/` klasöründe küçük markdown düğümlerinden ve bir çağrı grafından oluşan bir indekse dönüştürür; Claude Code oturumlarında `graft ask/grep/skeleton/callers/map` komutlarıyla bağlam sağlar (`.claude/skills/graft/SKILL.md:1-20`, araçlar `:20-88`).
- Repoya bağlanması: 2026-09-29 commit'i `fe50d2b9` ("chore: wire graft code graph for Claude Code").
  - Skill: `.claude/skills/graft/SKILL.md`.
  - Hook'lar `.claude/settings.json`: `post-edit` (`:36`), `tool-savings` (`:46`), `prompt` (`:57`), `session-start` (`:68`), `stop` (`:79`); izinler `Bash(graft:*)`, `Bash(npx graft:*)`, `Bash(graft-dev:*)` (`:92-94`); status line `graft-statusline.cjs` (`:98-104`).
  - Yardımcılar: `.claude/helpers/graft-hooks.cjs`, `.claude/helpers/graft-statusline.cjs`. Paket yolunu `require.resolve` / `npm root -g` ile bulur; bulamazsa geliştirici makinesine özgü sabit bir yola düşer (`.claude/helpers/graft-hooks.cjs:7`, `BAKED` = kullanıcının `AppData\Roaming\npm` yolu).
  - Graf çıktısı `graft/` **gitignore'dadır** (`.gitignore:141-142`); `graft build` ile yeniden üretilir. Bu yüzden worktree'de `graft/` klasörü yoktur.
- Ürünle ilgisi: yok. Çalışan sistem graft'a bağlı değildir; yalnız `.claude/` ajan ekibinin geliştirme deneyimini etkiler. Paket kurulu değilse hook'ların nasıl davrandığı (sessizce mi geçiyor, hata mı veriyor) **Doğrulanmadı**. Ajan ekibi ve skill'ler için: [`../08-gelistirme-pratikleri.md`](../08-gelistirme-pratikleri.md).

## 8. Doğrulanmadı

- finance-api'nin bir dönem ExamApp gateway'i (`5678`, `/api/finance`) arkasında çalışıp çalışmadığı (§5).
- finance-api'nin gerçek dinleme portu (`launchSettings` vs `ConfigureKestrel`) (§6).
- `finance-ios/FinanceTracker26` submodule içeriği (init edilmedi).
- graft paketi kurulu değilken `.claude/settings.json` hook'larının davranışı (§7).

## 9. Ayrı issue adayları

Mevcut issue araması (`finance`) yalnız #352'yi döndürdü.

1. **Kapsam kararı: finance-* ExamApp reposunda kalmalı mı?** — §5 kanıtları. `docs/aspire-migration-decisions.md:54` de "birinin karar vermesi gerekiyor" diyor. Öneri: ayrı repoya taşıma; `deploy/postgres/init/01-create-databases.sql:25-27`'deki `finance_db` oluşturmasını ve `deploy/README.md:167`'deki atfı temizleme.
2. **finance-api kimliksiz ve yıkıcı test uçları açık** — `finance-api/finance-api/Program.cs` (auth yok), `Controllers/TestController.cs:21`, `:151` (`seed-assets`, `clear-assets`). Herhangi bir ortamda dışa açılırsa veriler silinebilir. Ayrıca açılış migration'ı hatayı yutar (`Program.cs:121-133`).
3. **Yanıltıcı dokümantasyon** — `.github/copilot-instructions.md:7-8`, `:14`, `:22`, `:33` finance'ı ExamApp bileşeni gibi tanıtıyor; `docs/aspire-migration-decisions.md:42` finance'ın compose dosyalarında "comment'li" olduğunu söylüyor ama güncel compose dosyalarında hiç geçmiyor.
4. **Repoda ikili dosya** — `finance-api/packages-microsoft-prod.deb` (4 KB, tracked) ve kök `finance-api/Models/Asset.cs` (proje dışında, derlenmeyen kopya; `finance-api/finance-api/Models/` asıl yer).
5. **graft hook'unda makineye özgü sabit yol** — `.claude/helpers/graft-hooks.cjs:7`. Başka geliştiricide fallback çalışmaz; paket global kurulu değilse hook'lar her oturumda hata verebilir (Doğrulanmadı).
