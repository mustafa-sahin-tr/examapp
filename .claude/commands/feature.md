---
description: Uçtan uca özellik geliştirme akışını sırayla yürütür (plan → backend → frontend → review)
argument-hint: <özellik açıklaması>
---

Aşağıdaki özelliği ekip olarak uçtan uca geliştir: **$ARGUMENTS**

Sen tech lead'sin. Kodu kendin yazma; ilgili agent'a devret ve çıktıları birleştir.

## Akış

**1. Plan.** Önce ilgili kodu oku ve tek sayfalık bir plan çıkar:
- Hangi servisler etkileniyor (api / BadgeService / OutboxPublisher / Gateway / ui)
- Şema değişikliği var mı, async akış gerekiyor mu
- Hangi agent hangi parçayı alacak

Planı bana göster ve **onay bekle**. Onaysız kod yazma.

**2. Backend.** Onaydan sonra `dotnet-api-dev` agent'ına devret.
Akış outbox gerektiriyorsa o parçayı `event-integration-dev` alsın.

**3. Frontend.** Backend'in gerçek DTO'ları netleştikten sonra `angular-dev` agent'ına devret.
API sözleşmesini tahmin ettirme; backend çıktısındaki tipleri prompt'a koy.

**4. Test.** `test-engineer` agent'ı iş kuralı ve varsa consumer idempotency testlerini yazsın.

**5. Review.** `code-reviewer` ve `security-reviewer` agent'larını **aynı turda paralel** çalıştır.

**6. Rapor.** Bana şunu ver: değişen dosya listesi, kritik bulgular, benim elle yapmam gerekenler
(migration çalıştırma, config, gateway restart).

Kritik/bloklayıcı bulgu çıkarsa düzeltmeyi ilgili yazan agent'a geri gönder, reviewer'a düzelttirme.
