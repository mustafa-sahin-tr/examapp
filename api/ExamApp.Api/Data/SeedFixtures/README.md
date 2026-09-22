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
