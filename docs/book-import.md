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
  gider. Sayfa başına: tür (`cover|toc|test|activity|answerKey|selfAssessment|other`; `activity` #1134), basılı sayfa no, test başlığı (ünite, test no,
  başlık, dakika), sayfadaki en büyük soru no; içindekiler sayfalarından ünite satırları.
- **Cevap anahtarı** iki kez okunur (`BookImport:AnswerKeyModel`, varsayılan `claude-haiku-5-5`): tam sayfa (uzun kenar
  1568) ve sayfanın üst / alt %55'lik iki örtüşen döşemesi. İki okuma uyuşmazsa sayfayı hakem okur
  (`BookImport:AnswerKeyTieBreakModel`, varsayılan `claude-sonnet-5-5`); hakemin okuması alınır, ucuz tam sayfa okuması
  karşılaştırma olarak kalır (#1122: önce iki okuma da Sonnet'ti; 416 sayfalık LGS kitabında kurulumun $0,40 / $0,46'sı
  buydu). Okumalar soru soru karşılaştırılır; farklı hücreler onay ekranında sarı, okunamayanlar (`?`) kırmızı işaretlenir.
  Onay ekranı (#1188, #1192): sarı hücrenin altında diğer okuma durur (tıklayınca alınır; `-` diğer okumada soru yok demek,
  tıklayınca kutu silinir), Enter mevcut harfi onaylar, durum satırı ilk açık uyuşmazlığa götürür. Satırdaki göz düğmesi testin
  sayfalarını tam ekran açar (üstte testin anahtarı, uyuşmazlıklar `harf/diğer`; altta önceki / sonraki, ←/→, Esc); soldaki
  anahtar görseline tıklamak anahtar sayfalarını aynı görünümde açar.
