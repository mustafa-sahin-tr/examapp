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
Test ve etkinlik seçilen dışa aktarımda kaynak adı her zaman üretilir: seçim kaynakta tek dosyadır, aynı adla ikinci aktarım öncekini ezerdi. Paket sınırları: 2000 kayıt, kayıt başına 1000 madde, metin alanı 4000, anahtar 200 karakter. Süre 0-24 saate çekilir, etkinlikte süre ve çalışma testi sıfırlanır. Kapak `worksheets` bucket'ına yazılır. Tek kaydın hatası (depo dahil) yalnız o kaydı durdurur; ayrıntı yanıta değil günlüğe yazılır. Hedefte silinen kayıt yeniden gelmez, "daha önce alındı" sayılır: harita satırı kalır, silme kararına saygı duyulur.
