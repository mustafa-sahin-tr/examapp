# 09 — Bilinen sorunlar ve teknik borç

**Bu dosya neyi anlatır:** Devraldığın anda açık olan işlerin haritası. GitHub'daki açık issue'lar
etiketlerine göre gruplanmış (security, bug, SONRA, epic, etiketsiz backlog), her biri bir cümleyle
özetlenmiş ve bağlantılı. Ardından kapanmış önemli güvenlik issue'larında verilen kararlar (neden
bugünkü kod böyle), kod içindeki TODO/FIXME/HACK işaretleri ve kanıtıyla teknik borç başlıkları
geliyor. Son bölüm bu dokümanı yazarken bulunan, henüz issue'su olmayan sorunları toplar. Veriler
2026-10-05 tarihli `gh issue list --state open --limit 300` çıktısından alındı; o gün **32 açık issue**
vardı.

## İçindekiler

1. [Özet](#1-özet)
2. [Açık issue'lar](#2-açık-issuelar)
   - [2.1 security](#21-security)
   - [2.2 bug](#22-bug)
   - [2.3 SONRA](#23-sonra)
   - [2.4 Epic'ler](#24-epicler)
   - [2.5 Etiketsiz backlog](#25-etiketsiz-backlog)
   - [2.6 documentation / enhancement](#26-documentation--enhancement)
3. [Geçmiş güvenlik kararları](#3-geçmiş-güvenlik-kararları)
4. [Kod içi TODO / FIXME / HACK](#4-kod-içi-todo--fixme--hack)
5. [Teknik borç başlıkları](#5-teknik-borç-başlıkları)
6. [Doğrulanmadı](#6-doğrulanmadı)
7. [Ayrı issue adayları](#ayrı-issue-adayları)

---

## 1. Özet

| Etiket | Adet | Not |
|---|---|---|
| `security` | 6 | 2'si aynı zamanda `SONRA`, 1'i aynı zamanda `bug` |
| `bug` | 3 | #347 hem bug hem security |
| `SONRA` | 7 | PO kararıyla ertelenmiş; ayrıca başlığında "SONRA" geçen 5 etiketsiz/enhancement issue var |
| `epic` | 4 | Etiketsiz 3 epic daha var (#52, #104, #107) |
| `documentation` | 2 | #352 (bu doküman), #353 (kullanıcı kılavuzu) |
| `enhancement` | 1 | #67 |
| Etiketsiz | 12 | |

Öncelik önerisi (prod'a çıkmadan önce kapanması gerekenler): #347 (open redirect + PKCE),
#342 (audit alanları boş), #341 (kırık integration testleri), #333 (CSP Report-Only),
#311'in kalan maddeleri. Ayrıca bölüm "Ayrı issue adayları"ndaki Caddy yönetim paneli maruziyeti ve
CI yokluğu.

## 2. Açık issue'lar

### 2.1 security

| # | Başlık | Diğer etiket | Özet |
|---|---|---|---|
| [#347](https://github.com/mustafa-sahin-tr/examapp/issues/347) | auth-ui callback: state ile açık yönlendirme (open redirect) + PKCE yok | bug | `auth-ui/src/app/pages/callback/callback.component.ts` OAuth `state`'in `~` öncesini (`returnBase`) doğrulamadan `window.location.href`'e veriyor; kod değişiminde PKCE yok, login CSRF'e zemin. Not: prod gateway'de callback URL'i `state=${PUBLIC_BASE_URL}` ile kuruluyor (`deploy/docker-compose.prod.yml:460`), yani `state` fiilen dönüş adresi taşıyor |
| [#311](https://github.com/mustafa-sahin-tr/examapp/issues/311) | #98 takip: whiteboard hub kaynak sınırları ve gateway WebSocket auth doğrulaması | — | Kullanıcı başına bağlantı sınırı, global açık tahta sınırı (bellek N×2 MB'a çıkabiliyor), çoklu instance. Gateway tutarlılığı ve WebSocket auth testi PR #346 ile "kısaltılmış" olarak yapıldı (`tests/Gateway.Tests/GatewayWebSocketAuthTests.cs:95-100`); kalan maddeler açık |
| [#316](https://github.com/mustafa-sahin-tr/examapp/issues/316) | UI için Content-Security-Policy + whiteboard istemci sertleştirmesi (#98 takip) | — | Repoda hiç CSP yok. Link onayı ve istemci sahne sınırı #332 ile (kapalı) yapıldı; CSP kısmı #333'e ayrıldı |
| [#333](https://github.com/mustafa-sahin-tr/examapp/issues/333) | UI için CSP: önce Report-Only, sonra zorlama (#316 takip) | SONRA | `ui` ve `auth-ui` yanıtlarına önce `Content-Security-Policy-Report-Only` (önerilen direktifler issue'da), ana akışlar gezildikten sonra zorlama |
| [#320](https://github.com/mustafa-sahin-tr/examapp/issues/320) | #298 takip: askıda süren Jitsi görüşmesi, askı kalkınca bildirim, talep/askı yarışı | SONRA | Askı yalnız **yeni** Jitsi token'ını engelliyor; verilmiş token (60 dk) ve süren görüşme devam ediyor. Yarış maddesi #331 ile kapandı |
| [#326](https://github.com/mustafa-sahin-tr/examapp/issues/326) | #305 takip: yorum görünürlüğünde sahip fallback'i (PO) ve moderasyon geçmişi | — | Çoğu madde PR #329/#330 ile tamam; kalanlar #334 (kapandı, PR #338) ve #335'e (SONRA) taşındı; #326 öncesi oluşmuş okul dışı bildirim kayıtları için prod öncesi nötrleştirme notu var |

### 2.2 bug

| # | Başlık | Özet |
|---|---|---|
| [#341](https://github.com/mustafa-sahin-tr/examapp/issues/341) | IntegrationTests: master'da 4 kırık test (ExamEndpoints latest + TeacherActivity days 400) | `ExamEndpointsTests.Latest_worksheets_lists_newest_first` (seed `CreateUserId` vermiyor, `/latest` öğretmen için sahip kapsamlı) ve `TeacherActivityEndpointsTests.Days_outside_1_to_90_is_rejected_with_400` (3 vaka). Ayrıntı [08-gelistirme-pratikleri.md §4.2](08-gelistirme-pratikleri.md#42-integrationtests-testcontainers) |
| [#342](https://github.com/mustafa-sahin-tr/examapp/issues/342) | StudentService.Save: SaveChangesAsync(false) audit alanlarını atlıyor | `SaveChangesAsync(acceptAllChangesOnSuccess: false)` overload'u `AppDbContext`'teki `ApplyAuditInfo` override'ını atlıyor; yeni öğrencide `CreateTime`/`CreateUserId` boş, soft-delete dönüşümü de çalışmayabilir |
| [#347](https://github.com/mustafa-sahin-tr/examapp/issues/347) | (yukarıda, security) | |

### 2.3 SONRA

`SONRA` = product-owner "şimdi değil, kapatma da" dedi. Kanıt/talep gelince alınır.

| # | Başlık | Özet |
|---|---|---|
| [#57](https://github.com/mustafa-sahin-tr/examapp/issues/57) | Öğretmen dashboard — Sınıf bazlı kırılım (stretch) | Geride kalan öğrenciler listesini Grade'e göre filtreleme; `Teacher` üzerinde Grade ilişkisi yok, talep kanıtı bekleniyor (Part of #52) |
| [#194](https://github.com/mustafa-sahin-tr/examapp/issues/194) | Öğrenci/öğretmen okul değiştirme (transfer) | Tekli `SchoolId` modelinde okul değişimi akışı yok; cache invalidation ve SchoolOnly erişimin kesilmesi gerekecek (Part of #160) |
| [#195](https://github.com/mustafa-sahin-tr/examapp/issues/195) | Bağımsız öğretmen için "öğrenci ekle / davet et" roster akışı | "Öğrencim" bugün yalnız Approved Booking'den türüyor; booking'siz çiftler için davet/onay roster'ı (Part of #160) |
| [#314](https://github.com/mustafa-sahin-tr/examapp/issues/314) | Özel ders ödeme ve komisyon akışı | Ödeme sağlayıcısı, booking'de ödeme durumu, komisyon, fatura — hiçbiri yok; ücret yalnız profilde bilgi |
| [#315](https://github.com/mustafa-sahin-tr/examapp/issues/315) | Booking iptal, erteleme ve no-show kuralları | Booking yalnız oluşturuluyor/görüntüleniyor; iptal/erteleme/no-show yok, takvimde ölü randevular kalıyor |
| [#320](https://github.com/mustafa-sahin-tr/examapp/issues/320) | (security'de) | |
| [#333](https://github.com/mustafa-sahin-tr/examapp/issues/333) | (security'de) | |

Başlığında "SONRA" geçen ama etiketi olmayanlar:

| # | Başlık | Özet |
|---|---|---|
| [#64](https://github.com/mustafa-sahin-tr/examapp/issues/64) | SONRA: AI destekli soru/konu seçimi | Öğrencinin geçmiş performansına göre otomatik konu/soru seçimi; `PracticeSessionQuestion` verisinin birikmesi ön koşul (Part of #60) |
| [#65](https://github.com/mustafa-sahin-tr/examapp/issues/65) | SONRA: Sosyal sinyal tabanlı soru önerisi | Diğer öğrencilerin en çok yanlış yaptığı sorulardan öneri; #64 kanıtlanmadan başlanmaz, gizlilik etkisi var |
| [#66](https://github.com/mustafa-sahin-tr/examapp/issues/66) | SONRA: Günlük soru önerisi etkinlik ekranı | #99 (Günün soruları, PR #349/#350 ile geldi) altyapısına ders/konu seçimi eklemek |
| [#67](https://github.com/mustafa-sahin-tr/examapp/issues/67) | [SONRA] Gemini + YouTube ile otomatik konu çalışma linki bulma | #61'deki manuel çalışma linki altyapısının üzerine otomatik YouTube önerisi; etiketi `enhancement` |
| [#335](https://github.com/mustafa-sahin-tr/examapp/issues/335) | Yorum moderasyon geçmişi tablosu ve reddedilen deneme audit'i (#326 D1) [SONRA] | Gizle/aç geçmişi kayboluyor (`WorksheetComment.HiddenReason` siliniyor), `NotModerator` denemeleri yazılmıyor; `WorksheetCommentModerationEvents` tablosu öneriliyor. "Prod öncesi" notu var |

### 2.4 Epic'ler

| # | Başlık | Etiket | Özet |
|---|---|---|---|
| [#52](https://github.com/mustafa-sahin-tr/examapp/issues/52) | Epic: Öğretmenler için Dashboard Ekranı | — | Öğretmene özel dashboard (geride kalan öğrenciler vb.); kalan dilim #57 |
| [#104](https://github.com/mustafa-sahin-tr/examapp/issues/104) | Epic: Öğrenci-Öğretmen Etkileşim ve Mesajlaşma | — | İki dilim: #105 yorum thread'i (tamamlandı, PR #306/#308/#310) ve #106 DM (açık) |
| [#107](https://github.com/mustafa-sahin-tr/examapp/issues/107) | Epic: Admin öğretmen/öğrenci yönetim ekranları | — | Admin listeleri, filtre, şifre reset, devre dışı bırakma, öğretmen onayı; büyük kısmı kapanmış alt issue'larla geldi (#152, #153, #155, #156, #313…) |
| [#127](https://github.com/mustafa-sahin-tr/examapp/issues/127) | Rozet Sistemi Yenilikler | epic | Rozet tanımları yalnız kodda (BadgeSeeder), değerlendirme yalnız cevap event'inde; kalanlar #147, #149, #322 |
| [#159](https://github.com/mustafa-sahin-tr/examapp/issues/159) | Localization (epic): TR/EN çoklu dil desteği | epic | Altyapı kuruldu (#180–#185, bkz. [08 §6](08-gelistirme-pratikleri.md#6-i18n)); sayfa taşımaları ve auth-ui (Transloco yok) devam ediyor |
| [#160](https://github.com/mustafa-sahin-tr/examapp/issues/160) | tenant management | epic | Okul bazlı tenant izolasyonu; kalanlar #194, #195 |
| [#174](https://github.com/mustafa-sahin-tr/examapp/issues/174) | Epic: Öğretmen Müsaitlik Zamanı Ayarlama — Takvim Grid'i | epic | FullCalendar tabanlı haftalık grid; alt işlerin çoğu kapalı (#178 vb.) |

Epic gövdelerindeki checklist'lerin güncel olup olmadığı tek tek doğrulanmadı.

### 2.5 Etiketsiz backlog

| # | Başlık | Özet |
|---|---|---|
| [#106](https://github.com/mustafa-sahin-tr/examapp/issues/106) | Öğretmene direkt mesajlaşma (DM) — taslak | MVP değil; #105 thread ve bildirim altyapısını yeniden kullanacak. Ürün kararları verilmiş, açık soru yok |
| [#135](https://github.com/mustafa-sahin-tr/examapp/issues/135) | Program create ekranları daha farklı bir tasarıma ihtiyaç duyuyor | Sihirbaz sıfırdan yeniden tasarlanacak; ön koşul product-designer maketi (`ui/src/app/pages/program-create/`) |
| [#147](https://github.com/mustafa-sahin-tr/examapp/issues/147) | Rozet: tetikleyicilerin genişletilmesi için fizibilite notu (araştırma) | `BadgeEvaluator` yalnız `AnswerSubmittedConsumer`'dan tetikleniyor; streak/aktif gün rozetleri o gün soru çözülmezse gecikiyor. Kod değil araştırma notu |
| [#149](https://github.com/mustafa-sahin-tr/examapp/issues/149) | Rozet: ikon seti gözden geçirme | Eksik `achievements/disabled-dark.*.svg` referansları ve kazanılmış/kilitli görsel ayrımı. İkon API'si `feature/issue-149-rozet-ikon-api` ile geldi; issue hâlâ açık |
| [#322](https://github.com/mustafa-sahin-tr/examapp/issues/322) | Profil sayfasındaki eski rozet bloğunu (student.badges[].imageUrl) kaldır | Eski veri şekline bağlı blok yeni `BadgeDefinition.Icon` (Material Symbols) modeliyle uyumsuz |
| [#64](https://github.com/mustafa-sahin-tr/examapp/issues/64), [#65](https://github.com/mustafa-sahin-tr/examapp/issues/65), [#66](https://github.com/mustafa-sahin-tr/examapp/issues/66), [#335](https://github.com/mustafa-sahin-tr/examapp/issues/335) | — | Bkz. §2.3 |
| [#52](https://github.com/mustafa-sahin-tr/examapp/issues/52), [#104](https://github.com/mustafa-sahin-tr/examapp/issues/104), [#107](https://github.com/mustafa-sahin-tr/examapp/issues/107) | — | Bkz. §2.4 |

### 2.6 documentation / enhancement

| # | Başlık | Özet |
|---|---|---|
| [#352](https://github.com/mustafa-sahin-tr/examapp/issues/352) | Geliştirici devir dokümanı | Bu doküman |
| [#353](https://github.com/mustafa-sahin-tr/examapp/issues/353) | Kullanıcı kılavuzu: tüm roller için ekran görüntülü HTML (PDF'e çevrilecek) | Öğretmen, öğrenci, veli, okul yöneticisi ve admin için rol bazlı kılavuz |
| [#67](https://github.com/mustafa-sahin-tr/examapp/issues/67) | (enhancement, §2.3) | |

---

## 3. Geçmiş güvenlik kararları

Bugünkü kodun "neden böyle" olduğunu açıklayan, kapanmış güvenlik issue'ları. Hepsi kapalı;
yeniden açmadan önce kararı oku.

| # | Konu | Karar / sonuç | Kod izi |
|---|---|---|---|
| [#228](https://github.com/mustafa-sahin-tr/examapp/issues/228) | `StartupConfigDump` Redis parolasını düz yazıyordu | Config dump'ında Redis/Postgres/RabbitMQ parolaları maskelenir; tracked dosyalardan Redis parolası kaldırıldı (PR #239) | Her servisteki `StartupConfigDump.cs` (api, auth-api, BadgeService, Gateway) |
| [#238](https://github.com/mustafa-sahin-tr/examapp/issues/238) | Git'e girmiş secret'lar | Tracked secret'lar kaldırıldı, Keycloak secret'ları env/AppHost parametresine taşındı, gitleaks CI eklendi (PR #268). **Rotate yapılmadı**: kapanış yorumuna göre geçmişteki değerler hiçbir canlı ortamda kullanılmamış. **Prod'a ilk çıkışta tüm secret'lar yeni üretilmeli**; non-Development ortamda `devOnly` önekli değerler fail-fast ile reddedilir | `.github/workflows/gitleaks.yml`, `.gitleaks.toml` |
| [#240](https://github.com/mustafa-sahin-tr/examapp/issues/240) | Register ucu e-posta varlığını ifşa ediyordu; `GET roles` anonimdi | E-posta varlığı ifşa edilmiyor, rol allowlist, `GET roles` yalnız Admin (PR #261) | auth-api register / roles uçları |
| [#267](https://github.com/mustafa-sahin-tr/examapp/issues/267) | #240 rol yükseltme açığı sonrası Keycloak denetimi | Yetkisiz Admin/`exam-service` hesabı taraması yapıldı | — |
| [#246](https://github.com/mustafa-sahin-tr/examapp/issues/246) | Admin kullanıcı listeleri (KVKK) | Audit log, rate limit ve e-posta maskeleme | `tests/ExamApp.Api.IntegrationTests/AdminUserListAuditAndRateLimitTests.cs` |
| [#243](https://github.com/mustafa-sahin-tr/examapp/issues/243) → [#279](https://github.com/mustafa-sahin-tr/examapp/issues/279) | Puan hattı: tek admin RabbitMQ kullanıcısı, `AnswerSubmittedEvent` idempotent değil | **Servis başına RabbitMQ kullanıcısı** (`rabbitmq/definitions.json` + `load_definitions`; Aspire'da bind mount, parolalar AppHost Parameters); EventId/revision tabanlı dedup, `ProcessedAt` retention index'i. Sahte `StudentPointsChangedEvent` ile liderlik manipülasyonu riski bu yolla kapatıldı | `rabbitmq/definitions.json`, `AppHost/` (`#279` yorumu), commit `b2e5655a` |
| [#328](https://github.com/mustafa-sahin-tr/examapp/issues/328) | Yerel RabbitMQ: yeni exchange izinleri mevcut volume'a uygulanmıyordu (bug) | Servis kullanıcı izinleri her `docker-compose up`'ta `definitions.json`'dan yeniden senkronlanır (PR #336). Yeni outbox event'i eklerken `definitions.json` izin regex'ini güncellemeyi unutma; yoksa consumer `ACCESS_REFUSED` ile sessizce durur | `rabbitmq/sync-permissions.sh` |
| [#287](https://github.com/mustafa-sahin-tr/examapp/issues/287), [#289](https://github.com/mustafa-sahin-tr/examapp/issues/289), [#298](https://github.com/mustafa-sahin-tr/examapp/issues/298) | Öğretmen onayı ve askıya alma | Onaysız öğretmen öğretmen özelliklerini kullanamaz; askı mevcut randevuları/bildirimleri etkiler; askı yalnız yeni video token'ını engeller (kalan: #320) | `api/ExamApp.Api/Services/Teachers/Authorization/` |
| [#305](https://github.com/mustafa-sahin-tr/examapp/issues/305) | Yorum thread'i moderasyonu, okul kapsamı, worksheet varlık sızıntısı | Öğrenciye 404, okul kapsamlı görünürlük, moderasyon (PR #327, #329, #330) | — |
| [#323](https://github.com/mustafa-sahin-tr/examapp/issues/323), [#331](https://github.com/mustafa-sahin-tr/examapp/issues/331) | Müsaitlik slotu ve booking yarışları | Slot süresi için DB CHECK kısıtı + öğretmen bazında yazma kilidi; askıdaki öğretmende Pending talep kalmaz | `tests/ExamApp.Api.IntegrationTests/AvailabilitySlotIntegrityPostgresTests.cs`, `BookingSuspensionRacePostgresTests.cs` |
| [#332](https://github.com/mustafa-sahin-tr/examapp/issues/332) | Whiteboard dış link ve sahne boyutu | Dış link onay dialogu, uzak sahne eleman sınırı, tombstone temizliği (PR #348) | — |

Son satırlardaki "Kod izi" sütununda yalnız dosya/klasör adı verildi; satır düzeyinde doğrulanmadı.

---

## 4. Kod içi TODO / FIXME / HACK

`rg '\b(TODO|FIXME|HACK|XXX)\b'` — `node_modules`, `bin`, `obj`, `Migrations`, `dist` hariç — toplam
**7** eşleşme. FIXME, HACK veya XXX yok. Repo bu açıdan temiz; işler issue'larda tutuluyor.

| Yer | Metin | Değerlendirme |
|---|---|---|
| `api/ExamApp.Api/Helpers/WorksheetAccess.cs:25` | `TODO(#11/#12/#13): sharing == PublicAssignable dalı burada ele alınacak.` | #11, #12, #13 **kapalı**. Bayat yorum mu, eksik dal mı, kontrol edilmeli |
| `api/ExamApp.Api/Helpers/WorksheetAccess.cs:63` | `TODO(#13): studentVisibility == Restricted dalı burada ele alınacak.` | #13 kapalı; `CanView` içinde `Restricted` dalının eksik olup olmadığı yetki açısından önemli |
| `api/ExamApp.Api/Services/ExamService.cs:101` | `TODO(#13): CanAssign StudentVisibility=Restricted dalını da dikkate alacak.` | Aynı konu |
| `api/ExamApp.Api/Controllers/StudyItemsController.cs:19` | `[Route("api/study-pages")] // TODO(#140): ... alias'ı frontend rename tamamlanınca kaldırılacak` | #140 kapalı ve UI artık `/api/exam/study-items` kullanıyor (`ui/src/app/services/study-page.service.ts:18`); alias kaldırılabilir |
| `api/ExamApp.Api/Services/Worksheets/WorksheetAuthoringService.cs:153` | `TODO: bu kontrolleri contrrollerda yapabilirsin` | Not düzeyinde |
| `ui/src/app/pages/image-selector/image-selector.component.ts:1464` | `// TODO: Seçim modunu ayarla` | Satırın kendisi zaten modu ayarlıyor; bayat |
| `finance-api/finance-api/Services/TransactionService.cs:89` | `UserId = "default-user" // TODO: Get from authentication context` | finance-api ana ürünün parçası değil; ama orada kullanıcı izolasyonu yok |

---

## 5. Teknik borç başlıkları

### 5.1 BadgeService adı yanıltıcı

- Kural dosyaları bunu açıkça söylüyor: "İSİM YANILTICI, sadece badge değil TÜM outbox event'lerini
  handle eder" (`.claude/rules/architecture.md:9`) ve "ismi yanıltıcı olsa da oraya eklenir (henüz
  refactor edilmedi)" (`.claude/rules/workflow-rules.md:7`).
- Kanıt: `Services/BadgeService/Consumers/` altında 37 dosya var; rozetle ilgisi olmayanlar çoğunlukta:
  `BookingDecisionConsumer`, `BookingRequestCreatedConsumer`, `BookingTeacherUnavailableConsumer`,
  `LoginAttemptedConsumer`, `TeacherApplicationDecisionConsumer`, `TeacherApplicationSubmittedConsumer`,
  `TeacherSchoolRequestSubmittedConsumer`, `UserPreferredLocaleChangedConsumer`,
  `WorksheetAccessDecisionConsumer`, `WorksheetAccessRequestedConsumer`,
  `WorksheetComment{Created,Hidden,Replied}Consumer`, `WorksheetReminderDueConsumer` ve Gemini ile soru
  sınıflandıran `QuestionCreatedConsumer` (`architecture.md:11`). Bildirimler (`Notification`,
  `BadgeNotificationHub`) de burada.
- İstisna: veri başka servisin DB'sindeyse consumer orada olur — `StudentPointsChangedEvent`
  exam API'de `api/ExamApp.Api/Consumers/StudentPointsChangedConsumer.cs` ile tüketilir (#225,
  `architecture.md:12`).
- Etki: deploy/gateway adı `exam-badge-api`, image adı `exam-badge-api`
  (`.github/workflows/azure-vm-acr-deploy.yml`); yeniden adlandırma compose, AppHost, ocelot, k8s
  manifestleri ve RabbitMQ kullanıcılarına (`rabbitmq/definitions.json`) dokunur. Ayrıntı:
  [03-servisler/badge-service.md](03-servisler/badge-service.md).

### 5.2 Üç ocelot dosyasının elle senkronu

- `Services/Gateway/ocelot.json`, `ocelot.Development.json`, `ocelot.Production.json` — her birinde
  21 route var. Gateway açılışta `ocelot.{Environment}.json` varsa onu, yoksa `ocelot.json`'u okur
  (`Services/Gateway/Program.cs:21-23`); Aspire'ın dinamik portları için host/port'lar env ile ezilir
  (`Program.cs:33-43`).
- Bugünkü farklar (route içeriği karşılaştırması):
  - `/app/{everything}` → `ocelot.json:287` `DownstreamPathTemplate: "/{everything}"`,
    `ocelot.Development.json:287` `"/app/{everything}"`.
  - `ocelot.Production.json` downstream host adları farklı: `exam-question-detector`
    (`ocelot.Production.json:44`) ve `exam-minio` (`:275`); diğer iki dosyada `question-detector-dev`
    ve `minio`.
  - `ocelot.Production.json` 4 boşluk, diğerleri 2 boşluk girintili; satır satır diff gürültülü.
- Senkronu koruyan tek otomatik kontrol: `tests/Gateway.Tests/GatewayWebSocketAuthTests.cs:95-100`,
  üç dosyada da her WebSocket route'unun Bearer `AuthenticationOptions` taşıdığını doğrular (#311
  sonrası, PR #346). Diğer route alanları (auth, rate limit, yeni route) için kontrol yok.
- Dokümanlar yalnız bir dosyayı anıyor: `.claude/rules/workflow-rules.md:15`,
  `.claude/skills/gateway-route/SKILL.md:10`, `.claude/commands/ship.md:13`. Yeni route yalnız
  `ocelot.json`'a eklenirse Development ortamında (dosya var olduğu için) **görünmez**.
- Prod image'ında `gateway.Dockerfile` yalnız Production dosyasını `ocelot.json` adıyla bırakır
  (`deploy/dockerfiles/gateway.Dockerfile:16-18`).
- Ayrıntı: [03-servisler/gateway.md](03-servisler/gateway.md).

### 5.3 Proje adı çakışması: iki `ExamApp.Api.csproj`

`api/ExamApp.Api/ExamApp.Api.csproj` ve `auth-api/ExamApp.Api.csproj` aynı ada sahip (`ExamApp.slnx`).
CI Dockerfile'larında `APP_DLL=ExamApp.Api.dll` her ikisi için de geçerli
(`.github/workflows/azure-vm-acr-deploy.yml`, auth-api build bloğu). Aynı `--artifacts-path`'e build
edildiklerinde obj/ çakışıyor ([08 §4.4](08-gelistirme-pratikleri.md#44-kilitli-bin-tuzağı-ve-diğer-tuzaklar)).

### 5.4 Eski solution ve kalıntı dosyalar

Tracked: `api/examination.sln`, `auth-api/ExamApp.Api.sln`, `Services/BadgeService/app.sln` (asıl
solution `ExamApp.slnx`); `api/ExamApp.Api.zip` (~13,5 MB), `api/dump-tables.txt`, kök dizinde
`compare.py`, `subject-insert.sql`, `architect.md`, `aspire-migration-agent-brief.md`, `scratch/`
(mimari HTML + PNG'ler). Hangilerinin hâlâ kullanıldığı Doğrulanmadı.

### 5.5 Test altyapısı borçları

- Build/test CI'ı yok; yalnız gitleaks (`.github/workflows/`).
- `AuthApi.Tests` `ExamApp.slnx`'te yok; IntegrationTests ise var (Docker zorunlu).
- #341 kırık integration testleri; Karma'da tarihsel kırık spec'ler ([08 §4.3](08-gelistirme-pratikleri.md#43-karma-ui-auth-ui)).
- `tests/README.md` sayıları/tablosu eski.
- `question-detector/` için test yok.
- Integration testleri `postgres:16-alpine` (`tests/ExamApp.Api.IntegrationTests/Infrastructure/IntegrationApiFactory.cs:22`)
  ile, docker-compose ve prod `postgres:14` (`docker-compose.yml:286`, `deploy/docker-compose.prod.yml:24`)
  ile koşuyor; sürüm farkı.

### 5.6 Elle senkron tutulan diğer listeler

- Desteklenen diller: `api/ExamApp.Foundation/Localization/SupportedLocales.cs:19` ↔
  `ui/src/app/models/locale.ts` (`SupportedLocales.cs:11` "elle senkron tutulur").
- RabbitMQ izinleri: yeni event'te `rabbitmq/definitions.json` regex'leri (#328).
- Port değişikliği: `docker-compose.yml`, `ocelot*.json`, Angular environment, `local-dev.md`
  (`.claude/commands/ship.md:15-16`).
- Outbox publisher'lar: tek image, üç instance (`deploy/docker-compose.prod.images.yml:19-28`,
  `deploy/scripts/vm-update.sh:48-63`).

### 5.7 Tamamlanmamış i18n

auth-ui'de Transloco yok (`auth-ui/package.json`); UI'da sayfa taşımaları epic #159 altında sürüyor.
auth-api/BadgeService sözlükleri için exam API'deki `ResourcesIntegrityTests` benzeri parite testi
bulunamadı (Doğrulanmadı).

---

## 6. Doğrulanmadı

- Epic'lerin (#52, #104, #107, #127, #159, #160, #174) gövdelerindeki checklist'lerin güncelliği.
- §3 "Kod izi" sütunundaki dosya/klasör referansları satır düzeyinde doğrulanmadı.
- `WorksheetAccess.cs:25/63` ve `ExamService.cs:101` TODO'larının işaret ettiği dalların gerçekten
  eksik mi yoksa başka yerde mi ele alındığı.
- §5.4'teki kalıntı dosyaların kullanılıp kullanılmadığı.
- auth-api ve BadgeService sözlükleri için TR/EN parite testi.
- #149'un kapanmamış olmasının nedeni (ikon API'si merge edilmiş görünüyor).

---

## Ayrı issue adayları

### Tüm bölümlerden toplanan adaylar

Aşağıdaki liste, diğer handover dosyalarının sonundaki "Ayrı issue adayları" bölümlerinden toplanıp tekrarlar birleştirilerek hazırlandı. Kanıt (`path:line`) ve gerekçe, "Ayrıntı" sütunundaki dosyanın kendi "Ayrı issue adayları" bölümündedir. Yazarlar her aday için `gh issue list --state all --search` ile arama yaptı; mevcut bir issue varsa numarası yazıldı. Hiçbiri düzeltilmedi (issue #352 kapsam dışı).

#### Güvenlik (öncelikli)

| # | Aday | Kanıt | Ayrıntı |
|---|---|---|---|
| G1 | MinIO bucket'ları anonim okunur, gateway `/img/{everything}` kimliksiz ve POST'a açık. Soru aktarım (export) paketleri tahmin edilebilir adlarla aynı bucket'a yazılıyor; soru bankası anonim indirilebilir olabilir | `api/ExamApp.Api/Services/MinIOService.cs:131-158`, `QuestionTransferJobRunner.cs:261-268`, `Services/Gateway/ocelot.json:279` | [api.md](03-servisler/api.md), [gateway.md](03-servisler/gateway.md), [07](07-uctan-uca-akislar.md) |
| G2 | question-detector route'unda kimlik doğrulama yok; CORS `*` + credentials; diske yazan `/send-to-fix` herkese açık | `Services/Gateway/ocelot.json:39-49` | [question-detector.md](03-servisler/question-detector.md), [05](05-kimlik-yetki.md) |
| G3 | Öğrenci testi tamamladıktan sonra (doğru cevaplar gösterildikten sonra) cevabını değiştirebiliyor; puan toplayabilir | `TestSessionService.cs:376-384` | [07](07-uctan-uca-akislar.md) |
| G4 | Keycloak realm'inde `bruteForceProtected: false`; auth-api'nin IP rate limiter'ı Keycloak'ın kendi OIDC giriş formunu korumuyor; gateway'de hiç rate limit yok (`/token`, `/auth/realms/*`) | `deploy/keycloak/dev-import/realm-export.json` | [05](05-kimlik-yetki.md), [gateway.md](03-servisler/gateway.md) |
| G5 | Prod Caddyfile Keycloak `/admin`, MinIO konsolu, pgAdmin ve RabbitMQ yönetimini internete açıyor | `deploy/Caddyfile:16`, `:32-34`, `:50-73` | bu dosyada A2 |
| G6 | Gemini API anahtarı URL query string'inde gönderiliyor (log'lara düşebilir) | `Services/BadgeService/Services/GeminiQuestionClassifier.cs:192` | [badge-service.md](03-servisler/badge-service.md) |
| G7 | BadgeService RabbitMQ kimlik bilgisi yoksa `guest`/`guest`'e düşüyor (exam API ve auth-api fail-fast) | `Services/BadgeService/Program.cs:234-235` | [badge-service.md](03-servisler/badge-service.md), [06](06-asenkron-akislar.md) |
| G8 | Servis-servis token'ı admin client `exam-admin` ile alınıyor; servis hesabında hâlâ `manage-realm`; her yerde `aud=account` kabul ediliyor; `ServicePrincipal` içinde eski `preferred_username == exam-admin` yedek kontrolü duruyor | `ServiceTokenProvider.cs:61-66`, `api/ExamApp.Foundation/Security/ServicePrincipal.cs` | [05](05-kimlik-yetki.md), [foundation.md](03-servisler/foundation.md) |
| G9 | `RequireHttpsMetadata=false` prod dahil kodda sabit | `Services/Gateway/Program.cs` | [gateway.md](03-servisler/gateway.md) |
| G10 | Gateway, header varken bile hub token'ını query string'den kabul ediyor; whiteboard hub allowlist'i önek eşleşmesiyle çalışıyor | `Services/Gateway/HubWebSocketAuthExtensions.cs` | ilgili: #311 |
| G11 | `exam-client`'ta direct access grants açık; password-grant login yolu ölü ama erişilebilir | realm-export, `auth-api/Controllers/AuthController.cs:409-476` | [05](05-kimlik-yetki.md), [auth-api.md](03-servisler/auth-api.md) |
| G12 | Prod'da `ForwardedHeaders` `KnownNetworks/KnownProxies` tanımsız; auth-api rate limiter'ı gerçek istemci IP'sini göremeyebilir (Doğrulanmadı) | `auth-api/Helpers/AuthRateLimiting.cs:115-120` | [auth-api.md](03-servisler/auth-api.md) |
| G13 | Aspire'da `jitsi-web` `0.0.0.0`'a bağlanıyor | `AppHost/AppHost.cs:228-232` | [02](02-ortam-kurulumu.md) |
| G14 | MinIO kimlik bilgileri `api/ExamApp.Api/appsettings.json` içinde literal; BadgeService kullanmadığı MinIO root bilgisini alıyor | `api/ExamApp.Api/appsettings.json:81-82`, `AppHost/AppHost.cs:460-466` | [07](07-uctan-uca-akislar.md), [badge-service.md](03-servisler/badge-service.md) |
| G15 | finance-api'de kimlik doğrulama yok, `seed-assets`/`clear-assets` uçları açık | `finance-api/` | [finance.md](03-servisler/finance.md) |
| — | Callback'te open redirect, PKCE ve `state` doğrulaması yok; code/state `console.log` ile basılıyor | auth-ui callback | **zaten #347** |

#### Hata / veri tutarlılığı

| # | Aday | Kanıt | Ayrıntı |
|---|---|---|---|
| H1 | Soru kaydı ile `QuestionCreatedEvent` outbox satırı ayrı `SaveChanges` ile yazılıyor, transaction yok; olay kaybolabilir | `QuestionService.cs:311`, `:333` | [06](06-asenkron-akislar.md) |
| H2 | `QuestionCreatedConsumer` için `ConsumerDefinition` yok, dolayısıyla retry yok (yorum aksini varsayıyor) | `Services/BadgeService/Program.cs:213` | [06](06-asenkron-akislar.md), [badge-service.md](03-servisler/badge-service.md) |
| H3 | AI sınıflandırması öğretmenin etiketlerini eziyor; Gemini boş `subTopicIds` dönerse mevcut eşleşmeler siliniyor | `GeminiQuestionClassifier.cs` | [07](07-uctan-uca-akislar.md) |
| H4 | Yeniden zamanlanan hatırlatma BadgeService'te dedupe ediliyor, bildirim hiç gitmiyor | `WorksheetReminderService.cs:148`, `WorksheetReminderDueConsumer.cs:51-57` | [07](07-uctan-uca-akislar.md) |
| H5 | `BookingRequestCreated` bildirimi null `sub` ile kaydedilebiliyor; öğretmen görmüyor | `BookingRequestCreatedConsumer.cs:71` | [07](07-uctan-uca-akislar.md) |
| H6 | `Notification.SourceBookingId` üzerinde unique index yok; eşzamanlı teslimde çift bildirim olabilir, unique-ihlali catch'leri ölü kod | BadgeService booking consumer'ları | [06](06-asenkron-akislar.md) |
| H7 | Password grant ile `POST /api/auth/login` refresh cookie yazmıyor ve yerel `Users` satırı açmıyor; sonraki refresh 401 alır | `auth-api/Controllers/AuthController.cs:409-476` | [auth-api.md](03-servisler/auth-api.md) |
| H8 | identity `Users` tablosunda Email/KeycloakId için unique index yok; eşzamanlı kayıtta çift satır oluşabilir | `auth-api/Data/` | [auth-api.md](03-servisler/auth-api.md), [04](04-veri-modeli.md) |
| H9 | Aspire'da exam API'ye `BadgeApiBaseUrl` verilmiyor; öğrencinin kendi kendini sıfırlamasındaki BadgeService çağrısı başarısız olabilir | `BadgeResetApiClient.cs:31` | [api.md](03-servisler/api.md) |
| H10 | BadgeService `appsettings.json`'da eski `ExamApi:BaseUrl` (`http://api:8080`); `LoginAttemptedConsumer` bu ayar yoksa mesajı sessizce ack'leyip atıyor | `Services/BadgeService/appsettings.json:9`, `LoginAttemptedConsumer.cs:69-74` | [badge-service.md](03-servisler/badge-service.md), [06](06-asenkron-akislar.md) |
| H11 | BadgeService HttpClient'ları standart resilience handler altında: Gemini 60 sn timeout'u etkisiz, POST'lar yeniden denenebiliyor | `Services/BadgeService/Program.cs`, `ServiceDefaults/Extensions.cs` | [badge-service.md](03-servisler/badge-service.md) |
| H12 | `BadgeThropyComponent` `CommonModule` import etmiyor; `/certificates` sayfası bozuk | `badge-thropy.component.ts:25` | [ui.md](03-servisler/ui.md) |
| H13 | `authGuard` olmayan `/login`'e yönlendiriyor; auth-ui `login.component.ts:52` `access_token` okuyor, uygulama `auth_token` yazıyor | `auth.guard.ts:12` | [ui.md](03-servisler/ui.md), [auth-ui.md](03-servisler/auth-ui.md) |
| H14 | SignalR badge hub istemcisinde `withAutomaticReconnect` yok; sunucu tarafında backplane yok; whiteboard durumu bellekte (tek instance) | `signalr.service.ts:92-101` | [ui.md](03-servisler/ui.md), ilgili #311 |
| H15 | Günün soruları İstanbul gününe, seri (streak) UTC gününe göre hesaplanıyor | — | [07](07-uctan-uca-akislar.md) |
| H16 | Gateway `/oidc-login` middleware'i `ui_locales`'i Keycloak'a iletmiyor | `Services/Gateway/Program.cs:208-217` | [07](07-uctan-uca-akislar.md), [auth-ui.md](03-servisler/auth-ui.md) |
| H17 | Veli kaydında münhasırlık kontrolü yok; ne bağımsız ne okul talepli yeni öğretmen için admin bildirimi üretilmiyor; "onaylı öğretmen" üç yerde farklı kontrol ediliyor; tekrarlı müsaitlik slotları yalnız öğretmen kendi listesini açınca üretiliyor | `ParentController.cs:46`, `TeacherService.cs:285-286` | [07](07-uctan-uca-akislar.md) |
| H18 | Genel 500 işleyici yok; `ExceptionHandlingMiddleware` yalnız `UnauthorizedAccessException` → 403 eşliyor | `api/ExamApp.Api/` | [api.md](03-servisler/api.md) |
| H19 | `SuperAdmin` rolü kodda geçiyor ama realm'de yok; Hangfire girişinde sınıf/metot rol birleşimi SuperAdmin'i dışarıda bırakıyor | — | [05](05-kimlik-yetki.md), [api.md](03-servisler/api.md) |
| H20 | `OutboxEventRegistry.Resolve` kayıt dışı tipler için `Type.GetType`'a düşüyor | `OutboxEventRegistry.cs:72-73` | [foundation.md](03-servisler/foundation.md) |
| H21 | question-detector: 404/400 → 500 dönüyor; `/predict` hatada 200 dönüyor; UI'ın `/send-to-fix-for-answers` gövdesi şemaya uymuyor | `question-detector/main.py` | [question-detector.md](03-servisler/question-detector.md) |
| H22 | finance-api migration hatalarını yutuyor (diğer servisler fail-fast) | `finance-api/finance-api/Program.cs:126-132` | [04](04-veri-modeli.md) |
| H23 | auth-api `KeycloakRoleTransformer` bozuk claim'de exception fırlatabiliyor; `KeycloakService` paylaşılan HttpClient'ın `DefaultRequestHeaders.Authorization` alanını değiştiriyor | `auth-api/` | [05](05-kimlik-yetki.md), [auth-api.md](03-servisler/auth-api.md) |

#### Ortam / konfigürasyon

| # | Aday | Kanıt | Ayrıntı |
|---|---|---|---|
| O1 | `docker-compose.yml` repoda olmayan `./postgres/init-scripts`'i mount ediyor; temiz kurulumda `keycloak` DB'si oluşmuyor | `docker-compose.yml:296` | [02](02-ortam-kurulumu.md), [04](04-veri-modeli.md) |
| O2 | Compose `exam-outbox-publisher`'da `ConnectionStrings__DefaultConnection` yok; gitignore'daki bir dosyaya bağımlı | `docker-compose.yml:143-173` | [02](02-ortam-kurulumu.md), [outbox-publisher.md](03-servisler/outbox-publisher.md) |
| O3 | Compose'da Keycloak issuer (`8081`) ile `Keycloak:Authority` (`5678`) uyuşmuyor; compose auth-api'ye `Server__BaseUrl` ve `Redis__Configuration` verilmiyor | `api/ExamApp.Api/appsettings.json:87` | [02](02-ortam-kurulumu.md), [auth-api.md](03-servisler/auth-api.md) |
| O4 | Aspire'da exam/identity outbox publisher'ları sahip servisin migration'ını beklemiyor | `AppHost/AppHost.cs:484-523` | [02](02-ortam-kurulumu.md) |
| O5 | Compose'daki 8005 / 5080-5083 / 8007-8009 portlarında hiçbir şey dinlemiyor (Kestrel her zaman `Kestrel:Port`'a bağlanıyor) | `docker-compose.yml:10`, `:105`, `:149`, `:185`, `:222` | [02](02-ortam-kurulumu.md), [api.md](03-servisler/api.md) |
| O6 | Health endpoint'leri yalnız Development'ta; gateway `MapDefaultEndpoints()` çağırmıyor; MassTransit için OTel kaynağı yok | `ServiceDefaults/Extensions.cs` | [service-defaults-apphost.md](03-servisler/service-defaults-apphost.md) |
| O7 | Dead-letter outbox satırları ve `_error` kuyrukları için metrik/alarm/yeniden oynatma aracı yok; `OutboxMessages` poll sorgusu için index yok | `Services/OutboxPublisher/Publishers/OutboxProcessor.cs` | [06](06-asenkron-akislar.md), [outbox-publisher.md](03-servisler/outbox-publisher.md) |
| O8 | `exam_outbox_pub` kullanıcısında gereksiz `UserPreferredLocaleChangedEvent` izni; registry × `definitions.json` × `rabbitmq-init.sh` tutarlılığını test eden bir test yok | `rabbitmq/definitions.json:73-74`, `deploy/scripts/rabbitmq-init.sh:55` | [06](06-asenkron-akislar.md) |
| O9 | Keycloak sürümü Aspire'da 26.7.0, compose'da 24.0.1; realm export 26.x'e karşı denenmedi; `minio/minio` image'ı etiketsiz | `AppHost/AppHost.cs:298-299`, `:107` | [service-defaults-apphost.md](03-servisler/service-defaults-apphost.md) |
| O10 | `sync-theme.sh` kökteki eski tema kopyasıyla gerçek temayı `rsync --delete` ile eziyor | `sync-theme.sh` | [05](05-kimlik-yetki.md) |
| O11 | Dev Dockerfile'larda `npm set strict-ssl false`; birden çok `.devcontainer` var olmayan `docker-compose.yaml`'a işaret ediyor | `dockerfiles/`, `.devcontainer` | [ui.md](03-servisler/ui.md), [outbox-publisher.md](03-servisler/outbox-publisher.md) |

#### Doküman eskimesi

| # | Aday | Ayrıntı |
|---|---|---|
| D1 | `.claude/rules/local-dev.md`: port tablosu yanlış (ölü portlar, eksik 8082); RabbitMQ izin tablosunda `auth_api` kullanıcısı, `RABBITMQ_AUTH_API_PASSWORD` ve iki event eksik | [02](02-ortam-kurulumu.md), [06](06-asenkron-akislar.md) |
| D2 | `docs/local-development.md` eski (AddUvicornApp, `deploy/keycloak/import` yolu, portlar) | [02](02-ortam-kurulumu.md) |
| D3 | `.env.example:49` yorumu yanlış realm yolunu (`import`) gösteriyor; doğrusu `dev-import` | [02](02-ortam-kurulumu.md) |
| D4 | `docs/aspire-migration-decisions.md` (`environment.ts`, finance), `docs/data-ownership.md` (prod DB adları, finance_db, identity/badge outbox), `docs/auth-hardening.md` (realm yolu, transformer) eski | [02](02-ortam-kurulumu.md), [04](04-veri-modeli.md), [05](05-kimlik-yetki.md) |
| D5 | `.claude/skills/gateway-route/SKILL.md` port listesi yanlış; `outbox-event` skill'i ve `event-integration-dev` tanımı registry / `definitions.json` / `ConsumerDefinition` / consumer istisnası adımlarını anlatmıyor; `ef-migration` skill'i yalnız `api/ExamApp.Api`'yi anlatıyor | [gateway.md](03-servisler/gateway.md), [06](06-asenkron-akislar.md), [04](04-veri-modeli.md) |
| D6 | Kod içi yanlış yorumlar: `UserRoleChangedEvent.cs` (exam DB'de Users yok), `LoginEventsController` ("gateway'e route edilmez"), `AnswerSubmissionAggregationService.cs:101-103`, `User.RoleUpdatedAtUtc`, AppHost'taki `environment.ts` yorumu (`AppHost/AppHost.cs:771`) | [06](06-asenkron-akislar.md), [05](05-kimlik-yetki.md), [badge-service.md](03-servisler/badge-service.md) |

#### Temizlik / ölü kod

| # | Aday | Ayrıntı |
|---|---|---|
| T1 | auth-api'de ölü uçlar (`/register`, `/login`, `/complete-profile`, `/logout`), `Jwt:*` config, `MinioConfig`, `ImageHelper`, `BaseController`, `Book.cs`, `NotImplementedException` atan metotlar; exam API'de kullanılmayan `Jwt:*`/`ConfigConstants` | [auth-api.md](03-servisler/auth-api.md), [api.md](03-servisler/api.md) |
| T2 | `Services/OutboxPublisher/Worker.cs` kayıtsız şablon kalıntısı | [outbox-publisher.md](03-servisler/outbox-publisher.md) |
| T3 | `Services/Gateway.Tests` slnx dışında ve `tests/Gateway.Tests`'in alt kümesi; ocelot'taki `/oidc-login` route'u hiç çalışmıyor | [gateway.md](03-servisler/gateway.md) |
| T4 | UI: `pages/layout`, tracked `ui/src/app.zip`, kullanılmayan `environment.apiUrl`, `/headerlist` istemci metodu, her istekte `console.log` | [ui.md](03-servisler/ui.md) |
| T5 | Eski model parçaları: `TestPrototypes`, `Rewards`, `Exam` sınıfı, worksheet `Badge`/`StudentBadges` tabloları; manuel SQL script'leri (`clear-script.sql` vb.) güncel şemayla uyumsuz | [04](04-veri-modeli.md) |
| T6 | question-detector'da ~50 tracked `.pt` dosyası (~450 MB); finance-api'de tracked `.deb` | [question-detector.md](03-servisler/question-detector.md), [finance.md](03-servisler/finance.md) |
| T7 | `api/ExamApp.Api/.claude/agent-memory` altında yanlış yerde duran angular-dev hafızası | [api.md](03-servisler/api.md) |

### 08/09 yazımında bulunanlar

Bu bölümün (08 ve 09) yazımı sırasında bulunanlar. Her biri için `gh issue list --state all --search`
ile arandı; eşleşen açık/kapalı issue bulunamadı (aksi belirtilmedikçe).

| # | Ne | Nerede | Neden sorun |
|---|---|---|---|
| A1 | Build/test CI'ı yok | `.github/workflows/` (yalnız `gitleaks.yml` + iki `workflow_dispatch` deploy) | Kırık testler (#341) ve derleme hataları PR'da yakalanmıyor; `dotnet test ExamApp.slnx` + `ng test` için bir PR workflow'u gerek (Testcontainers runner'da Docker ile çalışır) |
| A2 | Prod Caddy yönetim yüzeylerini internete açıyor | `deploy/Caddyfile:16` (Keycloak `/admin`), `:32-34` (`/minio*` console), `:50-73` (`minio.`, `pgadmin.`, `rabbitmq.` alt alan adları) | Keycloak admin console, MinIO console, pgAdmin ve RabbitMQ management IP kısıtı olmadan public; `deploy/README.md:17` ise "MinIO/RabbitMQ dış dünyaya kapalı" diyor. Prod öncesi kapatılmalı veya allowlist/basic-auth eklenmeli. (Benzer bir issue bulunamadı; #267 yalnız Keycloak hesap denetimi) |
| A3 | `WorksheetAccess` içinde kapalı issue'lara bağlı TODO'lar | `api/ExamApp.Api/Helpers/WorksheetAccess.cs:25`, `:63`; `api/ExamApp.Api/Services/ExamService.cs:101` | #11/#12/#13 kapalı ama yorumlar `PublicAssignable` ve `StudentVisibility=Restricted` dallarının "ele alınacağını" söylüyor; yetki kontrolünde eksik dal varsa erişim hatası olur, yoksa yorumlar yanıltıcı |
| A4 | Kullanılmayan `api/study-pages` route alias'ı | `api/ExamApp.Api/Controllers/StudyItemsController.cs:19` | #140 kapalı, UI `/api/exam/study-items` kullanıyor (`ui/src/app/services/study-page.service.ts:18`); ölü yüzey |
| A5 | Üç ocelot dosyası için senkron testi yok | `Services/Gateway/ocelot*.json`; tek kontrol `tests/Gateway.Tests/GatewayWebSocketAuthTests.cs:95-100` | Yeni route yalnız `ocelot.json`'a eklenirse Development'ta kaybolur; dokümanlar (`workflow-rules.md:15`, `gateway-route/SKILL.md:10`, `ship.md:13`) yalnız `ocelot.json`'u anıyor. Route kümesi/auth eşitliğini doğrulayan bir test + doküman güncellemesi |
| A6 | `/feature` komutu `--base main` kullanıyor | `.claude/commands/feature.md:87` | Repoda `main` yok, hedef `master` |
| A7 | `ship_branch.sh` `Closes #N` ekleyemiyor | `scripts/ship_branch.sh:119` | Regex `^issue-`; gerçek branch'ler `feature/issue-N-...` |
| A8 | `AuthApi.Tests` solution dışında; IntegrationTests içinde | `ExamApp.slnx` | `dotnet test ExamApp.slnx` / `tests/coverage.ps1` auth-api testlerini atlıyor, Docker'sız makinede ise düşüyor |
| A9 | `.editorconfig`'te C# bölümü yok | `.editorconfig:4-9` | `indent_size = 2` C#'a uygulanır; `dotnet-format.sh` hook'u 4 boşluklu C# dosyalarını yeniden girintileyebilir |
| A10 | `api/k6-test.js` çalışmıyor | `api/k6-test.js:25` | `http.get(url, payload, params)` — `payload` tanımsız |
| A11 | Eski/yanlış dokümantasyon | `tests/README.md:38-45` ("96 tests"), `.github/copilot-instructions.md:27` (`api/ExamApp.Tests` yok), `.claude/README.md:3-4` (skill sayısı), `deploy/README.md:167` (`finance-api`, `CatalogService` prod'da yok), `.claude/CLAUDE.md:5-10` (göreli linkler `.claude/` içinden kırık) | Yeni geliştiriciyi yanıltıyor |
| A12 | Agent skill önyüklemesinde ad uyuşmazlığı | `.claude/agents/dotnet-api-dev.md`, `test-engineer.md` frontmatter `skills:` ↔ `.claude/skills/{csharp-coding-standards,microsoft-extensions-dependency-injection,testcontainers}/SKILL.md` `name` alanı | Klasör adı ≠ `name`; önyükleme ada göre çözülüyorsa bu skill'ler yüklenmiyor (Doğrulanmadı) |
| A13 | graft helper'larında kişisel mutlak yol | `.claude/helpers/graft-hooks.cjs:7`, `.claude/helpers/graft-statusline.cjs:7` | Önceki geliştiricinin kullanıcı dizini repoda; taşınabilirlik |
| A14 | Prod'da Jitsi yok | `deploy/docker-compose.prod.yml` (servis listesi), `deploy/gcp/k8s/apps.yaml` | Video görüşme (booking) prod topolojisinde çalışmaz; `docs/jitsi-video.md` yerel kurulumu anlatıyor |
| A15 | Azure ve GCP gateway image'ı farklı Dockerfile ile | `.github/workflows/azure-vm-acr-deploy.yml:289-296` (`dotnet-web.Dockerfile`) ↔ `gcp-gke-deploy.yml:146-150`, `deploy/docker-compose.prod.yml:444-452` (`gateway.Dockerfile`) | Aynı servis iki yolla paketleniyor; Azure image'ında üç ocelot dosyası da kalıyor |
| A16 | Integration test ve prod Postgres sürümü farklı | `tests/ExamApp.Api.IntegrationTests/Infrastructure/IntegrationApiFactory.cs:22` (`postgres:16-alpine`) ↔ `docker-compose.yml:286`, `deploy/docker-compose.prod.yml:24` (`postgres:14`) | Sürüme özgü SQL davranışı testte farklı olabilir |
| A17 | Tracked kalıntı dosyalar | `api/ExamApp.Api.zip` (~13,5 MB), `api/examination.sln`, `auth-api/ExamApp.Api.sln`, `Services/BadgeService/app.sln`, `compare.py`, `subject-insert.sql`, `scratch/` | Repo şişkinliği; hangi solution'ın asıl olduğu kafa karıştırıyor |
