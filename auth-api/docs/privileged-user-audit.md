# Yetkili hesap denetimi — `audit-privileged-users` (issue #267)

PR #261 (#240) öncesinde anonim `POST /api/auth/register` isteğindeki `role` değeri doğrudan Keycloak'a atanıyordu;
herkes `Admin` ya da `exam-service` rolüyle hesap açabiliyordu. Bu komut, açığın geçmişte istismar edilip edilmediğini
her ortamda (local, staging, prod) denetlemek içindir.

**Salt okunur.** Keycloak'a ve DB'ye hiçbir şey yazmaz, migration çalıştırmaz, web host (Kestrel/Redis) kurmaz. Bu
yüzden seed komutlarının aksine ortam guard'ı yoktur; Production'da da çalışır. Hesap devre dışı bırakma / rol kaldırma
**otomatik yapılmaz** — karar ve işlem insana aittir (Keycloak Admin Console).

## Kullanım

```bash
cd auth-api
dotnet run -- audit-privileged-users                 # tablo + özet (stdout)
dotnet run -- audit-privileged-users --format csv    # CSV stdout'a, özet stderr'e
dotnet run -- audit-privileged-users --format json   # özet dahil tek JSON belgesi
dotnet run -- audit-privileged-users --help
```

| Çıkış kodu | Anlamı |
|---|---|
| 0 | Tüm yetkili hesaplar açıklandı |
| 1 | Hatalı kullanım (bilinmeyen argüman / format) |
| 2 | En az bir `UNEXPLAINED` hesap ya da dolaylı yetki uyarısı (grup ataması / kompozit rol) var — incele |
| 3 | Çalışma hatası (Keycloak/DB erişimi, eksik yapılandırma, realm/URL yanlış, grup/kompozit kontrolü okunamadı, sayfa üst sınırı aşıldı) |

### Yapılandırma

| Anahtar | Not |
|---|---|
| `Keycloak:Host`, `Keycloak:RealmRolesUrl`, `Keycloak:UserUrl`, `Keycloak:TokenUrl`, `Keycloak:AdminClientId` | appsettings.json'daki değerler; Host ortamına göre override edilir |
| `Keycloak__AdminClientSecret` | **env** — admin client (`exam-admin`) secret'ı; `view-users`, `query-groups`, `view-realm` yeterli |
| `ConnectionStrings__DefaultConnection` | **env** — auth-api identity DB (`identity`); salt okunur DB kullanıcısı önerilir (aşağıda) |
| `PrivilegedAudit__KnownAccounts__<n>` | İsteğe bağlı, **env** — meşruiyeti bilinen hesapların **Keycloak id'leri (sub)**, tam eşleşme. Kullanıcı adı kabul edilmez. appsettings.json'da boş dizi; hesap listesi depoda tutulmaz |

Secret'lar komut satırı argümanı olarak verilmez (shell geçmişine düşer); yalnızca env.

### Salt okunur DB kullanıcısı (önerilen)

Komut identity DB'ye yazmaz; yine de staging/prod'da uygulama kullanıcısıyla değil, yalnızca `Users` tablosunu okuyabilen
ayrı bir kullanıcıyla çalıştır (kod hatası ya da bağlantı dizesi sızıntısı yazma yetkisine dönüşmesin):

```sql
-- identity DB'de, bir kez (DBA):
CREATE ROLE privileged_audit_ro LOGIN PASSWORD '<secret-store'dan>';
GRANT CONNECT ON DATABASE identity TO privileged_audit_ro;
GRANT USAGE ON SCHEMA public TO privileged_audit_ro;
GRANT SELECT ON TABLE "Users" TO privileged_audit_ro;
ALTER ROLE privileged_audit_ro SET default_transaction_read_only = on;
```

Aynı yaklaşım aşağıdaki exam DB sorguları için de geçerli (`LoginEvents`, `AdminDataAccessLogs` üzerinde yalnız `SELECT`).

### Yerel (Aspire) çalıştırma

Aspire container portları rastgeledir; `docker ps` ile Keycloak (`8080/tcp`) ve Postgres (`5432/tcp`) host portlarını bul.
Secret'lar AppHost user-secrets'tadır (`Parameters:keycloak-admin-client-secret`, `Parameters:postgres-password`).

```bash
cd auth-api
export Keycloak__Host=http://localhost:<keycloak-port>
export Keycloak__AdminClientSecret=<AppHost user-secrets'tan>
export ConnectionStrings__DefaultConnection="Host=localhost;Port=<pg-port>;Database=identity;Username=<postgres-user>;Password=<...>"
dotnet run --no-launch-profile -- audit-privileged-users
```

