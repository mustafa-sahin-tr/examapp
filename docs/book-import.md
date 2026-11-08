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
  `BookImport:MaxPages` (varsayılan 400; aşılırsa "The PDF has N pages; the limit is M.") → sayfalar 2363 px genişlikte
  (el ile aktarımdaki görsellerle aynı) WebP (kalite 82) olarak `book-imports/<jobId>/pages/<0001>.webp`'ye yazılır,
  her sayfa `BookImportPages` satırı (dosya sayfası, URL, boyut) ve `StageProcessed` artışı ile kaydedilir. Takip ekranı
  "Geçerli aşama: Sayfalar · N / M" gösterir.
- Şifreli PDF → "The PDF is password protected or encrypted.", açılamayan → "The PDF could not be opened (damaged or not a
  valid PDF).", kaynak yok → "The uploaded PDF could not be read from storage."; iş `Failed`, aşama `Pages`.
- Okuma: iş sahibi `GET /api/exam/book-imports/{id}/pages/{filePage}`; kitap oluştuktan sonra (#1021 `BookImportJobs.BookId`)
  işaretleme rolleri `GET /api/exam/labeling/books/{bookId}/pages/{filePage}/image` (kitaba bağlı en son aktarım).
- **Depolama:** görseller silinmez (işaretleme ekranları okur); kitap başına ~100–150 MB (gerçek sayfa ~300 KB). MinIO
  büyümesi izlenmeli; iş silme / temizleme #1025'te.

## Kurulum aşaması ve onay (#1021)

- **Tarama** (`BookSetupStage`, `IBookSetupAi.SurveyAsync`): sayfalar `BookImport:SurveyBatchSize` (varsayılan 8) sayfalık
  gruplar halinde, uzun kenarı 1000 px JPEG olarak ucuz modele (`BookImport:SurveyModel`, varsayılan `claude-haiku-5-5`)
  gider. Sayfa başına: tür (`cover|toc|test|answerKey|selfAssessment|other`), basılı sayfa no, test başlığı (ünite, test no,
  başlık, dakika), sayfadaki en büyük soru no; içindekiler sayfalarından ünite satırları.
- **Cevap anahtarı** iki kez okunur (`BookImport:AnswerKeyModel`, varsayılan `claude-sonnet-5-5`): tam sayfa (uzun kenar
  1568) ve sayfanın üst / alt %55'lik iki örtüşen döşemesi. İki okuma soru soru karşılaştırılır; farklı hücreler onay
  ekranında sarı, okunamayanlar (`?`) kırmızı işaretlenir.
- **Planlama** (`BookSetupPlanner`, saf; kurallar orada): test başlıklı sayfayla başlar, başlıksız ardışık test sayfaları ona
  eklenir; ofset (basılı = dosya + ofset) en sık farktır; beklenen soru sayısı anahtar uzunluğu (yoksa sayfadaki en büyük
  soru no). İşaretler: `noKey`, `keyMismatch`, `keyConflicts`; kitapta bulunamayan anahtar satırları "eşleşmeyen" listesi.
  Üniteler (ders seçiliyse) Haiku ile dersin o sınıftaki konularına eşlenir.
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
  İptal: `POST /api/exam/book-imports/{id}/cancel` (yalnız onay beklerken; diğer durumlar #1025).

## Soru aktarımı (#1022)

- **Taslak bayrağı** (karar 3, #1023'ün bayrak kısmı burada): `Question.IsDraft`, `Worksheet.IsDraft`; iki varlıkta da filtreler
  ADLI (`DraftQueryFilter.Key` taslak, `DraftQueryFilter.SoftDeleteKey` silinmiş). `WithDrafts()` yalnız taslak filtresini kapatır.
  Taslağı gören yollar: aktarım işi, sınıflandırıcının servis hesabıyla çağırdığı uçlar (`GET /api/questions/{id}/image`,
  `classification-text`, `PUT …/classification`; `QuestionClassificationService` sorguları). `IgnoreQueryFilters()` (tümü) taslakları
  da gösterir: yeni kullanımda bilinçli seçilmeli. Alan alan sızıntı testleri ve inceleme / onay ekranı #1023.
- **Akış** (`BookQuestionImportStage`): testler sırayla; yalnız `Pending` / `Running` olanlar çalışır (bitmiş test yeniden çalışmada
  atlanır, yarım test baştan alınır, kaydedilmişler kırpma tekrarı sayılır). Sayfa başına:
  1. Tespit: `ILabelingService.DetectForImportAsync` — işaretleme ekranının "Claude ile tespit"iyle aynı model listesi, etkin prompt,
     kayıt ve önbellek (`LabelingResults`); kullanıcı kotası / günlük bütçe yok, maliyet `AiCostUsd`'ye (kullanılamayan ücretli yanıt
     dahil). Model `BookImport:DetectModel` (varsayılan `claude-haiku-5-5`, izinli listede olmalı).
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