- **Kaynak PDF (#1198):** bütün sayfalar WebP olarak yazılınca `source.pdf` silinir (sayfa görselleri kalır; eğitim verisi için de
  gerekir). Sayfalar bitmişse (`PageCount` kadar `BookImportPages` satırı) yeniden deneme PDF'i aramaz. Klasör yüklemesinde kaynak
  görseller zaten çevrilince siliniyordu (#1144).
- **Kesik tarama (#1196):** tarama çağrısının çıktı sınırı sayfa başı 400 + 1000 token; uzun içindekiler sayfası (yüzlerce
  satır) bunu aşınca yanıt kesilir (`FinishReason Length`). Kesik tarama aynı sayfalarla bir kez 16 000 token sınırıyla yeniden
  sorulur (iki çağrının token'ı da sayılır); o da kesilirse kurulum hatayla durur. Anahtar okuması zaten 16 000 ile sorulur, yeniden
  denenmez.
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
- **Bölüm adlı testler** (#1163; ör. HiperZeka "MAX Fasiküller": "Kavramları Kavra 01", "Öğrendiğini Kanıtla 02", "Becerini
  Sergile 01"; anahtar da bu adların altında, ünite yok): tarama ve anahtar okuması bölüm adını (`series`) döner. Test, bölüm
  adı + numarayla tanınır ve anahtarın aynı adlı satırıyla eşlenir (ad büyük/küçük harf ve Türkçe harf farkı gözetmeden). Testler
  başladıkları içindekiler konusunun ünitesine girer (içindekiler yoksa başlıktaki konu), ünite içinde sayfa sırasıyla numaralanır,
  başlık "Kavramları Kavra 01" olur. Anahtarda hiç harfli satırı olmayan bölüm (açık uçlu "Şimdi Uygula") test sayılmaz; test
  sayfası olmayan anahtar satırı (tek soruluk "Doğru Yorumla") "eşleşmeyen" listesine düşer.
  Bölüm adıyla eşleme yalnız anahtar da bölüm adlıysa yapılır (#1165; ör. Define soru bankası: başlık "Pusula Kavrama Testi - 1",
  numara ünite içinde türden bağımsız sıralı, anahtar "1. ÜNİTE: Test 1 …"). Değilse normal ünite + test no yolu: başlıkların
  çoğunda ünite yoksa ünite, test numarasının geri düşmesinden çıkar; bölüm adı başlığa eklenir ("Pusula Kavrama Testi: Güneş").
- **Anahtar sayfası ünitenin devamıyla başlarsa** (#1167; Define s.225 "5. ÜNİTE"nin Test 3'üyle başlıyor): okuyucu üstünde
  başlık olmayan satıra ünite 0 verir, kurulum önceki satırın ünitesinden devam ettirir (ardışık anahtar sayfalarında; yeni anahtar
  aralığında 1). Onay ekranında sondaki kutuyu silmek anahtarı kısaltır (fazla okunan soru); aradaki boş kutu okunamadı sayılır.
- **Testler kitap boyunca numaralıysa** (Sosyal Bilgiler Soru Bankası: sayfada "Test 36", anahtarda "5. ÜNİTE → Test No 36"):
  ünitesi tutmayan test, numarası hem testlerde hem sahipsiz anahtar satırlarında tekse o satırı alır, `keyByNumber` işaretlenir.
  Her ünitede "Test 1" olan kitapta bu eşleme yapılmaz. Kurulum taramanın ve anahtar satırlarının kısa dökümünü loglar
  ("survey 10:test u1 t1 …; key rows p223 u5 t36 n=7").
- **Anahtarı gelmeyen test:** onay ekranında "Anahtar gir" beklenen soru sayısı kadar boş kutu açar (bilinmiyorsa 10);
  "+" anahtarın sonuna kutu ekler; bütün kutular silinirse test anahtarsız kalır.
- **"AI ile bul" (isteğe bağlı, kurulum kendiliğinden çağırmaz):** anahtarsız satırda "Anahtar gir"in yanında; üstte "Hepsini
  AI ile bul" anahtarsız testleri sırayla (tek tek, durdurulabilir) sorar. `POST {id}/setup/tests/{order}/ai-key`
  (`BookKeyMatchService`): önce kurulumun hiçbir teste bağlayamadığı anahtar satırlarından (`SetupJson.OrphanRows`, cevaplarıyla
  saklanır) birini ucuz modelle seçer; uymazsa anahtar sayfalarının görsellerinde testin satırını arar (komşu testler bağlam
  olarak verilir). Arama kurulumdaki anahtar okumasının düzeninde: ucuz model iki kez (sayfanın tamamı, üst / alt yarısı), iki
  geçerli okuma aynıysa kabul, değilse güçlü model. Kod denetler: soru sayısı biliniyorsa cevap sayısı tutmalı, yalnız A–E / "?", bir satır iki teste
  gitmez. Öneri kaydedilmez, ekranda "AI" çipiyle doldurulur, onayla gider; bulunamazsa satırda "AI bulamadı" yazar, anahtar elle
  girilebilir. Maliyet `SetupConfirmation` aşamasına ve iş bütçesine yazılır (429 bütçe, 502 Claude hatası).
- **QR'dan anahtar (#1219, isteğe bağlı, kurulum kendiliğinden çağırmaz):** kitapta anahtar sayfası olmayıp her testin ilk
  sayfasında yayıncının çözüm QR'ı olan kitaplar için (ör. Define Fen Bilimleri Soru Bankası, Fi Yayınları). Tarama sırasında her
  sayfanın QR'ı .NET'te okunur (`PageQrReader`, ZXing; yapay zekâ yok, maliyet yok), `SetupJson.QrLinks`'e yazılır; testin bağlantısı
  sayfa aralığındaki ilk QR'dır (onay ekranında satırda açılır). "QR'dan al" / "Hepsini QR'dan al"
  (`POST {id}/setup/qr-keys[?order=]`, `BookQrKeyService`) okumayı **Hangfire işine** verir (ses çevirisi CPU'da test başına
  dakikalar sürebilir; gateway 90 sn / Cloudflare 100 sn'de keser): ilerleme `SetupJson.QrRun`'da (`qrRun` olarak döner), onay
  ekranı okuma sürerken kurulumu 4 sn'de bir yeniden okur, sürerken onay kapalıdır (`bookImport.qrKeysRunning`). 10 dk ilerlemeyen
  okuma yarıda kalmış sayılır, yeniden başlatılabilir. Her test için: bağlantının okuyucusu varsa (`IQrKeyReader`; şimdilik
  `FernusQrKeyReader`: `*.frns.in`, `fideocozum.com`) çözüm sayfası çekilir, sayfadaki `solved_list` soru başına ses kaydı (mp3) ya
  da mp4 verir; cevap harfi verilerde yok, öğretmen kaydın sonunda söyler ("B seçeneğini işaretleyebiliriz") → ses question-detector'ın
  `/transcribe` ucunda (faster-whisper, Türkçe) yazıya çevrilir, son "X seçeneği / X şıkkı / cevap(ımız) X" alınır (`QrAnswerLetter`).
  Bulunamayan harf (YouTube çözüm, harf söylenmemiş) `?` kalır, tahmin yok; onaydan önce elle girilir. Soru sayısı sayfada görülenden
  farklıysa satırda uyarı çıkar. Okunan anahtar test satırına `KeySource=Qr` ile yazılır (yalnız hâlâ
  anahtarsız ve iş onay bekliyorsa; elle girilenin üstüne yazılmaz), ekranda "QR" etiketiyle görünür, öğretmen onaylar. Okuyucusu olmayan bağlantı çekilmez, yalnız
  gösterilir (doğrudan cevap anahtarı gösteren yayıncılar için okuyucu örnek bulundukça eklenir).
  **Güvenlik:** QR içeriği güvenilmez girdi — yalnız okuyucunun alan adlarına gidilir, yönlendirmeler elle izlenip her adım
  denetlenir, bağlantı DNS çözümünden sonra yalnız genel adreslere açılır (`QrSolutionClient`: loopback, özel, link-local, CGNAT,
  ULA yok), sayfa 2 MB, ses 24 MB (question-detector gövde sınırı 25 MiB), istek 30 sn.
  **Ses hizmeti:** `BookImport:TranscriberUrl` (Aspire question-detector'a bağlar; prod `BOOK_IMPORT_TRANSCRIBER_URL`,
  `--profile detector` ile `http://exam-question-detector:8080`; demoda adres `.env.demo`'da hep dolu, servis gerektiğinde
  `deploy/scripts/demo.sh detector up|down` ile açılıp kapatılır; model `.env.demo`'daki `DEMO_WHISPER_DIR` ile harici diskte,
  ilk okumada ~35 sn yüklenir). Boşsa, servis çalışmıyorsa ya da faster-whisper / model yoksa
  (`GET /transcribe/status` → `available: false`, 1 dk önbellek) "QR'dan al" görünmez; kurulumun geri kalanı değişmez. Model
  `WHISPER_MODEL` (varsayılan `large-v3-turbo`, ~1,6 GB; `small` şık harflerinde zayıf), ilk kullanımda iner, prod'da
  `whisper_cache` volume'unda kalır. Sesler sırayla çevrilir (CPU'da soru başına ~13 sn). Claude kullanılmaz, iş bütçesine yazılmaz.
- **Anahtarın kaynağı (#1219):** `BookImportJobTests.KeySource` — `Book` (kurulumun anahtar okuması), `Qr`, `Ai` ("AI ile bul"),
  `Manual` ("Anahtar gir"). Onay isteği satır başına kaynağı taşır; taşımazsa anahtar kurulumdakiyle aynıysa onun kaynağı kalır,
  değiştiyse `Manual`. Onay ekranı `Qr` / `Ai` kaynağını etiketle gösterir. Bu alandan önceki işlerde null (kitaptan sayılır).
- **Kayıt:** test satırları `BookImportJobTests` (sayfa aralığı, dakika, `AnswerKey` "ABCD…", `KeyConflicts` "." uyumlu /
  diğer okumanın harfi / "-"), özet `BookImportJobs.SetupJson`, maliyet `AiCostUsd` (başarısız çağrılar dahil). Model listesi
  ve fiyatlar `Anthropic:Models`'tan; API anahtarı yoksa iş "Claude is not configured" ile düşer. Hiç test başlığı yoksa
  "No tests or activity pages were found in the book". Sistem mesajı / şema / görsel boyutu değişince `ClaudeBookSetupAi.SchemaRevision` artar.
- **Onay ekranı** (takip sayfasında, iş `AwaitingConfirmation` + `SetupConfirmation`): özet, anahtar sayfası görseli, test
  tablosu (aktar / başlık / dakika / sayfalar) ve klavyeyle gezilen anahtar ızgarası (harf → sağa, oklar). Anahtar yoksa
  "cevapsız devam et" ya da "aktarımı iptal et". Uyuşmazlık ve okunamayan hücre kalırken onay kapalı.
- **Onay** (`POST /api/exam/book-imports/{id}/setup/confirm`): hak kontrolü (`CheckAsync`, 429) → kitap kaydı (#577:
  ofset, anahtar aralıkları, içindekiler) → `BookTest` satırları ("1-01 · Başlık") → hak düşer
  (`RecordUsageAsync(userId, "book-import", jobId)`) → iş `Questions` aşamasında kuyruğa girer (#1022 iskelet). Aynı adlı
  kitap varsa 409 + `existingBook`: mevcut kitabı kullan (kayıt kümeleri boşsa kurulumla doldurulur) ya da yeni ad.
  İptal: `POST /api/exam/book-imports/{id}/cancel` (onay beklerken burada; diğer durumlar aşağıda, #1025).

### Varsayılan test süresi (2026-11-20)

Testin başlığında süre yazmıyorsa kurulum süreyi soru sayısından önerir (`BookTestDurations`; kurulum ekranında değiştirilir,
boşaltılırsa test süresiz olur). Etkinlik bölümleri ve soru sayısı bilinmeyen testler süresiz kalır; üst sınır 600 dk.

| Düzey | Ders | Soru başına | Ölçüt |
|---|---|---|---|
| Okul öncesi, 1–4. sınıf | hepsi | 2 dk | merkezi sınav yok; okuma hızı düşük |
| 5. sınıf ve üstü, hazırlık | Matematik, Fen Bilimleri, Fizik, Kimya, Biyoloji, Geometri | 2 dk | LGS sayısal: 40 soru 80 dk |
| 5. sınıf ve üstü, hazırlık | diğerleri (ya da ders bilinmiyor) | 1,5 dk | LGS sözel: 50 soru 75 dk; TYT 120 soru 165 dk (~1,4) |

Bursluluk sınavı (İOKBS) daha sıkı: 5–8. sınıf 80 soru 100 dk (1,25), lise 100 soru 120 dk (1,2); sınav ortamı ölçüsü olduğu
için kitap testlerinde kullanılmadı. Değer yalnız yeni kurulumda yazılır: bu değişiklikten önce kurulumu biten kitapta süre boş
kalır (kurulum ekranında elle girilir ya da kurulum yeniden çalıştırılır).

### Paylaşım ve kitabın erişim listesi (#1259)

- **Onayda seçilir:** `ConfirmBookImportSetupDto.TeacherSharing` (`BookImportJob.TeacherSharing`, varsayılan Private); soru
  aktarımının açtığı taslak testler ve inceleme / toplama testleri bu değeri alır. Öğretmen Özel / Herkese açık (görüntüle,
  ata) / Yalnız okulum (okulu varsa, `CanShareWithSchool`) seçer.
- **Seçili okul ve öğretmenler** (`BookAudience = 4`, yalnız admin, `CanShareWithAudience`): erişim **kitap düzeyinde**
  `BookAudienceMembers` (satır başına okul **ya da** öğretmen; okul, o okulun bugünkü ve sonradan gelen öğretmenlerini kapsar).
  Listedekiler görür ve onaysız atar (PublicAssignable gibi). Onayda `Audience { schoolIds, teacherIds }` (`teacherIds` =
  Teacher kaydı id'si, admin öğretmen listesi kullanıcı id'si göstermez, #262; sunucu kullanıcı id'sine çevirir) listeye
  **eklenir**; liste `GET/PUT labeling/books/{id}/audience` (Admin) ile değiştirilir, en çok 500 satır.
- **Erişim kuralı:** `WorksheetAccess.VisibleToTeacherPredicate` testin kitabının listesinde öğretmeni ya da okulunu arar
  (`BookTestId` dolu testler). Testin görünürlük ucunda `BookAudience` yalnız admin + kitap testi ile seçilir, öğretmen geri seçemez.
- **Onaydan sonra değişir (#1266):** paylaşım bütün aktarıma aittir; inceleme ve onay panelinde bant olarak görünür
  (`app-book-import-sharing-bar`). İş bitene kadar (tamamlandı / iptal değil, kurulum onaylı, demo ve karışık sorular hariç,
  `CanChangeSharing`) `PUT /api/book-imports/{id}/sharing` (`SetBookImportSharingDto`, doğrulama onaydakiyle aynı) değiştirir:
  onaylanmamış taslak testler hemen güncellenir, sonra alınacak testler yeni değeri alır (soru aşaması taslak açarken değeri
  satırdan yeniden okur, çalışan iş eski değeri kullanmaz). Onaylanmış testlere dokunulmaz (testin ayarından değişir). Bitmiş
  işte bant salt okunur.
- **Henüz yok:** var olan kitabın listesini düzenleyen ekran (uç nokta var).

### Sayfa temizliği (#1234)

Bazı kitaplarda her sayfada aynı yerde filigran (ör. açık gri çapraz yayınevi adı) ve renkli kutuyla işaretli doğru şık
vardır; ikisi de kırpmalara girer. Sayfalar aşamasının sonunda, kurulum taramasından önce temizlenir:

- **Hizmet:** question-detector `/clean/*` (`page_clean.py`, harici diskteki `clean_pages.py` betiğinin servis hali). Adres
  `BookImport:PageCleanerUrl`, boşsa `BookImport:TranscriberUrl` (aynı servis); hizmet yoksa (detector kapalı, scipy yok) adım
  atlanır, sayfalar orijinal kalır. Demoda `demo.sh detector up` gerekir.
- **Analiz:** en çok 32 örnek sayfa gönderilir. Filigran: sayfaların piksel medyanında sayfa kenarına değmeyen büyük, açık, düz
  lekeler; çarpma biçimli örtü olarak bölünür (yazının üstündeki filigran da gider). İşaret rengi kendiliğinden ARANMAZ
  (2026-11-23): renkli kutularında şık harfi okunmayan kitapta (5. sınıf Türkçe, 21 renk grubu) her grup denenip analiz 10
  dakikalık istemci sınırını aşıyor, temizlik `Failed` oluyordu. Renk yalnız kurulum ekranında "İşaret rengini seç" ile verilir
  (düğme her zaman görünür); otomatik temizlik yalnız filigranı siler. Analiz bellekte jetonla tutulur (servis yeniden başlarsa
  API yeniden analiz eder).
- **Sayfa başına:** kutu silinir, harf sayfanın yazı renginde yeniden yazılır. Temiz kopya `book-imports/{id}/clean/0005.webp`,
  `BookImportPage.ImageUrl` temiz olanı gösterir, orijinal `OriginalImageUrl`'de; silinen işaretler (harf + kutu) `MarksJson`'da
  (#1235 cevap anahtarı için). Kurulum, soru tespiti, düzeltme ve görüntüleyiciler değişmeden temiz sayfayı okur.
- **Özet:** `BookImportJob.CleanJson` (durum `Done` / `NothingFound` / `Failed` / `Unavailable` / `Running`, filigran, renk, işaret
  sayısı). Hata aktarımı durdurmaz: orijinallere dönülür, `Failed`.
- **Kurulum ekranı:** sonuç, "Orijinal sayfaları kullan" (`PUT setup/clean`; yalnız etkin görsel değişir), "Önce / sonra"
  (görüntüleyicide orijinal / temiz; `GET pages/{n}?original=true`), "Sayfaları temizle / Yeniden temizle" ve "İşaret rengini
  seç" (orijinal sayfada işaretli kutuya tıklanır, o renkle temizlenir; `POST setup/clean {markColour}`, Hangfire). Hesap sabittir:
  aynı sayfa ve aynı renk aynı sonucu verir, yeniden denemenin anlamı farklı renk. Temizlik sürerken onay kapalıdır.
  Temizlik özelliğinden önce aktarılıp onay bekleyen kitap da buradan elle temizlenir.
- **Düzeltmede orijinal (#1260):** "İşaretlemede düzelt" tuvali temiz sayfayı açar; temizlik `Done` ve "Orijinal sayfaları
  kullan" kapalıysa (`BookImportJobDto.HasOriginalPages`) şeritte "Orijinal sayfa" küçük görseli çıkar. Tıklayınca orijinal
  (`pages/{n}?original=true`) tuvalin sağında modal olmayan panelde açılır; işaretli şık orada görülür, kutular temiz sayfada
  düzeltilir. Sayfa değişince panel yeni sayfanın orijinalini yükler.
- **Testin bütün sayfaları (#1263):** düzeltme şeridinde testin sayfaları düğme olarak (işaretliler bayraklı) görünür; "Yalnız
  işaretli sayfalar" yalnız önceki / sonraki ve kaydettikten sonraki geçişi süzer. İncelemede test başına göz düğmesi testin
  sayfalarını açar (uyarılı sayfanın "Sayfayı gör"ü de bütün sayfaları gezer); temizlenmiş kitapta "Orijinali / Temizi göster".

### Basılı işaretlerden cevap anahtarı (#1235)

Temizlikte silinen işaretler (`BookImportPage.MarksJson`: harf + kutu) cevap anahtarı olarak kullanılır (`BookMarkKeys`):

- **Kurulum:** kurulum taramasından sonra (ve onay beklerken yapılan her temizlikten sonra) testin sayfalarındaki işaretler okuma
  sırasıyla (sol sütun yukarıdan aşağı, sonra sağ) anahtar olur. Anahtarsız teste `KeySource = Marks` ile yazılır (ekranda
  "İşaretlerden" etiketi; soru sayısı sayfada görülenden farklıysa uyarı). Kitaptan / QR'dan / AI'dan gelen aynı uzunluktaki
  anahtarla farklı harf `KeyConflicts`'e "diğer okuma" olarak düşer (onaydan önce çözülür). Elle girilen anahtara dokunulmaz;
  yeniden temizlik (başka renk) işaretten gelen anahtarı yeniler.
- **Soru aşaması:** soru kutusunun içinde merkezi kalan işaret o sorunun cevabıdır (anahtardaki sıra kaymasını yakalar); anahtardaki
  harften farklıysa işaret kullanılır ve `markMismatch` inceleme bayrağı düşer. Kutuda işaret yoksa ya da iki farklı harf varsa
  anahtara bakılır. "Orijinal sayfaları kullan" işaretleri silmez: cevaplar yine işaretlerden gelir.

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
  2. Soru olarak yalnız `mcq_question` blokları (etkinlik blokları #1134, aşağıda); okuma sırası (sütun, sonra yukarıdan aşağı), şıksız kök + hemen altındaki şık kutusu birleşir.
  3. `BookQuestionLayout.Prepare`: soru kutusu taşan şıkları içine alır, şıklar Claude'un okuduğu harflere (tam takımsa, #1183)
     ya da (satır, x) sırasına dizilip dolgulanır (25 px),
     "Cevapları Hizala" kurallarıyla hizalanır, örtüşen dolgu tespit kutularının ortasından kesilir. Kurallar ve toleranslar el ile
     aktarım aracının Python portuyla aynı (2363 px genişliğe ölçekli); testler aynı girdilerde Python sonucuyla karşılaştırıldı.
  4. Numaralama sayfaya dayanır: kurulum taramasının okuduğu "sayfadaki en büyük soru no" (`BookImportPages.MaxQuestionNumber`)
     varsa sayfa önceki sayfanın sonundan başlar; eksik tespit sonraki sayfayı kaydırmaz. Cevap anahtardan (`AnswerKey[no-1]`).
  5. Kayıt: `ILabelingBankService.SaveImportedPageAsync` (ayrı DI scope'u) — `pages/bank` ile aynı kırpma, kitap kaynağı
     (`QuestionSources`: etkinlik etiketi = kitap testi adı "1-01 · Başlık", madde = soru no), içindekiler (`ClassificationSource=Book`)
     ve `QuestionCreatedEvent`; sorular taslak, `BankSharing=Private`, cevapsız olabilir. Kırpma hash'i bankada olan soru atlanır ve
     sayılır (`DuplicatesSkipped`). Rol özniteliği, saatlik kota ve sayfa "kaydedildi" kilidi yok.
- **İşaretler** (`BookImportJobTests.ReviewFlagsJson`, sayfa / soru referanslı): `countMismatch` (bulunan ≠ anahtar), `missingNumbers`,
  `extraQuestions`, `optionCount` (≠ 4), `noAnswer`, `notSaved` (ikiden az şık), `detectionFailed`, `saveFailed`,
  `activityUnsupported` / `activityInvalid` (#1134, aşağıda). İşaretli test
  `NeedsReview`, işaretsiz `Done`; her sayfası düşen test `Failed`. Bir sayfa ya da test diğerlerini durdurmaz.
- Takip ekranı test başına bulunan / anahtardaki / kaydedilen / atlanan sayıları ve işaretleri sayfa ve soru numarasıyla gösterir.

## Etkinlik sayfaları (#1134)

Çoktan seçmeli olmayan etkinlikler (eşleştirme, boşluk doldurma, tabloda işaretleme, metinde işaretleme …) taslak etkinlik
olarak aktarılır. Yalnız kitap aktarımında; hızlı ekleme ve karışık görseller etkinlik bloklarını bugünkü gibi atlar.

- **Kurulum:** tarama etkinlik sayfasına `activity` türü verir (`ClaudeBookSetupAi.SchemaRevision` 7; yalnız serbest yazma
  sorusu olan sayfa `other` kalır). `BookSetupPlanner.WithActivitySections`: hiçbir testin sayfa aralığına girmeyen ardışık
  etkinlik sayfaları **etkinlik bölümü** olur (kurulum işareti `activity`, anahtar ve beklenen soru sayısı yok). Dizi,
  içindekiler satırının başladığı sayfada ve `MaxActivitySectionPages` (8) sayfada bir bölünür. Ünite: bölümden önce başlayan
  son testinki (hiç test yoksa içindekiler satırınınki / sırası); numara ünite içinde sayfa sırasıyla; ad "1-E01 · Başlık"
  (başlık içindekilerden, yoksa "Etkinlikler"). Testin aralığındaki etkinlik sayfası (başlıksız kitabın bölümü) o testle aktarılır.
  Etkinlik bölümü "anahtarsız kitap" sayılmaz (`NoAnswerKey` yalnız testlere bakar), "AI ile bul" / "QR'dan al" onu atlar; onay
  ekranında satırda "Etkinlik bölümü" çipi durur, işareti kaldırılırsa aktarılmaz.
- **Soru aktarımı:** her sayfada (test ya da etkinlik bölümü) tespitin `activity` blokları da işlenir. Tipi #579 çıkarımının
  desteklediği blok (`matrixChoice`, `slotFill`, `dragPlace`, `match`, `textSelect`) için
  `IActivityStructureService.ExtractForImportAsync` çağrılır: işaretleme ekranıyla aynı talimat, şema ve önbellek
  (`LabelingActivityResults`), kullanıcı kotası yok. Model `BookImport:ActivityModel` (boşsa tespit modeli). Her çağrıdan önce
  aktarım bütçesi ve iş tavanı denetlenir, ödenen tutar testin maliyet satırlarına yazılır; hız sınırında iş bekler ve kaldığı
  testten sürer (çıkan bloklar önbellekte, ücretsiz).
- **Kayıt:** taslağı geçerli (`ValidateDefinition`) blok, sayfanın sorularıyla okuma sırasında tek kayıtta
  (`LabelingImportPageRequest.Activities`) şıksız ve **çözümsüz** taslak etkinlik olur: görsel blok kırpması, plan çıkarımın
  planı, madde etiketi "E1", "E2" … (test boyunca). Etkinlik bölümünün taslak sayfası `WorksheetKind.Activity` (puansız).
  **Testte numara tutan etkinlik (#1293):** test bölümünde çoktan seçmeli soru sayısı anahtardan azsa (ya da anahtar yoksa)
  etkinlik blokları basılı bir soru sanılmıştır (ör. A–D şıklı tablo): sayfanın sorularıyla okuma sırasına dizilip bir numara
  tutar, madde etiketi o numaradır. Sonraki sorular basılı numaralarını ve anahtar cevaplarını korur (önceden numara
  `ilk + i` ile etkinliği atlıyor, sonraki soruların cevabı bir kayıyordu). Sayı tutuyorsa etkinlikler "E1" … kalır.
- **İşaretler:** `activityUnsupported` (tip belirlenemedi ya da çıkarımı yok: çizim, açık uçlu …) ve `activityInvalid`
  (çıkarım başarısız ya da taslak geçersiz, ör. boşluksuz `slotFill`). Bu bloklar aktarılmaz, düz görsel soru da olmaz;
  sayfa uyarı listesinde görünür, test `NeedsReview` olur (toplu onay atlar). Etkinlik bölümünde sayfadaki basılı numaralar
  etkinliklerin numarasıdır: `missingNumbers` / `extraQuestions` üretilmez. Sayaçlar etkinlikleri de sayar; anahtarla sayı
  karşılaştırması yalnız çoktan seçmeli sorularla yapılır.
- **İnceleme:** önizlemede etkinlik taslağı tipinin çipiyle görünür, cevap düğmesi yoktur; "Cevaplanan x/y" ve onay engeli
  yalnız çoktan seçmeli soruları sayar (karar: etkinlik cevapsız onaylanır, cevabı sonradan etkinlik düzenleyicide elle
  girilir; otomatik çözüm #580 beklemede). Hatalı taslak "testten çıkar" ile atılır. "İşaretlemede düzelt" sayfanın çoktan
  seçmeli taslaklarını değiştirir; tuvalde kalan etkinlik kutuları da gönderilir (`activities`, #1293): sayfanın bunlarla
  örtüşmeyen etkinlik taslağı (tuvalde silinen ya da türü Soru yapılan) silinir, onun kutusuna çizilen soru etkinliğin
  numarasını ve anahtar cevabını alır; kalan numaralı etkinlikler sorularla numara sırasına dizilir. Elle doğrulanan sonuca
  da yalnız kalan etkinlikler yazılır (tuval yeniden açılınca silinen etkinlik geri gelmez). Şık çizilmiş etkinlik bölgesi
  kaydedilmez; tuval uyarır ve "Soruya çevir" önerir. `activities` gönderilmezse (hızlı ekleme) etkinliklere dokunulmaz.
  Sayfanın etkinlik uyarıları düzeltmeyle kapanır.
- **Temizlik hizmeti yanıt vermezse:** `DetectorPageCleaner.IsAvailableAsync` dayanıklılık işleyicisinin zaman aşımını da
  "hizmet yok" sayar (önceden iş "The operation didn't complete within the allowed timeout" ile düşüyordu; yerelde
  question-detector kapalıyken görüldü).
- **Atlanan / yanlış okunan işaretler (2026-11-20, demo, 2363 px genişlikte sayfalar):** `page_clean.colour_boxes` kutunun
  doluluğunu harfin bıraktığı boşlukla birlikte ölçüyordu; kalın harfli "A)" / "B)" kutuları yarı boş sayılıp atlanıyordu.
  Kapama artık çözünürlükle ölçeklenir, doluluk delikleri doldurulmuş lekede ölçülür. `read_box` kutunun koyu kenar çizgisini
  mürekkep saymaz ve "B" / "E" ayrımını harfin sağ kenarındaki karınlara bakarak yapar (`_has_bowls`; şablon eşleme "B)"yi
  "E)" okuyordu). 80 sayfalık kitapta işaret 259 → 383, harf dağılımı A 61 / C 141 / D 35 / E 22'den A 97 / B 96 / C 93 / D 97'ye;
  kalan pembe lekeler resimlerin içindeki kırmızılar. Eski sürümle temizlenmiş kitap aynı renkle yeniden temizlenir (anahtar da
  yenilenir).
- **"Temizlik yarıda kaldı" (2026-11-20, demo):** `page-cleaner` ve `transcriber` istemcilerinde standart dayanıklılık işleyicisi
  kaldırıldı (`RemoveAllResilienceHandlers`): analiz 10 sn'lik deneme sınırını aşınca istek iptal edilip yeniden gönderiliyor,
  Hangfire işi `TimeoutRejectedException` ile düşüyor, özet `Running`de kalıp 10 dk sonra "yarıda kaldı" görünüyordu. Tek sınır
  `client.Timeout` (10 dk / 2 dk). Temizlikte iptal dışındaki her hata artık `Failed` + hata metni olur.
- **Örnek kitapla deneme (2026-11-19, "5. Sınıf Matematik Altın Etkinlikli SB", öğretmen baskısı, s. 6–25, Haiku):** tarama
  etkinlik sayfalarını ayırdı (3 etkinlik bölümü + 2 test); tablo doldurma bloğu geçerli `slotFill` taslağı verdi; çizim
  etkinlikleri (bileşen F) `activityUnsupported`, cevabı basılı (boşluğu dolu) bloklar `activityInvalid` oldu. Öğretmen
  baskısında önce "Sayfaları temizle" (#1234) gerekir. Maliyet sayfa başına tespit ~$0,001 + blok başına ~$0,0005. Aynı 20
  sayfa yerelde arayüzden uçtan uca aktarıldı (yükleme → kurulum onayı → inceleme): toplam $0,02, 2 `slotFill` taslağı.

## İnceleme ve onay (#1023)

- **Taslak test:** soru aktarımı her test için kaydedilen soruları sırayla bağlayan taslak bir test (`Worksheet.IsDraft`) açar
  (ad "kitap – test", süre kurulumdan, öğretmen paylaşımı onayda seçilen (#1259), sahibi işin sahibi); `BookImportJobTest.WorksheetId`.
  Soru id'leri bankaya kayıttan kırpma hash'iyle döner (`LabelingImportPageResult.QuestionIds`).
- **Uç noktalar** (sahibi / Admin, iş `ReadyForReview`): `GET {id}/tests/{testId}/questions` (numara, sayfa, görsel, cevap),
  `PUT …/questions/{questionId}/answer`, `POST {id}/tests/{testId}/approve` (cevapsız soru → 409 `bookImport.answerless`),
  `POST {id}/approve` (onaylanabilenler; atlananlar `answerless` / `failed`), `DELETE {id}/tests/{testId}` (yalnız o testin taslak
  testi ve taslak soruları soft-delete). Onaylanacak test kalmayınca iş `Completed` / `Approval`.
- **Soruyu testten çıkar** (#1169): önizlemede soru başına iki tıkla; `DELETE {id}/tests/{testId}/questions/{questionId}` taslak
  soruyu ve bağını soft-delete eder, kalanların sırası değişmez. Yalnız inceleme sürerken ve onaylanmamış testte; testin son
  sorusu çıkarılamaz (409 `bookImport.lastQuestion`, testi silin). Cevapsız soru çıkınca test onaylanabilir.
- **Onay** `IsDraft`'ı kaldırır; öğretmen paylaşımı onayda seçilen (#1259), soru havuzu paylaşımı olduğu gibi kalır. Sınıflandırma taslakta da
  yazar (`WithDrafts`), onaydan sonra gelen sonuç da uygulanır.
- **Sızıntı:** taslaklar adlı global filtreyle her sorgudan gizlidir. `IgnoreQueryFilters()` kullanan okumalar yalnız oturum /
  cevap geçmişinden okur (taslak atanamaz, başlatılamaz); ham SQL soru / test tablolarına dokunmaz. Alan başına test: havuz
  araması ve paylaşılan havuz (`QuestionBankSearchTests`), test listeleri (`ExamServiceWorksheetListTests`), atama
  (`WorksheetAssignmentServiceSchoolOnlyTests`), çözme (`TestSessionServiceActivityKindTests`), sayılar
  (`TeacherServiceDashboardSummaryTests`), veli (`ParentAssignmentServiceTests`). Liderlik tablosu yalnız öğrenci puanından okur.
- **Ekran:** takip sayfasında "İnceleme ve onay" (`book-import-review`): "Yalnız bakılması gerekenler" varsayılan açık, satırda
  işaretler, Önizle (soru görselleri + A–D seçici, "Cevaplanan x/y"), Onayla, Sil (ikinci tıkla); toplu onay ("Sorunsuz testleri onayla") onay ister,
  işaretli (`NeedsReview`), cevapsız sorulu ve hatalı testleri atlar (`flagged` / `answerless` / `failed`) ve atlananları bildirir;
  işaretli test **onaylanmaz** (#1269, sunucu `bookImport.testFlagged` 409): işaretlemede düzeltilir, sayfa kaydı işaretleri
  yeniden hesaplar, işaret kalmayınca test `Done` olur ve Onayla açılır. İncelemede cevap seçmek "cevapsız" işaretini (cevapsız
  soru kalmayınca), soru çıkarmak o sorunun cevapsız / şık sayısı işaretini ve soru sayısı anahtarla tutarsa sayı uyuşmazlığını
  kaldırır; incelemede yeni işaret eklenmez. Düzeltilemeyen test silinir. **Elle düzeltildi (#1270):** ilk düzeltme
  (işaretlemede sayfa kaydı ya da incelemede işaret kaldıran düzeltme) `BookImportJobTests.FixedAt`'e yazılır, yeniden almada
  silinir; ızgarada hücrenin sağ üst köşesinde nokta, lejantta sayı, satırda "Elle düzeltildi" etiketi.
  Önizleme taslağı genel test ekranına açmaz (aktarım uç noktasından okur).
- **Sayfayı gör (#1190):** Uyarılar açılınca sayfa başlıklı her satırda "Sayfayı gör" işin sayfa görselini
  (`GET {id}/pages/{filePage}`) tam ekran açar: o sayfanın uyarıları üstte, oklarla uyarılı sayfalar arasında gezilir, oradan
  "İşaretlemede düzelt". Hızlı eklemede (sayfa görseli yok, `BookImportReviewApi.pageImage` tanımsız) düğme görünmez.
- **Kitabı gez (#1229):** kurulum ve inceleme ekranının başındaki düğme aynı tam ekran görünümü kitabın 1. sayfasından açar;
  oklar / ←→ bütün sayfaları (1…`pageCount`) gezer, alttaki numara alanı sayfaya atlar (aralık dışı yok sayılır). Başlıkta sayfayı
  kapsayan testler (kurulumda satırlardaki güncel aralıklarla), incelemede bütün testlerin o sayfadaki uyarıları ve ilk testine
  "İşaretlemede düzelt". Yalnız arayüz; sayfa görseli yine `GET {id}/pages/{filePage}`. Hızlı ekleme / karışık görsellerde yok.
- **Sonraya bırak (#1231):** inceleme başındaki "Kalanları sonraya bırak (n)" (toplu onay sonucunda da) `POST {id}/defer` ile
  `BookImportJob.DeferredAt`'i yazar; durum `ReadyForReview` kalır, inceleme uçları aynen çalışır, taslaklar gizli kalır.
  İncelemesi süren bırakılmış iş `GET book-imports` listesinden ve `counts`'tan çıkar (`counts.deferred` ayrı), "Sonra
  bakılacaklar" sayfası (`/book-imports/later`, `GET deferred`) kitapları ve onaylanmamış testlerini gösterir; teste tıklayınca
  `/book-imports/{id}?test=` incelemeyi o test görünür, uyarıları açık ve satırına kaydırılmış açar. `DELETE {id}/defer`
  ("Listeye geri al") geri alır. Son test onaylanınca / silinince iş `Completed` olur ve listeye tamamlanmış olarak döner.
- **İşaretlemede düzelt** (karar: seçenek 1 — sayfanın taslaklarını değiştir): satırdaki menü testin sayfalarını listeler
  (inceleme işaretinde geçenler bayraklı), önizlemede her sorunun yanında da kısayol var. `/labeling?bookImport=&test=&page=`
  klasör yerine işin sayfa görselini açar (kitap kilitli, sayaçlar gizli); tespit her zamanki kaynaklardan (aktarımın Claude
  sonucu önbellekte, maliyetsiz). "Taslakları değiştir" → `PUT {id}/tests/{testId}/pages/{filePage}/questions`
  (`BookImportPageFixService`): görsel sunucuda işin sayfasından okunur; yeni sorular taslak kaydedilir (eski taslakların
  kırpmaları tekrar sayılmaz, `ReplacingQuestionIds`), sonra tek transaction'da eskiler soft-delete, yeniler eskilerin
  sırasına (eskisi yoksa önceki sayfaların sorularından sonra) bağlanır. Numara (#1171): madde etiketi sayıysa o; yoksa kutunun
  örtüştüğü eski taslağın numarası (kesişim / küçük kutu ≥ 0.6; kutu `QuestionSources.Box*`'ta, kutusuz eski taslaklarda
  önbellekteki Claude tespitinden aktarımla aynı sıralama ve numarayla); o da yoksa okuma sırasında önceki kutudan sonraki boş
  numara. Böylece ortadan silinen kutu kalanların numarasını ve anahtar cevabını kaydırmaz; yeni sorular numara sırasıyla
  bağlanır. Cevap: işaretlenen şık, yoksa anahtar. Tuval açılırken (#1173) `GET {id}/tests/{testId}/pages/{filePage}/questions` sayfanın taslaklarını
  (numara, kutu, doğru şık) verir; örtüşen soru kutusu Madde'de taslağın numarasını, doğru şıkkı işaretli alır (elle yazılan
  etiket korunur; tespitin işaretli şıkkı, ör. eski elle kayıttan, taslağın cevabına yerini bırakır, #1179). Şık sırası incelemeyle
  aynı (`Order`, sonra `Id`; aktarımın şıklarında `Order` boş olabilir). Böylece kaydedilecek numara ve cevap görünür, incelemede seçilen cevap kaybolmaz. Sayfanın inceleme işaretleri yeniden hesaplanır, sayı
  uyuşmazlığı testin güncel soru sayısıyla; işaret kalmazsa test `Done`. Başarısız testte (taslak testi yok) düzeltme taslak
  testi açar. Boş sayfa kaydı onayla sayfanın taslaklarını siler. Kayıt başarısızsa test değişmez. Kırpması başka bir aktarımda
  ya da bankada etkin olan soru tekrar sayılır ve bağlanmaz (aktarımla aynı kural).
  **Anahtar sayfası (#1176):** kurulum her testin anahtarının okunduğu dosya sayfasını `BookImportJobTests.KeyFilePage`'e yazar
  (ilk okumanınki, yoksa ikincininki). İş detayındaki `keyPages` bu sayfayı, alan boşsa (önceki kurulumlar, demo kopyası)
  kitabın bütün anahtar sayfalarını (`SetupJson.AnswerKeyRanges`) verir; anahtarsız testte boş. Düzeltme şeridinde küçük görsel,
  tıklayınca büyür (birden çok sayfada önceki / sonraki). Hızlı eklemede yok. **#1272:** büyüyen anahtar ekranın altına
  sabitlenir (modal değil, %50–300 yakınlaştırma; işaretleme sayfası alttan boşluk bırakır); açık bırakılırsa
  (`localStorage` `labeling.fix.keyDocked`) sonraki sayfa / testte kendiliğinden açılır.
  **Sıradaki sorunlu yere geçiş (#1273):** "Yalnız işaretli sayfalar" açıkken "Taslakları değiştir"den sonra iş yeniden okunur:
  testin kalan işaretli sayfası (kaydedilenden sonrakiler önce), yoksa sıradaki kontrol gerekli test (sıraya göre, sonra
  baştan; ilk işaretli sayfası, yoksa ilk sayfası) bildirimle açılır; kalmadıysa aktarım sayfasına dönülür
  (`nextFlaggedFixTarget`). Filtre kapalıyken eski davranış: testin sıradaki sayfası, sonra dönüş.

**Sorular alınırken inceleme (#1267):** iş `Questions` aşamasında ve aktarım sürüyorken (`Queued`, `Running`, `AwaitingBudget`,
`AwaitingCostApproval`) bitmiş test (`Done`, `NeedsReview`, `Failed`) beklemeden incelenir: işaretlemede düzelt, cevap seçme,
soru çıkarma, onaylama ve testi çıkarma açık. Bekleyen / alınan testte bunlar kapalı (sunucu `InvalidState`); "hepsini onayla"
bitmemiş testleri sessizce atlar. Aktarım bittiğinde iş `ReadyForReview`'a geçer; en az bir test onaylanmış ve taslağı olan
incelenecek test kalmamışsa doğrudan `Completed` olur. Arayüzde `importing` girişi (detay sayfası: aşama `Questions`, aktarım
durumu, demo değil) bu düğmeleri test bazında açar; yeniden alma düğmeleri iş bitene kadar kapalı kalır.

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
- **Claude hız sınırı / yoğunluk:** SDK 429 / 529 / 5xx / bağlantı hatasını kendi içinde `Anthropic:MaxRetries` kez üstel beklemeyle
  dener; o da tükenirse (`ClaudeTransient.Is`) sayfa "tespit başarısız", kurulum "başarısız" sayılmaz: iş aynı `AwaitingBudget`
  durumuna geçer (`BookImportPauseReason.RateLimit`), 2 + 1 dk sonra kaldığı yerden sürer. Ekran metni ikisini birlikte anlatır
  ("Yapay zekâ bekleniyor").
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

## Akış (#1265)

Aktarım sayfasının altında zaman damgalı akış: sayacı ilerletmeyen işler de görünür (ör. sayfa temizliği analizi tek uzun
istek, dakikalar sürer). Kayıt `BookImportJobEvents` (iş, saat, işin o anki aşaması, olay kodu, değerler JSON); metin kayıtta
yok, arayüz kodu `activity.*` anahtarıyla çevirir. Kodlar `BookImportEvents`'te; olay bağlama eklenir, çağıranın sonraki
`SaveChangesAsync`'i ile yazılır.

- Yazılan olaylar: başladı / bekleme sonrası devam, aşama girişi, sayfalar hazır, temizlik (hizmet kapalı, analiz başı,
  sonuç, bitti, hata), kurulum bitti (test sayısı), kurulum onayı (paylaşım), test başı / sonu (durum, soru sayısı, hata),
  duraklama (bütçe / hız sınırı / maliyet tavanı), tavan yükseltme, yeniden deneme, hata, iptal, sorular bitti (özet).
- `GET /api/book-imports/{id}/events?afterId=&calls=&afterCallId=`: işi görebilen (sahibi, Admin) okur; yalnız yeni satırlar,
  yanıt başına en çok 500. `calls=true` ile `BookImportCosts` satırları (yapay zekâ çağrıları) da gelir; arayüzde aç / kapa,
  tercih tarayıcıda.
- Ekran iş sürerken (`Queued`, `Running`) 5 sn'de bir yoklar, bitince bir kez daha okur. Bu özellikten önceki aktarımların akışı
  boştur (geriye dönük doldurulmaz). Demo yeniden oynatması olay yazmaz.

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
