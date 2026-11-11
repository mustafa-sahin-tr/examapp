# Diğer özellik notları

Eskiden `.claude/rules/local-dev.md` içindeydi; 2026-11-01'de taşındı. Okul değişimi matrisi: `.claude/rules/school-change.md`.

**Issue #501 — profil sayıları ve veli rozet backfill'i.** Öğrenci profilindeki (`GET api/student/profile`) çözülen / doğru / yanlış
ve tamamlanan test sayıları artık istatistik ekranıyla aynı kuraldan (`GetGroupedStudentStatistics`: tamamlanmış, etkinlik olmayan
oturumlar, açık uçlu hariç), sıra genel liderlik tablosundan (#193) gelir; diğer uçların döndüğü `StudentProfileDto`'da bu alanlar 0.
Kimsenin yazmadığı `StudentPointHistories` / `StudentBadges` / `Leaderboards` / `StudentRewards` okunmuyor (tabloların kaldırılması ayrı
karar); profildeki "Kazanılan ödül" satırı ve DTO'daki `Badges` / `TotalRewards` kalktı. #422'den önce kazanılmış rozetleri veli paneline
taşımak için BadgeService dizininde bir kez: `dotnet run -- backfill-student-badges [--dry-run]` (her `BadgeEarned` için outbox'a
`StudentBadgeEarnedEvent`; exam API consumer'ı (öğrenci, rozet) UNIQUE ile idempotent — ikinci çalıştırma çift kayıt üretmez; Production'da
`backfill-student-points` gibi `--allow-production --confirm`). Migration yok.

**Çift rozet kaydı (2026-11-06).** Aynı (öğrenci, rozet) için iki `BadgeEarned` satırı rozet raporunu (`GetBadgeProgressAsync`,
`ToDictionary`) 500'e düşürüyordu. Olası kaynak: consumer retry'ı aynı DbContext'le çalışır, düşen denemenin eklediği satır change
tracker'da kalır ve ikinci denemede bir kopyası daha eklenir. Düzeltme: `BadgeEvaluator` `BadgeEarned.Local`'i de kazanılmış sayar;
`BadgeEarned (UserId, BadgeDefinitionId)` UNIQUE (`UniqueBadgeEarnedPerUser` migration'ı önce kopyaları siler, en eski kazanım kalır);
rapor kopyaya dayanıklı (ilk tarih). Bildirim (UserId, kaynak rozet) ve exam API projeksiyonu zaten UNIQUE'ti; kopya oralara geçmedi.
Mevcut kurulumda BadgeService'i yeniden başlatmak yeter (migration açılışta uygulanır).

**Issue #539 — öğretmen transferinde "Okulum" sınavları özel olur.** `PUT api/admin/teachers/{id}/school` okul gerçekten
değişince (aynı okul / yalnız bekleyen talebin kapanması değil) öğretmenin `TeacherSharing=SchoolOnly` sınavları (silinmişler dahil)
okul güncellemesiyle **aynı transaction'da** `Private` olur; sayısı yanıtta `privatizedWorksheetCount` ve `AdminUserActionLogs.AffectedCount`'ta.
Herhangi bir adım başarısızsa ne okul ne görünürlük değişir. Öğrenci erişimi `TeacherSharing`'e bakmaz: eski okula yapılmış atamalar
çözülmeye devam eder. Admin dialog'u onaydan önce sayıyı `GET api/admin/teachers/{id}/school-change-impact` (`schoolOnlyWorksheetCount`,
silinmemişler) ile gösterir. Yeniden paylaşım öğretmenin elinde (elle tekrar "Okulum"). Migration `AddAdminUserActionAffectedCount` exam API
açılışında uygulanır (Aspire'da exam API'yi yeniden başlat).

**Issue #701 — testi yazdırma.** Öğretmen Test detayındaki "Yazdır" ile `/exam/:id/print` sayfasına gider (uygulama kabuğu dışında). PDF
tarayıcının yazdırma penceresinden kaydedilir; sunucuda PDF üretimi ve yeni bağımlılık yok. Cevap anahtarı isteğe bağlı, ayrı sayfada.
`GET api/questions/bytest/{id}` soru başına `printBlock` döner (`QuestionPrintPolicy`): yayından kaldırılan, lisansı biten ve yayıncı (`Publisher`)
soruları çıktıya alınmaz; etkileşimli sorular (`mcq` dışı) da çıkarılır ve hepsi sayfada testteki sıra numarasıyla raporlanır. Yayıncı sözleşmelerinde baskı izni
alanı yok; ilk sözleşme gelince alan eklenip kural gevşetilir. Kaynağı bilinmeyen (`Unknown`) soru engellenmez. Şıkları görselin içinde olan
işaretlenmiş soruda tam görsel basılır ve anahtardaki harf şık etiketinden gelir. Migration yok; yeni alan için exam API yeniden başlatılır.
#895: sağdaki seçenek paneli tek / iki sütun (soru sütunda bölünmez), cevap anahtarı, ad-soyad satırı ve "Kendi numaramızı ekle"; yalnız görselden oluşan (kitap) sorularda numara görselin içinde olduğu için, testin bütün basılan soruları böyleyse bizim numaramız varsayılan kapalı. Başlık testin adı, altında ders · sınıf · basılan soru sayısı; açıklama addan farklıysa basılır. Anahtar testteki basılma sırasıyla numaralanır.

**Issue #692 — MEB kazanımları.** `LearningOutcomes` artık müfredat (`Previous`, `Maarif`), ders ve kaynak belge taşır; (müfredat, kod) benzersizdir, iki müfredat
birlikte yaşar. Soru bağı `QuestionLearningOutcomes` (çoka çok); kazanımı olmayan soru her yerde çalışır. İçe aktarım `POST api/admin/learning-outcomes/import`
(yalnız admin): `{ curriculumVersion, sourceReference, items: [{ code, description, gradeName, subjectName }] }`; sınıf ve ders adla eşleşir, tek hatalı satırda hiçbir şey
yazılmaz (400 + satır numaralı hatalar), yeniden çalıştırmak kopya üretmez. Liste: `GET api/learning-outcomes?gradeId&subjectId[&curriculumVersion&search]` (en çok 200).
Bağlama: `GET/PUT api/questions/{id}/learning-outcomes` (yazma yalnız soru sahibi/admin). Test kopyası aynı soru satırını kullandığı için bağlar korunur; soru aktarımı (paket)
 bağları (müfredat, kod) ile taşır, hedefte olmayan kazanım atlanır. Migration `AddLearningOutcomeVersionAndQuestionLinks` mevcut satırların sınıf ve dersini alt konudan
doldurur, yinelenen kod varsa durur; exam API yeniden başlatılır.

Öğretmen kazanımı soru tuvali önizlemesinde ve etkinlik editöründe seçer (`learningOutcomeIds`, toplu kayıtla aynı işlemde yazılır; bilinmeyen kimlikte depoya bir şey yüklenmeden kayıt reddedilir); kayıtlı sorunun
kazanımları Soru Havuzu kartındaki "Kazanımlar" ile ve etkinlik editöründe değişir. Kazanım seçmek zorunlu değildir; liste yüklenmemişse seçici bunu söyler, kayıt engellenmez.

**Katalog aktarımı (sınıf, ders, konu, alt konu, kazanım).** Bu dört tablo kalıcı `Code` taşır (ör. `5-sinif.matematik.dogal-sayilar`): kayıt oluşurken addan üretilir, ad değişse de korunur
(`CatalogCode`; migration `AddCatalogCodes` mevcut satırlara aynı kuralla kod verir, `CatalogCode.Slug` ile migration'daki SQL aynı kalmalı). Gateway yolları: `GET /api/exam/admin/catalog/export` dosyayı indirir,
`POST /api/exam/admin/catalog/import[?dryRun=true]` yükler (yalnız admin). Dosyada veritabanı kimliği yoktur. Eşleşme koda göredir: yoksa oluşturur, varsa adı ve üst kaydı günceller; dosyada olmayana dokunmaz, hiçbir şey silmez.
Kodu olmayan eski kayıt aynı adla eşleşip kodu alır. Aynı ad başka kodla varsa hata verir ve hiçbir şey yazılmaz. Kazanım (müfredat, kod) ile eşleşir; dosyada alt konu yoksa mevcut bağ korunur. Taksonomi değişirse sınıflandırıcı önbelleği yenilenir.
Yeni ortama taşırken önce `dryRun` ile sayılara bak. Yukarıdaki kazanım içe aktarımının gateway yolu da `/api/exam/admin/learning-outcomes/import`'tur.
Temiz veritabanı boş değildir: `SeedDataMigration` katalog tohumlar; testte bu adlarla çakışmayan ad kullan.

**Soru aktarımında hash eşleşmesi.** `Question.ContentHash` kimliktir: görselli soruda görselin SHA-256'sı (kitap sorusunda `CropSha256` ile aynı değer), görselsiz soruda `ContentFingerprint`. Parmak izi metin, şıklar, doğru cevap, plan ve çözümden hesaplanır
(`QuestionContentHash`); `dragPlace` ve `match` kayıtta karıştırıldığı için bu tiplerde id yerine öğenin içeriğine bakar. Görseli ve içeriği olmayan, görseli depoda bulunmayan ya da görselsiz olup şık görseli veya metinsiz paragrafı olan soru kimlik almaz, eşleşmez. Görselsiz paragraflı sorunun parmak izine paragrafın başlığı ve metni de girer: "Buna göre hangisi doğrudur?" farklı soruları birleştirmesin. Paragraf düzenlenince hash sıfırlanmaz. Her ortamın kalıcı kimliği (`QuestionTransferOrigins`) pakete `originKey` olarak yazılır ve içe aktarım haritası bununla tutulur: aynı ortamdan ikinci kez gelen soru, kimliği olmasa da ya da kaynakta düzenlenmiş olsa da kopya olmaz, "daha önce alındı" sayılır. Görseli zipte olmayan paket sorusu hata sayılır, alınmaz. İçe aktarım: hash ve parmak izi aynıysa kopya oluşmaz, hedefte yalnız boş ders / konu ve kaynağı `Unknown` sorunun lisans bilgisi doldurulur, eksik alt konu ve kazanım bağları eklenir. Tek istisna yayından kaldırmadır: kaynakta kaldırılmış soru hedefte de kaldırılır, önizleme sayısını gösterir, iş sonucu soruları listeler, `QuestionSourceActionLogs`'a kayıt düşer; ters yön yoktur, paket kaldırılmış soruyu yayına almaz;
hash aynı parmak izi farklıysa (aynı görsel başka cevapla) çakışmadır, soru alınmaz ve hedef değişmez; hash yoksa yeni soru yazılır ve kaynağın hash'i saklanır. Paket `version` 3: taksonomi önce kodla, eski pakette adla eşleşir; kaynak, lisans, yayın durumu, puan ve kitap adı da taşınır. Etkinliğin kendisi ve kitap bağı (`QuestionSource`) henüz taşınmaz.
Soru veya şık içeriği değişince `AppDbContext` hash'i sıfırlar; `QuestionContentHashService` (saat başı Hangfire, her içe aktarım başında) eksikleri MinIO'dan doldurur. Depoda olmayan görsel kalıcıdır, yeniden denenmez; depo yanıt vermediyse soru yazılmaz, sonraki çalışmada denenir, art arda 20 kez olursa iş durur. Hedefte hash'i tamamlanamayan soru varsa içe aktarım başarısız olur, kopya yazmaz. Şıkları soruyu izlemeden değiştiren yeni bir yazma yolu hash'i kendisi sıfırlamalıdır.
`POST api/question-transfer/exports` kapsam alır (`gradeId`, `subjectId`, `topicId`, `bookId`); kapsam boşsa iş başlamaz, boş kimlik listesi "tüm sorular" demek olduğu için. Paket kaynak adının bütün dosyalarını birleştirdiği için kaynak adı verilmezse sunucu her iş için yenisini üretir (`transfer-<zaman>-<rastgele>`), ekran böyle kullanır. Verilen kaynak adında kapsam dışı soru varsa iş başlamaz. İndirilen pakette kaynak, lisans ve yayın durumu o anki değerlerle yazılır. Paketteki görsel 50 MB, manifest 256 MB, yüklenen paket 200 MB ile sınırlıdır. MinIO'daki `question-transfer/exports/<ad>/` dosyaları kendiliğinden silinmez. Önizleme yeni, eşleşen, çakışan, taksonomisi eksik ve görseli eksik soruları sayar; iş sonucu `result` alanındadır. Migration `AddQuestionContentHash`; exam API yeniden başlatılır.

**Test ve etkinlik aktarımı.** `POST api/question-transfer/exports` `worksheetIds` alırsa seçilen kayıtların maddelerindeki sorular pakete girer, kayıtlar `worksheets.zip` dosyasına yazılır, indirilen pakette manifestin `worksheets` alanına birleşir (manifest `version` 4). Çıkarılmış madde taşınmaz. Seçim listesi `GET api/question-transfer/worksheets` (admin). İçe aktarımda önce sorular işlenir, sonra `QuestionTransferWorksheetImporter` her maddeyi hedefteki soru kimliğine bağlar: soru hedefte varsa onun kimliği kullanılır, kopya açılmaz. Kurallar: bağlanamayan madde (çakışan, alınamayan, yayından kalkmış, lisansı bitmiş soru) varsa kayıt hiç oluşmaz, nedeni `blockedSamples`'a yazılır, sorular yine alınır; sınıf hedefte yoksa kayıt alınmaz, önce katalog taşınır; ders / konu / alt konu bulunamazsa boş kalır ve `missingTaxonomy` sayılır; açık uçlu soru içeren kayıt test olarak alınmaz (etkinlik olarak alınır). Yeni kayıt kapalı gelir (`Private` + `Restricted`), sahibi içe alan admindir. Aynı ortamdan ikinci kez gelen kayıt `WorksheetTransferImportMaps` sayesinde güncellenmez, "daha önce alındı" sayılır: hedefte oturum başlamış olabilir. Hedefte aynı soruya düşen iki madde tek madde olur, uyarı yazılır. Taşınmaz: atama, oturum, sonuç, yorum, erişim izni, hatırlatma, kitap-test bağı, şablon. Bilinen sınır: A'dan B'ye giden kayıt B'den A'ya dönerse A'da kopya oluşur, harita ortam kimliğiyle tutulur. Önizleme aynı kuralları yazmadan uygular; hedefte hash'i eksik soru varken (`targetUnhashedCount`) çakışma sayıları eksik görünebilir. Migration `AddWorksheetTransferImportMap`.
Test ve etkinlik seçilen dışa aktarımda kaynak adı her zaman üretilir: seçim kaynakta tek dosyadır, aynı adla ikinci aktarım öncekini ezerdi. Paket sınırları: 2000 kayıt, kayıt başına 1000 madde, metin alanı 4000, anahtar 200 karakter. Süre 0-24 saate çekilir, etkinlikte süre ve çalışma testi sıfırlanır. Kapak `worksheets` bucket'ına yazılır. Tek kaydın hatası (depo dahil) yalnız o kaydı durdurur; ayrıntı yanıta değil günlüğe yazılır. Hedefte silinen kayıt yeniden gelmez, "daha önce alındı" sayılır: harita satırı kalır, silme kararına saygı duyulur. Gateway paket yükleme yolunda gövde sınırını 200 MB'a çıkarır (`UseQuestionTransferUploadLimit`), diğer yollar 30 MB kalır; `/api/exam/question-transfer` gateway'de de yalnız admin. Önünde nginx olan bir kurulumda `client_max_body_size` ayrıca ayarlanmalıdır.
Parmak izi biçimi değişirse (`QuestionContentHash.FormatVersion`) kayıtlı değerler kendiliğinden yenilenmez: aynı soru kendisiyle "çakışıyor" görünür. O ortamda `Questions.ContentHash` ve `ContentFingerprint` boşaltılır, `QuestionContentHashService` yeniden doldurur; soru içeriğine dokunulmaz. Geliştirme sırasında biçim değiştiği için #755 öncesi commit'leri çalıştırmış yerel veritabanlarında bu bir kez yapılmalıdır.

**Issue #819 — aktarılan kaydın paylaşımı.** İçe almada (`POST api/question-transfer/imports`, form alanları `teacherSharing`,
`studentVisibility`) test ve etkinliklerin paylaşımı seçilir: Özel / Herkese açık (görüntüle) / Herkese açık (atayabilir) ve Kısıtlı / Normal;
varsayılan bugünkü (Özel + Kısıtlı). Okul paylaşımı seçilemez (sahip içe alan admin, okulu yok → 400 `questionTransfer.sharingInvalid`).
Seçim iş kaydının `RequestJson`'unda (`QuestionImportWorksheetOptions`) taşınır; eski işlerde yoksa varsayılan. Daha önce alınmış kayıt
değişmez. Admin sonradan test detayında (`/test/:id`) **Paylaşım (admin)** kartından değiştirir: detay ucu (`GET api/exam/worksheet/{id}/detail`)
admine de açık (öğrenci olmayan admin öğretmen verisini admin olarak görür); öğretmen rolü olmayan admin öğrenci akışı yerine yalnız özet görür.

**Issue #819 — havuzda etkinlik maddeleri.** Havuz araması (`GET api/exam/questions/bank`) `kind=question|activity` filtresi alır:
etkinlik maddesi = `InteractionType` dolu ve `mcq` değil (`QuestionService.IsInteractive` ayrımı); boş ya da `mcq` klasik sorudur. Satırda
`interactionType` döner, kartta **Etkinlik** etiketi çıkar. "Doğru cevabı var" (`hasCorrectAnswer`, filtre ve kart) etkinlikte doğru şıkka değil
kayıtlı `InteractionSolution`'a bakar.

**MEB Maarif kataloğu.** `deploy/data/meb/maarif-catalog.json` 175 programdan üretilir (`deploy/scripts/meb-programs/`, kurallar README'de), API açılışta kendiliğinden yükler (`CatalogPackageImporter`): csproj dosyayı çıktıya `Data/Packages/` altına bağlar, açılış SHA-256'yı `AppliedDataPackages` satırıyla karşılaştırır ve yalnız paket değiştiyse Soru Aktarımı ekranının kullandığı içe aktarmayı çalıştırır. Kurallar aynı: koda göre ekler / günceller, silmez; tek hatada hiçbir şey yazılmaz, iz de kaydedilmez, sonraki açılış yeniden dener; hata açılışı durdurmaz, log'a düşer. Kapatmak: `CatalogPackage:AutoImport=false` (entegrasyon testleri kapatır), başka dosya: `CatalogPackage:Path`. Demo kopyasında (477 eski konu) çakışmasız, açılışta birkaç saniye. Elle yükleme ekranı yine çalışır. Dışarıda kalanlar `maarif-catalog-report.json`'dadır. Tamamı yüklenirse sınıflandırıcıya giden konu listesi sınıf başına yaklaşık 2,5-3 katına çıkar. `MaarifCatalogFileTests` paketi gerçek katalog servisinden geçirir: paket yeniden üretilince test de koşar.

**Kazanımdan görselsiz soru üretimi.** `/question-generation` (Admin, QuestionTrainer): sınıf, ders ve kazanım seçilir, Claude çoktan seçmeli taslak yazar (`POST api/question-generation/drafts`, en çok 5), inceleyen düzenleyip seçtiklerini kaydeder (`POST api/question-generation/save`). Taslak kaydedilmez. Üretim işaretlemenin `Anthropic:*` ayarlarını, saatlik kotasını ve günlük bütçesini kullanır; anahtar yoksa 503. Şık sayısı sınıfa göre: 8. sınıfa kadar 4, lisede 5; şıklar sunucuda karıştırılır. Görsele atıf yapan, şıkkı eksik ya da yinelenen taslak elenir, sayısı gösterilir. Kayıt soruyu (`IsCanvasQuestion=false`, `SourceType=Generated`), şıkları, kazanım bağını ve `QuestionCreatedEvent`'i tek işlemde yazar; sınıflandırıcı konuyu metinden belirler. Metin soru oynatıcıda `question-lite-view` ile gösterilir. Soru teste Soru Havuzu'ndan eklenir.

**Issue #693 — denemeler.** Sınav kuralı veridir (`ExamTemplates`, oturum ve ders satırları). LGS, TYT, AYT resmî 2026 kılavuzlarından tohumlanır (`ExamTemplateSeed`, kaynaklar kayıtta); kılavuz değişince şablon güncellenir. Din muafiyeti seçenekleri (LGS 60 dk, TYT ve AYT ilave felsefe) kapsam dışı. Tohum kimlikleri için sayaçlar 1000'den başlar. Admin şablon tanımlar (`/exam-templates`), deneme kurulmuş şablon değişmez. #899: liste solda, seçilen şablon sağdaki panelde; kilitli şablon salt okunur açılır ve `ExamTemplateDto.MockExamCount` ile kaç denemede kullanıldığını yazar. Düzenlerken oturum toplamı ve kayıtlı toplamla karşılaştırma ("Toplam 156 / 160") gösterilir; toplam değişirse uyarılır ama kayıt engellenmez (şablon henüz kullanılmıyor).
Deneme her oturum için sıradan bir test kaydı açar: süre sınırı, oturum, cevap kaydı ve puanlama testlerle aynı yoldan geçer; normal test ve etkinlikler değişmez. `MockExamQuestions` maddeyi ders satırına bağlar. Kurulum `/mock-exams` (öğretmen, admin): sorular Soru Havuzu'ndan, testlerle aynı bağlama kurallarıyla seçilir; aynı soru iki derste olamaz. Yanlış sayı, yayından kalkmış, lisansı bitmiş, doğru cevapsız, örnek ya da derse bağlı olmayan soru yayını engeller. Oturum kayıtları hep `Restricted` kalır: normal test listesine ve pratik havuzuna girmez. Sınıfa açık deneme (`OpenToGrade`, varsayılan kapalı, bütün okullar) `StartTestAsync` içinde açılır; kapalı deneme oturum başına atanır. Oturum kayıtları testler ekranından değişmez: süre, görünürlük, silme, kopyalama, madde ekleme, çıkarma, sıralama reddedilir, taslak atanamaz; öğretmenin test listesinde görünmez, ayrıntı sayfası yalnız atama için açıktır (`CanEdit=false`). Test ayrıntı sayfası yöneticiye kapalı olduğundan yönetici oturum atayamaz, denemeyi sınıfa açar. Öğrenci `/my-mock-exams` (Testler sekmesi); sonuç ders başına ve toplam doğru, yanlış, boş, net; net = doğru − yanlış / ceza, yanlış = toplam − doğru − boş. Süren oturumun sayıları gösterilmez. Başlanan deneme öğrencide kalır: okul ya da sınıf değişse de sonuç görünür. Puan ve rozet testlerle aynı; mezun düzeyi eklenmedi. Migration `AddMockExams`; exam API yeniden başlatılır.
**Issue #1094 — deneme sıralaması.** `MockExamResultDto.Ranking` (`takers`, `rank`, `topPercent`): denemeyi BÜTÜN oturumlarıyla bitiren öğrenciler arasında toplam nete göre yer (sonuçla aynı kural: oturum satırı, yanlış = toplam − doğru − boş); eşit net aynı sırayı paylaşır (1, 2, 2, 4), ilk yüzde ⌈100 × sıra / katılımcı⌉. Yalnız öğrencinin sonucu tamamsa dolar; `MockExamRankingRules.MinTakers` (5) altında yalnız katılımcı sayısı döner. Sonuç sayfası (`/my-mock-exams/:id/result`) ve hedef kartı (son denemeler; öğrenci ve veli) gösterir; geçmiş listesi (grafik) hesaplamaz. Kapsam bütün okullar; okul / sınıf / il kapsamı sonra.

**Issue #1094 — soru başına istatistik.** `QuestionAnswerStatsReader` exam DB'deki oturum satırlarından hesaplar (event / ayrı tablo yok): yalnız bitmiş oturumun son cevabı, boş cevap sayılmaz (#785). Bir öğrencinin aynı soruyu birden fazla oturumda çözmesinde en erken biten oturum sayılır, `answered` farklı öğrencidir; #609 aynı oturumun satırlarını yerinde yeniden yazdığından o oturumda son çözüm sayılır. Açık uçlu soru ve pratik oturumları dahil değil. Oran ve ortalama süre `QuestionAnswerStatsDto.MinAnswers` (5) cevaptan az ise boş ("az veri"). Görenler: öğretmen / admin soru bankası panelinde (`QuestionBankItemDto.AnswerStats`; aynı panel test düzenleyicinin "Havuzdan soru ekle"sinde ve deneme kurucuda); öğrenci yalnız deneme sonucunda, tamamlanınca: `MockExamResultDto.Questions` her madde için kendi cevabını (`correct` / `wrong` / `blank`, doğru şık yok) ve yalnız bu denemenin oturumlarından başarıyı taşır. Zorluk kalibrasyonu ve katkı kazancı (#1083) sonra.

**Issue #497 — konu performansı.** `GET /api/student/me/topic-performance?days=30&subjectId&groupBy=topic|subtopic|difficulty|interactionType&minQuestions=5` (yalnız öğrenci, kimlik token'dan, dakikada 30, gün en çok 90). `ITopicPerformanceService` yetki denetlemez: veli ve öğretmen uçları kendi kapılarını koyar. Veli ucu (#498) `GET /api/parent/children/{studentId}/topic-performance` aynı parametreleri alır (#767: `minQuestions` en az 5, `TopicPerformanceQuery.ParentMinQuestions`; altı 400 `InvalidMinQuestions`, tek cevabın doğru/yanlışı görünmesin; öğrenci ucu 1'i kabul eder), kapı ve erişim kaydı (`children.topic-performance`) sonra veri; pencere saate kesilir, son cevap veliye en çok bir saat geç yansır. Kaynak bitmiş test oturumları ve pratik cevaplarıdır; süren oturum okunmaz, etkinlik ve açık uçlu soru dışarıda. Doğru, yanlış, boş `WorksheetScoring`'den gelir. Doğruluk ve `minQuestions` cevaplanan sorulara bakar: hiç cevaplanmamış terk edilmiş test konuyu zayıf göstermez. Silinen test ve madde cevabı düşürmez; `ProgressResetAtUtc` öncesi okunmaz. Zorluk bantları `DifficultyBands` (kolay 1-3, orta 4-6, zor 7-10); sınıflandırılmamış soru varsayılan seviye 1 ile kolayda görünür. Migration `AddTopicPerformanceIndexes`.

**Issue #486 — ertelemede veli onayı.** Active velisi olan öğrencide karşı tarafın kabulü dersi taşımaz: `Bookings.RescheduleAcceptedAt` dolar, ders eski saatinde kalır, olay `RescheduleAwaitingParent`, velilere bildirim gider. Veli `GET /api/parent/reschedule-requests` listesinden gördüğü `proposedSlotId` ile `POST .../{id}/approve` yapıp taşır (öneri o arada değiştiyse 409) (`IBookingParentApprovalGate`, tür `ApproveReschedule`, #408 ödeme kancası) ya da `/reject` ile düşürür; ilk veli karar verir, diğer velilere bildirim gider. Velinin notu yoktur. Öneri slotu tutmaz: kabulde ve onayda kilit altında yeniden doğrulanır, arada dolarsa `CanApprove=false` olur ve veli yalnız reddedebilir. Taraflar onay beklerken de geri çekebilir ya da reddedebilir; bağlantı koparsa çıkış yolu budur. Veli kararı kabul anında öğrenci kilidi altında doğrulanır. Active velisi olmayan öğrencide kabul eski davranışla dersi taşır; ama veli onayıyla açılmış dersin (`ParentApprovedAt` dolu) velisi kalmadıysa onaysız taşınmaz, 409 `RescheduleParentRequired` (#441), ders iptal edilebilir. Onay beklerken geri çekilen ya da reddedilen öneri velilere bildirilir (`WasAwaitingParent`). Dersi biten öneri veli listesinden düşer. Karar erişim kaydına yazılır (`children.reschedule-approve`, `children.reschedule-reject`). Issue'daki no-show / ceza ilişkisi #408 ile ele alınır, burada yok.

**Issue #696 — kazanım seviyesi.** `GET api/student/me/learning-outcome-mastery`, `GET api/parent/children/{studentId}/learning-outcome-mastery` (kapı, erişim kaydı `children.learning-outcome-mastery`, veri; saate kesilir), `GET api/teacher/students/{studentId}/learning-outcome-mastery`; hepsi `subjectId` ve `curriculumVersion` süzgeci alır (boş müfredat ikisini de döner). Cevaplar `StudentAnswerReader`'dan gelir, konu performansıyla aynı kural: bitmiş test, pratik; etkinlik, açık uçlu, süren oturum, sıfırlama öncesi yok. Boş cevap seviyeye girmez, aynı sorunun yeniden çözümü yeni cevaptır. Seviye her satırın son `RecentAnswers` cevabından: 0 cevap başlanmadı, `ProficientPct` altı tanışık, üstü yeterli, `MasteredPct` ve en az `MasteredMinAnswers` cevap ustalaştı; `ReliableMinAnswers` altı `isTentative`. Varsayılanlar 20, %70, %90, 10, 5, geriye bakış 365 gün; `LearningOutcomeMastery` bölümünde ayarlı, yanıtta `rules` döner. Kazanımı olan soru her kazanımında sayılır; olmayan alt konularına, o da yoksa konusuna düşer (`kind`), hiçbiri yoksa `unclassifiedCount`. Sınıftaki çözülmemiş kazanım ve kazanımsız alt konu başlanmadı görünür. Öğretmen: kendi okulunun doğrulanmış öğrencisi ya da Approved Booking'i olan öğrenci (okullu öğretmen de), diğeri 404. Kazanım ifadesi yanıtta `statement`: veli yasak alan listesinde `description` var. Ekran: ortak `app-outcome-mastery-card` öğrenci profilinde (tanışık satırda `/practice?subjectId&topicId`), veli panelinde (`[childId]`, 404'te gizli) ve `/teacher/students/:studentId/outcomes` sayfasında (`[teacherStudentId]`, 404'te açıklama); bağlantılar öğretmen panelinin en aktif öğrenciler listesinde ve onaylı randevu satırında. Çalışılmış ders önce açılır, panel içinde zayıf satır üsttedir; iki müfredat varsa seçici çıkar, varsayılan Maarif. #894: ders panelinde ünite (konu) başına tek satır: dört seviyeli yığılmış çubuk ve "n / m başlandı" (başlanmış her satır az veriyse "Az veri"); çalışılmış ünite önce, konusuz satırlar sonda "Konusu belirtilmemiş". Ünite tıklanınca başlanmış satırlar (zayıf üstte, az veri soluk) açılır, başlanmamışlar "+ n başlanmamış satırı göster" ile. Seviye kuralı başlıktaki "?" içinde, renk açıklaması tek satır. Dev'de `LearningOutcomes` boş: satırlar alt konu. Öğrencinin "Velim ne görüyor?" listesine eklendi.

**Issue #521 — zayıf konular kartı.** Ortak `app-weak-topics-card` öğrenci profilinde (`childId` yok, pratik bağlantılı) ve veli ana sayfasında (`[childId]`, salt okunur) durur. Son 30 gün, konu başına en az 5 cevaplanmış soru (#497, #498). Sıra sunucudan gelir; istemci yalnız doğruluğu %70'in altında olanları (`WEAK_TOPICS_MAX_ACCURACY`) ve ilk 5'i alır, çünkü sunucu her konuyu döner ve %95'lik konu "zorlandığım" başlığı altında yanlış mesaj verir. Veride 404 kartı gizler. "Bu konuda pratik yap" `/practice?subjectId&topicId` açar: serbest pratik kurulumu önceden dolar, öğrencinin sınıfında olmayan ders ya da konu seçilmez. `daily=1` yoksa bu parametreler günlük seti açmaz; kapsam yalnız sayfa açılışında bir kez uygulanır, günlük setten serbest pratiğe geçişte kurulum boş açılır. Veli bu veriyi gördüğü için öğrencinin "Velim ne görüyor?" listesinde yazar. Kart konuya göre grupladığı için bağlantı alt konu taşımaz.

**Issue #499 — taksonomi araması.** `GET /api/subject/search?q=&gradeId&take=10` (giriş yapmak yeterli, dakikada 60, `take` en çok 20, `q` 2-100 karakter). Öğrencide `gradeId` varsayılanı kendi sınıfı, diğer rollerde bütün sınıflar. Eşleşme bellekte: `TaxonomySearchService.Normalize` Türkçe harfleri sadeleştirir, DB harmanlaması ya da `unaccent` kullanılmaz, migration yok; her arama adları okur, katalog büyürse önbellek gerekebilir. Her sorgu sözcüğü yolda geçmeli, son parçada en az biri: "matematik açılar" Açılar konusunu bulur, altındaki her alt konuyu dökmez. Önek ("aci") ve ek almış sözcük ("açıları" → "Açılar", ad en az 4 harf) eşleşir.

**Issue #500 — pratik kapsamı.** `POST practice/daily/start` kapsamla gelirse: oturumu açılmış, cevabı olmayan RASTGELE set aynı oturumda BİR KEZ yeniden üretilir, gösterilip cevaplanmayan satırlar silinir; cevap (atlama dahil) varsa ya da set zaten kapsamlıysa 409 `DailySetAlreadyStarted`; aynı kapsam hiçbir şeyi değiştirmez, konu sonradan taşınmış olsa da 400 almaz. Tek seferlik olması güvenlik kuralıdır (#99): A'dan B'ye geçince A serbest pratiğe açılır, A'ya dönülebilseydi öğrenci cevapları görüp puan alırdı. Kapsamsız istek eskisi gibi. Bu #66'daki "oturum açıldıktan sonra kapsam değişmez" kuralını yumuşatır: asistan isteği panoda açılmış set yüzünden yok sayılmasın. Ekran kapsamı yalnız oturum yokken gönderdiği için davranışı değişmez. `POST practice/sessions` `SubTopicIds` (en çok 50, öğrencinin sınıfından, değilse 400) ve `TargetCount` (1-50) alır; sayıya ulaşınca `TargetReached=true`, `PoolExhausted=false`. Migration `AddPracticeSessionSubTopicsAndTarget`.

## Karşılama sayfası (#395)

`/welcome` statiktir, backend çağrısı yapmaz. Veri `ui/src/app/pages/public/landing/landing-content.ts`, metin `ui/public/i18n/landing/{tr,en}.json`.

**Rakamları güncelleme:** `LANDING_STATS` değerlerini yuvarlayarak yaz, gerçek sayılara geçince `LANDING_STATS_ARE_SAMPLE = false` yap. Örnek oldukları sürece rakam bölümü hiç gösterilmez (#880).

**Yorumları güncelleme:** `LANDING_TESTIMONIALS` dizisine bir satır, iki sözlüğe `testimonials.items.<key>.{quote,name,role}` ekle (1–6 kart; soyad, okul, fotoğraf yok). Hepsi izinli gerçek yorum olunca `LANDING_TESTIMONIALS_ARE_FICTIONAL = false` yap; o zamana kadar yorum bölümü gösterilmez (#880). İzin kaydı (kimden, ne zaman) kodda değil, ürün sahibinde tutulur.

**Yayın öncesi:** örnek sayılar ve temsili yorumlar yayımlanmaz (bayraklar `true` iken bölümler kapalıdır); gerçekmiş gibi sunulursa yanıltıcı reklam sayılır.

**Veli (#880):** veli kendini kaydetmez, öğrencinin davetiyle hesap açar; rol kartı ve "Veliler" menü bağlantısı girişe götürür.

**Test çözme görüntüsü:** `assets/images/landing/test-solve.png` gerçek ekrandandır, ekran değişince yenilenir. Soru ve şıklar özgün örnektir, kitap içeriği kullanılmaz (#779). SVG'lerdeki metinler Türkçedir, İngilizce sürümü yok.

## Test çözme: kayıt ve devam (#777)

**Kayıt:** cevap seçilince ya da temizlenince hemen gönderilir (`selectedAnswerId: 0` + payload yok = temizleme). Soru değiştirmek aynı cevabı yeniden göndermez: BadgeService her `AnswerSubmittedEvent`'i istatistiğe yazar (toplam soru, doğru, seri), bu yüzden gereksiz kayıt sayaçları şişirir. Aynı sorunun kayıtları sırayla gider. Başarısız kayıt kalıcı uyarı ve "Tekrar dene" gösterir; bitirmede de yeniden denenir.

**Bu cihazda (localStorage):** `testSolve.instance.<oturum>` son soru, işaretler ve 5 dakika uyarısının gösterildiğini tutar; oturum bitince silinir. `testSolve.autoAdvance` otomatik geçiş tercihidir (varsayılan kapalı). Kayıt yoksa ilk boş soru açılır.

**Otomatik geçiş** yalnız boş soruya ilk cevapta ilerler, cevap değiştirilirken ilerlemez.

## Test çözme: ekrana göre düzen (#778)

`question-layout-planner.ts` soru ve şık düzenini kaynak piksele değil host'un alanına göre seçer: her düzenin (soru üstte 4/2/1 sütun, soru yanda 2/1 sütun) sığabildiği en büyük ölçeği hesaplar, küçültmeden önce düzeni değiştirir. Yalnız kayıtlı boyutları kullandığı için görsel yüklenmesini beklemez; bir sorunun ilk ve sonraki açılışı aynıdır. Düzen pencereye değil host genişliğine baktığı için önizleme çerçevesinde de (#780) aynı sonucu verir.

- `enableHeightFit` veren tüketicilerde çalışır: test çözme, pratik, etkinlik ve önizleme çerçevesi (#791). Test çözmede host kaydırıldığı için soru yüksekliğe de sığdırılır; pratik ve etkinlikte sayfa kaydırıldığı için düzen ve ölçek yalnız genişlikten gelir, uzun soru sayfayı kaydırır.
- `Question.SourcePageWidth` kırpılan sayfanın genişliğidir (toplu kayıtta yazılır, eski sorularda null). Gövde metni sayfa genişliğinin %1,8'i sayılır: ölçek metin 12–22 px arasında kalacak şekilde sınırlanır, yer olan aynı sayfa soruları aynı boyutta görünür. Sayfa genişliği yoksa yalnız alt sınır vardır: yükseklik yüzünden soru 400 px genişliğin altına küçültülmez.
- Dar alanda (600 px altı) soru hep üstte, şıklar en çok iki sütundur. Küçük kalan soru ve şıklar büyütme penceresinde birlikte açılır.
- Okuma parçası yerleşimi yalnız parçaya ve alana bakar (`planPassageLayout`): en/boy oranı 1,6 ve üstü geniş şekil üstte, uzun metin 840 px'ten geniş alanda solda kendi içinde kayar, daha dar alanda Metin/Soru sekmeleri çıkar. Kaydırma konumu ve "görüldü" bilgisi parça bazında bellekte tutulur; sekme ilk görüşte metinle, aynı grubun sonraki sorularında soruyla açılır.
- `Question.LayoutChoice` (`top`, `side`, `whole`, `stacked`; null = otomatik) önizlemeden seçilir ve `PUT /api/questions/{id}/layout-choice` ile hemen kaydedilir. `whole` tüm soru görselini gösterir, şıklar A B C D düğmesi olur; bir şıkkın görseli yoksa da bu düzene düşülür. `stacked` soruyu üstte, her satırda bir şıkla gösterir: başlıklı tablo şıkları (ör. Deterjan / Parlatıcı) ızgaraya dağılınca başlıktan kopar. `enableHeightFit` veren her ekran uygular.

## Test çözme: ekran düzeni (#779)

Seçili şık nötr mavi ve işaretlidir; yeşil yalnız doğru cevap içindir. 1024 px ve altında yan panel yoktur: harita alttan açılır, tüm araçlar yazılıdır. Son soruda "Gözden geçir", "Testi Bitir" ile aynı pencereyi açar; boş ve işaretli soruları listeler, soruya gitmek testi bitirmez. "Çık, sonra devam et" oturumu bitirmez, bekleyen cevapları kaydeder, süreli testte sürenin işlediğini söyler; kayıt başarısızsa çıkmaz. Aynı pencere rotadan her ayrılışta açılır (#821, `testSolveLeaveGuard`): bildirim, menü, tarayıcı geri. Testi bitirme, süre dolması, "zaten bitmiş" yönlendirmesi, etkinliğe yönlendirme ve onaylanmış çıkış sormaz. Sekme kapanırken yalnız kaydı süren ya da başarısız cevap varsa tarayıcı uyarır.

**Yan panel (#820):** harita öncelikli. Özet üç sayaç (cevaplanan, boş, işaretli) ve ince ilerleme çubuğu; numaralar 5 sütun kare (≥ 44 px) ve panelin kalan yüksekliğini alır, sığmazsa yalnız harita kayar; araçlar altta tek sıra ikon + kısa etiket (tam ad tooltip ve `aria-label`'da). Mobil harita sayfası aynı şablonu kullanır, ızgarası `auto-fill` kalır. Soru görseli kartta ortalanır (mobilde tam genişlik). Karalama "Yaz" modunda not alanı panelin boyunu doldurur.

## Soru önizlemesi: cihaz seçici (#780)

`QuestionDevicePreviewComponent` soruyu öğrenci ekranındaki soru alanının boyutunda çizer (`PREVIEW_DEVICES`: telefon, tablet, masaüstü) ve seçimi `questionPreview.device` anahtarıyla bu tarayıcıda hatırlar. Boyutlar öğrenci ekranından ölçülmüştür; başlık, alt çubuk veya panel değişirse yeniden ölçülür. Alan sütuna sığmazsa düzen bozulmadan görsel olarak küçültülür. Önizlemede doğru şık yeşil görünür, öğrencide görünmez. Önizlemede tek görünüm budur: şıka tıklamak doğru cevabı kaydeder; sınıflandırmayı uygulama ve soruyu çıkarma düğmeleri altındadır. Eski birleşik görünüm ve elle yakınlaştırma önizlemeden kaldırıldı.


## Soru bildirimi (#781)

Öğrenci test çözerken "Soruyu bildir" ile neden (cevap yanlış olabilir, okunmuyor, soruda hata, başka) ve isteğe bağlı not gönderir.

- **Yetki:** `POST api/questions/{id}/report` yalnız `Student`; soru öğrencinin o oturumunda değilse 404 (başka öğrencinin oturumu, var olmayan soru aynı yanıt). `QuestionReportsController` ayrıdır çünkü `QuestionsController` öğretmen yeteneği ister.
- **Sınır:** yorum yazma ile ortak `WorksheetCommentWriteRateLimiting` kovası (kullanıcı başına dakikada 10). Aynı öğrenci aynı soruyu bir kez bildirir (filtreli unique index); tekrar 200 + `alreadyReported`, ilk kayıt değişmez.
- **Görünürlük:** `/question-reports` (Soru Havuzu'nun yanındaki sekme); öğretmen kendi sorularını, admin hepsini görür. Yanıtta bildiren yok: öğretmen öğrenciyi tanımayabilir.
- **Onay:** toast yalnız kayıttan sonra; hata olursa diyalog açık kalır, not kaybolmaz.
- **Outbox yok:** tek DB yazması, async iş yok. Bildirimi öğretmene anlık haber veren bir bildirim istenirse event o zaman eklenir.

## Test çözme: karalama alanı (#782)

Çözüm ekranında soru başına çizim ve yazı. Puanı etkilemez.

- **Depolama:** yalnız bu cihazın `localStorage`'ı (`testSolve.scratch.<oturum>`), sunucuya gitmez; bu yüzden öğretmen göremez ve başka cihaza geçmez. Test bitince silinir; bu cihazda bitmeyen testin notları 30 gün sonra temizlenir (`test-solve-scratch-store.ts`).
- **Çizim:** SVG yolu; koordinatlar alan genişliği 1000 sayılarak tutulur, ekran döndüğünde çizim genişliğe göre ölçeklenir, alan kısaldıysa alttaki kısım geri dönene kadar görünmez. Kalem kullanıldıktan sonra dokunma çizmez (avuç içi). Silgi yok: geri al ve temizle var, temizleme de geri alınır.
- **Yerleşim:** masaüstünde yan paneli kaplar (panel 260 → 360 px), telefon ve tablette dock'un üstünde `42dvh`. Soru görünür ve kaydırılabilir kalır, pad açıkken cevap verilebilir.
- **Dock:** ≤1024 px otomatik geçiş harita sayfasına taşındı; ≤350 px araçlarda yalnız ikon kalır.

**Issue #534 — karışık mini deneme.** `POST practice/sessions` `mixedReview: true` ile sabit listeli oturum açar (`MixedReview.cs`). Varsayılan 15 soru: %40 tekrar zamanı gelen (#529), %30 yanlış defteri, %30 zayıf konulardan (son 30 gün, en az 5 cevap, doğruluk %70 altı, #497) hiç cevaplanmamış soru. Pay ve sayı `PracticeMixedReview` bölümünde; `TargetCount` sayıyı ezer. Aynı soru iki kez girmez, eksik pay diğer kaynaklardan tamamlanır, kaynaklar yetmezse oturum kısa kalır, hepsi boşsa 400 `practice.noMixedReview`. "Zor sorular" kaynağı (#526) henüz yok; yerine yanlış defteri kullanılır, #526 gelince değiştirilir. Bitmemiş test ve atama soruları ile bugünün günlük seti girmez. Süre ve net hesabı yok. Giriş pratik kurulum ekranındaki kart; asistan aracı sonraki adım, aynı isteği kullanır.

**Issue #695 — hedef okul ve net farkı (API).** Kullanıcı kararı: taban puanları admin resmî kaynaktan girer; dağıtım hukuken incelenene kadar serbest kabul edilir, `TargetGoals:Enabled=false` bütün uçları 404 yapar (`GET api/target-goals/status` hariç, ekran buna bakar). `TargetPrograms`: puan türü (`lgs`, `tyt`, `say`, `ea`, `soz`; hesaplayıcının kimlikleri, DİL yok çünkü hesaplayıcı üretmez), ad, kurum (YKS'de zorunlu), il, ilçe, yıl, taban puan, yüzdelik, kaynak. Her yıl ayrı satır. `NormalizedKey` Türkçe harfleri sadeleştirir; benzersizlik (puan türü, yıl, anahtar) ve arama buradan yürür, çünkü DB harmanlaması İ/I'yı eşlemez. Admin: `GET/POST/PUT/DELETE api/admin/target-programs`, `POST .../import/preview` ve `.../import/confirm` (multipart: `Exam`, `Year`, `Source`, `File`, en çok 10 MB, 50.000 satır). Sunucu önizleme durumu tutmaz, onayda aynı dosya yeniden gönderilir ve aynı kurallarla işlenir; hatalı satır atlanır, çünkü resmî dosyada puanı olmayan satır olağandır. Listeler ilk 200 satır, sayılar tam. CSV: ayraç başlıktan (`;`, `,`, sekme), kodlama içerikten (UTF-8 değilse Windows-1254), ondalık virgül ya da nokta; başlıklar `Okul/Program/Ad`, `Kurum/Üniversite`, `İl`, `İlçe`, `Puan Türü`, `Taban Puan`, `Yüzdelik`. Resmî dosyaların gerçek biçimi henüz denenmedi. Seçilmiş program silinmez (`InUse`), düzeltilebilir. Öğrenci: `GET/PUT/DELETE api/student/me/target-goal`, katalog `GET .../programs`; öğrenci başına tek hedef. Veli: `GET api/parent/children/{studentId}/target-goal` (kapı, erişim kaydı `children.target-goal`; kaynak gönderilmez, çoğu zaman bağlantıdır). Yanıt hedefi ve puan türünün gerektirdiği son tamamlanmış denemeleri verir (AYT puanı için TYT + AYT); bütün oturumları bitmemiş deneme girmez. Fark istemcide hesaplanır, puan modeli (#694) sunucuya kopyalanmaz. **Ekran:** ortak `app-target-goal-card` öğrenci profilinde (seçer, değiştirir, kaldırır) ve veli panelinde (`[childId]`, salt okunur, 404'te gizli). Hesap `score-calculator/target-gap.ts`: ders satırı şablon ve ad ya da grup ile hesaplayıcı testine eşlenir (`SECTION_TESTS`; TYT Sosyal ve Fen grubun toplamı), eşleşmeyen satır varsa yanlış sayı göstermemek için fark gösterilmez, bu yüzden şablon adı değişirse tablo da güncellenir. Tahmini puan ile taban arasındaki eksik puan, her dersin kalan netine oranla bölünür; bu öneridir, tek doğru dağılım değildir. Her ders tam net olsa da ulaşılamıyorsa ders önerisi verilmez. Ekranda yazar: sayılar tahmindir, YKS tabanı OBP içerir ama tahmin içermez, model yılı taban yılından farklıysa ikisi de belirtilir. Admin: `/target-programs` (menü durum ucuna bakar). Öğrencinin "Velim ne görüyor?" listesine eklendi. #900: ekranın üstünde sınav başına durum şeridi (`GET api/admin/target-programs/summary`: toplam, son yıl ve o yılın sayısı; toplam 0 ise öğrencinin hedef araması boş döner, şerit uyarır). CSV yükleme üç adımlı diyalog: dosya ve sınav (örnek CSV indirilir; başlıkları sunucunun tanıdığı test `Admin_sample_files_are_read_without_errors` ile sabit) → önizleme (her grupta ilk 10 satır, sayılar tam) → sonuç. Arama yazarken süzer (300 ms).

**Hedefim (öğrenci).** `/my-target` menüde, yanında `/my-score-calculator` sekmesi (aynı hesaplayıcı, `data.embedded` ile tanıtım öğeleri gizli). Hedef kartı profilden buraya taşındı. Gelişim: `GET api/student/me/target-goal/history?scoreType=` eskiden yeniye son 30 tam denemeyi döner; puanı istemci hesaplar (`targetProgress`, AYT her noktada o ana kadarki son TYT ile), sunucu puan üretmez. "Bu puanla hangi okullar": `GET .../programs/for-score?scoreType&score&city&take` en son yılın tabanına göre üç grup döner, sınır ±10 puan (`BorderlineMargin`). Puan istemciden gelir, yalnız katalog sorgulanır. Liste `TargetPrograms` doluysa çalışır: veri admin `/target-programs` CSV'si ile girilir, boşken `catalogEmpty` görünür. Yerleşme tahmini geçen yılın tabanıdır, garanti değildir; arayüz bunu söyler.

**Issue #694 — puan hesaplayıcı.** `/score-calculator`, hesapsız ve önceden üretilmiş sayfa. Veri `ui/src/app/pages/public/score-calculator/exam-score-data.json`, bundle'a gömülü: istek atılmaz, girilen sayı kaydedilmez. Puan = 100 + 400 × (ağırlıklı − en küçük) / (en büyük − en küçük). **YKS 2026:** ortalama ve standart sapma ÖSYM Sayısal Bilgiler (son sınıf adayları), ağırlıklar Kılavuz Tablo 1C ve 1E. ÖSYM en küçük ve en büyük ağırlıklı puanı yayımlamaz: en büyük tüm geçerli sorular doğru, en küçük her test boş ve bir gerekli testte 0,5 ham puan. Yayımlanan dağılımla kaba kontrol: TYT modelde ortalama adayda ~235, dağılım medyanı ~237, ortalaması ~248. AYT doğrudan sınanamadı. **LGS 2026:** MEB ders bazında ortalama ve standart sapma yayımlamıyor. Formül ve ağırlık resmî (Merkezî Sınav Raporu Tablo 7); standart sapma Tablo 8'den türetilir: soru × ayırt edicilik × √(p(1−p)) × (1 + w/3), w = 0,5 varsayım (doğru olmayan cevaplarda yanlış payı). Ortalama sonucu etkilemez, ölçek uçlarıyla sadeleşir; en küçük hepsi yanlış, en büyük hepsi doğru. Ayrı ayrı %20 standart sapma sapması ±17'ye kadar, en küçük varsayımı düşük netlerde daha fazla oynatır, bu yüzden aralık ±25. Gerçek bir sonuçla doğrulanmadı. Aralıklar (`marginPoints`) kanıtlanmış hata payı değil, yargıdır. Dışarıda: DİL, OBP, yerleştirme puanı, muafiyetler. "Nasıl hesaplıyoruz?" penceresi adımları, değerleri, ağırlıkları ve kaynakları gösterir; değerler JSON'dan gelir, yöntem metni `public-pages` sözlüğündedir, yöntem değişirse metin de güncellenir. **Her yıl:** sınavın yeni değerleri JSON'a yazılır, `year` ve kaynaklar güncellenir, ölçek uçları yeniden hesaplanır (`exam-score.spec.ts` uçları testlerden yeniden üretip denetler). AYT'de iptal edilen soru `validQuestions` ile yazılır. #896: geniş ekranda dersler solda, sonuç kartı sağda yapışkan (toplam net + tahmini puan); kutularda "0–soru sayısı" ipucu, doğru + yanlış soru sayısını aşan satır kırmızı, net sütununda "En çok n" ve sonuç kartı düzeltilecek satırları adıyla söyler. Tanıtım sayfasının üst çubuğu (P3) eklenmedi: gezinti bağlantıları tanıtım sayfasının bölümlerine kayar.

## Cevap istatistikleri: soru başına bir sayım (#785)

BadgeService `TotalQuestions`, `CorrectQuestions`, `TotalTimeSeconds`, `QuestionCount`, `CorrectCount` ve `ActivityScore` değerlerini artık oturum ve soru başına son cevapla hesaplar. Sayılmış durum `AnswerPointAwards` satırında durur (`CountedActivityDate`, `CountedCorrect`, `CountedTimeSeconds`, `HasBeenAnswered`); her event yalnız farkı uygular. Aynı soru başka oturumda yeniden sayılır.

- **Temizleme:** exam API seçim ve payload boşsa `AnswerSubmittedEvent.IsCleared` yazar. Soru sayaçlardan, süreden ve puandan çıkar, yanlış sayılmaz. Hiç cevaplanmamış sorunun temizlenmesi toplamlara dokunmaz.
- **Süre:** sorunun son gönderilen süresi geçerlidir. İstemci süreyi son ziyaretten ölçer, yani geri dönülen soruda önceki ziyaretin süresi toplama eklenmez (`TimeTaken` alanıyla aynı).
- **Gün:** fark, sorunun sayıldığı günün aktivite satırına yazılır. Gece yarısından sonraki değişiklik yeni aktif gün açmaz. Temizlenip yeniden cevaplanan soru yeniden cevaplandığı güne yazılır.
- **Seri:** yalnız sorunun oturumdaki ilk cevabı doğruysa seri artar; doğruya çevirmek veya temizleyip yeniden doğru vermek artırmaz. Doğruyu yanlışa çevirmek ve yeni yanlış cevap seriyi sıfırlar, yanlıştan yanlışa değişiklik sıfırlamaz. Temizleme seriye dokunmaz. `BestCorrectStreak` geri düşmez.
- **Rozetler:** kazanılmış rozet, sayaçlar sonradan düşse de geri alınmaz.

**Şişmiş eski toplamlar olduğu gibi kalır, yeniden hesaplanmaz.** BadgeService geçmiş cevapları saklamadığı için doğru değeri kendi verisinden çıkaramaz. Migration mevcut `AnswerPointAwards` satırlarını sayılmış işaretler (doğruluk `PointsAwarded > 0`, gün son uygulanan cevabın günü, süre bilinmediği için 0); böylece süren oturumlarda değişiklik soruyu yeniden saymaz, yalnız o sorunun süresi bir kez daha eklenebilir.

**Issue #531 — konunun haftalık gelişimi.** `GET /api/student/me/topic-performance/timeline?topicId|subTopicId&weeks=12` (yalnız öğrenci, konu performansıyla aynı hız sınırı; tam olarak bir hedef, hafta 1–26). Cevaplar `StudentAnswerReader`'dan, konu eşlemesi konu performansıyla aynı. Hafta Pazartesi başlar, yerel takvime göredir (Europe/Istanbul); içinde bulunulan hafta dahildir. Test cevabı oturumun bittiği haftaya sayılır. Boş cevap girmez; cevapsız haftada `accuracyPct` boştur, 3 cevabın altı `lowSample`. Issue'daki `bucket=week` parametresi yerine ayrı uç: yanıtın biçimi farklı, #497 yanıtı değişmez. Ekran: öğrenci profilinde `app-topic-progress-card`; konu listesi son 90 günden (en az 1 cevap), açılışta en çok çözülen konu. Grafik elle SVG: cevapsız hafta çizgiyi böler, az cevaplı hafta içi boş ve soluk işaretlidir; ipucu fare, dokunma ve ok tuşlarıyla açılır, aynı bilgi tablo görünümünde de var. Özet yeterli cevabı olan ilk ve son haftadan gelir. Alt konu ve veli görünümü ekranda yok; asistan aracı ertelendi.

## Bilgi kartları (#699)

Öğretmen destesi (sınıf + ders zorunlu, konu isteğe bağlı) atanır; öğrenci destesi özeldir, atanamaz, paylaşılamaz. Puan, rozet, liderlik yok; outbox event'i yazılmaz.

- **Uçlar:** öğrenci `api/flashcards` (`decks`, `cards`, `due`, `due-count`, `cards/{id}/review`), öğretmen `api/teacher/flashcards` (aynı yazım uçları + `decks/{id}/assignments`). Gateway joker route'u kapsar (`/api/exam/...`). İş kuralı hatası 400 `{ errorCode }`.
- **Takvim:** Leitner, aralıklar #529 ile ortak (1, 3, 7, 21 gün; `FlashcardSchedule`). `Forgot` ilk kutu, `Hard` aynı kutu, `Good` üst kutu. Son kutudaki kart çıkmaz. Yeni kart bugün gelir. Günü gelmemiş karta tekrar 409 `NotDue` (çift dokunuş kutuyu iki kez ilerletmez); aynı gün yeniden gösterim istemcide.
- **Görünürlük:** `FlashcardAccess.AssignedTo` worksheet kuralıyla aynı, platform geneli yok; zaman penceresi yok. Atama hedefi worksheet ile aynı denetimlerden geçer (`ApplyScope`, öğretmen kaydı, bağımsız öğretmen sınıfa atayamaz). Öğrenci okul değiştirince sınıf ataması düşer, doğrudan atama kalır; kapsam dışı öğrenci öğretmenin listesinde görünmez. Atama geri gelirse ilerleme kaldığı yerden sürer.
- **İçerik:** metin `AppDbContext`'te `RichTextSanitizer` ile; görsel (yalnız ön yüz, en çok 2 MB, sahip başına 300) çözülüp yeniden yazılır, anahtar `questions/flashcards/` altında: imza allowlist'i değişmez. Değiştirilen görsel depodan silinmez.
- **Sıfırlama:** `FlashcardReviews` gerçekten silinir, doğrudan atamalar kalkar (worksheet ile aynı), öğrencinin destesi kalır.
- **Ekranlar:** öğrenci `/flashcards`, `/flashcards/review` (`?deck=` ile tek deste), `/flashcards/:id` (yalnız kendi destesi); öğretmen `/teacher/flashcards`, `/teacher/flashcards/:id` (atamalar burada). Menü: Soru Çöz ve Çalışma Sayfaları sekmeleri. Ana sayfada bugün gelen kart varsa sayı görünür.
- **Metin kodlaması:** sunucu metni HTML temizleyiciden geçirir; `FlashcardService` gidişte kodlar, gelişte çözer. Ekranlar düz metin görür ve metni `innerHTML` ile basmaz. Sınır kodlanmış metne uygulandığı için istemci de kodlanmış uzunluğu denetler.
- **Tekrar:** `Hatırlamadım` denen kart aynı oturumda sona eklenir; ikinci değerlendirmede gelen 409 `NotDue` ve kart / atama kalktıysa 404 sessizce geçilir. Kuyruk bitince sunucuya yeniden sorulur (liste en çok 50).
- **İlk sürümde yok:** öğretmenler arası paylaşım, veli uçları, arka yüz görseli.


## Koç paneli (#700)

Yeni rol yok. Koç, öğrenciyi #696 kuralıyla görebilen öğretmendir: kendi okulunun doğrulanmış öğrencisi ya da onaylı özel ders (Approved Booking) verdiği öğrenci (`SchoolAccessPolicyCoachExtensions.CanAccessAsTeacherOrTutorAsync`, kazanım ucu da bunu kullanır). Kapsam dışı öğrenci her uçta 404. Outbox event'i yok.

- **Uçlar:** öğretmen `api/teacher/coach/students` (liste; `relationship` = `School` | `PrivateLesson`), `{id}/overview`, `{id}/goals` (`?weekStart=`), `{id}/notes`, hedef ve not için POST / PUT / DELETE. Öğrenci `api/student/me/coach-goals` (bu hafta, bütün koçlar). Veli `api/parent/children/{id}/coach-summary` (kapı → erişim kaydı → veri, `children.coach-summary`). Gateway joker route'u kapsar. İş kuralı hatası 400 `{ errorCode }` (`EmptyText`, `TooLong`, `MarkupNotAllowed`, `InvalidWeek`); hafta başına 10 hedef 409 `TooManyGoals`, öğretmen + öğrenci başına 200 not 409 `TooManyNotes`. Uzunluk sınırı temizlenmiş (kodlanmış) metne de uygulanır.
- **Özet:** hafta planı ("Planım" testleri, bitmiş oturumu olan tamamlandı sayılır; Keycloak kimliği çözülürse kişisel programın günleri ve sayfaları da), son denemeler (şablon başına en son tamamlanan, en çok 5), zayıf konular (son 30 gün, en az 5 soru, doğruluk %70 altı, en çok 5), aktif yanlış sayısı (yanlış defteri kuralı), öğretmenin bu haftaki hedefleri. Soru ve cevap içeriği yok. Veli özeti yalnız "Planım"ı sayar.
- **Hedef:** hafta Pazartesi başlar (yerel takvim); bu hafta ile 4 hafta sonrası arası. Öğretmen yalnız kendi hedeflerini görür ve değiştirir. Hedef düz metindir: bağlantı ve görsel etiketi 400. Öğrenci ve veli bu haftanın hedeflerini görür; veliye öğretmen kimliği gitmez.
- **Not:** yalnız yazan öğretmen okur; öğrenci, veli ve özet yanıtlarına girmez (`ParentForbiddenContentTests` tohumu). Silme gerçek silmedir, soft-delete yok. Liste en yeni 100.
- **Metin:** `AppDbContext`'te `RichTextSanitizer`; yalnız etiketten oluşan metin boş sayılır. Ekran #699 gibi gidişte kodlamalı.
- **Okul değişimi ve sıfırlama:** satırlar kalır, erişim her istekte yeniden denetlenir: ilişki biten öğretmen hedef ve notlarına da ulaşamaz, ilişki geri gelirse görür. İlerleme sıfırlamada hedef ve notlar silinir.
- **Bilinçli:** Admin + Teacher hesabı sınırsız kapsamla her öğrenciyi açar (#696 ile aynı). Hedef sınırı sayıp yazar, eşzamanlı istekte aşılabilir (yalnız öğretmenin kendi satırları).
- **Ekran:** öğretmende Mesajlar bölümünün "Koçluk" sekmesi (`/teacher/coach`, ana menü #719 sınırında kalır): öğrenci listesi (ad araması, ilişki etiketi), öğrenci sayfası (`/teacher/coach/:studentId`; özet kutuları, kazanım haritası bağlantısı, hafta seçicili hedefler, notlar). Hafta listesi sunucunun `plan.weekStart`'ından türetilir (bu hafta + 4). Öğrenci ana sayfasında günün sorularının altında `app-coach-goals-card` (hedef yoksa çizilmez), velide çocuk kartlarının arasında aynı kart ("Planım" sayıları + hedefler, 404'te gizli). Metin `CoachService`'te gidişte kodlanır: etiket yazan öğretmenin metni düz metin olarak saklanır, `MarkupNotAllowed` ekrandan gelmez.
- **Açık:** notların saklama süresi (#810), velinin bu özeti kapatabilmesi (bağlantıda izin alanı yok, Active bağlantı yeterli).
- **Sinyaller ve işaret (#893):** liste satırında `lastActiveDay` (veli panelinin "son etkinlik" kaynakları: cevap, test başlat / bitir, pratik; yerel gün), `mistakeCount` (yanlış defteri kuralı; öğrenci başına birkaç sorgu, listenin ilk 60 öğrencisine hesaplanır, kalanı `null`), bu haftanın bu koça ait `goalCount` / `goalsDone`. `needsAttention` (7+ gün etkinlik yok, 10+ yanlış ya da bu hafta hedef yok) önce sıralanır, grup içinde ad sırası. Öğrenci bu haftaki hedefini `PUT api/student/me/coach-goals/{id}/done` `{ done }` ile işaretler (`CoachWeeklyGoal.CompletedAt`; başka öğrencinin ya da başka haftanın hedefi 404); öğretmen "öğrenci işaretledi / henüz işaretlemedi" görür, veli kartında "Yapıldı". Hedef metni düzenlenince işaret kalır. Ekran iki panellidir: `/teacher/coach/:studentId` liste sayfasının çocuk rotasıdır (dar ekranda tek panel, geri bağlantısı yalnız orada); öğrenciler arasında geçişte sayfa yeniden kullanılır ve `paramMap`'ten yeniden yüklenir. Hedef eklenip silinince soldaki satır güncellenir (sıra değişmez).

## Bağlam temelli sorular ve beceri etiketleri (#703)

Kılavuz özeti ve beceri listesinin kaynağı `docs/context-based-questions.md`.

- **Şema:** `Question.IsContextBased` ve `QuestionSkills` (soru ↔ `Skills`, çoka çok, soft delete). `Skills` seed'den gelir (`SkillSeed`):
  KB2.1–KB2.20 (kimlik 1–20) ve KB3.1–KB3.3 (101–103); kod kalıcıdır, aktarım ve ekran çevirisi koda bakar. KB1, alan becerileri ve
  süreç bileşenleri ilk sürümde yok.
- **Uçlar:** `GET api/skills` (öğretmen/admin), `GET/PUT api/questions/{id}/context-tags` `{ isContextBased, skillIds }` (okuma görebilen,
  yazma yalnız soru sahibi/admin; en çok 10 beceri; bilinmeyen beceride hiçbir şey yazılmaz, 400). Bayrak ve beceriler bağımsızdır.
- **Soru Havuzu:** `contextBased` (true/false) ve `skillIds` (VEYA, en çok 30) filtreleri; kartta `isContextBased`, `skillCodes`. Test
  oluşturucu "Havuzdan ekle" ile aynı aramayı kullanır.
- **Aktarım:** paket `isContextBased` ve beceri kodlarını taşır; hedefte boşsa doldurulur, bilinmeyen kod atlanır.
- **Ürün dili:** "Maarif Modeli'ne uygun" gibi uygunluk iddiası yazılmaz; etiket öğretmenin beyanıdır. `context-tags-dialog.component.spec.ts`
  `question-bank` sözlüğünde bu kalıpları yasaklar.
- **Ekran:** Soru Havuzu filtresinde "Bağlam temelli" ve "Beceriler" (çoklu, kategoriye göre); kartta etiketler ve kendi sorusunda
  "Beceriler" penceresi (`ContextTagsDialogComponent`, kayıttan sonra kart yerinde güncellenir). Beceri adı koddan çevrilir:
  `skills.KB2.8` (`skillLabelKey`); yeni seed satırı iki dilde çeviri ister (spec yakalar).
- **Sonraya:** sınıflandırıcının etiket önermesi (Claude, `SchemaRevision`), toplu kayıtta ve etkinlik editöründe etiket.

## Program sihirbazı (#885)

- Dört adım: Ne kadar? (süre ya da günlük soru + günde kaç ders) · Hangi günler? (yedi gün, hepsi açık) · Hangi dersler?
  (öğrencinin sınıfının dersleri, `GET api/program/subjects`) · Ne zamana kadar? (2 hafta / 1 ay / 2 ay ya da tarih; ad boşsa
  "Kasım programı").
- Gönderim eski adım id'leriyle (`userSelections` 1/2/3/5/6) + `difficultSubjectIds`; doluysa eski 7. adım (sabit dört ders)
  yok sayılır, `UserProgram.DifficultSubjects` ders adlarını tutar (bilgi amaçlı).
- Sağdaki örnek hafta sunucunun takvim kuralının TS kopyası (`pickDailySubjects` ↔ `ProgramService.PickDailySubjects`);
  biri değişirse diğeri de değişmeli.
- `GET api/program/steps` ucu duruyor ama arayüz artık kullanmıyor.

## Okullar listesi (#898)

`GET api/admin/schools` satırı `teacherCount` (`Teacher.SchoolId`), `studentCount` (doğrulanmış üyelik) ve `pendingCount` (doğrulanmamış öğrenci üyeliği + bu okulu isteyen, onay bekleyen öğretmen) taşır; sayılar alt sorgularla tek istekte gelir. Silme artık kimlik bağına da bakar: okulda öğretmen (bağlı ya da okul isteyen) veya öğrenci (bekleyen dahil) varsa reddedilir; eski serbest metin `SchoolName` eşleşmesi de engellemeye devam eder. Ekran: liste üstünde yalnız arama ve il / ilçe süzgeci, "Okul ekle" diyalog açar; satırda konum, sayılar ve satır menüsü (Düzenle, Sil); üyesi ya da başvurusu olan okulda Sil devre dışı ve nedenini yazar.

Okul sayısı 54 binin üstünde olduğu için liste hiçbir ekrana bütün inmez. `GET api/school?q=&limit=` (anonim, en fazla 50 satır, üye sayısı taşımaz) ve `GET api/school/{id}` seçicilerin, `GET api/admin/schools?q=&provinceId=&districtId=&page=&pageSize=` okul yönetiminin kaynağıdır. Arama `School.SearchText` üzerindedir: ad + ilçe + il, Türkçe harf ve büyük/küçük farkı katlanmış (`SchoolSearchText`), okul yazılırken güncellenir. Bir il ya da ilçe adı değişirse bu kolon yeniden doldurulmalı (SQL: `AddSchoolSearchText` migration'ı).

## Çalışma sayfası blokları (#933)

- `StudyItemBlocks` tablosu: sayfa başına en fazla 50 sıralı blok — `Text`, `Image` (alt yazı), `Link` (başlık + platform),
  `Question` (soru havuzundan), `Flashcard` (ön / arka). Sayfanın eski ana içeriği (`ContentType`: görsel seti / bağlantı / kitap
  aralığı) yerinde kalır, çünkü program, takvim ve kitap kaydı onu okur; bloklar ana içerikten sonra gösterilir. Eski sayfalar
  taşınmadı (blokları boş, eskisi gibi görünür). Blokları olan sayfada görsel seti zorunlu değil.
- Kayıt: create / update formuna `Blocks` JSON'u (`[{id?, type, text, backText, imageUrl, url, platform, questionId}]`); alan
  yoksa bloklara dokunulmaz, `[]` hepsini siler. Id'si eşleşen blok yerinde güncellenir, listede olmayan soft delete edilir,
  başka sayfanın id'si yeni blok sayılır. Görsel `POST api/study-items/block-images` ile `study-pages/pages/blocks/{userId}/`
  altına yüklenir (PNG/JPEG/WebP/GIF, ≤ 10 MB); kayıtta `StudyPage` allowlist'ine normalize edilir.
- Soru bloğu: soru silinmemiş ve sayfaya yeni ekleniyorsa öğretmene görünür olmalı (`CanViewQuestionAsync`, havuzun `GET {id}`
  kuralı); sayfada zaten olan soru yeniden denetlenmez. Soru silinirse blok kalır, öğrenciye gösterilmez, editörde uyarı çıkar
  ve kayıtta düşer.
- Öğrenci görünümü `/study-page/:id` (plan detayındaki "Sayfayı aç", öğretmen listesinde göz simgesi). Soru bloğu pratik
  oturumu açar: `POST api/practice/sessions` `{ studyItemId, studyQuestionId? }` — yayımlanmış sayfanın silinmemiş soruları,
  blok sırasıyla sabit liste (en fazla 30). Editörde "Öğrenci görünümü" sekmesi aynı bileşeni (`study-page-blocks-view`) gösterir.

## Plan ve haklar (#1019, epic #1017; abonelik epic #1026)

- **Model (exam API):** `SubscriptionPlans` (anahtar, ad, aktif), `SubscriptionPlanFeatureLimits` (plan + özellik anahtarı +
  aylık sınır; null = sınırsız, satır yok = 0), `UserSubscriptionPlans` (kullanıcı + plan + `ValidFromUtc` / `ValidToUtc`,
  hariç üst sınır; kullanıcılar auth-api'de olduğundan FK yok) ve kullanım defteri `EntitlementUsages` (kullanıcı, özellik,
  `ReferenceKey`, zaman; `(FeatureKey, ReferenceKey)` tekil → aynı iş iki kez sayılmaz). Migration yalnız **Free** planını ve
  `book-import` = 0 sınırını seed'ler (Free öğretmen yalnız demo görür, #1032); değerler veritabanından değiştirilebilir.
- **Çözümleme (`IEntitlementService`):** geçerli ve aktif plan ataması yoksa Free. Birden çok geçerli atamada en son başlayan
  kazanır; admin ataması açık olanı yeni atamanın başında kapatır, sonra başlayacakları siler (hesap başına tek plan).
  **Admin** her özellikte sınırsız; rol muafiyeti özellik bazında `EntitlementRoles.UnlimitedByFeature`'da (şimdilik
  QuestionTrainer → `book-import`, #1017 pilotu). Sayaç **UTC takvim ayı**; sınırda `Allowed=false`, `ResetsAtUtc` = gelecek
  ayın 1'i. Rol muafiyetinde ve null sınırda `Limit`/`ResetsAtUtc` null.
- **Kitap aktarımı:** yükleme `CheckAsync` ile kapıdan geçer (dolu → 429 `entitlement.limitReached` + limit / used /
  resetsAtUtc). Kullanım **kurulum onaylanınca** sayılır: #1021 onayda `CheckAsync` + `RecordUsageAsync(userId, "book-import",
  jobId)` çağırır; onaydan önce düşen / iptal edilen iş sayılmaz. Başlangıç ekranındaki kart sınırsızda gizli, 0'da "planınızda
  yok" + demo notu, aksi hâlde "Bu ay N / M".
- **Admin uç noktaları:** `GET /api/exam/admin/subscription-plans` (katalog + sınırlar), `GET|PUT
  /api/exam/admin/subscription-plans/users/{userId}` (`{ planKey, validFromUtc?, validToUtc? }`; exam kullanıcı id'si). Arayüz
  #1030'da.
- **Admin plan ataması (#1030):** yönetim menüsünde Kullanıcılar → "Abonelikler" (`/admin/subscriptions`, yalnız Admin /
  SuperAdmin). **Kullanıcılar** sekmesi: ad / e-posta araması (`GET …/users?q=&role=`, en az 2 karakter, en çok 20 sonuç, e-posta
  maskeli, #246 hız sınırı ve `AdminDataAccessLogs` kaydı) auth-api'nin `POST /api/auth/users/search` ucuna gider (yalnız Service
  politikası, gateway'de kapalı; LIKE joker karakterleri kaçırılır). Detay (`GET …/users/{id}`, audit'li): güncel plan, son 50
  atama (Etkin / Planlandı / Bitti) ve son 50 değişiklik. Atama formu plan + başlangıç (boşsa şimdi) + bitiş (boşsa süresiz;
  arayüz seçilen günün son anını gönderir, gün dahil). "Atamayı bitir" (`POST …/users/{id}/end`, satır içi onay) başlamış
  atamayı şimdi kapatır, planlanmışları siler; kullanıcı Free'ye döner, bitirecek atama yoksa 404 `subscription.noAssignment`.
  **Okullar** sekmesi: okul + kitle (Hepsi / Öğretmenler / Öğrenciler) + plan + tarihler; sayılar `GET …/schools/{id}/members`
  (öğretmen: `Teacher.SchoolId`, öğrenci: yalnız doğrulanmış üyelik), "N kişiye ata" ikinci tıklamada onaylanır →
  `PUT …/schools/{id}`. Öğretmen planı yalnız öğretmenlere, öğrenci planı yalnız öğrencilere toplu atanır (400
  `subscription.audienceMismatch`; tekil atamada serbest); tek istekte en çok 5000 kişi. **Geçmiş:** her atama / bitirme
  kullanıcı başına bir `SubscriptionPlanAssignmentAudits` satırı yazar (yapan, eski → yeni plan anahtarı, tarihler, toplu
  atamada okul); kullanıcının kendi deneme / demo ödemesi de buraya düşer (detayda "Kendisi").
- **Katalog (#1027):** seed'li dört plan — `free` (herkes, varsayılan), `trial` (herkes, `TrialDays` = 14), `teacher-pro`
  (öğretmen), `student-plus` (öğrenci). Planda `DisplayNameKey` (`subscription.plans.*` çeviri anahtarı), `Audience`
  (`Any` / `Teacher` / `Student`), yer tutucu `MonthlyPrice` + `Currency` (`TRY`; gerçek ödeme yok), `SortOrder`. Sınırlar
  (aylık; satır yok = 0, null = sınırsız): `book-import` Free 0 / Trial 1 / TeacherPro 10; `ai-classification` Free 20 /
  Trial 50 / TeacherPro 1000; `mock-exam` Free 0 / Trial 2 / StudentPlus sınırsız (Free'de herkese açık tek deneme sınavı
  sınırdan muaf, #1031); `quick-add` (fotoğraf başına, #1052) Free 5 / Trial 20 / TeacherPro 200. Hepsi veritabanından değiştirilebilir. Uç noktalar (oturum açmış her kullanıcı):
  `GET /api/exam/subscription-plans` çağıranın rolüne uygun aktif planlar (`Any` + öğretmene Teacher, öğrenciye Student;
  Admin hepsi; veli yalnız `Any`), `GET /api/exam/subscription-plans/me` plan + `validToUtc` + `EntitlementFeatures.All` ve
  planın sınırladığı her özellik için bu ayki `allowed` / `limit` / `used` / `remaining` / `resetsAtUtc`.
- **Paket kademeleri (#1093, epic #1085):** her kitleye üç ücretli plan: `teacher-basic` / `teacher-essential` / `teacher-pro`
  (149 / 299 / 599, arayüzde Basic / Essential / Pro) ve `student-basic` / `student-essential` / `student-pro` (99 / 199 / 349);
  fiyat ve sınırlar sahibin yer tutucularıdır (#1082, #1084). `student-plus` pasif, atamaları `student-pro`'ya taşındı. Özellik
  listesi `EntitlementFeatures.Registry` (anahtar, kitle Teacher / Student / Both, tür Toggle / Metered, birim); açık / kapalı
  özellik = sınırı null satır, satır yok = planda yok. `GET api/exam/subscription-plans/features` çağıranın rolüne ait listeyi
  kart sırasıyla döner; `me` yalnız bu özellikleri sayar, satırlarda `kind` ve `unit` taşır. Deneme iki kitleye de Pro gibi açılır
  (sayılanlarda deneme sınırı). Free eski sınırlarını kilitleme işine (#1090) kadar korur. Yeni anahtarların kapı kontrolleri
  #1087-#1089'da bağlanır; şimdilik yalnız eski dört özellik (kitap, hızlı ekleme, sınıflandırma, deneme sınavı) uygulanır. Admin
  ekranında öğretmen / öğrenci planı adının önüne kitle yazılır ("Öğretmen · Pro").
- **Plan düzenleyici (#1086):** `/admin/subscriptions` → "Planlar" sekmesi; `PUT api/exam/admin/subscription-plans/{key}`
  (yalnız Admin) ad, aylık fiyat, deneme süresi (yalnız `trial`, 1–365 gün; diğerlerinde null), etkinlik ve bütün sınırları
  birlikte yazar. Gövdedeki `monthlyLimits`'te olmayan özellik planda yoktur: satırı silinmez (soft delete tekil indekse
  takılırdı), 0'a çekilir; açık / kapalı özellikte sayı yok sayılır. Kayıtta olmayan anahtar, planın kitlesine ait olmayan özellik
  (öğretmen planına öğrenci özelliği), negatif sınır 400; Free ve deneme kapatılamaz (`subscription.planRequired`). Her kayıt
  `SubscriptionPlanChangeAudits`'e eski → yeni JSON yazar. Sınırlar her denetimde veritabanından okunduğu için önbellek yok,
  sonraki denetimde geçerlidir; bu ayın kullanımı sıfırlanmaz. Kullanıcının gördüğü plan adı çeviri dosyasındadır (`Name` yalnız
  admin / kayıt adı). Limit satırı id'leri 1000'den başlar: admin eklemeleri seed id'leriyle çakışmaz.
- **Soru girişi plana bağlı (#1087):** öğretmenin soru girişi `QuestionTrainer` rolüne değil plana bakar. Uç noktalar
  `[RequiresEntitlement(feature)]` filtresiyle korunur: sınır 0 → 403 `entitlement.notInPlan`, sınır doldu → 429
  `entitlement.limitReached` (`InPlanOnly` uçlarda yalnız 403; filtre kullanım yazmaz, sayılan uç başarıdan sonra kendisi yazar).
  Kapılar: soru tuvali ve kitap sayfası düzeltme `manual-labeling` (Basic+), otomatik tespit `auto-detect` (Essential+; tuvalde
  düğmeler gizlenir, sayfa boş tuvalle açılır — `/question-detector-dev/predict` rol kapısında kalır), tuvalde ve etkinlik
  editöründe Claude `canvas-ai` (yalnız canlı çağrı sayılır, önbellekten dönen sayılmaz), hızlı ekleme `quick-add`, soru üretimi
  `question-generation` (taslak başına bir hak), kitap aktarımı yükleme `book-import` (yeniden deneme / sayfa değiştirme yalnız
  planda olmayı ister). Admin ve QuestionTrainer muaftır (`EntitlementRoles.UnlimitedByFeature`). İçerik ekibinde kalanlar:
  `/labeling` eğitim ekranı (send-to-fix, labeled-pages), kitap kataloğu yazımı, genel Claude prompt'ları. Free, #1090'a kadar
  `manual-labeling` + `auto-detect` taşır. Planında `ai-classification` olmayan öğretmen (Basic) toplu kayıtta her soru için konu
  seçer (soru → başlık → sayfa konusu sırasıyla çözülür); konusuz soru 400 `TopicRequired`, sınıflandırıcı çağrılmaz, kaynak
  `Human`. Arayüz: `EntitlementStateService` (`subscription-plans/me`, tek kopya, okunamazsa açık — sunucu karar verir),
  `entitlementGuard(feature)` (planda yoksa yükseltme penceresi; doğrudan adresle gelen panoya), 403 `entitlement.notInPlan`
  aynı yükseltme penceresini "Bu özellik planında yok" başlığıyla açar. Plan seçilince (deneme / ödeme) kapılar hemen açılır.
- **Öğretmen araçları ve iki taraflı özellikler (#1088):** yazma uçları plana bağlı, okuma / silme açık kalır (plan düşen öğretmen
  eskisini görür, temizler). `activity-editor`: tekil ve toplu soru kaydında çoktan seçmeli (`mcq`) dışı her tip (güncelleme de);
  tuvalde etkinlik tipi seçimi ve `/activity-editor` rotası yükseltme penceresini açar. `flashcard-sets`: öğretmen destesi
  oluşturma / düzenleme, kart ekleme / düzenleme, atama (öğrencinin kendi destesi serbest). `coach-panel`: öğretmen koç
  uçlarının tamamı ve `/teacher/coach`; öğrencinin hedefleri ve veli özeti açık. `print-test`: yalnız arayüz (`exam/:id/print`
  rotası, sunucu ucu yok). `tutoring`: müsaitlik aralığı / tekrar kuralı yazımı (`/availability`) ve iki taraflı randevu talebi /
  onayı; öğretmen profili (bağımsız öğretmen başvurusu) kapısız. İki taraflı (`ISharedEntitlementGate`): `messaging` (öğrenci ↔
  öğretmen mesaj gönderimi; okuma, engel, şikayet açık), `tutoring`, `live-lesson` (ders oturumu açma; katılımcı başına, ders
  başına bir hak, `live-lesson:{bookingId}:{userId}`; aynı derse yeniden katılım dolu sınıra takılmaz). Önce çağıran denetlenir
  (kendi engeli 403 `notInPlan` / 429 `limitReached`), sonra karşı taraf: 403 `entitlement.counterpartNotInPlan` + sabit sözlük
  mesajı (`Resources/entitlements.*.json`; karşı tarafın planı yanıtta yok). Rol muafiyeti yalnız çağırana uygulanır. Arayüzde
  bu yanıt yükseltme penceresi açmaz, mesaj bildirim olarak görünür (öğrencide karşı taraf öğretmen, öğretmende öğrencinin
  velisi yükseltir). Free yeni anahtarların hepsini #1090'a kadar sınırsız taşır.
- **Öğrencinin kendi çalışması plana bağlı (#1089):** öğretmenin atadığı iş (atama, ödev, sınıf denemesi) hiçbir zaman sayılmaz.
  `practice`: kendi seçtiği test (`start-test`, atama yokken) ve konu çalışması oturumu; `self-activity`: kendi seçtiği etkinlik
  (çalışma sayfası `Kind=Activity`); `mixed-mini`: karışık mini deneme. Üçü **başlarken** sayılır (`practice:worksheet:{id}:{userId}`,
  `{feature}:session:{id}`); dolu → 429, planda yok → 403. Günlük set açık. `study-tools` (planda var / yok): tekrar zamanı gelenler,
  kaydedilenler, yanlışlar (oturum ve liste), çalışma sayfaları, program ve kendi kart destesi yazımı; silme uçları açık kalır.
  `analytics`: konu performansı, gelişim zaman çizelgesi ve kazanım ustalığı; profilde kartların yerine `PlanLockedCardComponent`
  (karşılaştırma penceresini açar). Veli paneli **çocuğun** planına bakar: `GET /api/parent/children/{id}/plan-features`
  (bağlantı kapısı önce, audit `children.plan-features`), velinin konu performansı / kazanım uçları çocuğun planında yoksa 403.
- **Plan kilidi (#1090):** `Subscriptions:RequirePlan` (varsayılan `true`; AppHost, `docker-compose.yml` ve demo `false` verir, AppHost'ta
  `Subscriptions__RequirePlan=true dotnet run` ile açılır). Free'ye çözülen (ataması olmayan) **öğretmen / öğrenci** kilitlidir:
  `PlanRequiredMiddleware` (#441 yaptırımından sonra) yetkili her uçta 402 `{ code: "subscription.required", audience, message }`
  döner; `[AllowWithoutPlan]` (plan kataloğu / deneme / ödeme, kayıt) ve `[AllowWhenParentRequired]` uçları (oturum, profil, veli
  bağlama) açık. Admin, QuestionTrainer ve veli hiç kilitlenmez. **Denemeyi hiç kullanmamış** kullanıcı kilitlenmez, denemesi ilk
  istekte kendiliğinden başlar (kayıt sihirbazı, Google girişi, velinin açtığı çocuk hesabı); sihirbazın plan adımı "Denemeyle devam
  et" gösterir, kilit açıkken katalogda Free yoktur. `/me` `locked` döner. Arayüz: interceptor 402'de planı yeniler ve `/plans`'a
  yönlendirir (plan / abonelik sayfasında kalır); sayfa öğrenciye "velinin plan seçmesi gerekiyor", öğretmene "plan seç" der.
  Öğretmenin atama ekranı planı olmadığı için işi açamayan öğrencileri adlarıyla gösterir (`planLocked`, `planLockedCount`).
- **Veli öğrenci planını alır (#1091):** `GET /api/exam/parent/children/{studentId}/plan` çocuğun planını (yoksa `free`), bitişini,
  `isTrial`'ı, alınabilecek ücretli öğrenci planlarını ve öğrenci özelliklerini döner (audit `children.plan`);
  `POST …/plan/checkout { planKey }` #1028 gibi sahte ödemeyle planı çocuğa bir ay atar, plan kaydında aktör velidir (audit
  `children.plan-checkout`). Yalnız Active bağlantı (her veli); başka velinin çocuğu ve bekleyen bağlantı 404; öğretmen planı / deneme /
  Free 400. Öğrenci kendine alamaz: `POST /me/checkout` öğrenciye 403 `subscription.parentPays` (öğretmen / admin rolü de olan hesap
  alabilir). Arayüz: veli panelinde "Plan" sekmesi (`ParentChildPlanComponent`, bitişe ≤ 3 gün kala "yakında bitiyor"; seçici
  `childId` ile çocuk adına satın alır); öğrencinin `/plans` sayfası ve kayıt sihirbazı ücretli kartta "velin seçer" gösterir (seçici
  `parentPays`), velisi bağlı değilse "Veli bilgisi ekle" (`/parent-required`) sunar.
  **Hatırlatma:** `ParentPlanEndingReminderJob` (Hangfire `parent-plan-ending-reminder`, `ParentPlanEndingReminder:Cron` varsayılan saat
  başı, `LeadDays` 3) bitişine ≤ 3 gün kalan öğrenci planı / denemesi için her Active veliye `ParentChildPlanEndingEvent` yazar;
  atama başına bir kez (`UserSubscriptionPlans.EndingReminderSentAtUtc`, event'lerle aynı transaction). Velisi olmayan öğrenci
  işaretlenmez (sonradan bağlanan veli alır); ardından başlayan bir atama varsa (önceden yenilenmiş) hatırlatılmaz. BadgeService
  `ParentChildPlanEndingConsumer` velinin bildirimini yazar ("Plan bitiyor" / "Deneme süresi bitiyor", tarih İstanbul günü);
  bildirim `/parent?child=…&tab=plan` açar.
- **Sınıf bandı, kapsam, süre (#1108, epic #1118):** öğrenci planının fiyatı `SubscriptionPlanPrices`'tan (plan, band, isteğe bağlı
  sınıf, kapsam `AllSubjects` / `SingleSubject`, süre `Monthly` / `SixMonths` / `TwelveMonths` / `Summer`); kademe ve sınırlar
  planda kalır. Band öğrencinin sınıfından: 1–4 `Primary`, 5–7 `Middle`, 8 `Lgs`, 9–11 `HighSchool`, 12 `Yks`
  (`StudentPlanPricing.BandOf`). Sınıfa özel satır aynı seçeneğin band satırının önüne geçer. Satır yoksa seçenek satılmaz (Pro tek
  ders yok). Seed fiyatları yer tutucudur: band aylığı × 1 / 5,4 / 9 / 0,5, tek ders × 0,6. Tek ders yalnız sınav dersleri
  (`SingleSubjectCodes`) ve öğrencinin sınıfının dersi; yaz Haziran–Ağustos satılır, 1 Eylül'de biter. Kardeş indirimi %20: velinin
  Active bağlı başka çocuğunun şu an ücretli kişisel öğrenci planı varsa, veli ödediğinde. Veli ucu `plan` yanıtında `offer`
  (band, sınıf, fiyatlar, tek ders listesi, `summerOnSale`, `siblingDiscountPercent`) ve mevcut atamanın dersi döner; `checkout
  { planKey, subjectId?, duration? }` geçersiz seçenekte 400 `subscription.invalidOption`. Atama `ScopeSubjectId`, `Duration`,
  `PricePaid`, `DiscountPercent` taşır; mevcut atamalar aylık, bütün dersler. Sınıfı olmayan öğrenci eskisi gibi aylık, bütün
  dersler, `MonthlyPrice`. Tek ders kapsamının daraltması #1109'da. Admin plan düzenleyicisi öğrenci planında fiyat
  tablosunu (band / sınıf satırı × kapsam-süre) `prices` ile birlikte yazar: listede olmayan satır silinir, yeniden eklenen silinmiş
  satır canlanır (tekil indeks); geçersiz satır 400 `subscription.invalidPriceRow`.
- **Tek ders kapsamı (#1109, epic #1118):** `UserSubscriptionPlan.ScopeSubjectId` doluysa (`ResolvedPlan.ScopeSubjectId`; okul
  lisansı kapsamsız) öğrencinin **kendi** çalışması o derse daralır; öğretmen ataması, veli paneli ve raporlar daralmaz. Kapsam dışı
  istek 403 `entitlement.outOfScope` (`EntitlementLimitResponse.OutOfScope`, gövdede `scopeSubjectId`); arayüz yükseltme
  penceresini "Bu ders planında yok / Ders ekle" olarak açar (plan karşılaştırması yok). Yardımcı: `StudyScope.ForAsync`, servis
  katmanında `StudyScopeException`. Yapılan yüzeyler: pratik oturumu (kapsamsız istek o derse daralır; başka ders / konu / alt konu /
  çalışma sayfası 403; kayıtlı sorular oturumu yalnız o dersin soruları), `due-review` ve `mistakes` (ders verilmezse planın dersi,
  başka ders 403), günün soruları (set, eksiği dolduran havuz ve tekrar zamanı gelenler yalnız planın dersinden; plan kapsamı
  `ScopeJson`'a yazılmaz, öğrenci o dersin konusunu yine seçer; başka ders 403; gün içinde plan değişirse o günün seti kalır).
  Öğrencinin atanmadan açtığı test / etkinlik (sayfanın dersi; dersi olmayan sayfa kapsamda) ve deneme oturumu (oturumun bütün
  soruları planın dersinden olmalı: yalnız o dersin oturumu açılır) `TestSessionService.StartTestAsync`'te, hak sayımından önce
  denetlenir; atanan iş ve yarım oturuma dönüş daralmaz. Bilgi kartları: kendi destesi yalnız planın dersiyle (ya da derssiz)
  oluşturulur / düzenlenir; öğretmen destesi ve var olan destelerin çalışılması daralmaz. Çalışma sayfaları: öğrencinin listesi
  (`GET /api/study-items`) ders verilmezse planın dersine daralır, başka ders ve başka dersin sayfası 403. Sıradaki: soru havuzu.
- **Sınıf atlama (#1110, epic #1118):** Hangfire `student-grade-promotion` her gece 21:10 UTC (00:10 İstanbul) koşar, iş yalnız
  1 Ağustos'tan itibaren `StudentGradePromotion:CatchUpDays` (7) gün yapılır (sunucu kapalı kaldıysa telafi). 1 Ağustos'tan önce
  kaydolmuş, sınıfı 1–11 olan öğrenci bir üst sınıfa geçer (seviye sınıf kodundan / adından; üst sınıfı tanımlı değilse atlanmaz);
  12. sınıf yerinde kalır (YKS bandı). Öğrenci + yıl başına bir `StudentGradePromotions` satırı (tekil indeks) sınıf güncellemesiyle
  aynı transaction'da yazılır: ikinci tur hiçbir şey yapmaz. Ödenmiş dönem fiyatı değişmez; yeni bandın fiyatı yenilemede geçerli
  olur (teklif öğrencinin o anki sınıfından). Admin öğrenci listesinde "Sınıf atlamasını geri al" (`POST
  api/admin/students/{id}/grade-promotion/revert`): yalnız en yeni yılın, geri alınmamış atlaması ve öğrenci hâlâ o sınıftaysa
  (yoksa 404 / sınıf değiştiyse 409); satır kalır, o yıl yeniden atlatılmaz; audit `GradePromotionReverted`. Bildirim: atlamayla
  aynı transaction'da öğrenciye ve o an Active her veliye birer `StudentGradePromotedEvent` (tek alıcı / event, veli listesi
  `FOR SHARE` ile yeniden okunur); BadgeService `StudentGradePromotedConsumer` → `GradePromotedToStudent` (yalnız bilgi) /
  `GradePromotedToParent` (çocuğun paneline gider, bağ koparılınca nötrlenir). Geri almada bildirim yok. Yenileme uyarısı: Hangfire
  `plan-band-change-notice` (`PlanBandChangeNotice`: saat başı, `LeadDays` 30) bitişine 30 gün kalan ücretli öğrenci planında,
  plan başladıktan sonra geri alınmamış bir atlama varsa ve satın almadaki sınıfın bandı bugünkünden farklıysa her Active veliye
  `ParentChildPlanEndingEvent` (`NewGradeLevel` dolu → "Yenileme fiyatı değişiyor" metni, tip ve bağlantı aynı). Atama başına bir kez
  (`BandChangeNoticeSentAtUtc`); band aynıysa ya da okul lisansı sürüyorsa yalnız işaret; deneme ve velisiz öğrenci aday değil.
- **Satın alma koşulları (#1116, epic #1118):** iptal `POST api/subscription-plans/me/cancel` (öğretmen; öğrenci 403
  `subscription.parentPays`) ve `POST api/parent/children/{id}/plan/cancel` (her Active veli, audit `children.plan-cancel`). Yalnız
  ödenmiş (`PricePaid`) süren atama iptal edilir; deneme / admin ataması / okul lisansı 409 `subscription.nothingToCancel`, ikinci
  iptal 409 `subscription.alreadyCancelled`. Aylık ve cayma süresi geçmiş uzun plan: `CancelledAtUtc` yazılır, erişim `ValidToUtc`'ye
  kadar (yenilenmez). 6 / 12 ay / yaz planı satın almadan sonraki 14 gün (`PurchaseTermsPolicy.FullRefundDays`) içinde: plan hemen
  biter, `RefundAmount` = ödenen tutar (gerçek iade ödeme sağlayıcısıyla, canlıya geçişte). Plan yanıtları `purchase` taşır (süre,
  ödenen, iptal, tam iade sonu). Koşul metni ödeme ekranında (açık), Aboneliğim ve veli panelinde (kapalı kutu) ve Kullanım
  Koşulları § Planlar'da; maddeler birlikte güncellenir.
  **Fiyat değişikliği:** admin düzenleyicisinde satıştaki fiyatın artışı hemen geçmez: `PendingPrice` / `PendingFromUtc` (fiyat
  satırı) ya da `PendingMonthlyPrice` / `PendingMonthlyPriceFromUtc` (aylık plan fiyatı) olarak +30 gün (`PurchaseTermsPolicy.Apply`,
  `PriceIncreaseNoticeDays`) bekler; aynı artışı yeniden kaydetmek tarihi korur, farklı artış yeniden planlar, indirim / aynı fiyat hemen
  geçerli olup bekleyeni siler; yeni ya da canlanan satır hemen geçerli. Düzenleyici hücrede yeni fiyatı, altında bugünkü fiyat ve
  tarihi gösterir. `PlanPriceChangeJob` (`PlanPriceChange:Cron`, saat başı 40) süren satın almaların (ödenmiş, iptalsiz, bitmemiş,
  ardından atama yok, yaz değil) yenileme fiyatını bulur (öğrencinin bugünkü bandı / sınıfı, kapsam, süre; öğretmende ve sınıfsız
  öğrencide aylık fiyat), bekleyen artış varsa ödeyene `PlanPriceChangeNoticeEvent` yazar (Active veliler, yoksa öğrenci; öğretmen)
  ve atamaya `PriceChangeNoticeForUtc` = yürürlük tarihi işaretler (değişiklik başına bir kez); sonra vadesi gelen fiyatları geçirir.
  BadgeService `PlanPriceChangeNoticeConsumer`: in-app (`PlanPriceChangeToParent` → çocuğun Plan sekmesi, bağlantı kopunca
  nötrlenir; `PlanPriceChange` → Aboneliğim) + aynı metinle e-posta (`NotificationEventLog` `PlanPriceChangeEmail` ile tek).
  Ödenmiş dönemin fiyatı değişmez; yenileme yeni satın almadır. auth-api'ye ulaşılamazsa event e-postasız yazılır (yalnız in-app).
- **Okul lisansı (#1092):** özel anlaşmayla okulun bütün üyelerine plan: `SchoolLicenses` (okul başına tek satır; öğretmen ve öğrenci
  planı, varsayılan iki kitlenin Pro'su; başlangıç / hariç bitiş; anlaşma notu ≤ 200). Kimseye atama yazılmaz: plan çözümü
  (`EntitlementService.ResolvePlanAsync`, admin listesi `CurrentPlansAsync`, ortak kural `SchoolLicensePlans`) kullanıcının okuluna
  bakar (`UserSchoolResolver` kuralı: canlı öğretmen satırı → `Teachers.SchoolId` ve öğretmen planı; yoksa öğrencinin DOĞRULANMIŞ okulu
  ve öğrenci planı; belirsiz → lisans yok). Okula sonradan katılan (üyeliği onaylanınca) lisansı alır, ayrılan / okulsuz yapılan düşer.
  Sıra: kişisel atama lisans planından üst kademedeyse (`SubscriptionPlans.SortOrder` büyük) kişisel, değilse lisans; deneme ve Free
  her ücretli plandan aşağıdadır. Lisans bitince üye kendi planına, yoksa Free'ye / kilide (#1090) döner; pasif plan lisans vermez.
  `/me` ve veli `children/{id}/plan` `schoolLicense` döner ("Okulunun lisansıyla"). #1091 hatırlatması lisans atamanın bitişinden sonra
  da sürüyorsa yazılmaz (atama yine işaretlenir). Admin: `/admin/schools` satır menüsü "Lisans" → `SchoolLicenseDialogComponent`;
  `GET|PUT /api/exam/admin/subscription-plans/schools/{id}/license` (oluştur / uzat / değiştir), `POST …/license/end` (şimdi bitirir);
  her değişiklik `SchoolLicenseAudits` (Created / Updated / Ended, aktör, planlar, tarihler, not), diyalogda son 20 kayıt.
- **Plan seçimi (#1028):** kayıt sihirbazının (`ui` `/register`) öğrenci / öğretmen kaydından sonra 3. adımı plan kartlarıdır
  (`PlanPickerComponent`, scope `subscription`); veli adımı atlar. "Ücretsiz devam et" API çağırmaz (Free). "Denemeyi başlat"
  → `POST /api/exam/subscription-plans/me/trial`: hesap başına bir kez (silinmiş atamalar dahil; ikincisi 409
  `subscription.trialUsed`), `TrialDays` sonra biter; `/me` `trialAvailable` döner, kart yalnız Free'deyken önerir. Ücretli plan →
  açıkça "demo" işaretli ödeme ekranı (kart alanı yok) → `POST …/me/checkout { planKey }` şimdiden bir ay atar; Free / deneme /
  rolüne kapalı plan 400 `subscription.planNotAvailable`. Atama öncekini kapatır (geçmiş kalır, `CreateUserId` = kullanıcı).
  Plan adımı bitince önceki akış aynen sürer (onay bekleme sayfası / okul onayı kartı / tam sayfa yönlendirme). Sonradan
  yükseltme: `/plans` (Teacher / Student / Admin; Aboneliğim #1029 buraya bağlanır).
- **Aboneliğim ve sınır penceresi (#1029):** profil menüsünde "Aboneliğim" (`/subscription`, Teacher / Student / Admin; sözlükte
  "Planım" çalışma planına ayrılmış): `/me`'den plan, geçerlilik / deneme bitişi, özellik başına "kullanılan / sınır" ve sayaçların
  sıfırlanma tarihi; "Planı yükselt" → `/plans`. Sınırına ulaşılan her uç nokta **tek biçim** döner:
  `EntitlementLimitResponse.Create(featureKey, check, message)` → 429 `{ code: "entitlement.limitReached", feature, limit, used,
  resetsAtUtc, planKey, message }`. Arayüzde `entitlementLimitInterceptor` bu yanıtı yakalayıp `UpgradeDialogComponent`'i açar
  (aynı anda tek pencere; planların o özellikteki sınırı karşılaştırılır); hata çağırana da iletilir. Yeni sınırlı uç nokta
  (#1031) yalnız bu yardımcıyı döndürür, sayfaya ayrıca pencere kodu yazılmaz.
- **Sınırların uygulanması (#1031):** **`ai-classification`** kapısı soru kaydındadır: `POST /api/exam/questions` (yeni soru) ve
  `POST /api/exam/questions/save` çağıranı sahip olarak planına yazar (soru başına bir `question:{id}` kullanımı, ilk N soru). Sınır doluysa
  soru kaydedilir, `QuestionCreatedEvent` yazılmaz (AI çağrısı yok), yanıtta `classificationSkipped` döner; arayüz bildirimi
  (`ClassificationLimitNotice`) "Planı yükselt" ile gösterir (kayıt başarılı olduğundan 429 değil). Admin ve QuestionTrainer sınırsız.
  **Kitap ve hızlı ekleme taslakları sayılmaz** (`classificationOwner` null): maliyetleri `book-import` / `quick-add`'e yazılır,
  yoksa kitap başına yüzlerce soru `ai-classification` hakkını bitirirdi. **`mock-exam`** kapısı `start-test`'tedir, yalnız yeni oturum
  açan ve **atanmamış** sınıfa açık deneme için: admin'in seçtiği **sınıfın ücretsiz denemesi** (`MockExam.IsFreeTrial`, `PUT
  /api/exam/mock-exams/{id}/free-trial`, deneme kurma ekranı; yayımlanmış + sınıfa açık, sınıf başına tek) serbest; deneme başına bir
  kullanım (`mock-exam:{examId}:{userId}`, bütün oturumlar tek sayılır); dolu → 429 ortak yanıt. Süren oturuma dönmek ve geçmiş sonuç
  etkilenmez. **Kim öder:** öğretmenin atadığı işte öğrencinin planı kullanılmaz; ileride ödevde AI kullanan bir özellik eklenirse
  `RecordUsageAsync` öğretmenin kullanıcı id'sini yazar. Bugün öğrencinin tetiklediği ölçülü AI özelliği yok, atanan deneme de
  sayılmaz (öğretmen planında `mock-exam` sınırı yok). `book-import` kapısı #1019'dan beri yüklemededir.
- **Genişletme (abonelik epic'i):** yeni özellik = `EntitlementFeatures`'a sabit + plan başına sınır satırı + kullanım anında
  `RecordUsageAsync`, karar anında `CheckAsync`. Yeni planlar (#1027) satır ekler; plan tablolarında özelliğe özgü mantık yok.
