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
gerçek bir kullanıcı e-postası 400 ile reddedilir, hiçbir yazma yapılmaz. Keycloak'ta zaten var olan ama
identity'de seed kaydı olmayan hesaplara dokunulmaz (`SkippedForeign`, raporda hata sayılır).

### Seçenekler

| Seçenek | Açıklama |
|---|---|
| `--provinces a,b` | Hangi illerin seed okulları (varsayılan: 10 il) |
| `--limit-schools-per-province N` | İl başına ada göre (tr-TR) sıralı ilk N okul |
| `--dry-run` | auth-api çağrılmaz, yazılmaz; plan ve hesap listesi raporlanır (parola gerekmez) |
| `--keycloak-mode admin-api\|partial-import` | Aşağıdaki ölçüme göre seçin (varsayılan `admin-api`) |
| `--batch-size N` | auth-api'ye istek başına hesap (varsayılan 100, en fazla 500) |
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

### Gateway

`Services/Gateway/ocelot*.json` içinde `/api/auth/dev/{everything}` yolu `/api/auth/{everything}` route'undan
önce (Priority 2) tanımlıdır ve auth-api'de var olmayan `/__blocked/...` yoluna gider → gateway üzerinden
her zaman **404**. Dev ucu dışarıdan erişilemez; komut `AuthApiBaseUrl` ile auth-api'ye doğrudan gider.

### Bilinen yerel ortam notu

Yerel Keycloak volume'u `deploy/keycloak/import/realm-export.json`'dan sapmışsa (`school_id` protocol
mapper'ı / user-profile attribute'u yoksa) Keycloak `school_id` attribute'unu sessizce düşürür ve JWT'de
`school_id` claim'i çıkmaz — bu, normal register akışı için de geçerlidir; çözüm realm'i yeniden import etmektir.
