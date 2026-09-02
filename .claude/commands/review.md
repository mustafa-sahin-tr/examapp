---
description: Mevcut değişiklikleri code-reviewer ve security-reviewer ile paralel inceler
argument-hint: [opsiyonel: odak alanı]
---

Çalışma ağacındaki değişiklikleri incele. Odak: ${ARGUMENTS:-tüm değişiklikler}

1. `git status` ve `git diff HEAD --stat` ile değişim yüzeyini çıkar, bana tek satırda özetle.
2. `code-reviewer` ve `security-reviewer` agent'larını **aynı turda** çalıştır (paralel).
   Her ikisine de değişen dosya listesini ver.
3. İki raporu tek listede birleştir, aynı bulguyu tekrarlama.

Çıktı sırası: **Bloklayıcı** → **Düzeltilmeli** → **Öneri** → tek satır **Karar**.

Kod düzeltme. Ben istersem düzeltmeyi ilgili agent'a devredersin.
