---
name: issue-refinement
description: Ham bir GitHub issue'sunu geliştirilebilir hale getirme prosedürü — problem, kullanıcı hikayesi, kabul kriteri, kapsam dışı, açık sorular. Yeni fikir/talep issue'ya dönüştürülürken veya belirsiz issue netleştirilirken bu adımları izle.
---

# Issue detaylandırma

## Sıra

1. **Oku.** `gh issue view <no> --json number,title,body,labels,url,comments`.
   Yorumlar dahil. Hata alırsan dur, kullanıcıya göster.
2. **As-is'i doğrula.** İlgili ekranı/kodu gez. "Şu an ne oluyor" kısmını gerçeğe dayandır.
3. **Şablonu doldur** (aşağıda). Bilmediğini uydurma — "Açık sorular"a yaz.
4. **Kullanıcıya göster**, açık soruları maddele.
5. Onaydan sonra `gh issue edit <no> --body-file <dosya>` ile güncelle (açık izinle).

## Issue şablonu

```markdown
## Problem
<kim, ne zaman, hangi acı — çözüm değil>

## Kullanıcı hikayesi
<rol> olarak <şey> istiyorum, çünkü <fayda>.

## Kabul kriterleri
- [ ] <test edilebilir, gözlemlenebilir sonuç>
- [ ] ...

## Kapsam dışı
- <bu issue'da yapılmayacak>

## Etkilenen alanlar
- Servisler: api / BadgeService / OutboxPublisher / Gateway / ui
- Şema değişikliği: var / yok
- Async akış (outbox event): gerekiyor / gerekmiyor

## Bağımlılıklar & riskler
- <migration, dış servis, başka issue>

## Açık sorular
- <cevap bekleyen>
```

## Kalite kontrolü

- Her kabul kriteri tek bir şeyi doğruluyor ve "bitti mi?" sorusuna net cevap veriyor mu?
- "Hızlı olsun", "kullanıcı dostu olsun" gibi ölçülemez ifade kaldı mı? → ölç veya çıkar.
- Problem ile çözüm karışmış mı? Problem başlığında "buton ekle" yazıyorsa yanlış.
- Tek PR'a sığmıyorsa → `issue-breakdown` skill'ine geç.
