---
name: security-review-checklist
description: Bu platforma özel güvenlik denetim listesi — Keycloak yetkilendirme, secret yönetimi, dosya yükleme, EF sorguları, event payload ve gateway route'ları. Güvenlik incelemesi, auth değişikliği veya kullanıcı verisine dokunan her review'da kullan.
---

# Güvenlik denetim listesi

Değişen kod üzerinde uygula. Teorik risk listesi çıkarma; sadece diff'ten türeyen somut bulguları raporla.

## 1. Kimlik doğrulama ve yetkilendirme

- Yeni endpoint `[Authorize]` olmadan mı eklendi? Public olması bilinçli mi?
- Rol kontrolü var ama **kaynak sahipliği** kontrolü yok mu? (öğretmen A, öğretmen B'nin testini
  id değiştirerek çekebiliyor mu → IDOR)
- `ocelot.json`'daki yeni route'un auth ayarı, eşdeğer korumalı route'larla aynı mı?
- Yetki kararı frontend'de mi veriliyor? UI'da butonu gizlemek yetki kontrolü değildir.
- Keycloak token doğrulamasında issuer/audience gevşetilmiş mi?

## 2. Secret ve konfigürasyon

- `appsettings.json`, `.env`, Angular `environment.*.ts` veya test dosyalarına gerçek credential yazılmış mı?
- Log satırlarına token, şifre, connection string veya kişisel veri düşüyor mu?
- `.env.example` içine gerçek değer mi konmuş?
- Gemini / dış servis API anahtarı koda gömülmüş mü?

## 3. Girdi ve sorgu

- Ham SQL veya string interpolation ile kurulmuş sorgu var mı? (EF parametreli kalmalı)
- DTO'da uzunluk/aralık/format validation yok mu?
- Sayfalama olmadan sınırsız liste dönen endpoint var mı? (DoS + veri sızıntısı)
- Kullanıcıdan gelen değer dosya yoluna, komuta veya URL'e doğrudan konuyor mu? (path traversal / SSRF)

## 4. Dosya yükleme (MinIO / question-detector)

- Dosya tipi ve boyutu doğrulanıyor mu? Uzantıya değil içeriğe bakılıyor mu?
- Yüklenen dosya adı kullanıcıdan geldiği gibi mi kullanılıyor?
- Bucket/nesne erişimi public mi? Presigned URL süresi makul mü?

## 5. Event ve entegrasyon

- Outbox event payload'ında kişisel veri, token veya tam entity var mı?
- Consumer idempotent mi? Tekrar işleme yan etki üretiyor mu?
- Dış servise (Gemini) gönderilen içerikte istenmeyen kullanıcı verisi var mı?

## 6. Hata ve yanıt

- Exception mesajı, stack trace veya SQL hatası istemciye dönüyor mu?
- Hata mesajı kullanıcı varlığını sızdırıyor mu? ("böyle bir kullanıcı yok" vs "hatalı giriş")
- CORS `*` mi? Yeni origin bilinçli mi eklendi?

## Raporlama

Her bulgu: `dosya:satır` → **etki** → **istismar senaryosu** → **düzeltme**.
Şiddet: Yüksek (authz atlatma, secret sızıntısı, injection, IDOR) / Orta / Düşük.
Yüksek varsa raporun ilk satırı: `BLOKLAYICI BULGU VAR`.
