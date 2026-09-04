---
description: Ham bir GitHub issue'sunu iş analisti ile detaylandırır (problem, kabul kriteri, kapsam, alt issue önerisi)
argument-hint: <issue numarası veya URL>
allowed-tools: Bash(gh issue view:*), Bash(gh issue edit:*), Bash(gh issue create:*), Bash(gh issue comment:*)
---

## Issue içeriği

İlk iş: `gh issue view $ARGUMENTS --json number,title,body,labels,url,comments` çalıştır ve tam oku.
Hata alırsan dur ve bana göster.

---

Bu issue'yu geliştirilebilir hale getir. `business-analyst` agent'ına devret.

Agent `issue-refinement` skill'ini izlesin:
- Problem / kullanıcı hikayesi / kabul kriterleri / kapsam dışı / etkilenen servisler / riskler / açık sorular.
- As-is durumu ilgili koda bakarak doğrulasın, tahmin etmesin.
- Belirsizlikleri "Açık sorular" olarak maddelesin.

Issue tek PR'a sığmıyorsa `issue-breakdown` skill'ine geçsin ve dikey dilimlenmiş alt issue'lar önersin.

## Çıktı

Bana doldurulmuş issue gövdesini + açık soruları göster. **Onay bekle.**

Onaylarsam:
- `gh issue edit $ARGUMENTS --body-file <dosya>` ile gövdeyi güncelle.
- Alt issue önerisi onaylandıysa `gh issue create` ile aç ve epic'e bağla.

Onay gelmeden GitHub'a yazan hiçbir komutu çalıştırma.
