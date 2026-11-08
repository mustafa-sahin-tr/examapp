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
