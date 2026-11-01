# İşaretleme, Claude tespiti, soru bankası ve kitaplar

Eskiden `.claude/rules/local-dev.md` içindeydi; 2026-11-01'de taşındı. Sınıflandırıcı akışı için ayrıca `.claude/rules/architecture.md`.

**QuestionTrainer — soru tespit modeli eğitim verisi yetkisi.** Yeni realm rolü `QuestionTrainer`: soru
tuvalinde "Kaydet ve Düzelt" / "Eğit" ile question-detector'a eğitim verisi (`send-to-fix`) gönderebilen yetkili
öğretmen. Gateway (`Services/Gateway/RouteRoleAuthExtensions.cs`) `send-to-fix`'i yalnız `Admin` + `QuestionTrainer`'a,
`predict`/`headerlist`/`read-qr`'ı `Teacher` + `Admin`'e açar; UI düğmeleri token'daki realm rolüne bakar
(`hasRealmRole`). Düz "Kaydet" kimse için eğitime göndermez. Uygulama rolü değildir: auth-api `Users.Role`'e yazılmaz,
auth-ui `user_role`'ü Admin/Teacher/Student/Parent içinden seçer. Dev export rolü taşır; **çalışan Keycloak'ta** elle
oluştur: admin console → `exam-realm` → Realm roles → Create role `QuestionTrainer`; yetkili öğretmene Users →
*(öğretmen)* → Role mapping → Assign role. Rol yeni token'da gelir (çıkış/giriş). Öğretmen askıya alınınca rol
**otomatik kalkmaz** (gateway yalnız token rolüne bakar, onay/askı exam API'de) — elle Unassign et. Ayrıntı: `question-detector/EGITIM.md`.

**İşaretleme ekranı — Claude ön-etiketleme (feat/claude-labeling).** İşaretleme modunda (`/labeling`) araç çubuğunda
**Kaynak: Manuel | Model | Claude** seçimi var. Model = question-detector YOLO (her açılışta taze), Claude = exam API
`POST /api/exam/labeling/ai-detect` (Microsoft.Extensions.AI `IChatClient` + Anthropic C# SDK; sayfa → soru kutuları,
her soru kırpması → şık kutuları), Manuel = eğitime gönderilen son hal (send-to-fix başarılı olunca
`PUT /api/exam/labeling/pages/manual`; soru tuvalindeki "Kaydet ve Düzelt" de yazar). Sonuçlar exam DB'de
`LabelingPages`/`LabelingResults` (sayfa = ham görsel SHA-256; question-detector `content_id` = ilk 16 hane); aynı sayfa +
model için kayıtlı Claude sonucu varsa API'ye gidilmez ("Tekrar AI'a sor" zorlar, ücretli). Uçlar yalnız Admin +
QuestionTrainer (onaylı, askıda olmayan öğretmen; gateway'de de kural var).
- **Blok türleri (#578, `SchemaRevision` 5).** Claude 1. aşaması sayfadaki bütün blokları **tek çağrıda** türüyle döner:
  `kind` = `mcq_question` | `activity` | `info_box` (anlatım, "Bilgi"/"Hatırlayalım", "Örnek" çözümü) | `answer_key` | `noise`
  (optik form, karekod, başlık/altlık, logo); `activity` için `component` (epic #569 ailesi `A`–`I`) ve `interaction_type`
  (sunucu bileşenden türetir: A → `matrixChoice`, B → `slotFill`, C → `dragPlace`|`match`, D → `textSelect` (#582), diğerleri null). Şık aşaması yalnız
  `mcq_question` için çalışır; diğer bloklar şıksız, yalnız soru payı + sayfa kırpması. Güvenlik ağı (revizyon 3): bileşensiz
  `activity`'ye de şık aşaması sorulur, >= 2 şık bulunursa `mcq_question` olur (şıklarla), aksi halde etkinlik kalır; bileşenli
  etkinlik şık aşamasına girmez. Bilinmeyen tür `noise` sayılır (log). Sınırlar (revizyon 4): `MaxQuestionsPerPage` yalnız şık
  çağrısı alabilecek blokları (`mcq_question` + bileşensiz `activity`) sayar, tüm bloklar en çok `MaxQuestionsPerPage * 3`;
  1. aşama çıktı sınırı 12000 token. Kesik / reddedilmiş / şema dışı ama yanıt almış çağrının token'ı da bütçeye sayılır.
  Alanlar `PredictionDto`'da isteğe bağlı (null = `mcq_question`, YOLO/eski kayıt) ve `PUT pages/manual` ile gidip gelir;
  geçersiz değer 400 `labeling.invalidBlockKind`. Revizyon 2–5 eski Claude sonuçlarının önbellek anahtarını geçersiz kılar (5, #582: D tanımı metindeki `( )` noktalama
  boşluklarını, yazım yanlışı bulmayı ve düzeltmeyi kapsar; B'den ayrımı prompt'ta).
  **Kaydedilmiş özel prompt sürümü yeni varsayılanı ezer** (eski "yalnız MCQ" talimatıyla Claude yalnız `mcq_question` döner):
  yeni talimatı almak için Prompt penceresinde varsayılanı yükleyip kaydedin.
- **API anahtarı repoya girmez.** Aspire: `dotnet user-secrets set "Parameters:anthropic-api-key" "<anahtar>" --project AppHost`
  (AppHost `anthropic-api-key` secret parametresi → exam API `Anthropic__ApiKey`); AppHost'u yeniden başlat. Anahtar
  yoksa model listesi boş döner, UI Claude seçeneğini kapatır, `ai-detect` 503. docker-compose'da tanımlı değil.
- **Prompt ekrandan düzenlenir:** işaretleme araç çubuğunda **Prompt** → iki aşamanın talimatı (`{width}`/`{height}`
  yer tutucuları), soru/şık kutusu payı (px) ve "soru kutusu şıkları kapsasın". Her kayıt yeni sürüm
  (`LabelingPromptVersions`, geçmiş silinmez; tablo boşsa koddaki varsayılanlar = sürüm 0). Koordinat kuralı ve JSON şeması
  kodda sabit. AI sonuçları `PromptVersion` (gösterim) ve `PromptHash` (önbellek anahtarı: talimatlar + paylar +
  `LabelingPromptSettings.SchemaRevision`) taşır; içerik değişince eski sonuç kayıttan dönmez, Claude yeniden çağrılır.
  Aynı içerikle Kaydet yeni sürüm açmaz. Koddaki sabit kısım (sistem mesajı, şema, kırpma) değişirse `SchemaRevision`'ı artır.
  Düzenleme: Admin + QuestionTrainer (`GET/PUT /api/exam/labeling/prompt`).
- Ayarlar `api/ExamApp.Api/appsettings.json` → `Anthropic`: `Models` (izinli id + 1M token fiyatı, maliyet tahmini
  için; varsayılan seçili model Haiku 5.5 — tarayıcıda daha önce başka model seçildiyse localStorage `labeling.aiModel` onu tutar), `Effort` (varsayılan `low`), `MaxImageEdge` (1568), `MaxParallelCrops` (4), `MaxQuestionsPerPage` (40),
  `LiveCallsPerHour` (kullanıcı başına, 60), `DailyBudgetUsdPerUser` (5) / `DailyBudgetUsdTotal` (25) — 24 saatlik
  tahmini harcama tavanı (Redis sayaç; dolunca 429 `LabelingBudgetExceeded`). Yalnız canlı Claude çağrıları sayılır.
- Kaba ölçüm (2026-10-22, A4 test sayfası, effort low): Opus 5.5 ~7-13 sn, ~$0.04-0.05/sayfa; Haiku 5.5 ~11 sn,
  ~$0.001/sayfa — Ocelot'un varsayılan 90 sn zaman aşımının altında.

**Issue #579 — Claude ile etkinlik yapısı (blok → taslak etkileşim planı).** `POST /api/exam/labeling/activity-structure`
`{imageBase64 (bütün sayfa), model, force, blocks: [{key, x, y, width, height (sayfa pikseli), interactionType}]}` (1–20 blok;
tipler `matrixChoice` | `slotFill` | `dragPlace` | `match` | `textSelect` (#582)). Blok başına **bir** Claude çağrısı; talimat + JSON şeması bloğun
tipinden seçilir, çağrılar paralel (`Anthropic:MaxParallelCrops`). Kırpma = blok + 16 px pay (sayfaya kırpılır, `MaxImageEdge`'e
küçültülür). Yanıt istek sırasıyla blok başına `{key, interactionType, status (ok|error), origin (live|cached|null), plan,
instruction, valid, validationErrors, promptVersion, inputTokens, outputTokens, estimatedCostUsd, error}`: `plan` tipin
InteractionPlan v1'i (id'ler sunucuda: `c1`/`r1`, `s1` (+ seçenek `s1o1`), `t1`/`k1`, `l1`/`r1`, textSelect `t1`/`g1`), kutular **blok kutusuna göre
göreli** sayfa pikseli ve blok kutusuna kırpılmış; çözüm yok (#580). `valid` = kontrolcünün `ValidateDefinition`'ı (taslak geçersiz
olabilir, yine döner; `validationErrors` yerelleştirilmiş ipuçları). Tek bloğun Claude hatası yalnız o bloğu `status: error` yapar.
- **Önbellek** `LabelingActivityResults`: anahtar (kırpma SHA-256, tip, model); kırpma hash'i #577 `QuestionCropHash` tanımı (bankaya
  kayıttaki yolla, aynı blok bankaya kaydedilince `QuestionSources.CropSha256` ile aynı). Satır yalnız **o tipin** prompt parmak izi
  (tip + talimat + `ActivityStructureSettings.SchemaRevision` + tipin şema revizyonu) tutarsa döner (`origin: cached`, ücretsiz;
  token/maliyet ilk çağrınındır). Bir tipin talimatı değişince yalnız o tip yeniden çıkarılır; tip değiştirilen blok yeni tipin
  talimatıyla yeniden çıkarılır (eski tipin kaydı durur). `force` kaydı atlar ve üzerine yazar. Aynı istekte aynı kırpma + tip tek çağrı.
- **Kota:** #578 ile ortak sayaçlar — kayıtta olmayan her blok `LiveCallsPerHour`'dan 1 düşer (istek başında toplu; yetmezse istek
  bütünüyle 429 `LabelingRateLimited` ve reddedilen istek kotayı yemez). Çağrılardan önce en kötü durum maliyeti (blok başına kırpma
  görseli + ~3000 talimat token'ı girdi, 8000 çıktı, modelin fiyatıyla) kullanıcı ve toplam günlük bütçeden **atomik olarak ayrılır**;
  sığmazsa 429 `LabelingBudgetExceeded` (ayrılan geri alınır), çağrılar bitince gerçek maliyete düzeltilir (ödenmiş başarısız çağrı
  dahil). Pahalı modelde (Opus) tek istekteki blok sayısı bütçeyle sınırlanabilir. `ai-detect` bu rezervasyonu kullanmaz (en kötü
  durum sayfa + 40 şık çağrısı fazla karamsar); eski "harcanan ≥ sınır" kontrolüyle kalır. Kayıttan dönenler dahil kullanıcı
  başına dakikada `Anthropic:ActivityStructureRequestsPerMinute` (30) istek, aşınca 429 `LabelingRateLimited`. Canlı çağrı
  gerekiyor ve anahtar yoksa 503 (tamamı kayıtlıysa anahtarsız da döner).
- **İstemci giderse:** kota alındıktan sonra çağrılar istek iptalinden bağımsız koşar (yalnız 3 dk sunucu zaman aşımı ve uygulama
  kapanışı keser; hiç başlamamış çağrının saatlik kotası iade edilir); başarılı sonuçlar yine önbelleğe yazılır, bütçe düzeltilir.
  Tek bloğun önbellek kaydı başarısız olursa loglanır, yanıt yine döner (o blok sonraki istekte yeniden çıkarılır).
- **Tip başına talimat:** `GET /api/exam/labeling/activity-prompts` (5 tip: etkin, varsayılan = sürüm 0, son 20 sürüm),
  `PUT /api/exam/labeling/activity-prompts/{interactionType}` `{instruction}` (≤ 4000, `{width}`/`{height}`) → yeni etkin sürüm
  (`LabelingActivityPromptVersions`, tip başına numara; etkinle aynı içerik yeni sürüm açmaz). Sistem mesajı, koordinat kuralı ve
  şemalar kodda (`ActivityStructureSettings`, `ActivityDrafts`): ortak kısım değişirse `SchemaRevision`'ı, tek tipin şeması/eşlemesi
  değişirse o tipin revizyonunu artır. Yetki işaretlemeyle aynı (Admin + QuestionTrainer, onaylı öğretmen; gateway kuralı kapsıyor).
  **Bilinçli kabul (review L2):** talimatlar #578 sayfa prompt'u gibi **genel**dir — herhangi bir QuestionTrainer bir tipin
  talimatını değiştirince herkesin sonraki çağrıları onu kullanır (sürüm geçmişi kimin kaydettiğini tutar, eski sürüme dönülebilir).
- Migration `AddLabelingActivityStructure` exam API açılışında uygulanır (Aspire'da exam API'yi yeniden başlat).

**Issue #582 — `textSelect` (bileşen D, metin üzerinde işaretleme).** Modlar `select` (altını çiz / yuvarlak / yanlışı bul),
`punctuation` (`( )` boşluklarına palet sembolü), `correct` (yanlışı seç + düzeltmesini yaz; `SlotAnswerNormalizer` metin kuralı).
Plan token listesidir (`{id, text, line, boxes (1–3), gap?}`, en çok 600 token / 100 gap); kontrol bulunan / eksik / fazla sayar,
geri bildirim yalnız seçilen token'ları ve sayıları taşır. Claude (`activity-structure`, tip `textSelect`) yalnız **satır** döner
(metin aynen, hatalar düzeltilmez); token'lar ve kutuları sunucuda `TextSelectTokenizer` ile (satır sonu tiresi birleşir:
`dolaş-` + `mış` → `dolaşmış`, iki kutu). Aynı kural TS'de; ikisi `shared/fixtures/text-tokenize.v1.json`'u koşar — kuralı
değiştirirsen fixture'ı ve `textSelect` tip revizyonunu (`ActivityStructureSettings`) birlikte güncelle. İşaretleme ekranında
bileşen D artık `textSelect` türetir; #582'den önce kaydedilmiş Claude sayfa sonuçlarında D bloklarının tipi null kalır
("Tekrar AI'a sor" ya da elle seçim; sayfa prompt'u revizyon 5 olduğundan eski sonuçlar zaten kayıttan dönmez). Migration yok.

**Issue #470 — işaretleme ekranından soru bankasına kayıt.** Araç çubuğunda **Havuza kaydet** = bankaya kayıt + başarılıysa
**Eğit** ile aynı akış (send-to-fix → manuel kayıt → sonraki sayfa; eğitim başarısızsa sayfa açık kalır, Eğit ile yeniden
denenir; kayıt başarısızsa eğitime gönderilmez). Düz **Eğit** değişmedi (bankaya yazmaz). `POST /api/exam/labeling/pages/bank` sayfadaki MCQ soruları müfettişte
seçilen doğru şıkla, teste bağlamadan (`WorksheetQuestion` yok) mevcut toplu kayıtla (`SaveBulkQuestion`, `TestId=null`)
oluşturur — kırpma + MinIO + `QuestionCreatedEvent` (sınıflandırma otomatik), sahibi çağıran. Uç ek olarak Teacher|Admin
ister (QuestionTrainer tek başına yetmez); kullanıcı başına saatte 30 kayıt (429). Aynı sayfa (`LabelingPages` SHA-256;
`BankSavedAt`/`BankQuestionCount`, süren kayıt `BankSaveStartedAt` — 10 dk sonra bayat; migration `AddLabelingPageBankSave`)
ikinci kez kaydedilmek istenirse 409 `LabelingPageAlreadyInBank` → UI "Yine de kaydet" (force; sorular ikinci kez oluşur;
süren kayıt force ile de aşılmaz). Şık kutuları soru kutusuna kırpılır. Pasaj ve kitap adı (`BookName`) bu ekrandan kaydedilmez (açık soru). **Sınıf zorunlu (#565):** araç çubuğundaki **Sınıf** açılır kutusu (localStorage `labeling.gradeId`) seçilmeden
Havuza kaydet pasiftir; istek `gradeId` taşır (eksik/bilinmeyen → 400) ve `QuestionCreatedEvent.GradeId` olarak sınıflandırıcıya gider.

**Issue #597 — büyük sayfa görseli.** Sunucu sayfa görselini çözerken 40 MP / uzun kenar 12000 px sınırı uygular
(`SkiaImaging.MaxPixels`/`MaxEdge`, decompression bomb koruması; yükseltilmez). Soru tuvali ve işaretleme ekranı sınırı aşan sayfayı
yüklerken sınıra sığacak kadar küçültür (`image-selector/page-image-limits.ts`; PNG → PNG, diğerleri JPEG 0.92); kutular küçültülmüş
görsele göre çizilir. Sınır içindeki sayfaya dokunulmaz (SHA-256 önbellekleri değişmez). Küçültülen sayfanın eğitim id'si ham dosyanınkiyle
uyuşmaz: işaretleme havuzu böyle bir sayfayı "işaretlenmiş" saymayıp yeniden gösterebilir. Toplu kayıt yine de sınırı aşan görsel alırsa
400 `QuestionImageTooLarge` + yerelleştirilmiş mesaj (eskiden genel "Soru seti kaydedilemedi").

**Issue #472 — test ekranında "Havuzdan ekle".** Test detayında (`/test/:id`, düzenleyebilen öğretmen/admin) **Havuzdan Ekle**
soru bankası panelini seçim modunda açar; seçilenler `POST /api/exam/questions/test/{testId}/bank-questions` `{questionIds}` (en çok 60)
ile teste **bağlanır** (kopyalanmaz — test kopyalamayla aynı model; soruyu düzenlemek yine yalnız sahibine/admine açık, puan soruda kalır),
testin sonuna istek sırasıyla; testte olanlar atlanır ve panelde pasif görünür. Bağlanabilir soru: kendi sorusu ya da görebildiği teste
bağlı, sahibi damgalı soru (`CanLink`). Başkasının sahipsiz (legacy, `CreateUserId` 0/null) sorusu bağlanamaz — legacy sahiplik "soruyu
içeren kopya olmayan testin sahibi" kuralıyla türetildiği için bağlamak düzenleme yetkisi verirdi (admin başkasının orijinal testine
eklerken de). Bir soru bile bağlanamazsa hiçbir şey yazılmaz (404). Test başına advisory lock; kullanıcı başına dakikada 30 istek
(`RateLimiting:QuestionBankLink`, 429 `questions.bankLink.rateLimited`). Migration yok.

**Issue #473 — soru tuvalinde Claude ile tespit.** Soru tuvalinin (`/questioncanvas`) araç çubuğunda **Kaynak: Manuel | Model | Claude**
(işaretleme ekranındakiyle aynı uçlar: `ai-models`, `pages/{sha256}`, `ai-detect` force=false — kayıtlı sonuç ücretsiz döner). Yalnız
Admin + QuestionTrainer ve `ai-models` boş değilse görünür; diğerlerinde tuval eskisi gibi YOLO ile açılır. Varsayılan Model (YOLO);
seçim localStorage `questionCanvas.source` / `questionCanvas.aiModel` (işaretleme ekranından ayrı). Claude seçiliyken bir sonraki sayfa
arka planda Claude'a sorulur (önceden getirme; otomatik ilerle açıkken yok; tek seferde bir istek). Sayfa değişince açık sayfanın
isteği iptal edilir, önceden getirme edilmez. Claude hatasında sunucu mesajı araç çubuğunda kalır ve sayfa YOLO ile açılır; 403/429/503'te
(bütçe/kota) Claude o oturumda askıya alınır (sayfalar YOLO ile açılır), Claude yeniden seçilince bir kez daha denenir. Manuel kayıt otomatik
uygulanmaz, yalnız seçilince. Yetki / bütçe değişmedi (`Anthropic:LiveCallsPerHour`, `DailyBudgetUsd*`).

**Issue #577 — kitap kaydı.** Admin + QuestionTrainer için **Kitaplar** sayfası (`/books`): kitap, sayfa ofsetleri, cevap
anahtarı aralıkları ve içindekiler (dosya sayfa aralığı → sınıf / ders / konu / alt konu) girilir. Uçlar
`/api/exam/labeling/books` altında (işaretlemeyle aynı yetki, gateway kuralı da): `GET`/`POST`, `GET`/`PUT`/`DELETE /{id}` (PUT kümeleri
bütün olarak değiştirir; kullanımdaki kitap silinmez, 409 `LabelingBookInUse`), `GET /{id}/pages/{filePage}` (bastırılmış sayfa, cevap
anahtarı mı, içindekiler satırı), `GET /{id}/printed/{printedPage}?near=` (dosya sayfaları). Sayfalar **dosya** sayfasıdır (1'den);
**bastırılmış = dosya + ofset** (ofset negatif olabilir); fasiküllü kitapta (TT MatLab) her fasikül ayrı aralık, bastırılmış numara
tekrar edebilir → birden çok dosya sayfası, `near`'a en yakın önce. Aralıklar küme içinde çakışmaz. İşaretleme ekranında kitap + sayfa
seçilince `pages/bank` `bookId`/`filePage` taşır: sınıf (istekte yoksa) ve ders/konu içindekilerden gelir (`ClassificationSource=Book`,
sınıflandırıcı davranışı `QuestionAnalyzer:BookTocMode`, bkz. architecture.md), `BookName` kitap adı olur, soru başına `QuestionSources`
(bastırılmış sayfa, etkinlik/madde etiketi, yüklenen kırpmanın SHA-256'sı) yazılır. Aynı kırpma bankada varsa kayıttan önce 409
`LabelingCropAlreadyInBank` (+ mevcut kayıtlar; başkasının sorusu yalnız `{questionIndex, otherOwner: true}`, Admin hepsini görür) →
UI "Yine de kaydet" (`force`); cevap anahtarı sayfası 409 `LabelingAnswerKeyPage` (force ile kaydedilir); yanıtta `tocApplied`. Silinmiş
taksonomiye işaret eden içindekiler satırı uygulanmaz (`pages/{filePage}` → `toc: null`, `tocStale: true`). Öğretmen içeriğinde
kullanılan kitabın (kitap testi, çalışma öğesi, başka kullanıcının aynı adlı sorusu) adını / asıl kaydını yalnız Admin değiştirir
(403 `LabelingBookRenameForbidden`). Migration `AddBookRegistry` exam API açılışında uygulanır (Aspire'da exam API'yi yeniden başlat).
**Yayın:** exam API ile BadgeService birlikte deploy edilir; eski BadgeService `Book` kaynağını tanımaz ve kitap sorularını AI ile
sınıflandırıp içindekilerin konusunun üzerine yazar. Ayrı yayın gerekiyorsa önce BadgeService'i `QuestionAnalyzer__BookTocMode=Skip`
ile çıkar, exam API'den sonra `Hybrid`'e al.

**Issue #469 — Claude soru sınıflandırıcısı.** BadgeService'te `QuestionAnalyzer:Provider` = `Claude` (varsayılan, boşken de) |
`Gemini`; geçersiz değer açılışta patlar. Claude seçiliyken BadgeService
taksonomiyi exam API `GET /api/questions/classifier-taxonomy`'den (Admin/servis) alır (`Anthropic:TaxonomyCacheSeconds`,
varsayılan 300 sn bellek önbelleği) ve sistem mesajının sonuna prompt-cache breakpoint'iyle koyar (`Anthropic:CacheTtl`
`1h`|`5m`); yanıt JSON şemalı (structured output). Taksonomi dışı id dönerse sonuç yazılmaz, loglanır ve mesaj
`badge-service_error` kuyruğuna düşer (Gemini/exam API reddiyle aynı). Her çağrıda input / cacheWrite / cacheRead /
output token'ı ve tahmini USD loglanır (`Anthropic:InputUsdPerMTok`/`OutputUsdPerMTok`, varsayılan model Haiku 5.5).
- **Anahtar:** Aspire'da işaretleme ekranıyla aynı `anthropic-api-key` parametresi BadgeService'e de `Anthropic__ApiKey`
  olarak gider (AppHost'u yeniden başlat). Anahtar yoksa sınıflandırma uyarıyla atlanır. docker-compose'da tanımlı değil.
- `QuestionAnalyzer:AIActive=false` iken sağlayıcıdan bağımsız hiçbir sınıflandırma yapılmaz.
- **Sınıf bağlamı (#565).** `QuestionCreatedEvent.GradeId` doluysa Claude sınıflandırıcı taksonomiyi
  `GET /api/questions/classifier-taxonomy?gradeId=` ile yalnız o sınıfa bağlı ders/konu/alt konularla alır ve sınıf başına
  önbellekler (prompt daha küçük, başka sınıfın aynı adlı konusu seçilemez; taksonomi dışı id reddi başka sınıfın id'sini de
  reddeder). Sınıfın taksonomisi boşsa sınıflandırma uyarıyla **atlanır** (tüm taksonomiye düşülmez). `GradeId` null ise
  tüm taksonomi. Exam API `GradeId`'yi şöyle çözer: istekte açıkça verilen sınıf (işaretleme ekranı) → teste bağlı kayıtta
  testin `Worksheet.GradeId`'si → null. Gemini sağlayıcısı sınıfı yoksayar. `Question` tablosunda sınıf kolonu yok; sınıf
  konu (`Topic.GradeId`) üzerinden gelir.
