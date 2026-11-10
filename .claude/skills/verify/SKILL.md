---
name: verify
description: Bu repoda bir değişikliği çalışan uygulamada doğrulama tarifi — Aspire'ı açma, geçici kullanıcı / veri, Playwright ile akışı sürme, console ve API hatası kontrolü, gerekirse perf-verifier ile performans ölçümü, temizlik. /verify ya da "canlı dene" istendiğinde bu adımları izle.
---

# Canlı doğrulama

Kanıt çalışan uygulamadan gelir: ekran görüntüsü, yanıt gövdesi, DB satırı. Test koşmak doğrulama değildir (CI koşar).
Betikler, ekran görüntüleri ve geçici dosyalar oturumun scratchpad'ine yazılır, repoya değil.

## 1. Aç

- `cd AppHost && dotnet run` (arka planda). Hazır olunca gateway `http://localhost:5678` cevap verir; anonim bir uçla
  bekle (`curl -sf http://localhost:5678/api/school?limit=1`), UI `http://localhost:5678/app/`.
- Değişen servis Aspire'da yeniden derlenmez: kodu değiştirdiysen ilgili kaynağı dashboard'dan yeniden başlat
  (migration'lar exam API açılışında uygulanır).
- Demo yığınına (`hedefokul-demo`, `deploy/scripts/demo.sh`) dokunma. Demo build'i ile Aspire aynı anda bellek yetmez:
  biri çalışırken diğerini başlatma; bellek yüzünden öldürülen komutu sormadan yeniden başlatma.
- İş bitince Aspire'ı durdur.

## 2. Konteynerler

Aspire konteyner adları her açılışta değişir: `docker ps --format '{{.Names}}' | grep -E '^(postgres|keycloak)-'`.

- Postgres: `docker exec -i <postgres-…> psql -U examuser -d <db>`; DB'ler `worksheet` (exam API), `identity` (auth-api),
  `badge` (BadgeService, bildirimler), `keycloak`.
- Keycloak (`exam-realm`, port 8081): `docker exec <keycloak-…> /opt/keycloak/bin/kcadm.sh config credentials --server
  http://localhost:8080 --realm master --user admin --password admin`, sonra `kcadm.sh create users -r exam-realm …`.

## 3. Kullanıcı ve veri

- Dev kullanıcılarının ortak parolası yalnız memory'de (`dev-test-user-password`). Repoya, commit'e, PR / issue'ya, loga
  yazılmaz: scratchpad'de `chmod 600` bir dosyaya koy, betik oradan okusun, iş bitince sil.
- Mevcut kullanıcıyı kullan; yeni hesap gerekiyorsa (ör. rolsüz kullanıcı → `/register`) kcadm ile aç, adı
  `verify<issue>@example.test` olsun.
- Dev verisi azdır: denenecek ekran için gereken satırları psql ile ekle, adlarına ayırt edici önek koy (`V<issue> …`),
  unique index'lere dikkat (dış kodlu sütunları boş bırak).

## 4. Sür (Playwright)

- `require('<repo>/ui/node_modules/playwright')`, `chromium.launch({ channel: 'chrome', headless: true })`,
  `locale: 'tr-TR'`. Viewport `page.setViewportSize` ile (context'te değil).
- Giriş: `/app/login` → `#username` → (görünmüyorsa `#kc-login` ile ilerle) `#password` → `#kc-login`.
- Material panelleri gövdeye taşınır: `.mat-mdc-autocomplete-panel mat-option`, `.mat-mdc-select-panel mat-option`.
  Seçicide önce `data-testid`.
- **Her doğrulamada zorunlu:** sayfayı `browser-checks.cjs` ile izle, akışın sonunda raporu bas. Console error / warning,
  yakalanmamış JS hatası, 4xx/5xx API yanıtı ya da başarısız istek varsa doğrulama FAIL'dir; bilerek tetiklediğin hatayı
  `allow` ile açıkça muaf tut ve raporda yaz. Değişiklikten bağımsız, master'da da görünen bir hata FAIL sebebi değildir:
  bulgu olarak yaz (issue yoksa aç), `allow`'a ekle. Yanlış URL'e gitmek de console'a hata düşürür (`NG04002`): rotayı
  `ui/src/app/app.routes.ts`'den al.
  ```js
  const { watchPage } = require('<repo>/.claude/skills/verify/browser-checks.cjs');
  const checks = watchPage(page, { allow: [] });
  // … akış …
  checks.print();
  ```
- Mutlu yolu geçtikten sonra kenarları dene: boş değer, hızlı ardışık tıklama, geri al, çelişen filtre, yenile.
- Kaydedilen sonucu DB'den de doğrula (UI'nin söylediği değil, yazılan satır).

## 5. Performans (yalnız gerektiğinde)

Liste / tablo, grafik, büyük sayfa, ağır sorgu ya da yükleme yolu değiştiyse `perf-verifier` agent'ını çağır. Chrome DevTools
MCP yalnız o agent'ta yüklüdür; ana oturum ve geliştirme sırasında yoktur. Prompt'ta ver: URL'ler, giriş kullanıcısı ve
parola (memory'den), sayfaya varmak için tıklanacaklar, karşılaştırma için önceki değer varsa o. Küçük UI / metin / backend
iş kuralı değişikliğinde çağırma.

## 6. Temizlik

Eklediğin her şeyi sil ve sayarak doğrula (0 satır):

- Eklediğin satırlar (önekle), akışın yazdığı satırlar (ör. öğretmen / öğrenci kaydı, `identity` kullanıcısı).
- Akışın tetiklediği outbox olaylarının izleri: `worksheet` / `identity` outbox satırları ve BadgeService'in yazdıkları
  (`badge` DB'de bildirimler). Hangi event'in yazıldığını outbox tablosundan bak.
- Keycloak'ta açtığın kullanıcı, scratchpad'deki parola dosyası.

## Rapor

Verdict (PASS / FAIL / BLOCKED / SKIP), adımlar (✅ / ❌ / ⚠️ / 🔍 deneme), console / API satırı
(`console 0 · page error 0 · API>=400 0 · failed 0`), varsa performans özeti, ekran görüntüsü yolu, bulgular.
