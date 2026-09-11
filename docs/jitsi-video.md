# Jitsi self-host (docker-compose)

İlgili issue: #97. Kapsam: sadece `docker-compose.yml`. Aspire AppHost entegrasyonu
kasıtlı olarak **kapsam dışı** — ayrı bir issue olacak (bkz. Aşağıdaki "Aspire'a taşıma TODO").

## Ne eklendi

`docker-compose.yml`'e docker-jitsi-meet resmi imajlarıyla 4 servis eklendi:

| Servis | İmaj | Rol |
|---|---|---|
| `prosody` | `jitsi/prosody:stable-9584` | XMPP sunucusu, `meet.jitsi` network alias'ı bu container'da |
| `jicofo` | `jitsi/jicofo:stable-9584` | Konferans odası yönetimi (focus component) |
| `jvb` | `jitsi/jvb:stable-9584` | Video bridge (medya trafiği) |
| `jitsi-web` | `jitsi/web:stable-9584` | Web UI + `external_api.js` |

Tarayıcı Jitsi'ye **doğrudan** `http://localhost:8000` ile gider — `ocelot-gateway`
üzerinden geçmez. Gateway sadece exam API'lere route eder, Jitsi bunun dışında.

## Nasıl başlatılır

```bash
cp .env.example .env   # henüz yapmadıysanız; Jitsi değişkenleri dahil
docker-compose up -d prosody jicofo jvb jitsi-web
# veya tüm stack ile birlikte:
docker-compose up -d
```

İlk açılışta `jitsi-data/{web,prosody,jicofo,jvb}` altında config volume'ları
oluşur (git-ignored, bkz. `.gitignore`).

Doğrulama:
```bash
curl -I http://localhost:8000          # jitsi-web ayakta mı
docker compose logs -f jicofo          # jicofo prosody'ye bağlanabildi mi
```

## JWT auth

`ENABLE_AUTH=1`, `AUTH_TYPE=jwt`, `ENABLE_GUESTS=0` — token'sız kimse odaya giremez.
Backend (`exam-dotnet-api`) token üretirken şu claim'leri kullanmalı:

- `iss` = `JITSI_JWT_APP_ID` (`.env` değeri, varsayılan `examapp`)
- `aud` = `jitsi`
- `sub` = `meet.jitsi` (= `XMPP_DOMAIN`, compose içinde sabit)
- İmza anahtarı: `JITSI_JWT_APP_SECRET` (HS256, >= 32 byte olmalı)

`exam-dotnet-api` servisine bu amaçla env eklendi:
`Video__Jitsi__PublicBaseUrl`, `Video__Jitsi__AppId`, `Video__Jitsi__AppSecret`,
`Video__Jitsi__RoomSecret`. Token üretim kodu **yazıldı** —
`api/ExamApp.Api/Services/Video/JitsiVideoSessionProvider.cs`
(`IVideoSessionProvider` implementasyonu, config `Video:Jitsi:*` — bkz.
`VideoOptions.cs`). Oda adı deterministik ve tahmin edilemez:
`booking-{id}-{HMAC-SHA256(bookingId, RoomSecret)[ilk 12 hex]}`. Token'ın
`sub` claim'i `Video:Jitsi:XmppDomain` (appsettings varsayılanı `meet.jitsi`)
üzerinden üretilir — `PublicBaseUrl`'ün authority'sinden **değil** — böylece
bu doküman ile kod arasında `sub` tutarlılığı garanti edilir.

**Token ömrü**, katılım penceresinin kapanışını (`WindowClosesAtUtc`) aşamaz:
`min(now + TokenLifetimeMinutes, pencere kapanışı)`. Yani bir öğrenci/öğretmen
dersin bitişinden sonra elinde kalan bir token'la tekrar odaya giremez.

## Moderatör yetkisi

**Risk:** docker-jitsi-meet varsayılanında jicofo, odaya ilk giren katılımcıyı
otomatik olarak "owner" (moderatör) yapar (`JICOFO_ENABLE_AUTO_OWNER`
varsayılan `true`). Bu davranış JWT'deki rol bilgisini yok sayar — bir öğrenci
öğretmenden önce girerse moderatör o olur.

