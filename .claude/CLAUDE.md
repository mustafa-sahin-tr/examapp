# Exam Platform

Kurallar `.claude/rules/` altında konuya göre ayrılmıştır:

- [architecture.md](.claude/rules/architecture.md) — servis haritası ve sorumluluklar
- [stack.md](.claude/rules/stack.md) — teknoloji yığını
- [workflow-rules.md](.claude/rules/workflow-rules.md) — değişiklik yaparken uyulacak desenler
- [local-dev.md](.claude/rules/local-dev.md) — port referansı ve servislerin başlatılması
- [project-structure.md](.claude/rules/project-structure.md) — her servisin içindeki dizin yapısı
- [angular-conventions.md](.claude/rules/angular-conventions.md) — standalone komponent, signal, Material, SCSS token

## Agent ekibi

Bu repoda özelleşmiş agent'lar tanımlıdır (`.claude/agents/`). Uygun işi kendin yapmak yerine devret:

| Agent | Alan |
|---|---|
| `dotnet-api-dev` | C# backend: controller, service, DTO, EF entity |
| `angular-dev` | `ui/` ve `auth-ui/`: sayfa, komponent, servis |
| `event-integration-dev` | Outbox, RabbitMQ, BadgeService consumer'ları |
| `test-engineer` | xUnit ve frontend testleri |
| `devops-aspire` | docker-compose, Aspire AppHost, portlar, deploy |
| `ui-tester` | Tarayıcıda uçtan uca akış doğrulama (Puppeteer) |
| `code-reviewer` | Kalite incelemesi (salt okunur) |
| `security-reviewer` | Güvenlik incelemesi (salt okunur) |

Kural: **yazan agent ile inceleyen agent aynı olmaz.** Reviewer'lar kod düzeltmez, bulgu raporlar;
düzeltme yazan agent'a geri gönderilir.

Birden fazla dosya/servis etkileyen işlerde `/feature`, incelemede `/review`,
PR öncesi `/ship` komutlarını kullan.

## Prosedürler

Tekrar eden işlerin adımları `.claude/skills/` altındadır: `dotnet-endpoint`, `angular-feature`,
`outbox-event`, `ef-migration`, `gateway-route`, `aspire-migration`, `security-review-checklist`.
İlgili işe başlarken önce skill'i oku.
