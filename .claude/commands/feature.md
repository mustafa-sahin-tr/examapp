---
description: GitHub issue'dan uçtan uca özellik geliştirme akışı (issue → plan → backend → frontend → review)
argument-hint: <issue numarası veya issue URL'i>
allowed-tools: Bash(gh issue view:*), Bash(gh issue comment:*), Bash(git status:*), Bash(git fetch:*), Bash(git checkout:*), Bash(git pull:*), Bash(git add:*), Bash(git commit:*), Bash(git push:*), Bash(gh pr create:*)
---

## Issue içeriği

**0. Issue'yu oku.** İlk iş olarak şunu çalıştır ve çıktısını tam oku:

`gh issue view $ARGUMENTS --json number,title,body,labels,url,comments`

Hata alırsan dur ve hatayı bana göster — tahminle plan yapma.

Bu issue'yu ekip olarak uçtan uca geliştir. Sen tech lead'sin; kodu kendin yazma,
ilgili agent'a devret ve çıktıları birleştir.

Issue gövdesi eksik veya belirsizse (kabul kriteri yok, hangi servis etkileniyor belli değil,
çelişkili istekler var) kod yazmaya başlama — eksikleri bana madde madde sor.
Yorumları da oku; kabul kriteri çoğu zaman orada netleşiyor.

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

Planı bana göstermeden önce `grilling` skill'ini kullanarak planı kendi içinde sorgula
(varsayımlar, kaçırılan uç durumlar, kabul kriteriyle çelişki var mı).

Planı bana göster ve **onay bekle**. Onaysız kod yazma.

**1.5. Worktree aç.** Plan onaylandıktan sonra, kod yazılmadan önce:

- Aynı ana klasörde başka bir issue için zaten paralel bir `/feature` çalışıyor olabilir —
  bu yüzden ana klasörde `git checkout -b` YAPMA, her issue kendi git worktree'sinde çalışsın.
- `EnterWorktree` tool'unu çağır, `name: "issue-$ARGUMENTS"`. Bu, `.claude/worktrees/issue-$ARGUMENTS`
  altında **yeni bir branch üzerinde izole bir checkout** oluşturur ve session'ın çalışma dizinini
  oraya taşır (varsayılan olarak `origin/master`'dan dallanır, ana klasördeki kirli/commitlenmemiş
  değişikliklerden etkilenmez). Mater'ın pull edilip son haline geldiğinden emin ol sonra worktree'yi oluştur.
- Worktree oluştuktan sonra branch adını `feature/issue-$ARGUMENTS-<slug>` olacak şekilde yeniden adlandır
  (`git branch -m feature/issue-$ARGUMENTS-<slug>`) — slug: issue başlığından türetilmiş kısa,
  küçük harf, tire ile ayrılmış, Türkçe karakterleri sadeleştirilmiş en fazla 4-5 kelime.
- Zaten bu issue için açılmış bir worktree/branch varsa yenisini açma; `EnterWorktree`'ye
  `path` vererek ona geç ve bana söyle.
- Worktree yolunu ve branch adını raporda belirt.
- Claude'nin çalıştığı session ismini de `rename` komutu ile `issue-$ARGUMENTS` yap.
- Bundan sonraki tüm adımlar (agent devirleri, commit, push, PR) bu worktree içinde çalışır —
  ana klasöre dönmeye gerek yok.

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

**7. PR.** Rapor sonrası bana sor: PR açayım mı? **Onay gelmeden `gh pr create` çalıştırma.**

Onay verirsem:
- Değişiklikleri commit et (issue başlığından türetilmiş anlamlı bir mesaj, sonuna `#$ARGUMENTS`)
  ve branch'i push et: `git push -u origin HEAD`
- PR'ı aç:
  `gh pr create --base main --title "<issue başlığı>" --body "..."`
- Body şunları içersin: tek cümlelik özet, kabul kriteri checklist'i (adım 6'daki ✅/❌ tablosu),
  benim elle yapmam gerekenler (migration, config, gateway restart), ve son satırda `Closes #$ARGUMENTS`
- Bloklayıcı olmayan ama açık kalan bulgu varsa PR'ı `--draft` aç ve nedenini bana söyle
- PR URL'ini bana ver

`Closes #$ARGUMENTS` satırını atlama — merge'de issue'yu otomatik kapatan tek şey o.

**8. Worktree temizliği.** PR açıldıktan sonra worktree'yi hemen silme — PR review sürecinde
üzerinde tekrar çalışman gerekebilir. Bana sormadan `ExitWorktree` çağırma. Ben "worktree'yi
kapat" dersem, PR merge olduysa `action: "remove"`, olmadıysa `action: "keep"` kullan (keep,
branch ve dizini diskte bırakır, session ana klasöre döner).