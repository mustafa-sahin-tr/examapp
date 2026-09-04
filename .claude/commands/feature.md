---
description: GitHub issue'dan uçtan uca özellik geliştirme akışı (issue → plan → backend → frontend → review)
argument-hint: <issue numarası veya issue URL'i>
allowed-tools: Bash(gh issue view:*), Bash(gh issue comment:*), Bash(git checkout:*), Bash(git status:*)
---

## Issue içeriği

!`gh issue view "$1" --json number,title,body,labels,url,comments`

---

Yukarıdaki GitHub issue'yu ekip olarak uçtan uca geliştir.

Sen tech lead'sin. Kodu kendin yazma; ilgili agent'a devret ve çıktıları birleştir.

Issue gövdesi eksik veya belirsizse (kabul kriteri yok, hangi servisin etkilendiği belli değil,
çelişkili istekler var) kod yazmaya başlama — eksikleri bana madde madde sor.
Issue'daki yorumları da oku; kabul kriteri çoğu zaman yorumlarda netleşiyor.

## Akış

**1. Plan.** Önce issue'da geçen ilgili kodu oku ve tek sayfalık bir plan çıkar:
- Issue başlığı/numarası ve tek cümlelik özet
- Hangi servisler etkileniyor (api / BadgeService / OutboxPublisher / Gateway / ui)
- Şema değişikliği var mı, async akış gerekiyor mu
- Issue'daki kabul kriterlerinin karşılık geldiği somut değişiklikler
- Hangi agent hangi parçayı alacak

Planı bana göster ve **onay bekle**. Onaysız kod yazma.

**2. Backend.** Onaydan sonra `dotnet-api-dev` agent'ına devret.
Akış outbox gerektiriyorsa o parçayı `event-integration-dev` alsın.
Agent prompt'una issue'nun kabul kriterlerini birebir koy; kendi yorumunu değil.

**3. Frontend.** Backend'in gerçek DTO'ları netleştikten sonra `angular-dev` agent'ına devret.
API sözleşmesini tahmin ettirme; backend çıktısındaki tipleri prompt'a koy.

**4. Test.** `test-engineer` agent'ı iş kuralı ve varsa consumer idempotency testlerini yazsın.
Issue'daki her kabul kriteri için en az bir test olsun.

**5. Review.** `code-reviewer` ve `security-reviewer` agent'larını **aynı turda paralel** çalıştır.

**6. Rapor.** Bana şunu ver: issue numarası/başlığı, değişen dosya listesi, kritik bulgular,
kabul kriteri karşılama durumu (her kriter için ✅/❌), benim elle yapmam gerekenler
(migration çalıştırma, config, gateway restart).

Kritik/bloklayıcı bulgu çıkarsa düzeltmeyi ilgili yazan agent'a geri gönder, reviewer'a düzelttirme.

Raporu issue'ya yorum olarak da düşmemi istersen söyle — **sormadan `gh issue comment` çalıştırma.**
