# Devam notu: soru, test ve etkinlik aktarımı

Geçici dosya. Diğer bilgisayarda devam etmek için yazıldı (Claude memory'si makineler arasında taşınmaz). **PR açmadan önce sil.**

Claude'a başlangıç talimatı: "`docs/handoff-worksheet-transfer.md` notunu oku, 4. adımdan devam et."

## Branch'ler (birbirinin üstünde)

| Branch | Durum |
|---|---|
| `feat/question-transfer-hash` | PR #755 açık, `no-auto-merge`, CI yeşil. Birleştirme kullanıcıda. |
| `feat/question-transfer-screen` | Push edildi, PR yok. PR için kullanıcıdan izin iste, `no-auto-merge` ile aç. |
| `feat/worksheet-transfer` | Bu branch, yarım iş. PR yok. |

Her şey #754 sonrası master'ın üstünde. Sıra: #755 birleşir, ekran PR'ı açılır, sonra bu branch.

## Test durumu

- `feat/question-transfer-screen`: entegrasyon 43/43. Birim 6738/6739: tek hata `TestSessionServiceTimeLimitTests.The_instance_snapshot_wins_over_a_later_worksheet_change`, süre testi yükte 2 sn kaçırdı, tek başına 3 kez geçti, ilgisiz. Arayüz 4627 test geçti; `image-selector.component.spec.ts` tam koşuda yüklenemedi, tek başına 178/178 geçti. Bu makine yükte zorlanıyordu.
- `feat/worksheet-transfer`: yalnız `FullyQualifiedName~QuestionTransfer` filtresi koşuldu. **Test ve etkinlik taşımayı sınayan test henüz yok.**

## 4. adım: test ve etkinlik taşıma

Kullanıcının isteği: test (sınav) taşınınca içindeki sorular da gelir; soru hedefte varsa onun id'si kullanılır. Deneme (#693) henüz yok, gelince pakete eklenir.

Onaylanan kurallar:
1. Eksik soru varsa (çakışan, hatalı, yayından kalkmış, lisansı bitmiş) test hiç alınmaz, nedeni raporlanır, diğer sorular yine alınır.
2. Test kapalı gelir: `Private` + `Restricted`, sahibi içe alan admin.
3. İkinci kez gelen test güncellenmez, "daha önce alındı" sayılır.
4. Sınıf hedefte yoksa test alınmaz; ders / konu bulunamazsa boş kalır ve sayılır.
5. Açık uçlu soru içeren kayıt test olarak alınmaz.
6. Taşınmaz: atama, oturum, sonuç, yorum, erişim izni, hatırlatma, kitap-test bağı, şablon.

### Yapıldı (bu branch, sunucu tarafı)
- `WorksheetTransferImportMap` + migration `AddWorksheetTransferImportMap`.
- `StartQuestionExportDto.WorksheetIds`; dışa aktarma maddelerin sorularını toplar, `worksheets.zip` yazar.
- `QuestionTransferWorksheetImporter`: içe aktarma ve önizleme. Önizleme ve iş sonucunda `worksheets` sayıları.
- `GET api/question-transfer/worksheets` (seçim listesi, admin).
- Paket birleştirme controller'dan `QuestionTransferService.WritePackageAsync`'e taşındı, manifest `version` 4.

### Kalan
1. **Testler.** `QuestionTransferRoundTripTests` üzerine, gerçek paket `WritePackageAsync` ile üretilerek: test taşınır ve sıra korunur; var olan soru id'si kullanılır, kopya açılmaz; çakışan soruda test alınmaz ama diğer sorular alınır; ikinci içe alma güncellemez; sahip ve görünürlük; sınıf yoksa; açık uçlu soru; önizleme sayıları içe aktarmayla aynı; sorunun sahipliği `SetCurrentUser` yüzünden değişmiyor; dışa aktarım listesi.
2. **Dokümantasyon:** `docs/feature-notes.md`.
3. **Kod ve güvenlik incelemesi** (paralel, sonra düzelt). Bu branch hiç incelenmedi.
4. **Ekran** (ayrı PR): dışa aktarma kartına "Testler ve etkinlikler" seçimi; `WorksheetIds` gönderimi; önizleme ve iş sonucunda test sayıları + `blockedSamples`; `tr` / `en` çevirileri + spec.
5. Test sırasında kullanılan `ListWorksheetsAsync` içindeki `w.Subject.Name` null davranışını gerçek Postgres'te doğrula.

## Yapılmamış kontroller
- **Canlı deneme hiç yapılmadı.** Yerel test hesaplarının ortak dev şifresi kayıtlı değil, kullanıcıdan iste. Base URL yalnız `localhost:5678` (gateway).
- **Gateway yükleme sınırı:** gateway'de `MaxRequestBodySize` yok; varsayılan 30 MB ise görselli paketler reddedilebilir. Canlıda doğrula, gerekirse gateway'de yükselt.

## Bilinen açıklar (rapor edildi, düzeltilmedi)
- Her dışa aktarma MinIO'da kalıcı kopya bırakır, otomatik silme yok. "Tümünü dışa aktar" onaysız. Ayrı temizlik işi gerekir.
- Önizleme soru başına birkaç sorgu çalıştırır, her önizleme hash işi kuyruğa atar.
- Eski paket, hedefte yeniden yayına alınmış soruyu tekrar yayından kaldırabilir (`exportedAtUtc` karşılaştırılmıyor).
- Paket başına toplam açılan bayt sınırı yok, manifestte tekrar eden `id` reddedilmiyor.
- API'den verilen `sourceKey` doğrulanmıyor (önceden var, ekran kullanmıyor).
- A'dan B'ye giden test B'den A'ya dönerse A'da kopya oluşur.

## Yerel kurulum
- Parmak izi biçimi branch boyunca değişti (`FormatVersion` 3). Önceki commit'leri çalıştırmış yerel veritabanında `Questions.ContentHash` ve `ContentFingerprint` sıfırlanmalı, yoksa sahte çakışma görünür.
- Yeni migration'lar exam API açılışında uygulanır.
- Aspire DLL kilitlerse: `dotnet test ... --artifacts-path bin/test-artifacts` (yol repo içinde olmalı, fixture testleri repo kökünü arar).
- UI testi: `CHROME_BIN="C:/Program Files/Google/Chrome/Application/chrome.exe" npx ng test --watch=false`.

## Sonraki adımlar
5. **MEB çıkarımı:** 175 TYMM programı ilk bilgisayarın Temp klasöründe, git'te yok; `mufredat.meb.gov.tr/Programlar.aspx`'ten yeniden indirilir. Program ailesi başına, örnek kontrolüyle. Değersiz program ancak gerekçeyle dışarıda kalır, kullanıcı karar verir. Pilot dosyası 23 satır MAT.5.
6. Görselsiz (metin) soru üretimi, ardından #693.

## Çalışma kuralları (memory'den)
- Master'da çalışma, önce branch. Commit ve branch push serbest; **PR açmadan önce izin iste**, CI tetikler ve CI yeşilse otomatik merge olur (istisna `no-auto-merge`). Merge kullanıcıda.
- Karar sorularında önerini uygula, raporda listele. Geliştirme + test sonrası `code-reviewer` ve `security-reviewer` paralel çalıştır, bulguları düzelt.
- Kullanıcı istemeden yerel veritabanı şifresi deneme.
