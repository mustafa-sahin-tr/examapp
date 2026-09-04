---
description: Bir epic/issue'yu ürün ekibiyle uçtan uca hazırlar (PO önceliklendirir → BA detaylandırır → tasarımcı maket → alt issue'lar açılır)
argument-hint: <issue numarası veya URL>
allowed-tools: Bash(gh issue view:*), Bash(gh issue edit:*), Bash(gh issue create:*), Bash(gh issue comment:*)
---

## Issue içeriği

İlk iş: `gh issue view $ARGUMENTS --json number,title,body,labels,url,comments` çalıştır ve tam oku.
Hata alırsan dur ve bana göster.

---

Bu issue'yu ürün ekibi olarak geliştirilmeye hazır hale getir. Sen kolaylaştırıcısın;
kararları ilgili agent'a ver, çıktıları birleştir.

## Akış

**1. Öncelik (product-owner).** Değer, kanıt, aciliyet, MVP kesimi.
Çıktı: YAP ŞİMDİ / SONRA / YAPMA + MVP kapsamı. "YAPMA" veya "SONRA" çıkarsa dur, bana söyle.

**2. Detaylandırma (business-analyst).** `issue-refinement` ile issue gövdesini doldur:
problem, kullanıcı hikayesi, kabul kriterleri (MVP kapsamına göre), kapsam dışı, etkilenen servisler,
riskler, açık sorular. Açık sorular varsa **bana sor**, tahminle doldurma.

**3. Bölme (business-analyst).** İş tek PR'a sığmıyorsa `issue-breakdown` ile dikey dilimlenmiş
alt issue'lar + bağımlılık sırası öner.

**4. Maket (product-designer).** UI değişikliği varsa `design-mockup` ile canvas + tasarım notları.
Yoksa bu adımı atla ve nedenini yaz.

**5. Rapor.** Bana tek sayfa: PO kararı + MVP, doldurulmuş issue gövdesi, alt issue tablosu,
canvas linki, açık sorular, önerilen çalışma sırası.

## Onay ve yazma

Raporu onaylayınca:
- `gh issue edit` ile epic gövdesini güncelle.
- `gh issue create` ile alt issue'ları aç, epic checklist'ine bağla, gövdelerine `Part of #$ARGUMENTS`.
- Maket linkini/notları `gh issue comment` ile epic'e ekle.

Onay gelmeden GitHub'a yazan hiçbir komutu çalıştırma. Kod yazılmaz — bu komut sadece hazırlık.
Geliştirmeye geçmek için hazır alt issue üzerinde `/feature <no>` kullan.
