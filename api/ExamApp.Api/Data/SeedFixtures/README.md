# SeedFixtures — test verisi kaynak dosyaları (issue #216)

Bu klasördeki **README dışında hiçbir dosya git'e girmez** (`.gitignore`). Buraya `seed-schools`
aracının indirdiği okul listesi önbelleklenir. Repo herkese açık olduğu için MEB türevi liste
repoya commit edilmez; araç dosyayı sabit bir commit'ten indirir.

## Okul listesi (`meb-okullar-0f2b3e6af008.csv`)

| | |
|---|---|
| Kaynak | `github.com/dalgali/MEB-okul-listesi` → `data/meb-okullar.csv` |
| Lisans | MIT (Copyright (c) 2026 cemaldalgali). MEB resmî okul dizininin üçüncü taraf kopyası; veri MEB'e aittir. |
| Sabit commit | `0f2b3e6af008f283c1f9b43bdcbd1556eab8da1f` (çekim tarihi 2026-08-31) |
| Ham URL | `https://raw.githubusercontent.com/dalgali/MEB-okul-listesi/0f2b3e6af008f283c1f9b43bdcbd1556eab8da1f/data/meb-okullar.csv` |
| SHA-256 | `710fae0e71df551e58827e06d3a1be7c8ff1a1bf2ba88f8707bb1a7645e2c9a4` (9.300.250 bayt, ~55.216 satır) |
| Format | UTF-8 (BOM'lu), CRLF, sütunlar `il,ilce,okul_adi,kurum_kodu,okul_turu,web_sitesi,adres,telefon,harita,cekim_tarihi` |

Sabitler kodda: `Services/Schools/Seed/GitHubSchoolSeedSourceProvider.cs`
(`SourceCommitSha`, `SourceUrl`, `ExpectedSha256`). İndirilen dosyanın hash'i `ExpectedSha256` ile
uyuşmazsa araç durur. Önbellekteki dosyanın hash'i uyuşmuyorsa yeniden indirilir. Kaynak
güncellenecekse: yeni commit SHA + yeni hash birlikte değiştirilir, önbellek dosya adı otomatik değişir.

**MEB alan adlarına istek atılmaz** (robots.txt AI ajanlarını yasaklıyor). Tek ağ adresi
`raw.githubusercontent.com`. Veri yenilenecekse upstream depodaki betiği bir insan çalıştırmalı.

## Kullanım

`api/ExamApp.Api` dizininden, **yalnızca Development/Staging** ortamında (aksi halde çıkış kodu 2):

```bash
cd api/ExamApp.Api

# Ne olacağını gör, hiçbir şey yazma
dotnet run -- seed-schools --dry-run

# Kars için ada göre ilk 5 okul
dotnet run -- seed-schools --provinces Kars --limit 5

# 10 ilin tamamı, İmam Hatip Ortaokulları da dahil (issue #209 kararı)
dotnet run -- seed-schools --include-imam-hatip

# Sadece ortaokullar, iki il
dotnet run -- seed-schools --provinces "İstanbul,Ankara" --types ortaokul

# Aspire'ın ürettiği yerel Postgres (appsettings'teki docker-compose adresi çözülmez) —
# tercih edilen yol ortam değişkeni (bağlantı dizesi kabuk geçmişine/komut listesine düşmez):
ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=<port>;Database=worksheet;Username=examuser;Password='<pw>'"   dotnet run -- seed-schools --dry-run
# alternatif: --connection "<aynı dize>"
```

Yerel API çalışırken `bin/Debug` kilitliyse `dotnet run -c Release -- seed-schools ...` kullan.
`dotnet run` `Properties/launchSettings.json`'daki `ASPNETCORE_ENVIRONMENT=Development`'ı uygular;
kabuktan farklı ortam vermek için `--no-launch-profile` gerekir.

**Açılış adımları:** komut modu varsayılan olarak uygulamanın normal açılışındaki gibi bekleyen
migration'ları ve il/ilçe referans seed'ini (`ReferenceDataSeed`) uygular; `--no-migrate` ile ikisi de
atlanır. Ortam kontrolü host kurulmadan, bu adımlardan ÖNCE yapılır — Production'da hedef veritabanına
hiç dokunulmaz.

| Seçenek | Varsayılan | Açıklama |
|---|---|---|
| `--provinces a,b,...` | İstanbul, Ankara, İzmir, Erzincan, Trabzon, Adana, Mersin, Antalya, Mardin, Kars | İl listesi; `Province` tablosuyla Türkçe büyük/küçük harf ve İ/I duyarsız eşleşir |
| `--limit N` | sınırsız | İl başına en fazla N okul — normalize ada göre (tr-TR) sıralı ilk N, deterministik |
| `--types ilkokul,ortaokul` | ikisi | Tür seçimi |
| `--include-imam-hatip` | kapalı | `İmam Hatip Ortaokulu` türünü Ortaokul olarak dahil et |
| `--dry-run` | kapalı | Yazma; rapor "eklenecek" sayılarını gösterir |
| `--connection` | `ConnectionStrings:DefaultConnection` | Bağlantı override'ı (tercih: `ConnectionStrings__DefaultConnection` env var) |
| `--no-migrate` | kapalı | Bekleyen migration'ları ve il/ilçe referans seed'ini atla |

Çıkış kodları: 0 başarılı, 1 hatalı kullanım, 2 ortam reddi, 3 kaynak/çalışma hatası.

## İdempotency

Anahtar `Schools.ExternalCode` (= MEB kurum kodu, filtreli unique index; soft-delete edilmiş satırlar
dahil — bunlar raporda `softDeleted` olarak ayrı sayılır, yeniden eklenmez). Kodu olmayan satır için
ad + il + ilçe. Ad 200 / kod 32 karakteri aşan satırlar `uzun` sayacıyla atlanır. İkinci koşu 0 yeni
kayıt üretir. İçe aktarılan
kayıtlarda `IsSeedData = true` — elle açılan okullardan ayırt etmek ve gerekirse toplu temizlemek için
(`DELETE FROM "Schools" WHERE "IsSeedData"` — öğrenci/öğretmen FK'leri varsa önce onlar).

## Filtre ve normalizasyon kuralları

- Tür: `okul_turu ∈ {İlkokul, Ortaokul}`; `--include-imam-hatip` ile `İmam Hatip Ortaokulu` → Ortaokul.
  Yatılı Bölge Ortaokulları kaynakta zaten `Ortaokul`.
- İlçesi `Büyükşehir` olan satır atlanır (kaynakta sadece öğretmenevleri için var; tür filtresi zaten eliyor, kural yine de uygulanır ve raporlanır).
- Adında `Özel Eğitim` ya da `Uygulama Merkezi` geçen ama türü İlkokul/Ortaokul görünen kayıtlar atlanır
  (yanlış sınıflanmış; 10 ilde 6 kayıt).
- Ad normalizasyonu (`SchoolNameNormalizer`): fazla boşluk temizlenir; tamamı büyük ya da tamamı küçük harf
  adlar tr-TR Title Case'e çevrilir (`KARATAŞ İLKOKULU` → `Karataş İlkokulu`, `FATMA-YUSUF BİLGİÇ İLKOKULU` →
  `Fatma-Yusuf Bilgiç İlkokulu`); karışık yazımlı adlarda yalnızca `ilkokulu/ortaokulu/imam/hatip` kelimeleri
  düzeltilir (`Kapıkaya ilkokulu` → `Kapıkaya İlkokulu`), `TOKİ`, `DSİ`, `T.E.K.` gibi kısaltmalara dokunulmaz.
  Karışık adlardaki tamamı büyük soyadları (`Ahmet KABAKLI İlkokulu`) kasıtlı olarak olduğu gibi bırakılır.
  **Bilinen sınırlama:** tamamı büyük harf bir adda Roma rakamı varsa Title Case onu da bozar
  (`II. ABDÜLHAMİD HAN ORTAOKULU` → `Ii. Abdülhamid Han Ortaokulu`); bu commit'teki 10 il verisinde
  böyle bir kayıt yok, ileride görülürse normalizer'a istisna eklenir.
- Eşleşmeyen il/ilçe **raporlanır** (`UnmatchedProvinces`, `UnmatchedDistricts`), sessizce atlanmaz.
  Bu commit'teki veriyle 10 ilin 186 ilçesinin tamamı `ReferenceDataSeed` ile eşleşir.

## Veri kalitesi notları (2026-09-21 araştırması, 10 il)

- 55.216 satır toplam; 10 il × {İlkokul, Ortaokul, İHO} = 8.813 satır (5.000 + 3.229 + 584).
- Kurum kodu her satırda dolu ve benzersiz; ad+il+ilçe de benzersiz.
- 84 ad tamamı büyük harf, ~180 adda küçük harfli son ek ya da büyük harfli soyad/kısaltma.
- Kolon sınırlarını aşan kayıt yok (en uzun ad 87, kurum kodu 6 karakter).
- Özel okul dizinde pratikte yok. `okul_turu` ad'dan türetilmiş (upstream README).
- "Bakanlık / Merkeze Bağlı Taşra" satırları il değildir; il filtresinde zaten dışarıda kalır.

## `seed-teachers` — okula bağlı öğretmen hesapları (issue #217)

`seed-schools` ile açılmış (`Schools.IsSeedData = true`) okullar için öğretmen hesabı üretir. Her hesap üç
yerde doğar: Keycloak kullanıcısı (rol `Teacher`, `school_id` attribute'u), identity DB `Users`
(`IsSeedData = true`), exam DB `Teachers` (`SchoolId`, `ApprovalStatus = Approved`, `IsSeedData = true`,
branş `TeacherSubjects`). Ortaokul: 2 Türkçe, 2 Matematik, 2 Fen Bilimleri, 2 Sosyal Bilgiler, 1 İngilizce,
1 Din Kültürü (10). İlkokul: 1'er Türkçe/Matematik/Fen/Sosyal/İngilizce (5). Tür okul adından çıkarılır
(`…Ortaokulu` / `…İlkokulu`; İmam Hatip Ortaokulu = Ortaokul), çıkarılamayan okul raporlanıp atlanır.

```bash
cd api/ExamApp.Api
SeedData__Password='<parola>' dotnet run -- seed-teachers --provinces Kars --limit-schools-per-province 2 --dry-run
SeedData__Password='<parola>' dotnet run -- seed-teachers --provinces Kars --limit-schools-per-province 2
```

### Ön koşullar

| Gereksinim | Neden |
|---|---|
| Ortam `Development` ya da `Staging` | Program.cs host kurulmadan reddeder (exit 2); servisler Production'da DI'a bile kaydedilmez |
| **auth-api ayakta ve bu sürümü içeriyor** (`POST /api/auth/dev/seed-users`) | Keycloak + identity yazımını auth-api yapar; eski build 404 döner |
| `AuthApiBaseUrl` → **doğrudan auth-api** (örn. `http://localhost:6079`), gateway DEĞİL | Gateway `/api/auth/dev/*` yolunu bilerek engeller (aşağıya bakınız) |
| `Keycloak:AdminClientId/Secret` (exam API) → client_credentials servis token'ı | auth-api ucu yalnızca `Service` policy ile (`exam-service` rolü ya da auth-api `Keycloak:ServiceClients` listesi; varsayılan `exam-admin`) |
| `SeedData:Password` | Ortak parola; **yalnızca** ortam değişkeni `SeedData__Password` ya da `dotnet user-secrets set "SeedData:Password" "<parola>"` (api/ExamApp.Api dizininde). `.env` dosyası bu komut için okunmaz. Koda/commit'e girmez. |
| `Subjects` tablosunda 6 branş adı (TopicSeed) | Yoksa komut hangi branşların eksik olduğunu söyleyip durur |

E-posta deseni (deterministik, idempotency ve #218 temizliği için anahtar):
`seed.t.<kurumKodu>.<brans>.<n>@seed.examapp.local` (`brans ∈ turkce|matematik|fen|sosyal|ingilizce|din`).
Ad/soyad uydurma havuzdan e-postanın SHA-256'sı ile seçilir; tekrar koşu aynı adı verir. auth-api yalnızca
`seed.*@seed.examapp.local` e-postalarını kabul eder (`ExamApp.Foundation.Security.SeedDataConventions`);
gerçek bir kullanıcı e-postası 400 ile reddedilir, hiçbir yazma yapılmaz. Keycloak'ta zaten var olan bir hesap için üç durum:

| Identity'de | Sonuç | Ne yapılır |
|---|---|---|
| `IsSeedData = true` satır | `Existing` | Eksik roller ve `school_id` onarılır; parola yalnızca `--reset-password` ile |
| Hiç satır yok (**yetim** — önceki koşu Keycloak'tan sonra kesilmiş) VE Keycloak hesabı `seed_origin=examapp-seed` attribute'unu taşıyor | `Adopted` | E-posta bu koşunun deterministik planında olduğu için sahiplenilir: roller/`school_id`/`seed_origin` onarılır, identity `User` (IsSeedData) ve exam `Teacher` açılır. Parola yalnızca `--reset-password` ile |
| Hiç satır yok ama `seed_origin` işareti YOK | `SkippedForeign` | Seed aracı açmamış olabilir (bkz. sahiplik kilidi); yalnızca tek seferlik `--adopt-unmarked` ile sahiplenilir |
| `IsSeedData = false` satır (elle açılmış) | `SkippedForeign` | Hiçbir sisteme dokunulmaz; raporda hata sayılır (#217 güvenlik kararı) |

Plan dışı (bu koşunun e-posta listesinde olmayan) seed-domain Keycloak hesapları `seed-teachers` tarafından hiç
görülmez; onlar için `seed-cleanup --include-orphans`.

**Sahiplik kilidi (`seed_origin`):** seed aracı açtığı her Keycloak kullanıcısına `seed_origin=examapp-seed` attribute'unu
yazar (`SeedDataConventions.KeycloakOriginAttribute`; tutor'lar dahil). Identity'si olmayan seed-desenli bir Keycloak hesabı
başka yoldan da açılmış olabilir (Keycloak self-registration / admin konsolu; auth-api `register` ucu seed alanını 400 ile
reddeder ama Keycloak'ın kendi kayıt formu reddetmez). Bu yüzden adoptasyon (`seed-teachers`/`seed-tutors`) ve yetim silme
(`seed-cleanup --include-orphans`) **yalnızca işaretli** hesaplara uygulanır; işaretsiz yetim her iki araçta da
`SkippedForeign`. `Existing` hesaplarda eksik işaret her koşuda tamamlanır. `--adopt-unmarked`: işaret eklenmeden önce
açılmış yetimleri (2026-09-22 incident'ı) sahiplenip işaretlemek için **tek seferlik, yalnızca incident temizliği** bayrağı;
normal koşularda kullanılmaz. Realm'in user-profile'ı bilinmeyen attribute'a izin vermeli (`realm-export.json`:
`unmanagedAttributePolicy: ADMIN_EDIT`); yerel volume bundan sapmışsa Keycloak `seed_origin`'i (ve `school_id`'yi) sessizce
düşürür ve kilit hiçbir hesabı işaretli görmez — önce realm'i export'la hizalayın.

**Parola uyarısı:** `Existing`/`Adopted` hesapların Keycloak parolası, önceki koşu farklı bir `SeedData:Password` ile
yapıldıysa bugünkü değerden farklıdır — komut değiştirmez, raporun sonunda uyarır. Eşitlemek için `--reset-password`
(Keycloak `PUT /users/{id}/reset-password`, kalıcı parola; yalnızca mevcut/adopt edilenlere uygulanır, yeni açılanlar
zaten bu parolayla doğar).

### Seçenekler

| Seçenek | Açıklama |
|---|---|
| `--provinces a,b` | Hangi illerin seed okulları (varsayılan: 10 il) |
| `--limit-schools-per-province N` | İl başına ada göre (tr-TR) sıralı ilk N okul |
| `--dry-run` | auth-api çağrılmaz, yazılmaz; plan ve hesap listesi raporlanır (parola gerekmez) |
| `--keycloak-mode admin-api\|partial-import` | Aşağıdaki ölçüme göre seçin (varsayılan `admin-api`) |
| `--batch-size N` | auth-api'ye istek başına hesap (varsayılan 100, en fazla 500). Ölçüm notu aşağıda |
| `--reset-password` | Keycloak'ta zaten var olan (`Existing`/`Adopted`) seed hesaplarının parolasını bu koşunun `SeedData:Password` değeriyle sıfırla (varsayılan kapalı) |
| `--adopt-unmarked` | TEK SEFERLİK incident temizliği: `seed_origin` işareti taşımayan yetimleri de sahiplen ve işaretle (varsayılan kapalı; normal koşuda kullanma) |
| `--no-events` | `UserPreferredLocaleChangedEvent` outbox satırlarını yazma (BadgeService dil tercihi varsayılana düşer; hacim için) |
| `--no-migrate`, `--connection` | `seed-schools` ile aynı |

Çıkış kodları: 0 tamam, 1 kullanım, 2 ortam reddi, 3 hata **(kısmi başarı dahil — bir hesap bile başarısızsa 3;
tamamlanan partiler kalıcıdır, tekrar koşu eksikleri tamamlar)**.

### Keycloak yazma yolu ve ölçüm (yerel Aspire, Keycloak 26.7, 2026-09-22)

| Mod | İstek / hesap | Ölçülen | ≈ ms/hesap | 86k hesap (10 il tam kapsam) tahmini |
|---|---|---|---|---|
| `admin-api` | `POST /users` + `POST role-mappings` (admin token parti başına cache) | 15 hesap → 2 743 ms | **≈180** | ≈ 4,3 saat yalnız Keycloak |
| `partial-import` | parti başına tek `POST /partialImport` (SKIP), önceden hash'lenmiş `pbkdf2-sha512` parola, `realmRoles` = `Teacher` + realm default rolü | 20 hesap → 442 ms | **≈20–30** | ≈ 35–45 dk Keycloak; identity/exam DB ile toplam ≈ 1–1,5 saat |

Küçük örneklem için `admin-api` yeterli; tam kapsam için `--keycloak-mode partial-import --batch-size 500`
(gerekirse `--no-events`). Partial import'ta Keycloak parolayı verildiği hash ile saklar ve ilk başarılı
login'de realm politikasına (argon2) yeniden hash'ler — beklenen davranış. İkisi de idempotenttir; tekrar
koşu Keycloak/identity/exam'de mevcut kaydı bulur ve kopya açmaz.

### HTTP zaman aşımı / yeniden deneme (resilience) — neden özel ayar var

`ServiceDefaults.AddServiceDefaults()` **tüm** `HttpClient`'lara Aspire'ın standart resilience handler'ını ekler:
deneme başına 10 sn zaman aşımı + 3 yeniden deneme, toplam 30 sn. `client.Timeout` bunu **ezmez**. 500'lük bir
partial-import isteği 10 sn'yi aşınca exam API isteği iptal edip aynı partiyi yeniden gönderiyordu; Keycloak ilk
transaction'ı bitirdiği için ikinci istek 409 "Duplicate resource error" aldı ve identity/exam yazılmadan binlerce
yetim Keycloak kullanıcısı kaldı (2026-09-22 Kars+Erzincan koşusu: 4.480 yetim). Seed uçları idempotent değildir,
bu yüzden iki client standart handler'dan **muaf** tutulur (`RemoveAllResilienceHandlers()`, yeniden deneme yok,
tek sınır 30 dk `Timeout`):

| Client | Nerede | Kapsam |
|---|---|---|
| `AuthApiSeedClient` (exam API → auth-api) | `TeacherSeedServiceCollectionExtensions` | seed-users / cleanup uçları |
| `KeycloakAdmin` (auth-api → Keycloak) | auth-api `Program.cs` + `KeycloakService.AdminHttpClientName` | partialImport, sayfalı arama, rol/attribute/parola onarımı, silme. Login/token akışları varsayılan client'ta kalır (resilience açık) |

`--batch-size` ölçüm notu: parti boyutu artık zaman aşımıyla sınırlı değil; Keycloak `partialImport` 500 kullanıcıda
tek transaction'dır ve yerel Aspire'da ≈ 10–20 sn sürer (yukarıdaki ≈ 20–30 ms/hesap). Daha küçük parti = daha çok
istek, daha küçük transaction; 100–500 arası fark yalnızca toplam sürede görülür. Testler
(`TeacherSeedHttpClientResilienceTests`, auth-api'de `KeycloakAdminHttpClientTests`): named client'ın pipeline'ında
`ResilienceHandler` yok; testin kendi kısa süreli standart handler'ıyla (attempt 1 sn) 1,5 sn süren tek istek iptal
edilmeden ve tekrarlanmadan tamamlanır, kontrol client'ında aynı istek iptal edilip yenilenir.

### Gateway

`Services/Gateway/ocelot*.json` içinde `/api/auth/dev/{everything}` yolu `/api/auth/{everything}` route'undan
önce (Priority 2) tanımlıdır ve auth-api'de var olmayan `/__blocked/...` yoluna gider → gateway üzerinden
her zaman **404**. Dev ucu dışarıdan erişilemez; komut `AuthApiBaseUrl` ile auth-api'ye doğrudan gider.

### Bilinen yerel ortam notu

Yerel Keycloak volume'u `deploy/keycloak/import/realm-export.json`'dan sapmışsa (`school_id` protocol
mapper'ı / user-profile attribute'u yoksa) Keycloak `school_id` attribute'unu sessizce düşürür ve JWT'de
`school_id` claim'i çıkmaz — bu, normal register akışı için de geçerlidir; çözüm realm'i yeniden import etmektir.

## `seed-tutors` — bağımsız öğretmen hesapları (issue #218)

`seed-teachers` bittikten SONRA koşar. İl + branş bazında, seçilen illerin seed okullarındaki seed öğretmen
sayısının **yarısı** (`floor(n/2)`: 400 → 200, 5 → 2, 1 → 0) kadar bağımsız öğretmen üretir:
`IsIndependentTutor = true`, `SchoolId = null`, `ApprovalStatus = Approved` (normal Pending akışını atlayan
bilinçli seed kararı — öğrenci aramasında çıksınlar diye), `IsSeedData = true`, tek branş `TeacherSubject`, ve
`teacher/search` filtrelerinde görünmek için deterministik tutor profili: saatlik ücret 250–900 ₺ (50 adım),
`TeachesOnline = true`, `TeachesInPerson` ≈ %50, kısa `Bio`. Aynı üç sistem, aynı auth-api ucu (`SchoolId = null`
→ Keycloak'ta `school_id` attribute'u yazılmaz), aynı `SeedData:Password`.

```bash
cd api/ExamApp.Api
dotnet run -- seed-tutors --provinces Kars --limit-schools-per-province 3 --dry-run
SeedData__Password='<parola>' dotnet run -- seed-tutors --provinces Kars --limit-schools-per-province 3
SeedData__Password='<parola>' dotnet run -- seed-tutors --pending-ratio 0.2     # her il+branş grubunda floor(%20) Pending kalır
```

`--pending-ratio` verilmezse ortam varsayılanı: **Development 0** (hepsi Approved — aramada görünsün), **Staging 1**
(hepsi Pending — paylaşılan ortamda onaysız tutor öğrenci aramasında çıkmasın; admin onay akışıyla açılır). Açık değer
ortamı ezer.

| Seçenek | Açıklama |
|---|---|
| `--provinces a,b` | Hangi iller (varsayılan: 10 il) |
| `--limit-schools-per-province N` | Tabana il başına ada göre sıralı ilk N seed okulun öğretmenleri sayılır — `seed-teachers` ile aynı limitle koşulduğunda tutarlı sayılar |
| `--pending-ratio 0..1` | Her il+branş grubunda tutor'ların bu oranı (`floor`, sıra numarası en yüksek olanlar) `Pending` kalır; varsayılan ortama göre (Dev 0 / Staging 1) |
| `--dry-run`, `--no-events`, `--keycloak-mode`, `--batch-size`, `--reset-password`, `--adopt-unmarked`, `--no-migrate`, `--connection` | `seed-teachers` ile aynı (yetim adoptasyonu, `seed_origin` kilidi ve parola uyarısı dahil) |

E-posta deseni: `seed.i.<ilSlug>.<brans>.<n>@seed.examapp.local` (`ilSlug` ASCII: `istanbul`, `sanliurfa`;
`i` = independent, okul öğretmenleri `seed.t.`). Ad/soyad ve profil e-postanın SHA-256'sından; tekrar koşu
aynı değerleri üretir, kopya açmaz (`Existing`), yalnızca eksik ders eşlemesini tamamlar — onay durumu ve profil
elle değiştirilmişse dokunulmaz. Soft-delete edilmiş bir seed tutor (`IsDeleted=true`) `Existing` sayılmaz: global
filtre dışında kaldığı için yeniden açılır (`seed-teachers` ile aynı davranış); kalıntıyı `seed-cleanup` hard-delete
eder. İl etiketi/slug/Bio için `Province` tablosundaki ad kullanılır (`--provinces kars` → `Kars`, `seed.i.kars.`).
Çıkış kodları `seed-teachers` ile aynı.

## `seed-cleanup` — test verisini üç sistemden geri alma + özet rapor (issue #218)

Ayrı bir `seed-report` komutu yoktur: **`seed-cleanup` seçeneksiz = dry-run = özet rapor.** Envanter (okul il/tür,
okul öğretmeni ve bağımsız öğretmen il/branş — tutor ili e-postadaki slug'dan Province adına çözülür) ve neyin
silinip neyin atlanacağı raporlanır, hiçbir şey yazılmaz. Silme yalnızca `--apply` ile.

```bash
cd api/ExamApp.Api
dotnet run -- seed-cleanup                    # rapor (dry-run; --dry-run ile aynı)
dotnet run -- seed-cleanup --apply            # sil (Staging'de --yes zorunlu)
dotnet run -- seed-cleanup --apply --force    # müsaitlik verisi (slot/kural) olan seed öğretmenleri de sil
dotnet run -- seed-cleanup --apply --include-orphans   # Keycloak'ta seed desenli ama identity'de hiç satırı olmayan yetimleri de sil
```

`--include-orphans`: kesilmiş bir seed koşusu Keycloak'ta kullanıcı açıp identity'ye yazamadan durduysa bu hesaplar
identity'de yoktur; varsayılan kapsam onları "yabancı" sayıp dokunmaz (dry-run raporunda `yabancı=` bunları da içerir,
not satırı uyarır). Bayrakla yalnızca **kullanıcı adı `seed.*@seed.examapp.local` desenine uyan, identity'de hiç
satırı olmayan VE `seed_origin=examapp-seed` işaretini taşıyan** Keycloak kullanıcıları silinir (`keycloakYetim=`); gerçek
alan adları, identity'de `IsSeedData=false` satırı olan hesaplar (harf duyarsız) ve işaretsiz yetimler bu bayrakla da
silinmez. `ExcludeUserIds` yetimlere uygulanamaz (identity id'leri yoktur). Plandaki yetimleri silmek yerine tamamlamak için
`seed-teachers` / `seed-tutors` (adoptasyon) tercih edilir; `--include-orphans` plan dışı kalıntılar içindir.

`--apply` başında hedef yazdırılır: `Ortam=<name> DB=<host:port/db> auth-api=<url>` (parola yazılmaz). Yanlış hedefe
karşı: `--apply` ile `--connection` **birlikte kabul edilmez** (exit 1) — farklı bir hedef için
`ConnectionStrings__DefaultConnection` ortam değişkenini verin; Staging'de `--yes` zorunludur (exit 1); `--apply --dry-run`
çelişkidir (exit 1).

Kapsam ve kurallar (**seed dışı hiçbir satıra dokunulmaz**):

| Sistem | Silinen | Atlanan (raporlanır) |
|---|---|---|
| exam `Teachers` (+`TeacherSubjects`) | `IsSeedData = true` — soft-delete kalıntıları dahil, **hard delete** (`ExecuteDelete`, soft-delete interceptor'ından geçmez) | **Gerçek öğrenci randevusu (`Bookings`) olan öğretmen — `--force` ile de silinmez** (Booking seed-dışı satırdır; ayrı sayaç "gerçek öğrenci randevusu N"). Müsaitlik verisi (`TeacherAvailabilitySlots`, `RecurringAvailabilityRules`) ya da yazdığı worksheet/soru (`CreateUserId`) olan öğretmen: `--force` yoksa atlanır; `--force` müsaitlik satırlarını öğretmenle birlikte siler, **worksheet/soru asla silinmez** (sahipsiz kalır, sayısı raporlanır) |
| exam `Schools` | `IsSeedData = true` | Bağlı seed-dışı öğretmen, öğrenci, worksheet ataması ya da korunan (atlanan) seed öğretmen varsa |
| identity `Users` | `IsSeedData = true` VE seed alanı e-postası — hard delete | `ExcludeUserIds` (exam'de atlanan öğretmenler); seed alanında ama `IsSeedData = false` (elle açılmış) → yabancı |
| Keycloak | Kullanıcı adı `seed.*@seed.examapp.local` VE identity'de seed kaydı olanlar; `--include-orphans` ile identity'de hiç satırı olmayanlar da; arama `email=` + `username=` infix (`search=` prefix eşleştirdiği için kullanılmaz), boş sayfaya kadar sayfalı; aramada çıkmayan ama identity `KeycloakId`'si bilinen hesap id ile silinir (404 → `Missing`) | Identity'de `IsSeedData=false` satırı varsa (yabancı — her modda), identity'de hiç satır yoksa ve `--include-orphans` verilmediyse (yetim), ya da exclude edilmişse. Aynı e-postadaki tüm identity satırları (soft-delete kalıntıları) tek grup: biri exclude ise hiçbiri silinmez |

Neden hard delete: seed satırları benzersiz indekslerde (`Schools.ExternalCode`, identity e-posta) yer tutar;
soft-delete kalıntısı yeniden koşuda "Existing" sayılırdı. Sıra: exam öğretmen → exam okul → auth-api
(`POST /api/auth/dev/seed-users/cleanup`: Keycloak → identity; identity satırı yalnızca Keycloak silme başarılı ya da
kullanıcı zaten yoksa silinir, böylece identity satırı = yeniden deneme listesi). Kısmi hatada **tekrar koşu kalanı
temizler** — her sistem kendi işaretinden okur. auth-api'ye ulaşılamazsa exam tarafı geri alınmaz, hata raporlanır,
çıkış 3; sonraki koşu yalnızca auth-api tarafını temizler. `--skip-auth-api` yalnızca exam DB.

Ön koşullar `seed-teachers` ile aynı (auth-api güncel build + `AuthApiBaseUrl` doğrudan, servis token'ı). Cleanup ucu
da `Service` policy + ortam guard'ı + DI guard + gateway `/api/auth/dev/*` engeli arkasındadır; kapsamı istekle
GENİŞLETİLEMEZ (yalnızca `ExcludeUserIds` ile daraltılır). Silinmeyen kalıntılar: exam API Redis kullanıcı profili
cache'i (TTL ile düşer), `LoginEvents` (Keycloak sub ile; rapor/analitik satırı), BadgeService/identity outbox
kayıtları. Çıkış: 0 tamam, 2 ortam reddi, 3 hata/kısmi.
