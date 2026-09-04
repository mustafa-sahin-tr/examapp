---
name: issue-breakdown
description: Büyük bir issue'yu / epic'i alt issue'lara bölme prosedürü — dikey dilimleme, bağımlılık sırası, GitHub'da alt issue oluşturma ve epic'e bağlama. Bir iş tek PR'a sığmadığında bu adımları izle.
---

# Epic bölme

## Ne zaman böl

- Birden çok servis + frontend + migration aynı anda gerekiyorsa.
- Birden çok rol dokunuyorsa (BA + tasarımcı + iki geliştirici).
- Kabul kriterleri 6-7'yi geçtiyse.
- "Bunun bir kısmını erken çıkarabilir miyiz?" cevabı evetse.

## Sıra

1. Ana issue'yu **epic** yap: gövdeye özet + "Alt issue'lar" checklist bölümü ekle, `epic` label'ı.
2. **Dikey dilimle.** Her alt issue tek başına anlamlı bir sonuç üretmeli
   (katman değil: "DB tablosu" değil, "öğretmen taslak sınavı kaydedebiliyor").
3. Her alt issue için `issue-refinement` şablonunu kısaca doldur + hangi rol/agent alacak.
4. **Sırala.** Bağımlılık grafiği: neyin neyden önce gelmesi gerektiğini yaz.
5. Kullanıcıya böa planını göster. Onaydan sonra:
   - `gh issue create --title ... --body-file ... --label ...` ile alt issue'ları aç.
   - Epic gövdesindeki checklist'e `- [ ] #<no>` olarak ekle (`gh issue edit`).
   - Her alt issue gövdesine `Part of #<epic-no>` satırı koy.

GitHub'a yazan hiçbir komutu açık izin olmadan çalıştırma.

## Çıktı

- Epic özeti + alt issue tablosu (no, başlık, rol, bağımlılık).
- Önerilen çalışma sırası.
