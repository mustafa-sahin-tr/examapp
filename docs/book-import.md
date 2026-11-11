# PDF'ten kitap aktarımı (epic #1017)

Özellik notları: yükleme / iş / takip ekranı (#1018) `docs/labeling.md` sonunda; plan ve haklar (#1019)
`docs/feature-notes.md` § Plan ve haklar. Bu dosya epic'in mimari kararlarını ve aşamaların teknik ayrıntısını tutar.

## Karar kaydı (#1020)

**1. İş çalışma zamanı: exam API içinde Hangfire.** Emsal `QuestionTransferJobRunner` / `QuestionTransferJob`:
`Hangfire.PostgreSql`, kendi kuyruğu (`book-import`), `[AutomaticRetry(Attempts = 0)]`, iş başına dağıtık kilit
(`book-import:<jobId>`), iş başında sahibin öğretmen onayı yeniden denetlenir.
- *Neden outbox değil:* `workflow-rules.md`'deki outbox kuralı **servisler arası** olaylar içindir. Bu iş baştan sona exam
  API'nin kendi verisini (exam DB: iş, sayfalar, testler, sorular) okuyup yazar; başka servise olay yayınlamaz, başka servisten
  sonuç beklemez. Outbox + RabbitMQ + BadgeService tüketicisi burada yalnız bir sıçrama ve servisler arası DB / HTTP
  paylaşımı (yasak) getirirdi.
- *Outbox'ta kalan:* aktarılan soruların sınıflandırması mevcut `QuestionCreatedEvent` → BadgeService
  `QuestionCreatedConsumer` yolundan geçer (#1022); kitap içindekileri `ClassificationSource=Book` kuralı aynen geçerli.
- *Devam noktası:* iş satırı (`BookImportJobs`: aşama, `StageProcessed`) ve test başına çocuk satır (`BookImportJobTests`);
  Sayfalar aşaması kayıtlı son sayfadan devam eder (#1025 yeniden dene / iptal).

**2. PDF → görsel exam API içinde, PDFium ile.** Kütüphane **PDFtoImage** (MIT) → **PDFium** (BSD-3, `bblanchon.PDFium.*`
paketleri Apache-2.0 / BSD) + SkiaSharp (MIT, zaten kullanılıyor). Sürüm `6.0.0-preview.2`: projedeki SkiaSharp
`4.153.1` ile tam uyumlu tek sürüm (5.x SkiaSharp 3 ister); kararlı 6.0 çıkınca yükseltilir.
- *Reddedilenler:* PyMuPDF / poppler (AGPL / GPL), `question-detector`'a servisler arası çağrı (yasak; Python servisi yalnız
  YOLO tespiti), Docnet.Core (bakımı durgun, ham BGRA verir).
- *Bellek:* belge bir kez açılır, sayfalar tek tek üretilir ve hemen WebP'ye kodlanıp MinIO'ya yazılır; süreç bellek kullanımı
  sayfa sayısıyla büyümez (400 sayfada ~180 MB, gerçek kitaplarda ~200–330 MB çalışma kümesi). PDF'in kendisi bellektedir
  (en fazla 200 MB, `BookImport:MaxUploadBytes`).
- *Süre (MacBook, tek çekirdek):* 400 sayfalık sentetik PDF 99 sn (~250 ms/sayfa); gerçek resimli kitap sayfaları
  ~370 ms/sayfa → 400 sayfalık kitap ~2,5 dk.

**3. Taslak = global EF filtresi.** `Question` ve `Worksheet`'te taslak bayrağı; global query filter taslakları gizler,
bilinçli açma (`IgnoreQueryFilters` ya da ayrı adlı filtre) yalnız aktarım / inceleme kodunda. Unutulmuş bir sorgu taslak
sızdıramaz (#1023).

**4. Cevap hizalama backend'e taşınır.** `alignAnswers` (işaretleme ekranı, yalnız UI) backend servisine taşınır; el ile
aktarım aracındaki Python portu referanstır (#1022).

**5. Ad:** plan varlığı `SubscriptionPlan` (çalışma planı / "Planım" ile karışmasın).

## Sayfalar aşaması (#1020)

- `BookImportJobRunner.RenderPagesAsync`: kaynak PDF MinIO'dan okunur → `IPdfRasterizer.GetPageCount` → sınır
  `BookImport:MaxPages` (varsayılan 600, #1122 öncesi 400 — 416 sayfalık LGS kitabı; aşılırsa "The PDF has N pages; the limit is M.") → sayfalar 2363 px genişlikte
  (el ile aktarımdaki görsellerle aynı) WebP (kalite 82) olarak `book-imports/<jobId>/pages/<0001>.webp`'ye yazılır,
  her sayfa `BookImportPages` satırı (dosya sayfası, URL, boyut) ve `StageProcessed` artışı ile kaydedilir. Takip ekranı
  "Geçerli aşama: Sayfalar · N / M" gösterir.
- Şifreli PDF → "The PDF is password protected or encrypted.", açılamayan → "The PDF could not be opened (damaged or not a
  valid PDF).", kaynak yok → "The uploaded PDF could not be read from storage."; iş `Failed`, aşama `Pages`.
- Okuma: iş sahibi `GET /api/exam/book-imports/{id}/pages/{filePage}`; kitap oluştuktan sonra (#1021 `BookImportJobs.BookId`)
  işaretleme rolleri `GET /api/exam/labeling/books/{bookId}/pages/{filePage}/image` (kitaba bağlı en son aktarım).
- **Depolama:** görseller silinmez (işaretleme ekranları okur); kitap başına ~100–150 MB (gerçek sayfa ~300 KB). MinIO
  büyümesi izlenmeli; iş silme / temizleme #1025'te.
- **Görsellerden kitap (#1144):** PDF yerine sayfa görselleri (taranmış / telefonla çekilmiş kitap) yüklenebilir.
  `POST …/uploads` gövdesinde `source: "Images"` + `imageCount` (1..`BookImport:MaxPages`, aşılırsa `tooManyPages`); her
  parça bir sayfa görseli (1. parça = 1. sayfa, sıra istemcide doğal sıralamayla), JPEG / PNG / WebP, ≤ 15 MB, EXIF
  döndürmesi hesaba katılmış genişlik ≥ 1000 px (`notImage`, `imageTooSmall` → 400). Görseller
  `book-import-uploads/<uploadId>/image-<0001>`'e yazılır; tamamlamada hepsi varsa iş `Source=Images`, `FileUrl` = bu önek,
  `PageCount` = görsel sayısı ile açılır. Sayfalar aşaması rasterize yerine `NormalizeImagesAsync`: görseli EXIF'e göre
  döndürür, 2363 px genişliğe ölçekler, WebP 82 olarak aynı `pages/` yoluna yazar (kaldığı sayfadan devam eder), bitince
  kaynak görselleri siler. Sonraki aşamalar PDF'le aynıdır. Hatalar: "The uploaded page images are missing.", "Page image N could not be read from
  storage.", "… could not be decoded.", "… is too tall for a page.".
  Ekranda (`/book-imports`) "PDF / Sayfa görselleri" seçimi: görseller çoklu seçimle, "Klasör seç" düğmesiyle
  (`webkitdirectory`) ya da klasör sürükle-bırakla alınır; görsel olmayan ve gizli dosyalar atlanır, kalanlar göreli yola
  göre doğal sırayla (`sayfa2` < `sayfa10`) dizilip liste olarak gösterilir, kitap adı boşsa klasör adı olur. İstemci tür,
  15 MB ve 600 görsel sınırını önceden denetler; genişlik denetimi sunucuda. Ayrıntı ekranında dosya yerine "Görseller"
  (klasör adı) yazar.
- **Karışık sorular (#1144):** görsellerde ikinci seçim "Kitap sayfaları / Karışık sorular". Karışık: `source: "LooseImages"`;
  yükleme ve Sayfalar aşaması aynı, ama tamamlamada görsel başına bir test satırı ("Görsel n", `FileFrom = FileTo = n`) açılır ve
  kitap hakkı (iş başına bir, `BookImport` kullanımı iş id'siyle) burada düşer; kurulum ve kurulum onayı yok, koşucu Sayfalar'dan
  doğrudan Sorular'a geçer. Kitap kaydı yok (`BookId` boş, soru kaynağı kitapsız), cevap anahtarı yok (cevaplar incelemede),
  bütçe kitap aktarımının sayacı. İnceleme hızlı ekleme gibi (`HasLooseQuestions`): onayda sorular etkinleşir, iç taslak testler
  silinir. "Hazır görselleri onayla" onayında isteğe bağlı **"Bu soruları yeni bir teste de topla"** + ad (varsayılan aktarımın adı):
  `POST {id}/approve` gövdesi `{ collectTestName }`; o çağrıda onaylanan sorular sırayla yeni bir teste (taslak değil, öğretmen
  paylaşımı özel) eklenir, yanıt `collectedTestId` (ekranda "Oluşturulan testi aç" → `/test/:id`). Tek tek onaylananlar
  toplanmaz; kitap ve hızlı eklemede ad yok sayılır.

## Kurulum aşaması ve onay (#1021)

- **Tarama** (`BookSetupStage`, `IBookSetupAi.SurveyAsync`): sayfalar `BookImport:SurveyBatchSize` (varsayılan 8) sayfalık
  gruplar halinde, uzun kenarı 1000 px JPEG olarak ucuz modele (`BookImport:SurveyModel`, varsayılan `claude-haiku-5-5`)
  gider. Sayfa başına: tür (`cover|toc|test|answerKey|selfAssessment|other`), basılı sayfa no, test başlığı (ünite, test no,
  başlık, dakika), sayfadaki en büyük soru no; içindekiler sayfalarından ünite satırları.
- **Cevap anahtarı** iki kez okunur (`BookImport:AnswerKeyModel`, varsayılan `claude-haiku-5-5`): tam sayfa (uzun kenar
  1568) ve sayfanın üst / alt %55'lik iki örtüşen döşemesi. İki okuma uyuşmazsa sayfayı hakem okur
  (`BookImport:AnswerKeyTieBreakModel`, varsayılan `claude-sonnet-5-5`); hakemin okuması alınır, ucuz tam sayfa okuması
  karşılaştırma olarak kalır (#1122: önce iki okuma da Sonnet'ti; 416 sayfalık LGS kitabında kurulumun $0,40 / $0,46'sı
  buydu). Okumalar soru soru karşılaştırılır; farklı hücreler onay ekranında sarı, okunamayanlar (`?`) kırmızı işaretlenir.
- **Planlama** (`BookSetupPlanner`, saf; kurallar orada): test başlıklı sayfayla başlar, başlıksız ardışık test sayfaları ona
  eklenir; ofset (basılı = dosya + ofset) en sık farktır; beklenen soru sayısı anahtar uzunluğu (yoksa sayfadaki en büyük
  soru no). İşaretler: `noKey`, `keyMismatch`, `keyConflicts`; kitapta bulunamayan anahtar satırları "eşleşmeyen" listesi.
  Üniteler (ders seçiliyse) Haiku ile dersin o sınıftaki konularına eşlenir.
- **Test başlığı olmayan kitap** (#1122; ör. MEB "LGS Çalışma Kitabı": ünite kapağı + ünite boyunca kesintisiz numaralı
  sorular, ünitelerin anahtarı sonda tek tabloda; ardından kapak + 20 soru + etiketsiz anahtar biçiminde çıkmış sınavlar):
  hiçbir sayfada test başlığı yoksa her ünite / bölüm bölünmeden tek test olur (test başına en fazla 200 soru). Bölüm kapak,
  içindekiler, anahtar sayfasında ya da soru numarası başa dönünce kapanır; başlığı bölümden önce başlayan son içindekiler
  satırıdır. Anahtar satırları okundukları sayfayla gelir: anahtar, önceki anahtardan bu yana biten bölümlere bağlanır (tek
  bölümse sayfanın tüm satırları onun, birden çoksa satırın ünite no'su bölüm sırasıdır).
- **Kayıt:** test satırları `BookImportJobTests` (sayfa aralığı, dakika, `AnswerKey` "ABCD…", `KeyConflicts` "." uyumlu /
  diğer okumanın harfi / "-"), özet `BookImportJobs.SetupJson`, maliyet `AiCostUsd` (başarısız çağrılar dahil). Model listesi
  ve fiyatlar `Anthropic:Models`'tan; API anahtarı yoksa iş "Claude is not configured" ile düşer. Hiç test başlığı yoksa
  "No tests were found in the book". Sistem mesajı / şema / görsel boyutu değişince `ClaudeBookSetupAi.SchemaRevision` artar.
- **Onay ekranı** (takip sayfasında, iş `AwaitingConfirmation` + `SetupConfirmation`): özet, anahtar sayfası görseli, test
  tablosu (aktar / başlık / dakika / sayfalar) ve klavyeyle gezilen anahtar ızgarası (harf → sağa, oklar). Anahtar yoksa
  "cevapsız devam et" ya da "aktarımı iptal et". Uyuşmazlık ve okunamayan hücre kalırken onay kapalı.
- **Onay** (`POST /api/exam/book-imports/{id}/setup/confirm`): hak kontrolü (`CheckAsync`, 429) → kitap kaydı (#577:
  ofset, anahtar aralıkları, içindekiler) → `BookTest` satırları ("1-01 · Başlık") → hak düşer
  (`RecordUsageAsync(userId, "book-import", jobId)`) → iş `Questions` aşamasında kuyruğa girer (#1022 iskelet). Aynı adlı
  kitap varsa 409 + `existingBook`: mevcut kitabı kullan (kayıt kümeleri boşsa kurulumla doldurulur) ya da yeni ad.
  İptal: `POST /api/exam/book-imports/{id}/cancel` (onay beklerken burada; diğer durumlar aşağıda, #1025).

## Soru aktarımı (#1022)

- **Taslak bayrağı** (karar 3, #1023'ün bayrak kısmı burada): `Question.IsDraft`, `Worksheet.IsDraft`; iki varlıkta da filtreler
  ADLI (`DraftQueryFilter.Key` taslak, `DraftQueryFilter.SoftDeleteKey` silinmiş). `WithDrafts()` yalnız taslak filtresini kapatır.
  Taslağı gören yollar: aktarım işi, sınıflandırıcının servis hesabıyla çağırdığı uçlar (`GET /api/questions/{id}/image`,
  `classification-text`, `PUT …/classification`; `QuestionClassificationService` sorguları). `IgnoreQueryFilters()` (tümü) taslakları
  da gösterir: yeni kullanımda bilinçli seçilmeli. Alan alan sızıntı testleri ve inceleme / onay ekranı #1023.
- **Akış** (`BookQuestionImportStage`): testler sırayla; yalnız `Pending` / `Running` olanlar çalışır (bitmiş test yeniden çalışmada
  atlanır, yarım test baştan alınır, kaydedilmişler kırpma tekrarı sayılır). Sayfa başına:
  1. Tespit: `ILabelingService.DetectForImportAsync` — işaretleme ekranının "Claude ile tespit"iyle aynı model listesi, etkin prompt,
     kayıt ve önbellek (`LabelingResults`); kullanıcı kotası yok, aktarım bütçesi ve maliyet aşağıda (#1024). Model `BookImport:DetectModel` (varsayılan `claude-haiku-5-5`, izinli listede olmalı).
  2. Yalnız `mcq_question` blokları; okuma sırası (sütun, sonra yukarıdan aşağı), şıksız kök + hemen altındaki şık kutusu birleşir.
  3. `BookQuestionLayout.Prepare`: soru kutusu taşan şıkları içine alır, şıklar (satır, x) sırasıyla dolgulanır (25 px),
     "Cevapları Hizala" kurallarıyla hizalanır, örtüşen dolgu tespit kutularının ortasından kesilir. Kurallar ve toleranslar el ile
     aktarım aracının Python portuyla aynı (2363 px genişliğe ölçekli); testler aynı girdilerde Python sonucuyla karşılaştırıldı.
  4. Numaralama sayfaya dayanır: kurulum taramasının okuduğu "sayfadaki en büyük soru no" (`BookImportPages.MaxQuestionNumber`)
     varsa sayfa önceki sayfanın sonundan başlar; eksik tespit sonraki sayfayı kaydırmaz. Cevap anahtardan (`AnswerKey[no-1]`).
  5. Kayıt: `ILabelingBankService.SaveImportedPageAsync` (ayrı DI scope'u) — `pages/bank` ile aynı kırpma, kitap kaynağı
     (`QuestionSources`: etkinlik etiketi = kitap testi adı "1-01 · Başlık", madde = soru no), içindekiler (`ClassificationSource=Book`)
     ve `QuestionCreatedEvent`; sorular taslak, `BankSharing=Private`, cevapsız olabilir. Kırpma hash'i bankada olan soru atlanır ve
     sayılır (`DuplicatesSkipped`). Rol özniteliği, saatlik kota ve sayfa "kaydedildi" kilidi yok.
- **İşaretler** (`BookImportJobTests.ReviewFlagsJson`, sayfa / soru referanslı): `countMismatch` (bulunan ≠ anahtar), `missingNumbers`,
  `extraQuestions`, `optionCount` (≠ 4), `noAnswer`, `notSaved` (ikiden az şık), `detectionFailed`, `saveFailed`. İşaretli test
  `NeedsReview`, işaretsiz `Done`; her sayfası düşen test `Failed`. Bir sayfa ya da test diğerlerini durdurmaz.
- Takip ekranı test başına bulunan / anahtardaki / kaydedilen / atlanan sayıları ve işaretleri sayfa ve soru numarasıyla gösterir.

## İnceleme ve onay (#1023)

- **Taslak test:** soru aktarımı her test için kaydedilen soruları sırayla bağlayan taslak bir test (`Worksheet.IsDraft`) açar
  (ad "kitap – test", süre kurulumdan, öğretmen paylaşımı Private, sahibi işin sahibi); `BookImportJobTest.WorksheetId`.
  Soru id'leri bankaya kayıttan kırpma hash'iyle döner (`LabelingImportPageResult.QuestionIds`).
- **Uç noktalar** (sahibi / Admin, iş `ReadyForReview`): `GET {id}/tests/{testId}/questions` (numara, sayfa, görsel, cevap),
  `PUT …/questions/{questionId}/answer`, `POST {id}/tests/{testId}/approve` (cevapsız soru → 409 `bookImport.answerless`),
  `POST {id}/approve` (onaylanabilenler; atlananlar `answerless` / `failed`), `DELETE {id}/tests/{testId}` (yalnız o testin taslak
  testi ve taslak soruları soft-delete). Onaylanacak test kalmayınca iş `Completed` / `Approval`.
- **Onay** `IsDraft`'ı kaldırır; öğretmen paylaşımı Private, soru havuzu paylaşımı olduğu gibi kalır. Sınıflandırma taslakta da
  yazar (`WithDrafts`), onaydan sonra gelen sonuç da uygulanır.
- **Sızıntı:** taslaklar adlı global filtreyle her sorgudan gizlidir. `IgnoreQueryFilters()` kullanan okumalar yalnız oturum /
  cevap geçmişinden okur (taslak atanamaz, başlatılamaz); ham SQL soru / test tablolarına dokunmaz. Alan başına test: havuz
  araması ve paylaşılan havuz (`QuestionBankSearchTests`), test listeleri (`ExamServiceWorksheetListTests`), atama
  (`WorksheetAssignmentServiceSchoolOnlyTests`), çözme (`TestSessionServiceActivityKindTests`), sayılar
  (`TeacherServiceDashboardSummaryTests`), veli (`ParentAssignmentServiceTests`). Liderlik tablosu yalnız öğrenci puanından okur.
- **Ekran:** takip sayfasında "İnceleme ve onay" (`book-import-review`): "Yalnız bakılması gerekenler" varsayılan açık, satırda
  işaretler, Önizle (soru görselleri + A–D seçici, "Cevaplanan x/y"), Onayla, Sil (ikinci tıkla); toplu onay onay ister ve
  atlananları bildirir. Önizleme taslağı genel test ekranına açmaz (aktarım uç noktasından okur).
- **İşaretlemede düzelt** (karar: seçenek 1 — sayfanın taslaklarını değiştir): satırdaki menü testin sayfalarını listeler
  (inceleme işaretinde geçenler bayraklı), önizlemede her sorunun yanında da kısayol var. `/labeling?bookImport=&test=&page=`
  klasör yerine işin sayfa görselini açar (kitap kilitli, sayaçlar gizli); tespit her zamanki kaynaklardan (aktarımın Claude
  sonucu önbellekte, maliyetsiz). "Taslakları değiştir" → `PUT {id}/tests/{testId}/pages/{filePage}/questions`
  (`BookImportPageFixService`): görsel sunucuda işin sayfasından okunur; yeni sorular taslak kaydedilir (eski taslakların
  kırpmaları tekrar sayılmaz, `ReplacingQuestionIds`), sonra tek transaction'da eskiler soft-delete, yeniler eskilerin
  sırasına (eskisi yoksa önceki sayfaların sorularından sonra) bağlanır. Numara: madde etiketi sayıysa o, yoksa sayfanın eski
  ilk numarasından sırayla; cevap: işaretlenen şık, yoksa anahtar. Sayfanın inceleme işaretleri yeniden hesaplanır, sayı
  uyuşmazlığı testin güncel soru sayısıyla; işaret kalmazsa test `Done`. Başarısız testte (taslak testi yok) düzeltme taslak
  testi açar. Boş sayfa kaydı onayla sayfanın taslaklarını siler. Kayıt başarısızsa test değişmez. Kırpması başka bir aktarımda
  ya da bankada etkin olan soru tekrar sayılır ve bağlanmaz (aktarımla aynı kural).

## Maliyet ve bütçe (#1024)

- **Bütçe:** aktarımın bütün Claude çağrıları (kurulum taraması, anahtar okumaları, ünite eşleme, sayfa tespiti) `BookImportBudget`'tan
  geçer. Her canlı çağrıdan önce aktarım sayacı (`labeling-ai-usd:book-import`, `Anthropic:DailyBudgetUsdImport`, varsayılan 15 $)
  ve toplam sayaç (`labeling-ai-usd:all`, `DailyBudgetUsdTotal`, 25 $) denetlenir; ödenen tutar ikisine de yazılır.
- **Kural:** aktarım kullanıcı bütçesine (`DailyBudgetUsdPerUser`) ve saatlik canlı çağrı kotasına (`LiveCallsPerHour`) yazılmaz:
  öğretmenin işaretleme ekranındaki kotasını tüketmez. Aktarım sınırı toplamdan küçük tutulur; toplamın en az `Total − Import` kadarı
  canlı işaretlemeye kalır. Canlı işaretleme toplamı doldurmuşsa aktarım da bekler.
- **Duraklama:** sayaç doluysa iş başarısız olmaz, `AwaitingBudget` ("Bütçe bekleniyor") olur ve pencerenin kalan süresi + 1 dk
  sonra (bilinmiyorsa 30 dk) Hangfire ile yeniden kuyruğa girer (`ResumeAt`). Soru aktarımında bitmiş testler atlanır, yarım test
  baştan alınır (tespit önbelleği tekrar ödetmez); kurulumda aşama baştan çalışır.
- **Tavan:** `BookImport:MaxJobCostUsd` (boş / 0: yok) işin maliyet tavanı. Aşılınca iş `AwaitingCostApproval`; Admin takip
  ekranında "Devam et" der (`POST {id}/continue`, yalnız Admin), tavan şimdiki maliyet + yapılandırılan tavana yükselir
  (`BookImportJobs.CostCeilingUsd`). Bir çağrı tavanı en fazla kendi maliyeti kadar aşar.
- **Kayıt:** ödenen her çağrı (kullanılamayan ücretli yanıt dahil; önbellekten dönen hariç) `BookImportCosts` satırı: aşama, test
  (soru aktarımında), model, girdi / çıktı token, USD. `AiCostUsd` bu satırların toplamı. Takip ekranında yalnız Admin toplam
  maliyeti, tavanı, aşama × model dökümünü ve ızgarada test başına maliyeti görür.
- **Log:** iş incelemeye geçince tek satır: `Book import job {JobId} finished: … pages, … tests, … questions, … flagged tests,
  … failed tests, cost $…`; duraklama `paused in stage …` satırıyla.
- **Rapor (#1056):** `/admin/book-import-costs` (menü "Aktarım Maliyetleri", yalnız Admin / SuperAdmin), uç `GET /api/exam/admin/book-import-costs`
  (`from`, `to` UTC günü, iki uç dahil, varsayılan içinde bulunulan ay; `kind`, `userId`, `page`, `pageSize`) ve `.../jobs.csv` (en çok 10.000 satır; `;` ayraç,
  virgüllü ondalık). Maliyet, aralıkta yazılan `BookImportCosts` satırlarının toplamıdır: ay sınırını aşan iş her aya kendi çağrılarıyla girer. İş kümesi: aralıkta
  açılan ya da aralıkta maliyet yazılan işler (iptal ve hata dahil). Sayfa ve soru sayıları işin tamamına aittir, incelemede silinen testin soruları sayılmaz.
  Ad ve rol auth-api'den (`users/lookup`), plan `IAdminSubscriptionService.CurrentPlansAsync`; erişim `AdminDataAccessLogs`'a yazılır.

## Devam, yeniden deneme ve iptal (#1025)

- **Kaldığı yerden devam:** Hangfire işi (`book-import` kuyruğu, iş başına dağıtık kilit; ikinci çalıştırma kilidi alamayıp çıkar)
  yeniden çalışınca sayfalar kayıtlı son sayfadan, soru aktarımı ilk bitmemiş testten devam eder; bitmiş sayılan testler
  `Done`, `NeedsReview`, `Failed`, `Approved`, `Removed`. Yarıda kalan test baştan alınır: sayfa kaydı `ResumeDrafts` ile
  çağrılır, aynı kullanıcının bu kitaptaki, başka bir teste bağlı olmayan (ya da testin kendi taslak testine bağlı) taslağı
  kırpma hash'iyle bulunup yeniden kullanılır (tekrar sayılmaz, yeni soru yazılmaz); taslak testin sırası bu çalıştırmaya göre
  yeniden yazılır, test ↔ taslak test bağlantısı taslak testle aynı kayıtta yazılır.
- **Geçici Claude hataları:** bağlantı, zaman aşımı, 408 / 409 / 429 / 5xx Anthropic SDK içinde kısa üstel beklemeyle
  `Anthropic:MaxRetries` (varsayılan 3, 0–10; bütün işaretleme çağrıları) kez yeniden denenir; sonra sayfa tespiti başarısız
  sayılır (`detectionFailed`), testin bütün sayfaları başarısızsa test `Failed` + hata metni.
- **Takılmış iş:** `Queued` / `Running` iş `BookImport:StallMinutes` (varsayılan 15) dakikadır güncellenmediyse (her sayfa ve test
  satırı günceller) ya da `AwaitingBudget`'ın `ResumeAt`'i bu kadar geçtiyse DTO `stalled: true`; takip ekranı bant + "Yeniden dene".
- **Uçlar** (sahip ya da Admin): `POST {id}/retry` — `Failed` ya da takılmış işi yeniden kuyruğa verir (hata ve bitiş temizlenir);
  `POST {id}/tests/{testId}/retry` ve `POST {id}/tests/retry` — hatalı testi / bütün hatalı testleri `Pending` yapar, iş
  `Questions` aşamasında kuyruğa girer (yalnız `ReadyForReview`, `Completed` ya da soru aşamasında `Failed` işte);
  `POST {id}/cancel?deleteDrafts=` — `Queued`, `Running`, `AwaitingConfirmation`, `AwaitingBudget`, `AwaitingCostApproval`,
  `Failed` işi `Cancelled` yapar. Çalışma iptali sayfa, test ve aşama arasında görür, yarıdaki test `Pending` kalır; iptal edilen
  iş hataya çevrilmez. Taslaklar kalırsa iptal edilen işte testler incelenip onaylanabilir / silinebilir (işaretlemede düzelt de);
  `deleteDrafts=true` onaylanmamış testlerin taslak testlerini ve sorularını, ayrıca bu kitapta sahibin hiçbir teste bağlı
  olmayan taslaklarını siler (testler `Removed`).
- **Ekran:** hata bandı aşamaya göre sade açıklama + "Teknik ayrıntı" + "Yeniden dene"; hatalı test "Bu testin soruları
  alınamadı" + "Testi yeniden dene", birden çok hatalı testte "Hatalı testleri yeniden dene (n)"; "Aktarımı iptal et" satır içi
  onay: taslak varsa "İptal et, taslakları sakla" / "İptal et ve taslakları sil".

## Örnek kitapla demo (#1032)

Öğretmen gerçek aktarımı açmadan özelliği görür. Claude çağrısı, depolama yazımı ve `book-import` hakkı yoktur.

- **Kaynak:** admin hazır bir kitap aktarımının detayında "Demo kaynağı yap" der (`PUT /api/book-imports/{id}/demo-source`).
  Tek kaynak olur (`BookImportJobs.IsDemoSource`, filtreli tekil dizin). Sorusu olan bir testi yoksa reddedilir.
- **Başlatma:** `POST /api/book-imports/demo`. Kaynağın sorusu olan ilk testi, en çok 5 soruyla, yeni işe yazılır
  (`DemoSourceJobId` doluysa iş demodur). Sayfa satırları yalnız testin ve cevap anahtarının sayfaları için kopyalanır, görseller
  paylaşılır. Bitmemiş demo varsa yenisi açılmaz, o döner.
- **Oynatma:** `BookImportJobRunner` demoda `BookImportDemoReplay`'e ayrılır; kuyruğa nereden düşerse düşsün AI aşamasına girmez.
  Sayfalar → kurulum → onay → sorular → inceleme. Adım süresi `BookImport:DemoStepMilliseconds` (varsayılan 1500).
- **Kopya:** onaydan önce de sorular öğretmenin özel taslak kopyasıdır (görsel, şık, pasaj aynı; kaynak satırı kitaba bağlı değil,
  kırpım özeti rastgele, gerçek aktarımda tekrar sayılmaz). Onay normal akıştır: `IsDraft` kalkar, sorular öğretmende kalır.
- **Sınır:** `BookImport:DemoMaxRuns` (varsayılan 3). Silinen demo sayılır, başarısız demo sayılmaz. Demo maliyet raporuna girmez.
- **Roller:** gerçek aktarım hâlâ pilot (Admin, QuestionTrainer). Öğretmene yükleme, yeniden deneme ve sayfa düzeltme uçları kapalı.

## Hızlı ekleme (#1048, #1052, #1053)

- **Model:** hızlı ekleme bir kitap aktarımı işidir (`BookImportJobs.Kind = QuickAdd`; mevcut işler `Book`). Fotoğraf = sayfa
  (`BookImportPages`, `quick-adds/<jobId>/photos/<n>.jpg`), fotoğraf başına bir test satırı ("Fotoğraf n", `FileFrom = FileTo = n`).
  Kitap kaydı ve kurulum yok: iş yüklemede `Questions` aşamasında kuyruğa girer; koşucu, devam / yeniden deneme / iptal (#1025),
  bütçe ve tavan (#1024), inceleme işaretleri ve taslak test (#1023) kitapla aynı. `BookImportOwner.Kind` görünürlüğü ayırır:
  `api/book-imports` yalnız kitapları, `api/quick-add` yalnız hızlı eklemeleri görür (Admin dahil).
- **Kaynak:** `QuestionSources.BookId` boş olabilir; hızlı ekleme sorusunun kaynağı kitapsız (sayfa = fotoğraf sırası, etiket
  "Fotoğraf n"). Kırpma hash'i tekrar ve devam kuralları aynı. İçindekiler yok: sorular normal sınıflandırılır (#1047 dahil).
- **Yükleme** `POST /api/exam/quick-add` (multipart: `photos[]`, `answers[]`, `gradeId`, `subjectId?`; Teacher / Admin /
  QuestionTrainer, onaylı öğretmen policy'si): en çok 10 fotoğraf, her biri en çok 15 MB; JPEG / PNG / WebP (iOS Safari
  `accept="image/*"` ile HEIC'i JPEG'e çevirir). Önce hepsi çözülür (EXIF yönü uygulanır, uzun kenar 2400 px, meta verisiz JPEG);
  bozuk biri varsa hiçbir şey saklanmaz, 400 `photo` = sırası. Perspektif düzeltme yok.
- **Cevap anahtarı** (fotoğraf başına, isteğe bağlı): harfler sırayla, ayrık ya da bitişik, numaralı da olabilir ("A C B",
  "ACB", "1-A 2) C"); bilinmeyen `?` / `-` / `_`. Tespit edilen sorular okuma sırasıyla numaralanır, anahtar **yalnız bulunan
  sayı anahtar uzunluğuna eşitse** uygulanır; değilse hiç yazılmaz, fotoğraf `countMismatch` (+ `noAnswer`) işaretini alır,
  cevaplar incelemede girilir (kitapta anahtar numarayla yine uygulanır).
- **Hak:** `quick-add` fotoğraf başına, yüklemede (`quick-add:<jobId>:<n>`; yeniden deneme tekrar saymaz). Kalan hak bütün
  fotoğraflara yetmiyorsa 429 (#1029 biçimi), hiçbir şey saklanmaz. `GET /api/exam/quick-add/quota`.
- **Bütçe:** ayrı sayaç `labeling-ai-usd:quick-add` (`Anthropic:DailyBudgetUsdQuickAdd`, varsayılan 5 $) + toplam; kitap
  aktarımının sayacını tüketmez. Maliyet tavanı `BookImport:MaxJobCostUsd` aynı.
- **Uçlar:** `GET /api/exam/quick-add` (liste), `GET {id}` (fotoğraf satırları, işaretler), `GET {id}/photos/{n}` (JPEG),
  `POST {id}/retry`, `POST {id}/cancel?deleteDrafts=`.
- **Ekran (#1053):** `/quick-add` (menü "Hızlı soru ekle", yalnız öğretmen; Soru Havuzu başlığında da bağlantı) — sınıf (son seçim
  `localStorage` `quickAdd.gradeId`), isteğe bağlı ders, `<input type="file" accept="image/*" capture="environment" multiple>`
  (telefonda kamera, başka yerde galeri / dosya; Capacitor #1045), fotoğraf başına küçük resim + cevap anahtarı alanı. Tür / boyut /
  10 sınırı yüklemeden önce denetlenir; hak satırı "Bu ay x/y fotoğraf" (sınırsızda gizli), kalan haktan fazla seçimde uyarı;
  429'da yükseltme penceresi `entitlementLimitInterceptor`'dan. Yükleme ilerlemeli; sonra `/quick-add/:id` durum kartı: Sırada /
  İşleniyor / İncelemeye hazır / Başarısız (+ Yeniden dene; takılmış iş de), fotoğraf başına bulunan / taslak sayısı ve işaretler.
  İş bitene kadar 3 sn'de bir yoklanır (liste 5 sn). İnceleme ve onay ekranı #1054.
- **İnceleme ve onay (#1054):** `/quick-add/:id`'de taslak varsa sonuç listesi yerine kitap aktarımı inceleme ekranı (`BookImportReviewComponent`,
  `kind="QuickAdd"`): satır = fotoğraf, küçük resim, işaretler, A–E seçici; yüklemede girilen cevaplar seçili ve değiştirilebilir. Uçlar
  `GET {id}/tests/{testId}/questions`, `PUT .../questions/{questionId}/answer`, `POST {id}/tests/{testId}/approve`, `POST {id}/approve`,
  `DELETE {id}/tests/{testId}`; `BookImportOwner.Kind` iki türü ayırır (hızlı ekleme kitap uçlarından 404). Cevapsız soru 409
  `bookImport.answerless`. **Onay:** sorular etkinleşir (özel), iç taslak test ve bağlantıları silinir; test yayımlanmaz.
- **İşaretlemede düzelt (#1054):** `PUT {id}/tests/{testId}/pages/{filePage}/questions`, yalnız Admin ve QuestionTrainer; `/labeling` ve
  `LabelingController` öğretmene kapalı olduğu için düğme de öğretmene görünmez (karar: kullanıcı, 2026-11-08). Öğretmen fotoğrafın taslaklarını silip
  yeniden yükler. Bağlantı `/labeling?bookImport=&test=&page=&kind=QuickAdd`. Düzeltmede anahtar yalnız sayı anahtar uzunluğuna eşitse uygulanır.

## Sorudan ayrı çerçevedeki uyaranlar (#1062)

Soru kutusunun üstünde kendi çerçevesinde duran metin, şiir, tablo, görsel ya da kural (`info_box`) pasaj olarak kaydedilir; sorunun
kırpımı değişmez. `BookQuestionLayout.AttachPassages` kutuyu aynı sütunda altındaki ilk soruya bağlar, sahibin tepesinde keser, bir
sorunun birden çok kutusunu tek pasajda birleştirir. "1 - 2. soruları ..." başlıklı kutuda Claude `shared_questions` döner, aynı pasaj
sonraki sorulara da bağlanır. İnceleme ekranı pasajı sorunun üstünde gösterir.

Bilinen sınırlar: işaretleme ekranından sayfa düzeltmesi (`ReplacingQuestionIds`) pasaj taşımaz; sonraki sayfadaki soruya ait kutu
bağlanmaz. Mevcut Puan BTS kitaplarına 97 pasaj elle eklendi (102 soru); araç ve plan harici diskte `book-import/info-box-passages/`.