**Seçilen çözüm:** `jicofo` servisinde `JICOFO_ENABLE_AUTO_OWNER=false` set
edildi, bu otomatik owner atamasını kapatır. MUC affiliation bunun yerine
prosody'nin **stock** `mod_auth_token` eklentisinden gelir: bu eklenti,
doğruladığı JWT'nin `context.user.affiliation` (`owner`/`member`) ve
`moderator` claim'lerini okuyup katılımcıyı MUC'a o affiliation ile sokar —
`exam-dotnet-api` zaten bu claim'leri üretiyor (`JitsiVideoSessionProvider.cs`,
öğretmen → `owner`/`moderator=true`, öğrenci → `member`/`moderator=false`).
Bu, `jitsi/prosody` imajına ekstra bir plugin (ör. ayrı bir "token_affiliation"
modülü) eklemeyi gerektirmez; `XMPP_MUC_MODULES` boş bırakıldı.

**Sınırlar / doğrulanmadı:**
- Bu davranış `mod_auth_token`'ın stock implementasyonuna dayanıyor; bu görev
  sırasında Docker daemon çalışmadığından **gerçek bir odaya girip affiliation'ı
  doğrulamak mümkün olmadı**. İlk gerçek `docker-compose up` sonrası mutlaka
  test edin: önce öğrenci token'ıyla girin, ardından öğretmen token'ıyla girin,
  öğretmenin moderatör (kayıt/mute-all/kick yetkisi olan) olduğunu doğrulayın.
- Eğer stock `mod_auth_token` beklenen affiliation davranışını göstermezse,
  alternatif olarak prosody-plugins-contrib'den bir affiliation modülü
  eklemek gerekebilir — bu durumda `./jitsi-data/prosody/prosody-plugins-custom`
  volume'u zaten mount edilmiş durumda (bu compose'da hazır), sadece plugin
  dosyasını oraya koyup `XMPP_MUC_MODULES`'a modül adını eklemek yeterli olur.

## Tek makinede iki tarayıcıyla test

`JVB_ADVERTISE_IPS=127.0.0.1` olarak ayarlı — aynı PC üzerinde iki farklı
tarayıcı (veya bir normal + bir gizli pencere) ile `http://localhost:8000`
açıp aynı odaya girerek test edebilirsiniz. Kamera/mikrofon izni için
`localhost` zaten secure context sayıldığından `DISABLE_HTTPS=1` ile HTTP
üzerinden de çalışır.

**LAN/prod için değişecekler:**
- `JVB_ADVERTISE_IPS` → gerçek LAN IP'si (LAN testi) veya public IP (prod, NAT arkasında).
- `DISABLE_HTTPS=1` / `ENABLE_LETSENCRYPT=0` → prod'da gerçek TLS sertifikası şart
  (tarayıcı kamera izni için secure context ister; `localhost` dışında HTTPS zorunlu).
- `JITSI_PUBLIC_URL` (`.env`) → gerçek domain; `jitsi-web`'in `PUBLIC_URL`'i ve
  `exam-dotnet-api`'nin `Video__Jitsi__PublicBaseUrl`'i **aynı** `.env` değişkeninden
  besleniyor, ikisi ayrı ayrı güncellenmesin diye.
- `jitsi-web` portu şu an `127.0.0.1:8000:80` — sadece localhost'tan erişilebilir
  (LAN'dan değil). LAN/prod'da bu bind adresini kaldırıp gerçek reverse-proxy/TLS
  önüne koyun.
- Tek JVB instance NAT arkasında birden fazla eşzamanlı kullanıcıyla ölçeklenmez;
  prod'da TURN sunucusu (coturn) ve/veya birden fazla JVB gerekir.
- Prod'da `Content-Security-Policy` varsa `script-src`/`frame-src`'e Jitsi origin'i
  eklenmeli (bkz. aşağıdaki CORS/CSP notu).
- `jitsi-web`'de `Referrer-Policy: no-referrer` eklenmesi önerilir (JWT, join URL'sinde
  query string olarak taşınıyor — `Referer` header'ıyla sızmasın diye); ayrıca reverse
  proxy/nginx access log formatı `jwt=` query param'ını maskelemeli (ör. nginx
  `map`/`log_format` ile `$request` içindeki `jwt=...` kısmını `jwt=REDACTED` yapan
  bir filtre) — token log dosyalarında düz metin kalmasın.
