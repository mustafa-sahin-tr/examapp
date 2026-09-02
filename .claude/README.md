# Exam Platform — Agent Harness

Bu paket, mevcut `.claude/rules/` yapının üstüne bir agent ekibi kurar: 8 agent, 7 skill,
4 slash command ve 3 hook.

## Katmanlar ne işe yarıyor

Dördü farklı problemi çözer, birbirinin yerine geçmez:

| Katman | Ne yapar | Nerede çalışır |
|---|---|---|
| **rules/** (mevcut) | Her oturumda geçerli değişmez gerçekler (port haritası, dizin yapısı) | Ana context'e yüklenir |
| **skills/** | Tekrar eden işin adım adım prosedürü | Çağrıldığında context'e girer |
| **agents/** | Rol + araç kısıtı + izole context | Kendi context penceresinde |
| **hooks/** | Modelin ikna edilemeyeceği deterministik kural | Tool çağrısından önce/sonra, kod olarak |

Pratik kural: *bilgi* rules'a, *prosedür* skill'e, *rol ve izolasyon* agent'a,
*asla ihlal edilmemesi gereken* hook'a.

## Kurulum

```bash
# repo kökünde
cp -r agent-harness/.claude/agents   .claude/
cp -r agent-harness/.claude/skills   .claude/
cp -r agent-harness/.claude/commands .claude/
cp -r agent-harness/.claude/hooks    .claude/
chmod +x .claude/hooks/*.sh

# settings.json'ı mevcut içeriğinle birleştir (enabledMcpjsonServers korunmuş halde)
cp agent-harness/.claude/settings.json .claude/settings.json

# CLAUDE.md'yi agent ekibi bölümüyle güncelle
cp agent-harness/CLAUDE.md ./CLAUDE.md

claude   # yeni agents/ ve skills/ dizinleri için oturumu bir kez yeniden başlat
```

`jq` kuruluysa hook'lar onu kullanır, değilse `python3`e düşer. İkisi de yoksa hook sessizce geçer.

### Puppeteer'ı ana context'ten çıkarma (opsiyonel ama önerilir)

`ui-tester` agent'ı puppeteer'ı kendi frontmatter'ında inline tanımlıyor. `.mcp.json`'daki tanımı
silersen tool açıklamaları ana oturumu şişirmez, sadece `ui-tester` çalışırken yüklenir.
Aynı mantık `question-craetor.md` akışın için de geçerli: onu bir slash command'a çevirip
içeriden `ui-tester`'a devredebilirsin.

## Ekip

**Yazanlar** — `dotnet-api-dev`, `angular-dev`, `event-integration-dev`, `test-engineer`, `devops-aspire`
**İnceleyenler** (salt okunur, `Edit`/`Write` yok) — `code-reviewer`, `security-reviewer`
**Doğrulayan** — `ui-tester` (tarayıcı, salt okunur)

Ayrımın sebebi: kodu yazan model kendi kodunu savunmaya meyillidir. Reviewer'lardan `Edit` ve `Write`
araçlarını almak, "bulguyu sessizce düzeltip geçme" davranışını yapısal olarak imkânsız kılar.
Bulgu, yazan agent'a geri gider.

## Günlük kullanım

```
/feature Öğretmen bir testi arşivleyebilsin; arşivlenen test öğrenci listesinde görünmesin
/review
/ship
/onboard Services/BadgeService
```

Doğrudan çağırmak istersen: `@dotnet-api-dev` yaz veya "`angular-dev` agent'ı ile ... yap" de.

`/feature` planı gösterip **onay bekler**. Onay adımını atlamak istemezsin: 5 dosyalık bir hata
tek turda 5 agent'a yayılıyor.

## Orkestrasyon deseni

Ana oturum tech lead'dir. Kod yazmaz, devreder ve birleştirir.

```
ana oturum (lead)
├── Explore .................. keşif, ana context'i kirletmeden
├── dotnet-api-dev ........... backend
├── event-integration-dev .... outbox akışı        ─┐ sıralı: DTO'lar netleşmeden
├── angular-dev .............. frontend             ─┘ frontend başlamaz
├── test-engineer ............ testler
└── code-reviewer + security-reviewer ...... aynı turda, paralel
```

İki reviewer'ı aynı mesajda çağır — paralel çalışırlar. Backend ve frontend'i paralel çalıştırma;
frontend'in backend'in gerçek DTO'larına ihtiyacı var, yoksa uydurulmuş tiplerle çalışır.

## Hook'lar

| Hook | Olay | Davranış |
|---|---|---|
| `protect-migrations.sh` | PreToolUse(Edit\|Write) | `Migrations/*.cs` elle düzenlenirse **bloklar** (exit 2) |
| `secret-guard.sh` | PreToolUse(Edit\|Write) | `.env` yazımını, private key ve gömülü credential'ı **bloklar** |
| `dotnet-format.sh` | PostToolUse(Edit\|Write) | Düzenlenen `.cs` dosyasını biçimlendirir, asla bloklamaz |

`secret-guard.sh` sezgiseldir. Yanlış alarm verirse script içindeki muafiyet listesini genişlet;
hook'u tamamen kapatma. Halka açık bir şirkette bu, ödediğin en ucuz sigorta.

## Sonraki adım: dynamic workflow

Aynı orkestrasyonu tekrar tekrar çalıştırmak istediğinde, Claude Code'un dynamic workflow özelliği
(`v2.1.154+`, Pro'da `/config` içinden açılır) işi bir JavaScript orkestrasyon scriptine çevirir ve
onlarca subagent'ı paralel yürütür. Kullanışlı olduğu yer tek özellik değil, **repo geneli** işlerdir:

> Tüm servislerdeki controller'ları tarayan, `[Authorize]` olmadan eklenmiş veya kaynak sahipliği
> kontrolü olmayan endpoint'leri bulan bir workflow yaz. Her servis için ayrı subagent kullan,
> bulguları ikinci bir doğrulayıcı subagent'a teyit ettir.

Beğendiğin çalışmayı `/workflows` → ilgili run → `s` ile `.claude/workflows/` altına kaydedersin;
sonrasında `/<isim>` olarak çalışır. `/feature` ve `/review` gibi tek dalda çalışan işler için
komut + agent yapısı yeterli, workflow'a gerek yok.

## Bakım

- Agent tanımı düzenlendiğinde Claude Code birkaç saniye içinde yakalar; **yeni** bir dizin eklendiğinde
  oturumu yeniden başlat.
- Agent `description` alanlarını kısa tut. Hepsi her oturumda context'e girer.
- `memory: project` tanımlı agent'lar `.claude/agent-memory/` altında öğrendiklerini biriktirir;
  bu dizini repoya commit'lersen ekip ortak hafıza kazanır.
- Bir reviewer aynı bulguyu üçüncü kez raporluyorsa, o kural ya bir hook'a ya da bir skill'e taşınmalıdır.
