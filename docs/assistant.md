# Uygulama içi asistan (epic #495)

Ayrıntılı plan (tool kataloğu, ekran senaryoları) epic #495'te. Bu dosya alınan kararları ve kurulan altyapıyı tutar.

## Faz 0 kararları (#496, 2026-11-23)

- **Kapsam:** önce yalnız öğrenci (Faz 1a); veli (Faz 1b) sonra, öğretmen / admin Faz 2. `Assistant:Roles` ile açılır.
- **Saklama:** konuşmalar 30 gün, sonra kalıcı silinir; hesap silinince hemen. Admin içerik görmez, yalnız metrik.
- **KVKK:** geliştirme ve demo iç kullanıcılarla sürer; gerçek öğrencilere açmadan önce #522 (aydınlatma + yurt dışı aktarım
  rızası) zorunlu. Veli çocuğun asistanını kapatabilir (varsayılan açık, #523). Aktarımın hukuki dayanağı hâlâ açık.
- **Bütçe (demo / pilot):** kullanıcı başına günlük 0,10 USD, toplam günlük 5 USD, saatte 30 mesaj, öğrenci başına günde
  5 görsel açıklama. Varsayılan model Haiku 5.5.
- **Ödev dışı / kriz mesajları:** sabit kısa yönlendirme; kişisel kriz mesajına model üretimi değil sabit güvenli yanıt
  (güvendiği bir yetişkine / rehber öğretmene yönlendirme).
- Veliye sınıf ortalaması (#511) ve öğretmenin tek öğrenci sonucu (#516) sonraki fazlarda.

## Servis (#502)

`Services/AssistantService` — .NET, kendi veritabanı `assistant` (`Conversations`, `Messages`, `PendingActions`, `Usage`,
`Feedback`; kullanıcı Keycloak `sub` ile), migration'lar açılışta uygulanır. Port 8010, dışarıya yalnız gateway
`/api/assistant/{everything}` (Bearer) ile açılır; `/mcp` Faz 4'e (#518) kadar yönlendirilmez.

- **Kapı** (`AssistantGate`, her `/api/assistant/*` isteğinde, sırayla): token yok → 401; `Assistant:Enabled` kapalı ya da
  `Anthropic:ApiKey` boş → 503 `{ code: "AssistantDisabled" }`; kullanıcının rolü `Assistant:Roles`'ta yok → 403
  `{ code: "AssistantRoleNotEnabled" }`.
- **Varsayılanlar:** `appsettings.json` kapalı (`Enabled=false`, rol yok), Development açık ve yalnız `Student`.
  Prod / demo compose'da `ASSISTANT_ENABLED` (varsayılan `false`) ve `ASSISTANT_ROLES` (varsayılan `Student`).
- `GET /api/assistant/status` panelin (#507) kendini gösterip göstermeyeceği için: açık roller ve limitler.
- Limitler `Assistant:*` (`DailyBudgetUsdPerUser`, `DailyBudgetUsdTotal`, `MessagesPerHour`, `ImageExplanationsPerDay`,
  `RetentionDays`, `Model`); uygulanmaları #504 / #505'te.

## SSE spike sonucu (#502)

Yalnız Development'ta açık `GET /api/assistant/spike/stream?seconds=&intervalMs=` ile, Aspire'da doğrudan (8010) ve gateway
(5678) üzerinden aynı anda ölçüldü:

| Akış | Doğrudan | Gateway |
|---|---|---|
| 5 sn, 500 ms aralık | 11 olay, aralık 499–515 ms | 11 olay, aralık 498–526 ms |
| 120 sn, 5 sn aralık | 25 olay, tamamlandı | 25 olay, tamamlandı |
| 200 sn, 5 sn aralık | 41 olay, tamamlandı | 41 olay, tamamlandı |

- **Ocelot 24 SSE'yi tamponlamaz:** olaylar doğrudan servisteki aralıklarla gelir. Ayrı bir yol gerekmez.
- **Zaman aşımı yalnız yanıt başlıklarına kadar:** route'taki `QoSOptions.TimeoutValue` (180 sn; varsayılan 90 sn) başlıklar
  gelene kadar geçen süreyi sınırlar, açılmış akışı kesmez (200 sn'lik akış geçti). Sohbet ucu başlıkları hemen yazmalı,
  uzun model çağrısını akış içinde yapmalı.
- **Demo / prod yolu:** Caddy `text/event-stream`'i tamponlamadan geçirir; Cloudflare Tunnel boşta kalan bağlantıyı
  ~100 sn'de keser → sohbet ucu uzun beklemelerde düzenli `: keep-alive` yorumu gönderir (#504). Demoda ayrıca denenecek.
- **Asistan → exam API:** gateway'den değil, iç ağdan doğrudan (`ExamApi:BaseUrl`), kullanıcının kendi Bearer token'ıyla.
  Exam API'de IP'ye bağlı korumalar yalnız veli davet / aktivasyon uçlarında; oturumlu limitler `sub` ile. Asistan
  güvenilir proxy değildir (`ForwardedHeaders:KnownProxies`'e girmez), istemcinin IP'sini taşıyamaz. Veli tool'larında
  (Faz 1b) audit'e yazılan IP asistanın IP'si olur; o dilimde ele alınacak.
- MCP C# SDK ve Microsoft.Extensions.AI seçimi tool host'la birlikte #503'te (iskelet bunlara bağlı değil; BadgeService
  `Anthropic` 13.1.0 + `Microsoft.Extensions.AI` 10.11.0 kullanıyor).
