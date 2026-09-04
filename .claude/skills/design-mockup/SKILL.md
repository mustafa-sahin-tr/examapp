---
name: design-mockup
description: Bir issue veya akış için maket/wireframe üretme prosedürü — mevcut token sistemine uyma, Claude Design canvas ile artboard oluşturma, tasarım notu yazma ve çıktıyı issue'ya ekleme. Yeni ekran veya UX değişikliği görselleştirilecekse bu adımları izle.
---

# Maket oluşturma

## Sıra

1. **Bağlam.** Issue + kabul kriterleri oku. Hangi rol (öğretmen/öğrenci/veli), hangi cihaz, hangi durumlar.
2. **Mevcut dili çıkar.**
   - `ui/src/styles.scss` — renk ve tipografi token'ları (`--primaryColor`, `--secondaryColor`,
     `--main-bg-color`, `--main-foreground-color`, `--ms-action-background`, `--ms-danger-background`,
     `--mainFontFamily`, `--paragraphFontFamily`).
   - Benzer sayfalar `ui/src/app/pages/` — yerleşim, Material komponent kullanımı.
   - Yeni bir görsel dil icat etme.
3. **Canvas.** `design` skill'ini (`/design`) çağır. Artboard'lar:
   - Ana ekran — dolu durum.
   - Boş durum, yükleniyor, hata (frontend bunları zorunlu ele alıyor).
   - Mobil + masaüstü, eğer akış ikisinde de kullanılıyorsa.
   - Çok adımlı akışsa her adım ayrı artboard + oklarla bağla.
4. **Tasarım notu** (ekran başına, markdown):
   - Yerleşim gerekçesi.
   - Kullanılan token + Material komponent eşlemesi.
   - Etkileşim: tıklama/hover/validasyon/klavye davranışı.
   - Erişilebilirlik: kontrast oranı, odak sırası, dokunma hedefi ≥ 44px.
5. **Devir notu** — `angular-dev` için: yeni shared komponent gerekiyor mu, hangi mevcut komponent
   yeniden kullanılır, veri ihtiyaçları.

## Issue'ya ekleme

Kullanıcı onaylayınca:
- Canvas Artifact linkini + PNG export'larını issue'ya yorum olarak ekle:
  `gh issue comment <no> --body-file <dosya>`
- Tasarım notlarını aynı yoruma koy.

`gh issue comment` / `gh issue edit` komutlarını açık izin olmadan çalıştırma.

## Kalite kontrolü

- Hardcoded renk yok — hepsi token'a karşılık geliyor.
- Her kabul kriterinin görsel bir karşılığı var.
- Boş / hata / yükleniyor durumları çizildi.
- Tasarım mevcut sayfalarla tutarlı (tipografi ölçeği, boşluk, buton stili).