- `RoomSecret` (`.env` → `Video__Jitsi__RoomSecret`) rotasyonu, oda adı bu secret'tan
  türetildiği için (`BuildRoomName`, HMAC-SHA256) **ders saatleri dışında** yapılmalı;
  rotasyon anında devam eden bir dersin oda adı değişir ve mevcut katılımcılar
  yeniden bağlanana kadar farklı bir odada kalabilir.

## Angular entegrasyonu — CORS/CSP notu

Angular, Jitsi IFrame API'yi `http://localhost:8000/external_api.js` adresinden
`<script>` tag ile yükleyecek. Bu bir script yüklemesi olduğundan CORS kısıtlaması
uygulanmaz (CORS sadece fetch/XHR için geçerli). Ancak Angular tarafında CSP
(Content-Security-Policy) tanımlıysa `script-src` ve `connect-src`/`frame-src`
direktiflerine `http://localhost:8000` eklenmesi gerekebilir — bu repoda şu an
aktif bir CSP header tespit edilmedi, ama ileride eklenirse bu noktayı unutma.

## Aspire'a taşıma TODO (ayrı issue)

Bu servisler şu an sadece docker-compose'da. Aspire AppHost'a taşırken:
- `aspire-migration` skill'indeki iki bilinen tuzağı (Ocelot service discovery,
  Keycloak issuer uyuşmazlığı) Jitsi için de kontrol et — özellikle `PUBLIC_URL`
  ve JWT `sub`/issuer değerlerinin Aspire'ın dinamik adresleriyle çakışıp
  çakışmadığına bak.
- Jitsi 4 container'ı Aspire'da `AddContainer` ile eklenip `WithReference` yerine
  açık env var mapping'i tercih edilmeli (bkz. `aspire-configuration` skill —
  "Explicit configuration only" prensibi).
- Bu docker-compose yolu, Aspire tarafı doğrulanana kadar **silinmemeli**.

## `.env` zorunlu değişkenler

`docker-compose.yml`'de kritik Jitsi secret'ları `${VAR:?...}` sözdizimiyle
zorunlu kılındı — bu değişkenler `.env`'de tanımlı değilse `docker-compose up`
anlamlı bir hata mesajıyla başarısız olur (sessizce boş/None geçmez):
`JICOFO_AUTH_PASSWORD`, `JVB_AUTH_PASSWORD`, `JITSI_JWT_APP_SECRET`,
`JITSI_ROOM_SECRET`. (`JICOFO_COMPONENT_SECRET` ve `JITSI_JWT_APP_ID` bilinçli
olarak zorunlu tutulmadı — quantity/isim değerleri, boş kalsalar da compose
kırılmaz; yine de `.env.example`'daki değerleri kullanın.)

## Riskler / açık noktalar

- Pinlenmiş tag `stable-9584` — bu görev sırasında Docker daemon çalışmadığından
  imajlar gerçekten çekilip test edilemedi; ilk `docker-compose up`'ta bu tag'in
  Docker Hub'da mevcut olduğunu doğrulayın, yoksa `stable` (floating) tag'e
  düşün ve burada güncelleyin.
- **Moderatör yetkisi doğrulanmadı** — `JICOFO_ENABLE_AUTO_OWNER=false` +
  stock `mod_auth_token` affiliation davranışına dayanıyor, gerçek bir
  `docker-compose up` ile test edilmedi (yukarıdaki "Moderatör yetkisi"
  bölümüne bakın). Bu, prod'a çıkmadan önce mutlaka doğrulanmalı.
- Tek JVB + `JVB_ADVERTISE_IPS=127.0.0.1` sadece aynı makine testi içindir.
- `jitsi-web` `127.0.0.1:8000` olarak bind edildi — Docker Desktop/WSL2
  ortamlarında host loopback'in container'a nasıl map edildiğine bağlı olarak
  bazı kurulumlarda `localhost:8000`'in beklendiği gibi çalıştığını doğrulayın.