Çalışan Aspire auth-api'si `bin/`'i kilitliyorsa: `dotnet build -c Release -o <scratch>` ve
`dotnet <scratch>/ExamApp.Api.dll audit-privileged-users`.

## Çıktı

Her satır = (rol, kullanıcı). Kişisel veri maskelidir: e-posta `a***@d***.com`, e-posta biçimli kullanıcı adı aynı
şekilde, diğer adlar `a***`. Keycloak id (sub) **açıktır** — aşağıdaki exam DB sorgularının anahtarıdır.

| Kolon | Kaynak |
|---|---|
| `source` | `direct` (rol kullanıcıya doğrudan atanmış) ya da `group:/yol` (rolün atandığı grup / alt grup üzerinden) |
| `keycloakId`, `username`, `email`, `enabled`, `kcCreatedUtc` | Keycloak `GET /roles/{role}/users` ve `GET /groups/{id}/members` (tüm sayfalar, `briefRepresentation=false`) |
| `identity`, `idCreatedUtc`, `idDeleted`, `idRole`, `idSeed` | identity DB `Users` (`KeycloakId` ile; soft-delete **dahil**): `CreateTime`, `IsDeleted`, `Role`, `IsSeedData` |
| `class`, `note` | Sınıflandırma (aşağıda) |

Özet: rol başına toplam ve sınıf sayıları; realm'de tanımlı olmayan rol `(rol realm'de tanımlı değil)` ile işaretlenir
(hata değildir — tanımsız rol kimseye verilemez). Ardından dolaylı yetki uyarıları ve `UNEXPLAINED` hesapların sub listesi.

Sayfalı listeler boş sayfa gelince ya da sayfada yeni id gelmeyince durur; 1000 sayfa üst sınırı aşılırsa liste sessizce
kesilmez, komut exit 3 ile biter. Tablo/CSV/özet çıktısındaki ham Keycloak/DB değerlerinde kontrol karakterleri `?` ile
değiştirilir; CSV'de `= + - @ \t \r` ile başlayan hücreler `'` ile nötrlenir.

### Dolaylı yetki (uyarı → exit 2)

- **Grup ataması:** `GET /roles/{role}/groups` boş değilse her grup (ve miras alan alt grupları) bir uyarıdır; grup ve alt
  grup üyeleri de denetime `source=group:/yol` ile katılır ve tek tek sınıflandırılır. Tüm üyeler açıklansa bile grup
  ataması uyarısı exit 2 verir — rolün gruba neden atandığı gerekçelendirilmeli.
- **Kompozit rol:** Realm rollerinden biri `Admin`/`exam-service`'i (geçişli olarak) içeriyorsa uyarıdır; o rolün
  sahipleri dolaylı yetkilidir. `default-roles-*` içeriyorsa realm'deki **herkes** yetkilidir.
- Bu kontrollerden biri okunamazsa (403, ağ hatası) sonuç "temiz" sayılmaz: exit 3.

### Sınıflandırma (ilk eşleşen)

Register açığıyla açılan hesap kullanıcı adını (= e-posta) serbestçe seçebildiği için **addan türeyen hiçbir işaret
meşruiyet sayılmaz** ve her kanıt **fail-closed** değerlendirilir:

1. **SERVICE_ACCOUNT** — kullanıcı adı `service-account-` ile başlar **ve** Keycloak `serviceAccountClientId` döndürür;
   döndürmüyorsa (Keycloak 26 döndürmüyor, client listesi de `view-clients` ister) e-posta boş **ve**
   `GET /users/{id}/credentials` boş **ve** `GET /users/{id}/federated-identity` boş. Bu uçlardan biri 404/hata verirse
   "doğrulanamadı" → UNEXPLAINED. Register ile açılan hesabın hem e-postası hem parolası vardır.
2. **KNOWN** — Keycloak id'si (sub) `PrivilegedAudit:KnownAccounts` listesinde (tam eşleşme). Kullanıcı adı eşleşmesi
   KNOWN yapmaz: aynı adla açılmış taklit hesap farklı id taşır.
