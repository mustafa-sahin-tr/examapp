# Exam Platform

Kurallar `.claude/rules/` altında konuya göre ayrılmıştır:

- [architecture.md](.claude/rules/architecture.md) — servis haritası ve sorumluluklar
- [stack.md](.claude/rules/stack.md) — teknoloji yığını
- [workflow-rules.md](.claude/rules/workflow-rules.md) — değişiklik yaparken uyulacak desenler
- [local-dev.md](.claude/rules/local-dev.md) — başlatma, port tablosu, sık tuzaklar ve `docs/` özellik dokümanlarının dizini
- [project-structure.md](.claude/rules/project-structure.md) — her servisin içindeki dizin yapısı
- [angular-conventions.md](.claude/rules/angular-conventions.md) — standalone komponent, signal, Material, SCSS token (yalnız `ui/`, `auth-ui/` dosyalarında yüklenir)
- [school-change.md](.claude/rules/school-change.md) — okul değişiminin etki matrisi (liderlik, atama, oturum, DM; #194; yalnız okul / admin dosyalarında yüklenir — okul kapsamına dokunan başka bir işte elle oku)

Özellik spesifikasyonları (etkinlik tipleri, işaretleme, veli hesapları, kurulum geçiş notları) `docs/` altındadır; dizin
`local-dev.md` § Özellik dokümanları. Yeni özellik notu oraya yazılır, her zaman yüklenen rule dosyalarına değil.

## Context ekonomisi

- Okumadan önce yeri `grep -n` ile bul, dar aralık oku (`sed -n a,bp`); büyük dosyayı (ör. `image-selector.component.ts`) bütün okuma.
- Diske kaydedilmiş büyük araç çıktısını (`tool-results/`) bütün `Read` etme; `grep` / `head` ile oku. `git diff`'i dosya bazında oku.
- Merge / issue kapanışında ya da context ~150k'yı geçince kullanıcıya `/clear` öner; süren işin durumu memory'de tutulur.

## Agent ekibi

Bu repoda özelleşmiş agent'lar tanımlıdır (`.claude/agents/`). Varsayılan: işi **kendin yap**. Her subagent ~40k sabit yükle
başlar ve dosyaları yeniden okur (2026-10-26/27'de 76 subagent aylık tüketimin ~%57'sini harcadı). Agent'ı yalnız çok servisli
büyük işte, paralel bağımsız işte ya da bağımsız inceleme (güvenlik, büyük PR) gerektiğinde kullan:

| Agent | Alan |
|---|---|
| `dotnet-api-dev` | C# backend: controller, service, DTO, EF entity |
| `angular-dev` | `ui/` ve `auth-ui/`: sayfa, komponent, servis |
| `event-integration-dev` | Outbox, RabbitMQ, BadgeService consumer'ları |
| `test-engineer` | xUnit ve frontend testleri |
| `devops-aspire` | docker-compose, Aspire AppHost, portlar, deploy |
| `ui-tester` | Tarayıcıda uçtan uca akış doğrulama (Puppeteer) |
| `code-reviewer` | Kalite incelemesi (salt okunur) |
| `security-reviewer` | Güvenlik incelemesi (salt okunur) |
| `business-analyst` | Issue detaylandırma, kabul kriteri, epic → alt issue (kod yazmaz) |
| `product-owner` | Önceliklendirme, kapsam/MVP kararı (kod yazmaz) |
| `product-designer` | Maket/wireframe (Claude Design canvas), tasarım notu, issue'ya ekleme |

Kural: **yazan agent ile inceleyen agent aynı olmaz.** Reviewer'lar kod düzeltmez, bulgu raporlar;
düzeltme yazan agent'a geri gönderilir.

Çok servisli büyük işlerde `/feature`, bağımsız incelemede `/review`, PR öncesi `/ship` komutları var; küçük işte kullanma.
Reviewer'a geniş `git diff` yerine değişen dosya listesi ve odaklı diff ver.

Kod öncesi ürün hazırlığı için: `/refine` (issue detaylandır), `/mockup` (maket üret),
`/groom` (PO + BA + tasarımcı ile epic'i uçtan uca hazırla). Hazır alt issue'da `/feature` ile geliştirmeye geç.

## Prosedürler

Tekrar eden işlerin adımları `.claude/skills/` altındadır: `dotnet-endpoint`, `angular-feature`,
`outbox-event`, `ef-migration`, `gateway-route`, `aspire-migration`, `security-review-checklist`,
`issue-refinement`, `issue-breakdown`, `design-mockup`.
İlgili işe başlarken önce skill'i oku.

## Üçüncü parti referans skill'ler

Bunlar bu projeye özel değil, framework'ün resmi/uzman bilgisini taşır. `angular-developer` ve `testcontainers`
ilgili agent'ta önceden yüklenir; diğerlerini gerekince `SKILL.md`'den oku (önyükleme spawn başına ~14k token tutuyordu): `angular-developer` (angular/skills, resmi Angular ekibi),
`efcore-patterns`, `database-performance`, `csharp-coding-standards`,
`microsoft-extensions-dependency-injection`, `aspire-integration-testing`, `aspire-configuration`,
`aspire-service-defaults`, `testcontainers` (Aaronontheweb/dotnet-skills). Kaynak ve lisans detayı:
`.claude/skills/THIRD_PARTY_NOTICES.md`.
