---
description: Bir issue için tasarımcı ile maket üretir (Claude Design canvas), tasarım notunu yazar ve onaydan sonra issue'ya ekler
argument-hint: <issue numarası veya URL>
allowed-tools: Bash(gh issue view:*), Bash(gh issue comment:*)
---

## Issue içeriği

İlk iş: `gh issue view $ARGUMENTS --json number,title,body,labels,url,comments` çalıştır ve tam oku.

---

Bu issue için maket üret. `product-designer` agent'ına devret.

Agent `design-mockup` skill'ini izlesin:
- `ui/src/styles.scss` token'larını ve benzer sayfaları referans alsın — yeni görsel dil icat etmesin.
- `/design` (Claude Design canvas) ile artboard'lar: dolu / boş / yükleniyor / hata durumları,
  gerekiyorsa mobil + masaüstü, çok adımlı akışsa her adım.
- Ekran başına tasarım notu: yerleşim gerekçesi, token + Material komponent eşlemesi, etkileşim,
  erişilebilirlik.
- `angular-dev` için devir notu.

## Çıktı

Bana canvas linkini + tasarım notlarını göster. **Onay bekle.**

Onaylarsam maket linkini/PNG'lerini ve notları issue'ya yorum olarak ekle:
`gh issue comment $ARGUMENTS --body-file <dosya>`.

Onaysız `gh issue comment` çalıştırma.
