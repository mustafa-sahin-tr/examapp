# ExamApp geliştirici devir dokümanı

Bu klasör, ExamApp'i devralan ya da ekibe yeni katılan bir geliştiricinin, önceki geliştiriciye sormadan ortamı kurup herhangi bir özelliği bulup değiştirebilmesi için yazıldı (issue [#352](https://github.com/mustafa-sahin-tr/examapp/issues/352)). Daha önce `.claude/rules/*`, `docs/*.md`, agent hafızaları, issue yorumları ve kodun içine dağılmış bilgiyi tek bir giriş noktasında toplar ve bu kaynaklara bağlantı verir. Kaynakların yerine geçmez: bir çelişki görürsen kod doğrudur, dokümanı düzelt.

Yazım tarihi: 2026-10-05, `master` @ `84b39b2c` (PR #350 merge'ü) üzerinden.

## Nasıl okunur

| Amaç | Okuma sırası |
|---|---|
| İlk gün: sistemi tanı ve ayağa kaldır | [01](01-sistem-haritasi.md) → [02](02-ortam-kurulumu.md) → [08](08-gelistirme-pratikleri.md) |
| Bir özelliği değiştireceğim | [07](07-uctan-uca-akislar.md)'de akışı bul → ilgili [03-servisler](#03--servisler) dosyası → [04](04-veri-modeli.md) |
| Yeni olay / consumer ekleyeceğim | [06](06-asenkron-akislar.md) → [03-servisler/badge-service.md](03-servisler/badge-service.md) |
| Yetki / giriş sorunu | [05](05-kimlik-yetki.md) → [03-servisler/gateway.md](03-servisler/gateway.md) |
| Neyin kırık olduğunu bilmek istiyorum | [09](09-bilinen-sorunlar.md) |

## İçindekiler

### [01 — Sistem haritası](01-sistem-haritasi.md)
Tüm servisler, sorumlulukları, portları ve bağımlılıkları; Mermaid mimari diyagramı; servisler arası HTTP çağrıları ve olay akışının özeti; gateway yol haritası; veritabanı sahipliği; değiştirmeden önce bilinmesi gereken mimari kurallar.

### [02 — Ortam kurulumu](02-ortam-kurulumu.md)
.NET Aspire (`AppHost/`) ve docker-compose ile sıfırdan kurulum; `.env` anahtarları ve AppHost parametreleri (değerleri değil, yerleri); Keycloak realm importu; RabbitMQ kullanıcıları ve izin senkronu; migration ve seed; sağlık kontrolü; sık hata → çözüm tablosu. [`.claude/rules/local-dev.md`](../../.claude/rules/local-dev.md) ve [`docs/local-development.md`](../../docs/local-development.md) ile birlikte okunur.

### 03 — Servisler
Her servis için dizin yapısı, katmanlar, önemli sınıflar (`path:line`), konfigürasyon anahtarları ve testler.

| Dosya | Kapsam |
|---|---|
| [api.md](03-servisler/api.md) | Ana exam API (`api/ExamApp.Api`): controller'lar, servisler, policy'ler, Hangfire, whiteboard hub, rate limit |
| [foundation.md](03-servisler/foundation.md) | Paylaşılan kütüphane (`api/ExamApp.Foundation`): event sözleşmeleri, `OutboxMessage`, localization, `ServicePrincipal` |
| [auth-api.md](03-servisler/auth-api.md) | Kimlik servisi (`auth-api/`): Keycloak entegrasyonu, exchange/refresh, identity DB |
| [badge-service.md](03-servisler/badge-service.md) | `Services/BadgeService`: tüm consumer'lar, puan/rozet, bildirim hub'ı, Gemini sınıflandırma |
| [outbox-publisher.md](03-servisler/outbox-publisher.md) | `Services/OutboxPublisher`: tek proje, üç instance (exam / identity / badge) |
| [gateway.md](03-servisler/gateway.md) | `Services/Gateway` (Ocelot): route tablosu, JWT, WebSocket hub auth, üç ocelot dosyasının farkları |
| [service-defaults-apphost.md](03-servisler/service-defaults-apphost.md) | `ServiceDefaults/` ve `AppHost/AppHost.cs` kaynak kaynak dökümü |
| [ui.md](03-servisler/ui.md) | Ana Angular uygulaması (`ui/`) |
| [auth-ui.md](03-servisler/auth-ui.md) | Giriş / kayıt / callback uygulaması (`auth-ui/`) |
| [question-detector.md](03-servisler/question-detector.md) | Python FastAPI + YOLO soru tespiti |
| [finance.md](03-servisler/finance.md) | `finance-api`, `finance-app`, `finance-ios` ve graft: kısa özet, ürünle ilişkisi |

### [04 — Veri modeli](04-veri-modeli.md)
Her veritabanı (worksheet, identity, badge, keycloak, finance_db), DbContext'ler, entity'ler ve ilişkiler (Mermaid ER diyagramları), önemli `OnModelCreating` kuralları, outbox tablosu, migration üretme/uygulama, elle çalıştırılan SQL dosyaları.

### [05 — Kimlik ve yetki](05-kimlik-yetki.md)
Keycloak realm'i, client'lar ve roller; Parent / bağımsız öğretmen / öğretmen onayı / askıya alma gibi uygulama içi ayrımların nerede tutulduğu; giriş, callback, refresh ve logout akışı (sequence diyagramı); gateway ve servislerde JWT doğrulama; policy'ler; SignalR token'ı; servisler arası token; UI guard'ları; yetki matrisi.

### [06 — Asenkron akışlar](06-asenkron-akislar.md)
Outbox → OutboxPublisher → RabbitMQ → consumer zinciri; 20 olayın tam kataloğu (yayınlayan, tüketen, yan etki); RabbitMQ izin matrisi; retry, idempotency, sıralama; SignalR, Hangfire ve hosted service'ler; yeni olay ekleme reçetesi.

### [07 — Uçtan uca akışlar](07-uctan-uca-akislar.md)
Adım adım ve sequence diyagramlı: kayıt/giriş, öğretmenin çalışma kağıdı oluşturması, öğrencinin çözmesi ve puan/rozet kazanması, soru tespiti, özel ders/booking, Jitsi video, whiteboard, yorum ve moderasyon, bildirimler; ayrıca günün soruları, pratik modu, hatırlatmalar ve dil değişimi.

### [08 — Geliştirme pratikleri](08-gelistirme-pratikleri.md)
Branch/PR akışı, `.claude/` agent ekibi, komutlar, skill'ler ve hook'lar; `.github/` workflow'ları; xUnit, IntegrationTests ve Karma testlerinin çalıştırılması; k6; deploy (`deploy/`, Azure, GCP); i18n; kod stili.

### [09 — Bilinen sorunlar ve borçlar](09-bilinen-sorunlar.md)
Açık `security` / `bug` / `SONRA` issue'larının özeti ve linkleri, geçmiş güvenlik kararları, kod içi TODO'lar, teknik borç başlıkları ve bu doküman yazılırken bulunan **ayrı issue adaylarının** birleştirilmiş listesi.

## Yazım kuralları (bu klasörü güncellerken)

- Türkçe yazılır; kod tanımlayıcıları orijinal haliyle kalır.
- Her iddia koda dayanır ve `path:line` ile gösterilir (yol repo köküne göredir). Doğrulanamayan nokta **Doğrulanmadı** diye işaretlenir; her dosyanın sonunda bir "Doğrulanmadı" bölümü vardır.
- Gerçek sır yazılmaz: şifre, client secret, token, API anahtarı değeri yerine değerin durduğu dosya ve anahtar adı söylenir (örn. "dev-only değer `.env.example` içindeki `KEYCLOAK_CLIENT_SECRET` anahtarında").
- Kodda hata bulunursa burada düzeltilmez; [09](09-bilinen-sorunlar.md#ayrı-issue-adayları) içindeki aday listesine eklenir.
- Satır numaraları kod değiştikçe kayar. Bir referans tutmuyorsa sınıf/metot adıyla arayın ve referansı güncelleyin.

## Bilinen sınırlar

- Doküman koddan okunarak yazıldı; akışlar çalışma anında uçtan uca denenmedi, testler koşturulmadı. Her dosyanın "Doğrulanmadı" bölümü bunun ayrıntısıdır.
- Yazım sırasında başka bir worktree'de paralel olarak kod yazılıyordu. `84b39b2c` sonrasında değişen alanlarda satır referansları kayabilir.
- `finance-*` ve graft için yalnız kısa özet var (issue kapsamı gereği).
