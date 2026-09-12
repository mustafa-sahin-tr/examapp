# Jitsi self-host (docker-compose + Aspire)

İlgili issue: #97. Jitsi 4 container'ı hem `docker-compose.yml`'de hem de `AppHost/AppHost.cs`'de
tanımlı — iki yol paralel yaşıyor, biri diğerini bozmuyor (bkz. `aspire-migration` skill'inin
"Kural" bölümü). Aspire tarafının detayları için aşağıdaki "Aspire ile çalıştırma" bölümüne bakın.

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

## Aspire ile çalıştırma

Jitsi'nin 4 container'ı (`prosody`, `jicofo`, `jvb`, `jitsi-web`) `AppHost/AppHost.cs`'de
docker-compose ile birebir aynı image tag'i (`stable-9584`), env değişkenleri ve portlarla
tanımlı. `aspire-configuration` skill'inin "Explicit configuration only" prensibine uyularak
her değer açık `WithEnvironment(...)` çağrısıyla eşleniyor; `WithReference` kullanılmıyor
(Jitsi container'ları Aspire'ın connection-string/service-discovery modeline uymuyor).

### Parametreler — nereden geliyor

AppHost'ta `builder.AddParameter(...)` ile 6 parametre tanımlı:

| Parametre | Secret mi? | Kaynak |
|---|---|---|
| `jitsi-jwt-app-id` | Hayır | `AppHost/appsettings.json` → `Parameters` (`"examapp"`, `.env.example`'daki `JITSI_JWT_APP_ID` ile aynı) |
| `jitsi-jwt-app-secret` | Evet | `AppHost/appsettings.json` → `Parameters` (dev-only placeholder, `.env.example`'daki `JITSI_JWT_APP_SECRET` ile aynı değer) |
| `jitsi-room-secret` | Evet | aynı şekilde, `.env.example`'daki `JITSI_ROOM_SECRET` ile aynı değer |
| `jicofo-auth-password` | Evet | aynı şekilde, `.env.example`'daki `JICOFO_AUTH_PASSWORD` ile aynı değer |
| `jvb-auth-password` | Evet | aynı şekilde, `.env.example`'daki `JVB_AUTH_PASSWORD` ile aynı değer |
| `jicofo-component-secret` | Evet | aynı şekilde, `.env.example`'daki `JICOFO_COMPONENT_SECRET` ile aynı değer |

Bu proje zaten `postgres-password`, `rabbitmq-password`, `keycloak-admin-password` gibi
`secret: true` parametreleri de **committed** `AppHost/appsettings.json`'a dev-only
placeholder değerlerle yazma konvansiyonunu kullanıyor (production'a asla taşınmaz, sadece
yerel Aspire dev ortamı için) — Jitsi parametreleri de aynı konvansiyona uyuyor, tutarlılık
için ayrı bir `appsettings.Development.json`'a bölünmedi.

`.claude/hooks/secret-guard.sh` (PreToolUse) bu değerlerin appsettings.json'a yazılmasını
**bloklamadı** — hook'un placeholder muafiyet listesi (`changeme`, case-insensitive) bu
değerlerdeki `ChangeMe`'yi tanıdı. Eğer ileride farklı bir placeholder biçimiyle (örn.
`changeme` geçmeyen bir string) hook engellerse, alternatif:

```bash
cd AppHost
dotnet user-secrets set "Parameters:jitsi-jwt-app-secret" "<deger>"
dotnet user-secrets set "Parameters:jitsi-room-secret" "<deger>"
dotnet user-secrets set "Parameters:jicofo-auth-password" "<deger>"
dotnet user-secrets set "Parameters:jvb-auth-password" "<deger>"
dotnet user-secrets set "Parameters:jicofo-component-secret" "<deger>"
```

(`AppHost.csproj`'da `UserSecretsId` zaten tanımlı.) user-secrets kullanılırsa
`AppHost/appsettings.json`'a o anahtarları **eklemeyin** — Aspire, `Parameters` config
kaynaklarını (appsettings → appsettings.Development → user-secrets → env var) sırayla
okur, en son kazanır.

### Container'lar arası adresleme

Aspire, aynı container network'teki container resource'ları varsayılan olarak resource
adıyla çözer (docker-compose'un servis adı DNS'ine denk gelir) — bu yüzden `jicofo`/`jvb`
üzerinde `XMPP_SERVER=prosody` literal string olarak duruyor, ekstra bir şey gerekmiyor.

`meet.jitsi` (XMPP virtual host, `XMPP_DOMAIN`) sabit kalıyor — bu bir DNS adı değil,
Prosody'nin sanal domain adı. Ancak docker-compose'da prosody'ye `networks.mynetwork.aliases:
[meet.jitsi]` network alias'ı verilmişti, çünkü `jitsi-web`'in nginx şablonu
`/xmpp-websocket` proxy_pass hedefini `$XMPP_SERVER`'dan alıyor ve bu değişken jitsi-web
container'ında **set edilmemiş** — image'ın kendi script'i `XMPP_SERVER` boşsa `XMPP_DOMAIN`'e
düşüyor, yani nginx gerçekten `meet.jitsi`'yi DNS ile çözmeye çalışıyor. Aspire'da bunun
karşılığı `WithContainerNetworkAlias("meet.jitsi")` — bu extension method mevcut Aspire.Hosting
13.5.0'da var ve prosody container'ına eklendi, `jitsi-web`'den `meet.jitsi` prosody'ye
çözülüyor.

### HTTP-only yerel kurulumda XMPP websocket kapalı, BOSH göreli yol kullanıyor

`jitsi-web`'in `/defaults/system-config.js` şablonu şunu yapıyor:
`$PUBLIC_URL_DOMAIN := PUBLIC_URL | trimPrefix "https://"`, sonra bu değerin başına **sabit**
`https://` (BOSH) / `wss://` (websocket) prefixi ekliyor — `BOSH_RELATIVE` set değilse. Bizim
yerel `PUBLIC_URL`/`JITSI_PUBLIC_URL` değerimiz `http://localhost:8000` (HTTP), yani
`trimPrefix "https://"` hiçbir şey kırpmıyor ve üretilen config şöyle kırık oluyordu:

```
config.bosh = 'https://http://localhost:8000/' + subdir + 'http-bind';
config.websocket = 'wss://http://localhost:8000/' + subdir + 'xmpp-websocket';
```

Bu, canlı bir Aspire ortamında (`docker ps` + `/config/config.js` içeriği) doğrulandı. Çözüm
(hem `AppHost/AppHost.cs` hem `docker-compose.yml`'de aynı):

- `BOSH_RELATIVE=1` — şablonun `config.bosh` dalında göreli bir seçenek var
  (`config.bosh = '/' + subdir + 'http-bind'`), bu tarayıcının kendi origin'ini (`http://localhost:8000`)
  kullanır ve HTTP'de çalışır.
- `ENABLE_XMPP_WEBSOCKET=0` — websocket için eşdeğer bir göreli seçenek **yok**; şablon her
  zaman `wss://` sabitliyor. HTTP-only yerelde websocket'i kapatıp XMPP trafiğini BOSH'a
  bırakıyoruz.

Gerçek bir HTTPS deployment'a (LAN/prod, `DISABLE_HTTPS=0` + gerçek sertifika) geçildiğinde bu
kısıtlama ortadan kalkar — `PUBLIC_URL` `https://` ile başlayınca `trimPrefix` doğru çalışır ve
`ENABLE_XMPP_WEBSOCKET=1`'e geri dönülebilir (BOSH_RELATIVE de kalabilir, göreli yol HTTPS'te de
çalışır).

### Portlar ve host binding

- `jitsi-web`: `WithHttpEndpoint(port: 8000, targetPort: 80)`. **Fark:** docker-compose
  `127.0.0.1:8000:80` ile sadece loopback'e bind ediyordu; Aspire/DCP'nin container port
  publish mekanizmasında bind-address seçeneği yok, bu yüzden Aspire tarafında `8000` tüm
  arayüzlerde (`0.0.0.0`) açılıyor. LAN'dan erişim compose'a göre daha geniş — Aspire ile
  çalışırken bunu bilerek kabul edin (yerel dev makinesinde risk düşük, ama firewall/VPN
  senaryolarında compose'dan daha geniş yüzey).
- `jvb`: `WithEndpoint(port: 10000, targetPort: 10000, protocol: ProtocolType.Udp, isProxied: false)` —
  compose'un `10000:10000/udp` eşleniği. **`isProxied: false` zorunlu:** olmadan Aspire/DCP bu
  UDP endpoint'i kendi proxy'siyle rastgele bir host portuna yönlendiriyor (`docker ps`'te
  `127.0.0.1:49666->10000/udp` gibi görülür), ama `JVB_ADVERTISE_IPS=127.0.0.1` +
  `JVB_PORT=10000` istemciye hep `10000`'i söylüyor — medya gerçekte hiç ulaşmayan bir porta
  yönlendirilmiş oluyor, bağlantı sessizce başarısız oluyor. `isProxied: false` ile Docker
  `10000/udp`'yi doğrudan yayınlıyor (`exam-dotnet-api`'nin `WithHttpEndpoint(..., isProxied: false)`
  deseniyle aynı gerekçe, bkz. `AppHost/AppHost.cs`).
- `JVB_ADVERTISE_IPS=127.0.0.1`, `PUBLIC_URL=http://localhost:8000` — compose ile aynı,
  tek makine testi içindir (bkz. aşağıdaki "Tek makinede iki tarayıcıyla test").

### exam-dotnet-api wiring

`Video__Jitsi__PublicBaseUrl=http://localhost:8000` (literal — jitsi-web server-side
resolve edilmiyor, sadece token'ın içine/response'a konan bir URL), `Video__Jitsi__AppId`,
`Video__Jitsi__AppSecret`, `Video__Jitsi__RoomSecret` parametrelerden geliyor.

**`WaitFor(jitsiWeb)` kasıtlı olarak yok.** `exam-dotnet-api` Jitsi'ye hiçbir zaman HTTP
çağrısı yapmıyor — `JitsiVideoSessionProvider` sadece HS256 JWT imzalıyor (bkz.
`api/ExamApp.Api/Services/Video/JitsiVideoSessionProvider.cs`). API, Jitsi container'ları
henüz ayağa kalkmadan da token üretebilir; üretilen token sadece jitsi-web/prosody hazır
olana kadar kullanılamaz. `WaitFor` eklemek gereksiz bir başlatma bağımlılığı yaratırdı.

### Named volume'lar

`examapp-jitsi-prosody-config`, `examapp-jitsi-prosody-plugins-custom`,
`examapp-jitsi-jicofo`, `examapp-jitsi-jvb`, `examapp-jitsi-web` — AppHost'un
postgres/redis/minio için kullandığı `WithVolume("examapp-...", <container-path>)`
deseniyle aynı; docker-compose'un `./jitsi-data/...` bind mount'larının Aspire
karşılığı (host-relative path yerine named volume, dev makineleri arasında taşınabilir).

### Tek makine testi (Aspire)

```bash
cd AppHost
dotnet run
```

Dashboard'dan `prosody`/`jicofo`/`jvb`/`jitsi-web` container'larının `Running`/healthy
olduğunu doğrulayın, sonra `http://localhost:8000`'i iki farklı tarayıcı/pencereden açıp
"Tek makinede iki tarayıcıyla test" bölümündeki adımları izleyin — moderatör yetkisi
doğrulaması dahil, aynı doğrulama adımları docker-compose ve Aspire için geçerli.

### Doğrulanmadı / riskler (Aspire'a özgü)

- Bu AppHost değişikliği derlendi (`dotnet build AppHost`) ama **hiç çalıştırılmadı** —
  Docker daemon bu görev sırasında kullanılamadı. İlk `dotnet run` sonrası mutlaka:
  prosody/jicofo/jvb/jitsi-web'in hepsinin ayağa kalktığını, `WithContainerNetworkAlias`
  ile `meet.jitsi`'nin gerçekten prosody'ye çözüldüğünü (jitsi-web loglarında XMPP
  bağlantı hatası olmamalı) ve exam-dotnet-api'nin ürettiği token'ın jitsi-web'de kabul
  edildiğini doğrulayın.
- `WithContainerNetworkAlias` Aspire.Hosting 13.5.0'da mevcut (binary'de doğrulandı) ama
  bu proje için ilk kullanımı — compose'daki `networks.aliases` davranışıyla birebir aynı
  şekilde çalıştığı varsayımı test edilmedi.
- docker-compose yolu bu değişiklikle **bozulmadı** — `docker-compose.yml` dokunulmadan
  kaldı, iki yol paralel duruyor (`aspire-migration` skill kuralı).

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