3. **UNEXPLAINED** — geri kalan her şey. **Yetkili roldeki seed hesabı da buradadır** (`note: SEED hesabı, rol sonradan
   eklenmiş`): seed aracı yalnızca Student/Teacher/Parent atar, Admin/exam-service'i hiçbir seed yolu açıklamaz.
   `note` ipucu verir: `identity kaydı yok`, `identity rolü=<...>` (register açığında identity'ye yazılan rol), taklit
   önek vb.

Bilinen sınırlar:
- Kompozit kontrolü yalnızca **realm** rollerini kapsar; bir **client rolü** kompozitinin realm `Admin`'i içermesi
  `view-clients` yetkisi olmadan görülemez (`exam-admin`'de yok). Bu durumu Admin Console → Clients → Roles'tan elle kontrol et.
- Kompozit rol sahipleri tek tek listelenmez (varsayılan rolde bu tüm realm demektir); uyarı yapılandırmanın düzeltilmesini ister.
- Yerel dev realm'i (`deploy/keycloak/dev-import/realm-export.json`) `admin` kullanıcısını Admin rolüyle import eder;
  yerelde bu hesabın **id'si** `KNOWN` listesine env ile eklenmedikçe `UNEXPLAINED` görünür. Bu beklenen durumdur.

## UNEXPLAINED hesaplar için exam DB incelemesi

Aşağıdaki sorgular **exam API veritabanında** (`worksheet`) çalışır; auth-api o DB'ye bağlanmaz (servisler arası DB
erişimi yok) — sorguları DB'ye erişimi olan operatör elle çalıştırır. `READ ONLY` transaction içinde çalıştır.
`<sub-listesi>` yerine komut özetindeki id'leri koy.

```sql
BEGIN TRANSACTION READ ONLY;

-- 1) Giriş geçmişi (#84): ilk/son giriş, başarılı/başarısız sayısı, girişte görülen roller.
--    LoginEvents: KeycloakUserId, Role, OccurredAtUtc, Success (IsDeleted soft-delete bayrağı).
SELECT "KeycloakUserId",
       MIN("OccurredAtUtc") FILTER (WHERE "Success")      AS first_success_utc,
       MAX("OccurredAtUtc") FILTER (WHERE "Success")      AS last_success_utc,
       COUNT(*)             FILTER (WHERE "Success")      AS success_count,
       COUNT(*)             FILTER (WHERE NOT "Success")  AS failure_count,
       STRING_AGG(DISTINCT "Role", ',')                   AS roles_seen
FROM "LoginEvents"
WHERE "KeycloakUserId" = ANY (ARRAY['<sub-1>', '<sub-2>'])
GROUP BY "KeycloakUserId";

-- 2) Giriş olayları tek tek (zaman çizelgesi).
SELECT "KeycloakUserId", "OccurredAtUtc", "Role", "Success"
FROM "LoginEvents"
WHERE "KeycloakUserId" = ANY (ARRAY['<sub-1>', '<sub-2>'])
ORDER BY "OccurredAtUtc";

-- 3) Admin veri erişimleri (#246): hangi kaynağı, hangi filtreyle, kaç kayıt gördü.
--    AdminDataAccessLogs: ActorKeycloakId, Resource, SchoolIdFilter, UnassignedFilter, Page, PageSize,
--    ReturnedCount, TotalCount, OccurredAtUtc.
SELECT "ActorKeycloakId", "Resource",
       COUNT(*)               AS requests,
       SUM("ReturnedCount")   AS rows_returned,
       MIN("OccurredAtUtc")   AS first_utc,
       MAX("OccurredAtUtc")   AS last_utc
FROM "AdminDataAccessLogs"
WHERE "ActorKeycloakId" = ANY (ARRAY['<sub-1>', '<sub-2>'])
GROUP BY "ActorKeycloakId", "Resource"
ORDER BY "ActorKeycloakId", first_utc;

-- 4) Ayrıntı (etki analizi için).
SELECT "ActorKeycloakId", "OccurredAtUtc", "Resource", "SchoolIdFilter", "UnassignedFilter",
       "Page", "PageSize", "ReturnedCount", "TotalCount"
FROM "AdminDataAccessLogs"
WHERE "ActorKeycloakId" = ANY (ARRAY['<sub-1>', '<sub-2>'])
ORDER BY "OccurredAtUtc";

ROLLBACK;
```

**IP adresi:** `LoginEvents` IP **saklamaz** (kolon yok). Giriş IP'si için Keycloak Admin Console → Events → User events
(`LOGIN`, `ipAddress`) — yalnızca realm'de event kaydı açıksa ve saklama süresi içindeyse vardır. Aktif oturumlar ve
IP'leri: Users → kullanıcı → Sessions.

## Bulgu sonrası (elle)

1. Keycloak Admin Console → Users → kullanıcı → Role mapping: yetkisiz rolü kaldır ya da hesabı `Enabled=off` yap.
2. Sessions → Logout (açık oturumları sonlandır).
3. Sonucu (hesap sayısı, yapılan işlem) issue #267'ye yaz; istismar kanıtı varsa etki analizi ayrı issue.
