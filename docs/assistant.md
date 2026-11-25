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
- Limitler `Assistant:*` (`DailyBudgetUsdPerUser`, `DailyBudgetUsdTotal`, `MessagesPerHour`, `ExplanationsPerDay`,
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

## Tool host (#503, öğrenci dilimi)

`Services/AssistantService/Tools/` — her tool bir kez tanımlanır (`IAssistantTool`: ad, açıklama, roller, JSON şema,
`ExecuteAsync`); sohbet döngüsü (#504) ve ileride `/mcp` (#518) `ToolRegistry`'den okur. MCP SDK / Microsoft.Extensions.AI
bu dilimde eklenmedi: tool arayüzü bunlara bağlı değil, model çağrısı #504'te seçilecek.

- **Downstream:** `DownstreamClient`, exam API'yi iç ağdan (`ExamApi:BaseUrl`, boşsa açılışta patlar) kullanıcının kendi
  Bearer token'ı ve `Accept-Language`'ıyla çağırır. API'nin `errorCode`'u tool hatasına aynen geçer; yoksa durum kodundan
  (`Forbidden`, `NotFound`, `Conflict`, …). Ağ hatası `DownstreamUnavailable`.
- **Rol süzgeci:** model yalnız rolünün tool'larını görür (`ForRoles`); `ToolExecutor` her çağrıda rolü yeniden denetler
  (`ToolNotAllowed`).
- **Sonuç biçimi:** `data` modele, `cards` (kpi, topicTable, confirm) ve `uiActions` (navigate, suggestions) panele gider.
  Kartlardaki sayılar sunucu verisinden kurulur, modelin yazdığı sayılardan değil. Listeler en çok 20 satır; görsel URL'leri
  ve iç alanlar atılır; kullanıcı yazımı metin (test adı) `<untrusted>…</untrusted>` ile işaretlenir.
- **Yazma akışı:** `IAssistantWriteTool`'un model çağrısı yalnız önizler ve sahibi kullanıcı olan bir `PendingAction`
  (10 dk) yazar; değişiklik `POST /api/assistant/actions/{id}/confirm` ile olur. Talep tek koşullu UPDATE ile alınır
  (çift dokunuş iki kez çalıştırmaz). Başkasının / bilinmeyen id → 404, süresi dolmuş → 410, karar verilmiş → 409;
  `.../reject` → 204. Commit başarısızsa eylem `Failed`.
- **Ekranlar:** `ui_open_screen` yalnız listedeki anahtarları açar (rotayı sunucu kurar; `/testsolve` yok, asistan test
  sırasında kapalı).
- `GET /api/assistant/tools` rolün tool listesi; yalnız Development'ta `POST /api/assistant/tools/{name}/invoke` (canlı deneme).

| Tool | Exam API | Not |
|---|---|---|
| `get_my_profile` | `GET api/student/profile` | kpi kartı |
| `search_taxonomy` | `GET api/subject/search` | en çok 10 sonuç |
| `search_help` | — | kılavuz indeksi (#506), en çok 3 makale |
| `get_my_weak_topics` | `GET api/student/me/topic-performance` | isabeti %70 altı en çok 5 konu (yoksa boş liste); `days` 7–90 (API sınırı) |
| `get_my_performance_summary` | aynı uç | ders toplamları sunucu kodunda hesaplanır |
| `get_daily_set_status` | `GET api/practice/daily` | |
| `list_my_completed_tests` | `GET api/worksheet/CompletedTests` | sayfa 10 |
| `get_test_result` | `GET api/worksheet/test-canvas-instance-result/{id}` | bitmemiş / başkasının test boş döner (`TestNotFinished`); çoktan seçmelide `isCorrect` null, `question.correctAnswerId` ile sayılır |
| `start_daily_practice` ✎ | `POST api/practice/daily/start` | başlamış set kapsamı değiştirmez |
| `start_practice_session` ✎ | `POST api/practice/sessions` | konu / ders ya da yanlışlar / tekrar şart, 1–50 soru |
| `ui_open_screen`, `ui_suggest_actions` | — | panel eylemi |

Canlı deneme (Aspire, gateway üzerinden, öğrenci hesabı): 11 tool listelendi; okuma tool'ları gerçek veriyle döndü, başkasının
test id'si `TestNotFinished`, `ui_open_screen admin` reddedildi; yazma akışında ret → 204, retten sonra onay → 409, onay → 200
ve pratik oturumu açıldı (`/practice?session=…`), ikinci onay → 409, bilinmeyen id → 404.

## Sohbet ucu (#504)

`POST /api/assistant/chat` `{ conversationId?, message, context: { route, worksheetId?, instanceId?, locale } }` → SSE.
Olaylar: `text {delta}`, `tool_start {tool}`, `tool_end {tool, ok, errorCode, ms}`, `card` (kpi, topicTable),
`confirm_required` (yazma tool'unun onay kartı, `pendingActionId`), `ui_action` (navigate, suggestions),
`done {conversationId, messageId, usage}`, `error {code}`. Uzun beklemede 15 sn'de bir `: keep-alive` yorumu.

- **Akış öncesi hatalar (HTTP):** boş / 2000 karakterden uzun mesaj → 400 `InvalidMessage`; `context.route` `/testsolve…`
  ya da `/activity/…` → 409 `AssistantClosedDuringTest` (panel UI'da gizli, sunucu da reddeder); başkasının konuşması → 404;
  limit → 429 `AssistantBudgetExceeded` (`limit`: `MessagesPerHour` / `UserBudget` / `TotalBudget`, `Retry-After`).
- **Akış içi hatalar (`error` olayı):** `TokenExpired` (tool'un exam API çağrısı 401 aldı; tur saklanmaz, istemci token'ı
  yenileyip aynı mesajı yeniden gönderir; token'ı erişimde zaten süresi dolmuşsa kapı 401 verir), `AssistantBudgetExceeded`
  (tur ortasında bütçe doldu), `AssistantDeclined` (model reddetti), `AssistantUnavailable` (model hatası; Development'ta
  `detail` ile nedeni). Yarım kalan ilk tur boş konuşma bırakmaz.
- **Model:** Anthropic SDK'nın Microsoft.Extensions.AI köprüsü (`AsIChatClient`, BadgeService sınıflandırıcısıyla aynı),
  `UseLogging` + `UseOpenTelemetry` (Aspire panosunda gen_ai span'leri). `Assistant:Model` (Haiku 5.5), `Effort` (low),
  `MaxOutputTokens` (1024 / çağrı), `MaxToolCallsPerTurn` (6; dolunca son çağrı tool'suz), `MaxHistoryMessages` (30).
  Yükseltme modeli (çok adımlı işler) öğretmen / veli dilimlerine kaldı.
- **Prompt cache:** tool tanımları (rol başına, ada göre sıralı) + sabit sistem metni tek önek; kesme noktası sabit metnin
  sonunda (`CacheTtl`, 5m). Ekran bağlamı ve tarih kesme noktasından sonra. Sabit metin `SystemPrompt.Revision` ile.
- **Sistem istemi:** veriyi tool'dan oku, sayı uydurma; kartlar zaten görünür; yazma tool'u yalnız onay kartı hazırlar;
  sınav bütünlüğü; cinsiyet çıkarımı yok; yargılamayan dil; `<untrusted>` içindeki talimatlar yok sayılır; kapsam dışı →
  tek cümle yönlendirme. **Kriz:** model `safety_redirect` tool'unu çağırır, sunucu sabit güvenli metni (`SystemPrompt.CrisisResponse`)
  gönderir; o metni model üretmez (#496).
- **Limitler:** saatte mesaj (kullanıcı), günlük USD (kullanıcı + toplam) Redis sayaçlarıyla (`Redis:Configuration`; yoksa /
  erişilemezse süreç içi sayaç, uyarı loglanır). Harcama her model çağrısından sonra token × fiyat (`InputUsdPerMTok`,
  `OutputUsdPerMTok`; cache yazma ×1,25, okuma ×0,1) ile eklenir; tur yalnız bütçede yer varken başlar, tur içinde her
  model çağrısından önce yeniden bakılır. `GET /api/assistant/usage/me`.
- **Saklama:** turun mesajları (kullanıcı, asistan, tool çağrısı ve sonucu) MEAI içerik JSON'u olarak tur bitince yazılır;
  `Usage` günlük satırı. Loglarda mesaj metni ve tool sonucu yok: tur başına mesajın hash'i, tool adı / süresi / hata kodu,
  ilk token ve tur süresi, token (cache yazma / okuma dahil) ve USD.
- `POST /api/assistant/messages/{id}/feedback` `{ rating: up|down, reason? }` (`wrongInfo`, `notHelpful`, `tooLong`,
  `inappropriate`, `other`; yalnız 👎'de saklanır) → 204; kendi asistan mesajı değilse 404; yeni oy eskisinin yerine geçer.

**Yerel ölçüm (2026-11-23, Aspire, gateway üzerinden, Haiku 5.5, öğrenci hesabı, 7 tur):** ilk token p50 1,76 sn (en çok
2,8 sn; ilk turda önbellek yazımıyla), tur p50 3,4 sn, p95 4,8 sn — hedefler (p50 ilk token < 2 sn, p95 tur < 8 sn) tuttu.
Önbellek: ilk tur 2954 token yazdı, sonraki her model çağrısı aynı öneki okudu (`cacheReadTokens`). Tur başına 0,0002–0,0011 USD.
Canlı denemede bulunan: model öneri çiplerinden sonra cevabını yeniden yazıyordu → yalnız panel tool'ları (`ui_suggest_actions`,
`ui_open_screen`) cevaptan sonra gelirse tur biter, model yeniden çağrılmaz; olmayan butondan söz ediyordu → sistem istemi r2.

## Konuşma geçmişi, saklama ve sıfırlamada temizlik (#505)

- **Uçlar (yalnız sahibi; başkasınınki 404, var olup olmadığı sızmaz):** `GET /api/assistant/conversations` (son 50, en yeni
  önce: `id`, `title`, `createdAt`, `updatedAt`), `GET /conversations/{id}` (yalnız kullanıcı mesajları ve asistanın metni;
  tool çağrıları ve sonuçları sunucuda kalır), `DELETE /conversations/{id}` → 204, `DELETE /conversations` (geçmişimi temizle)
  → `{ deleted }`. Mesajlar, onay bekleyen işlemler ve geri bildirimler konuşmayla birlikte gider (FK cascade). Yöneticiye
  konuşma okuyan uç yok; yönetici yalnız toplu sayı görecek (ileride).
- **Saklama süresi:** `RetentionPurgeService` açılıştan 1 dk sonra ve her `Assistant:PurgeIntervalMinutes` (60) dakikada
  `RetentionDays`'ten (30) eski olanları kalıcı siler: son etkinliği eski konuşmalar, hâlâ kullanılan konuşmanın eski mesajları,
  eski onay bekleyen işlemler ve `Usage` günleri. Asistan kapalıyken de çalışır. Hata loglanır, sonraki turda yeniden dener.
- **Öğrenci sıfırlama:** `StudentResetJob` (#396) exam verisini sildiği SaveChanges içinde outbox'a `StudentProgressResetEvent`
  (`EventId`, `KeycloakId`, `UserId`, `StudentId`, `ResetAtUtc`) yazar; AssistantService (`assistant-service` kuyruğu, dead-letter
  `assistant-service_error`, 1/5/15 sn retry) öğrencinin sıfırlamadan önce başlattığı konuşmaları siler. Tekrar gelen mesaj fazladan
  bir şey silmez. `Usage` sayaçları kalır (içerik yok; günlük bütçe sıfırlamayla açılmasın).
- **RabbitMQ:** servise özel `assistant_service` kullanıcısı (#279): yalnız bu event'i okur, hiçbir şey yayınlayamaz.
  Parola `RABBITMQ_ASSISTANT_SERVICE_PASSWORD` (`.env`, `.env.demo`, prod) / `rabbitmq-assistant-service-password` (AppHost).
  Production'da `RabbitMQ:Host` zorunlu (tüketicisiz açılış sıfırlanan öğrencinin konuşmalarını bırakırdı).
- **Hesap silme:** bugün üründe kullanılmış bir hesabı silen akış yok (auth-api yalnız hiç kullanılmamış çocuk hesabını geri
  alıyor; onun asistan verisi olmaz). Silme akışı eklenince aynı temizlik (`AssistantDataCleaner.EraseUserConversationsAsync`)
  kendi event'iyle bağlanır.

## Kullanım kılavuzundan yardım araması (#506)

Kılavuz `docs-private/` altında beklenmişti ama orada yok (tam kılavuz #353'te açık). Asistanın kullandığı yayınlanabilir
kısım repoda: `Services/AssistantService/Help/<rol>/*.md`, bir makale bir soru. Başta front matter (`slug` kebab-case,
`title`, `roles` virgülle realm rolleri, `keywords` virgülle, kök biçiminde: `veli`, `ekle`), sonra kısa Markdown.
Faz 1a'da yalnız öğrenci makaleleri var (15); menü adları `ui/public/i18n` metinlerinden alındı, ekran değişince makale de güncellenir.

- **İndeks:** csproj `Help/**/*.md`'yi gömülü kaynak yapar; `HelpIndex` açılışta kurulur. Makale eklemek / değiştirmek yeni
  build'le indekse girer, elle adım yok. Bozuk makale (front matter, bilinmeyen rol, tekrar eden slug, boş metin) açılışta patlar.
- **Arama:** taksonomi aramasıyla aynı normalizasyon (`ExamApp.Foundation.Text.SearchText`: Türkçe harf sadeleştirme,
  sözcüklere bölme, kök eşleşmesi "velimi" ↔ "veli"). Soru sözcükleri ("nasıl", "nerede", …) atılır. Başlık + anahtar
  sözcük eşleşmesi 3, metin eşleşmesi 1 puan; başlıkta ya da anahtar sözcükte hiç eşleşme yoksa makale dönmez. En çok 3 sonuç,
  yalnız çağıranın rolündeki makaleler.
- **Tool:** `search_help` (öğrenci) `{ slug, title, text }` döner; sistem mesajı (Revision 3) uygulama kullanımı sorularında
  bu tool'u çağırıp yalnız dönen metinden cevaplamasını söyler. `examapp://help/{slug}` resource'u `/mcp` ile (#518) gelir.

Canlı deneme (Aspire, öğrenci): "Velimi nasıl eklerim?", "Şifremi nasıl değiştiririm?", "Ödevlerimi nerede görürüm?" doğru
makaleyle cevaplandı (ilk token p50 1,4 sn); konu dışı soru tool çağırmadı.

## Velinin asistanı kapatması (#523)

- Seçim veli başına, bağlantıda tutulur: `ParentStudentLinks.AssistantBlocked` (varsayılan `false` = açık). Çocuğun velisi silinmemiş
  **herhangi bir** Active bağlantısında kapalıysa asistan kapalıdır; koparılan bağlantının seçimi sayılmaz (`ParentAssistantControl`).
- Veli: `PUT /api/exam/parent/children/{studentId}/assistant` `{ allowed }` → `{ allowed, effectiveAllowed }`. Kapı
  `IParentChildAccess` (bağlı olmayan / Pending 404), her değişiklik `ParentAccessAudit` `children.assistant-setting` (ResourceId 1 açık,
  0 kapalı). *Çocuklarım* kartında anahtar; `my-children` yanıtında `assistantAllowed` + `assistantBlockedByOtherParent`.
- Öğrenci: `GET /api/exam/student/assistant-access` → `{ allowed }` (asistan paneli #507 düğmeyi buna göre gizler; derin linkle açılırsa
  "Velin asistanı kapattı" mesajı orada).
- AssistantService (`Security/ParentControl`): öğrenci rolünde her `/chat` turunda ve onaylanan eylemde (`actions/{id}/confirm`) aynı ucu
  öğrencinin token'ıyla iç ağdan okur, sonucu kullanıcı başına **30 sn** önbellekler (kapatma en geç 30 sn'de etkili). Kapalı →
  403 `AssistantDisabledByParent`; exam API cevap veremezse 503 `AssistantUnavailable` (önbelleğe alınmaz, kapalı kabul); exam API'nin
  kendi 403 kodu (ör. `ParentRequired`) aynen geçer. Konuşma listesi / silme / kullanım açık kalır (öğrenci geçmişini silebilsin).

## Panel (#507)

`ui/src/app/shared/components/assistant-panel/`: `AssistantLauncherComponent` (FAB, `Ctrl+K` / `⌘K`, CDK Overlay) ve
`AssistantPanelComponent` (mesajlar, kartlar, çipler, bağlam şeridi, 👍/👎, kalan mesaj). Durum `AssistantChatStore`
(kökte; panel kapanınca konuşma ve akan tur sürer, yenilemede yeni konuşma), görünürlük `AssistantPanelService`.

- **Görünürlük:** kabuk açılırken bir kez `GET /api/assistant/status`. Kapı bu ucu (yalnız GET) her oturum açmış kullanıcıya
  geçirir; asistan kapalı / rol açık değilse 200 + `{ enabled: false, code }` döner (her sayfada console'a 403 / 503 düşmesin).
  Öğrencide ayrıca `GET /api/exam/student/assistant-access` (#523): kapalıysa giriş noktaları gizli, panel açılırken yeniden
  bakılır; sohbette `AssistantDisabledByParent` gelirse de gizlenir. Test çözme / etkinlik oynatıcısı (`isExamRoute`) ve tam
  ekranda gizli, açık panel kapanır. Herkese açık sayfalar kabuğun dışında.
- **Yerleşim:** masaüstünde 440px sağ panel (üst çubukta "Asistan" düğmesi + sağ altta FAB), mobilde tam ekran (FAB alt
  navigasyonun üstünde; gezinme eylemi paneli kapatır). Overlay `mat-sidenav-content` stacking context'inin dışında.
- **Akış:** `fetch` + Bearer (süresi dolmak üzereyse önce yenilenir; 401'de bir kez yenile-tekrarla; `TokenExpired` olayında
  aynı mesaj bir kez yeniden gönderilir). `navigate` yalnız `/` ile başlayan uygulama yolunu açar. Metinde yalnız `**kalın**`
  işlenir (innerHTML yok).
- **Bağlam:** `route` (sorgu dizesi atılır), `/test/:id`'de `worksheetId`, dil. Seçili çocuk veli dilimiyle gelecek.
- i18n `public/i18n/assistant/{tr,en}.json`; üst çubuk düğmesi kök sözlükte `layout.toolbar.assistant`.

Canlı deneme (Aspire, öğrenci): `Ctrl+K` açtı; "Zorlandığım konular" turu, öneri çipleri ve kalan mesaj göründü; "5 soruluk pratik
başlat" onay kartı → Başlat → `/practice?session=…` açıldı; `Esc` kapattı; `/testsolve/…`'da FAB yok, kısayol çalışmadı; 390px'te
tam ekran. Veli hesabında `/status` 200, giriş noktası yok, console temiz.

## Testler: yetki, prompt injection, altın set, canlı E2E (#509)

- **Yetki (LLM'siz, her PR'da):** `tests/AssistantService.Tests` — başkasının konuşması / eylemi 404, onaysız yazma yok,
  rolü kapalı 403, `/testsolve` 409, bitmemiş testin sonucu yok (`ToolTests`, `ChatEndpointTests`, `HistoryRetentionTests`).
- **Prompt injection:** kullanıcının yazdığı metin (bugün yalnız test adları) `ToolJson.Untrusted` ile tek `<untrusted>`
  sarmalında gider; `<` / `>` `‹ ›` olur, `</untrusted>` ile dışarı çıkılamaz (`User_text_cannot_close_the_untrusted_wrapper`,
  `Instructions_in_a_test_name_reach_the_model_only_as_wrapped_data`). Anthropic SDK tool sonucunu yeniden serileştirir; model
  etiketleri düz metin görür (JSON'daki `<` kaçışı istek gövdesinde çözülür, 2026-11-24 denendi). Yeni bir tool kullanıcı /
  öğretmen metni döndürürse (yorum, öğretmen notu, ödev başlığı) o alan da `Untrusted` ile sarılır.
- **Altın set:** `tests/AssistantService.Tests/Golden/student.json` (sahte exam API verisi + 22 öğrenci senaryosu) ve
  `GoldenSetTests` (`Category=Golden`, PR koşusunda filtreli; `ASSISTANT_GOLDEN_API_KEY` yoksa atlanır). Kural puanı: çağrılan /
  çağrılmaması gereken tool, onay kartı sayısı, onaylanınca exam API'ye giden gövde, cevapta geçmesi gereken kelimeler (Türkçe
  normalize); `rubric` varsa LLM hakem (`claude-sonnet-5-5`). Rapor: geçen / kalan + tur başına maliyet. Koşu:
  `.github/workflows/assistant-golden.yml` (elle + prompt / tool / kılavuz değiştiren PR'lar; secret `ASSISTANT_GOLDEN_API_KEY`
  tanımlı değilse yeşil atlar) ya da yerelde `ASSISTANT_GOLDEN_API_KEY=… dotnet test tests/AssistantService.Tests --filter Category=Golden`.
  İlk koşu (2026-11-24, Haiku 5.5, prompt r3): 21/22 → hakem ölçütü düzeltildi; tur başına ≈ 0,00036 USD, set ≈ 0,008 USD + hakem.
  Veli seti `Golden/parent.json` (#1336, 17 senaryo, iki çocuk; ikincinin paketinde analiz yok → 403): 2026-11-24 prompt r4
  17/17, tur başına ≈ 0,00048 USD. Başarısız senaryoda modelin cevabı da yazılır.
- **Canlı E2E:** `deploy/scripts/assistant-e2e.mjs --user <öğrenci> [--parent <veli>] [--confirm]` (`ASSISTANT_E2E_PASSWORD`, gateway :5678):
  A zorlandığım konular, B açılar günlük seti tek onay kartı (`--confirm` seti açar, veri değiştirir), test sırasında 409.
  C (`--parent`, #1336) "Çocuğum bu hafta nasıl, geciken ödevi var mı, nerede zorlanıyor?" → `list_my_children` + çocuk
  tool'ları. 2026-11-24 dev'de C PASS (selin.yildiz → Ali); B dev taksonomisinde birkaç "açı" alt konusu olduğundan bazen
  hangisi diye sorar (kurala uygun).

## Pratik başlatma kaynağı (#524)

`PracticeSession.StartedVia` (`Ui` = 0 varsayılan, `Assistant`, `DeepLink`, `Notification`): serbest pratik ve günlük setin
oturumu. `POST api/practice/sessions` ve `POST api/practice/daily/start` isteğe bağlı `startedVia` alır; yalnız bu adlar
(büyük/küçük harf fark etmez, sayı yok), başka değer 400 `InvalidStartedVia`. Günlük set idempotent: kaynak yalnız oturum ilk
açılırken yazılır. Asistanın iki yazma tool'u `Assistant` gönderir; UI yalnız `?session=` linki açılamayınca (#508) kurulumdan
başlatılan oturumda `DeepLink` gönderir. İstemciden geldiği için yalnız ürün metriğidir (yetki / puan buna bağlanmaz).
Sayım: `IPracticeSessionService.CountStartsBySourceAsync(since)` → kaynak × (günlük / serbest) adetleri; admin özeti (#513) bunu
gösterecek. Epic ölçütü: `Assistant` / tümü ≥ %15. `Notification` bugün gönderen yok (bildirimden başlatma gelince).

## Veli dilimi (#1336, Faz 1b)

- **Tool'lar (yalnız Parent):** `list_my_children` (`api/parent-links/my-children`, yalnız Active: `studentId`, ad, sınıf;
  Pending sayısı), `get_child_weekly_summary` (`summary`), `list_child_assignments` (`assignments`, `status`
  overdue / pending / completed), `get_child_test_result` (`test-results/{id}`; #1310 soru / karalama listesi modele gitmez),
  `get_child_weak_topics` (`topic-performance`, `minQuestions=5`, %70 altı; çocuğun paketinde analiz yoksa 403 olduğu gibi
  iletilir), `get_child_progress`, `get_child_schedule` (`from`/`to` ikisi birlikte, `yyyy-MM-dd`). Hepsi exam API'nin veli
  uçları (`api/parent/children/{studentId}/…`); veli–çocuk bağı, paket kapısı, oran sınırı ve `ParentAccessAudit` orada kalır.
  Başka velinin çocuğu / Pending bağlantı 404 → `NotFound`.
- **Saatler:** veli tool'ları UTC anları (`startAt` / `endAt` / `deadline` / `lastActivityAt`, test `startedAt` / `finishedAt`)
  İstanbul saatiyle `yyyy-MM-dd HH:mm` verir (`ParentApi.LocalTimes`); model 12:00Z'yi 15:00 yerine "12:00" diyordu.
- **Veri azaltma:** modele çocuğun yalnız ilk adı gider (soyadı, okul, öğretmen adı gitmez); ad ve ödev / plan başlıkları
  `<untrusted>` ile işaretli. Audit tablosunda IP alanı yok: "asistanın IP'si yazılır" endişesi geçersiz (kayıt veli ve çocuk id'si).
- **Ortak tool'lar** (`search_help`, `ui_open_screen`, `ui_suggest_actions`) öğrenci ve veliye açık; ekranlar rol başına
  (`parentDashboard` `/parent`, `childDashboard` `/parent?child={studentId}`, `myChildren`). Kılavuzda veli makaleleri
  `Help/parent/` (panel, çocuk hesabı, kodla bağlanma, şifre sıfırlama, ders onayı, asistan ayarı).
- **Prompt (r4):** veli sürümü ayrı (`siz`, önce `list_my_children`, birden çok çocukta mesajdaki ada göre, yoksa sor; çocukları
  karıştırma; başka öğrencilerle karşılaştırma yok; tek tek soru / şık gösterilmez). Kriz metni veliye ayrı
  (`ParentCrisisResponse`). Öğrenci prompt'una: konu açıksa soru sayısı sormadan kart hazırla.
- **Panel:** veli boş durum metni ve önerileri (`parentEmpty`, `parentStarters`: haftası, geciken ödev, zorlandığı konular, şifre).
- **Açma:** `ASSISTANT_ROLES` virgülle (`Student,Parent`); dev `appsettings.Development.json` ikisini de açar. Demo / prod'da veli
  ayrıca açılmalı (deploy kararı).
- Sonraki dilim: `list_pending_lesson_approvals`, `explain_parent_notifications`, veli yazma tool'ları.

### Etkinlik trendi, çocukları karşılaştırma, haftalık rapor (#511, Faz 2)

- **Uç:** `GET api/parent/children/{studentId}/activity?days=` (7–28, varsayılan 14; dışı 400 `invalidDays`): son `days` yerel gün
  (bugün dahil) gün gün cevap, doğru ve puan (`StudentDailyXps`), aynı toplamlar önceki `days` gün için (`previous`). Cevap kuralı
  özetle aynı (`ParentDashboardService.DailyAnswersAsync`; `WeekAnswersAsync` artık onu toplar): worksheet cevabı `UpdateTime` +
  pratik, pas hariç. Yalnız sayılar; saat bilgisi yok. Active bağlantı kapısı, `children.activity` audit, `ChildActivityPolicy`
  oran sınırı, paket kapısı yok (özet gibi). IDOR / rol matrisi `ParentEndpointCatalog` ile otomatik koşar.
- **Tool'lar:** `get_child_activity_trend` (`days` 7–28, varsayılan 7; `answeredChangePct`, önceki dönem boşsa null; KPI kartı:
  çözülen, önceki dönem, doğruluk, aktif gün, puan), `compare_children` (velinin Active çocukları, en çok 6, her biri için aynı uç;
  biri hata verirse diğerleri gelir; kart yok), `get_child_weekly_report` (#1112 `weekly-report`; `weeksAgo` 0–11 API'nin son biten
  haftasından geri sayılır, servis kendi takvimini kullanmaz; paket dışı 403 → `Forbidden`; deneme adı `<untrusted>`).
- **Prompt (r8):** "geçen haftaya göre" → trend; biten hafta → haftalık rapor; çocukları yalnız veli isterse ve tarafsız karşılaştır,
  sıralama yok.
- **Sınıf ortalaması** (#496 madde 5) bu dilimde yok; ders talebi listesi + onay ekranı (onay / ret asistanda yok, karar
  2026-11-24) ve öğretmen arama ayrı PR.
- Altın veli seti: `trend`, `weekly-report`, `weekly-report-forbidden`, `compare-siblings`.

### Ders talepleri ve öğretmen arama (#511, dilim 2)

- `list_pending_lesson_requests`: `api/parent/booking-requests` + `reschedule-requests` (velinin bütün Active çocukları); çocuğun ilk
  adı, gün ve saatler İstanbul saatiyle (`start` / `end` / `expiresAt`, ertelemede `proposedStart` / `proposedEnd`, `canApprove`),
  öğretmen adı yok. **Onay / ret asistanda yok** (karar 2026-11-24): model veli panelini (`parentDashboard`) açar.
- `search_tutors_for_child`: `subject` adı verilmezse çocuğun özetindeki en zayıf konunun dersi (`basis: weakTopic`; özet çağrısı
  bağlantıyı da doğrular, başka velinin çocuğu `NotFound`; zayıf konu yoksa `NoSubject`). Ders id'si `api/subject/search`, öğretmenler
  `api/Teacher/search?subjectId=&take=5` (#95 / #439, yalnız onaylı bağımsız öğretmen). Ad ve tanıtım yazısı `<untrusted>`, yazı 200
  karakter. Randevu asistanda yok: yeni ekran anahtarı `childTutors` (`/my-children/{studentId}/tutors`).
- Prompt r9. Altın veli seti: `lesson-requests`, `lesson-approve-refused`, `tutors-weak-subject` (tanıtım yazısında talimat).

## Soru açıklama ve son yanlışlar (#510, Faz 2)

Karar (2026-11-24): açıklama **yalnız öğrencinin cevapladığı sorular** için (bitmiş test ya da cevaplanmış pratik sorusu);
cevaplanmamış soruya ipucu yok (issue'daki `hint-context` / `get_hint` yapılmadı); öğrenci başına günde 20 açıklama.

- **Exam API** (`StudentQuestionExplainController`, yalnız `Student`, öğrenci token'dan; konu performansının hız sınırı):
  - `GET /api/student/me/questions/{id}/explain-context` → kaynak (`test` / `practice`), sonuç, ders / konu / alt konular,
    şık sayısı, öğrencinin şıkkı ve doğru şık (harf), soru metni (sınıflandırıcı metni: kapsam, yönerge, kök, şıklar; çözüm yok)
    ve görsel (JPEG, uzun kenar 1568, base64). Cevaplanmamış / başkasının / olmayan soru 404 `QuestionNotAnswered`.
    **Sınav bütünlüğü:** soru öğrencinin süren testinde ya da ona atanmış (atama süresi bitmemiş) ve bitirmediği bir testteyse
    pratikte çözülmüş olsa bile 403 `QuestionInOpenTest`. Etkinlikler test sayılmaz.
  - `GET /api/student/me/mistakes?subjectId&topicId&days=30&take=20` → bitmiş test + pratik yanlışları, soru başına bir satır
    (son yanlış zamanı, kaç kez, sonra doğru çözüldü mü), yeniden eskiye. Cevaplar konu raporuyla aynı okuyucudan
    (`StudentAnswerReader`: süren test okunmaz, sıfırlama öncesi sayılmaz). `days` 1–90, `take` 1–20, dışı 400.
- **Asistan:** `explain_question` (görseli tool sonucunun içinde modele verir) ve `get_my_recent_mistakes`; ikisi yalnız öğrenci.
  Kota `Assistant:ExplanationsPerDay` (20; eski `ImageExplanationsPerDay` yerine), Redis sayacı `assistant:explain:{sub}:{gün}`,
  yalnız başarılı açıklama sayılır, aşımda tool hatası `ExplainQuotaExceeded` (API çağrılmaz).
- **Görsel saklanmaz:** soru görseli yalnız o turdaki model çağrılarına gider; konuşmaya tool sonucunun metni yazılır, sonraki
  turlarda görsel yeniden gönderilmez (veritabanı şişmez, görsel her turda yeniden faturalanmaz).
- Prompt r5: yanlışlar listelenir, açıklama öğrencinin seçtiği soru için çağrılır; açıklama adım adım, öğrencinin şıkkını
  yargılamadan; görsel okunmuyorsa tahmin yok. Altın sete `mistakes-list`, `explain-mistake`, `explain-open-test` eklendi.
- Issue'daki `start_weak_topic_practice` mevcut `start_practice_session` (konu / yanlışlar) ile karşılanıyor;
  `search_study_materials` ve `list_practice_sessions` bu dilimde yok.

## Zorlandığım sorular (#526, Faz 2)

İki kip: **`mine`** (öğrencinin kendi kaçırdıkları) ve **`global`** (öğrencilerin çoğunun yanlış yaptığı sorular).

- **Exam API:** `GET /api/student/me/hard-questions?mode=mine&subjectId&topicId&subTopicId&days=90&take=10`
  (`StudentQuestionExplainController`, yalnız `Student`, konu performansının hız sınırı; `days` 1–90, `take` 1–30, dışı 400).
  Cevaplar `StudentAnswerReader`'dan (#497 / #510 ile aynı); kaçırma = yanlış ya da boş. Sıra: son denemesi doğru olan en sona,
  sonra kaçırma sayısı (azalan), sonra son doğrudan sonra yine kaçırılmış olması, sonra son deneme tarihi. Yalnız pratik
  havuzunun verebileceği sorular (sınıf, Normal görünürlük, yayında) ve yanlış defteriyle aynı dışlama (süren test, bitmemiş
  açık atama, bugünün cevaplanmamış günlük seti). Satırda görsel / metin var, **doğru şık yok** (açıklama #510 ile).
- **Pratik:** `PracticeSessionStartDto.QuestionIds` (en çok 30): her id aynı kuraldan (90 gün, tek ders planında o ders) geçer,
  geçmeyen id 400 `practice.hardQuestionInvalid` (sessizce atlanmaz); başka sabit liste kipiyle (yanlışlar, tekrar, kaydedilen,
  karışık, çalışma sayfası) ya da alt konuyla birlikte 400. Oturum listeyi verilen sırayla sorar, `TargetCount` = soru sayısı.
  Plan özelliği yanlış defteri gibi `StudyTools` (sayılmaz).
- **Asistan:** `get_hard_question_examples` (model id / konu / sayıları görür; görseller ve metin yalnız panelde `questions`
  kartında, "Bunları çöz" düğmesi mesaj gönderir) ve `start_hard_question_practice` (W, onay kartı, `ui_action` ile pratik
  ekranı). Prompt r6. Altın sete `hard-mine`, `hard-angles-solve` eklendi.
- **`global` kipi:** `GET /api/student/me/hard-questions?mode=global&subjectId&topicId&subTopicId&take=10` (`days` uygulanmaz).
  Kaynak gece işi `QuestionAnswerStatsJob` (Hangfire `question-answer-stats`, `HardQuestions:StatsCron`, varsayılan 03:00 UTC)
  ve `QuestionAnswerStats` tablosu (soru başına farklı öğrenci ve doğru yapan sayısı; istek anında toplam sorgusu yok). Sayım:
  bitmiş test oturumları ve pratik cevapları, öğrenci başına **ilk** cevabı (yeniden çözme oranı şişirmez), boş / atlanan cevap
  sayılmaz (#785), süren test okunmaz, etkinlik ve açık uçlu soru dışarıda. Sorgu: pratik havuzu (sınıf, Normal görünürlük,
  yayında) + `mine` ile aynı dışlama + son denemesini doğru yaptığı sorular hariç; en az `HardQuestions:MinStudents` (20) farklı
  öğrenci şartı; doğruluk oranı artan. Satırda yalnız toplu `correctPercent` döner (öğrenci sayısı ya da başka öğrenciye ait veri
  yok); deneme / kaçırma alanları öğrencinin kendisi (hiç çözmediyse 0, `lastAttemptAt` boş). `QuestionIds` ile pratik: id
  `mine` listesinde değilse `global` kuralından geçmeli, ikisinden de geçmezse 400. Tool'da `mode=global`, panel kartında
  "doğru yapanlar %N". Prompt r7 ("en zor sorular" / "herkesin zorlandığı" → global). Altın sete `hard-global` eklendi; altın
  fikstür anahtarı artık sorgu taşıyabilir (`GET path?mode=global`).
- **Veli kipi (#536):** `GET /api/parent/children/{studentId}/hard-questions?subjectId&days=90&take=5`
  (`ParentDashboardController`, `ChildActivityPolicy` oran sınırı; `days` 1–90, `take` 1–10, dışı 400
  `parentLinks.errors.invalidHardQuestionsQuery`). Sıra #498: Active bağlantı kapısı (yoksa 404), paket kapısı (`Analytics`, yoksa
  403), `children.hard-questions` audit, sonra `mine` sorgusu değişmeden (çocuk grant'tan; doğru / seçilen şık yok).
  `lastAttemptAt` saate kesilir (dakika izi yok). Karar: veli soru başına deneme / kaçırma sayısını görür (issue: "soru görseli,
  konu, yanlış sayısı"); seçilen şık hiç dönmez, tool `lastAttemptCorrect`'i modele vermez. `global` kipi velide yok (başka öğrencilerle karşılaştırma yasağı).
  `ParentForbiddenContentTests` bu uçta yalnız `items[*].imageUrl/text` alanlarını dar istisna olarak izinli sayar.
  Tool `get_child_hard_questions` (`take` 1–10, varsayılan 5; model id / konu / sayıları görür, `lastAttemptAt` İstanbul saati);
  kart `questions` + `solvable: false`: panel "Bunları çöz" düğmesini göstermez. Prompt r10: velinin görebildiği tek soru bu
  tool'unki; doğru cevap verilmez / tahmin edilmez, veliye pratik önerilmez, çocukla birlikte bakmak önerilir. Altın veli setine
  `child-hard-questions` eklendi.

## Hiç çalışmadığım konular (#530, Faz 2)

- Exam API: `GET /api/student/me/topic-performance?groupBy=topic&minQuestions=5&includeUntouched=true` (`docs/feature-notes.md` #530).
- Tool `get_untouched_topics` (`days` 7–90, varsayılan 90; `subjectId`): eşik zayıf konularla aynı (5 cevap). Modele pratik yapılabilen
  ilk 10 konu (`topicId` ile, pratik için `search_taxonomy` gerekmez), `practicableCount`, `neverAnsweredCount` ve havuzunda soru
  olmayan konu sayısı (`withoutPracticeQuestions`, listelenmez) gider. Kart `topicTable` (konu, cevap sayısı, doğruluk boş).
- Prompt r11: "hiç çalışmadığım konular" / "sınava hazır mıyım" → bu tool (hazırlıkta zayıf konularla birlikte); "zayıf konun yok"
  demeden önce bakılır; hazırlık yüzdesi verilmez. Altın öğrenci setine `untouched-topics`, `exam-ready` eklendi.


## Hep aynı tuzağa düştüğüm şıklar (#528, Faz 2)

- **Popüler çeldirici:** gece işi `QuestionAnswerStatsJob` artık `QuestionChoiceStats` da yazar (soru + şık başına, öğrencinin **ilk**
  cevabında o şıkkı seçen farklı öğrenci sayısı; açık uçlu cevapta şık yok). Bir sorunun popüler çeldiricisi en çok seçilen yanlış
  şıktır; yalnız `HardQuestions:MinStudents` (20) farklı öğrencinin cevapladığı sorularda, eşitlikte yok. Yalnız toplu bilgi:
  başka öğrencinin seçimi hiçbir yanıtta yok. Migration `AddQuestionChoiceStats`.
- **Exam API:** `GET /api/student/me/repeated-traps?subjectId&topicId&days=90&take=5` (`StudentQuestionExplainController`, yalnız
  `Student`, konu performansı hız sınırı; `days` 1–90, `take` 1–10, dışı 400). Cevaplar `StudentAnswerReader`'dan (artık seçilen şıkkı
  da taşır); soru başına öğrencinin **son** yanlış seçimi popüler çeldiriciyle karşılaştırılır. Alt konu başına (alt konusuz soru
  konusunda) en az `HardQuestions:TrapMinMistakes` (3) yanlış soru ve bunların çoğunluğu (yarıdan fazlası) popüler çeldiriciyse
  desen. Yanlış defteriyle aynı dışlama (süren / bitmemiş atanmış test, bugünün günlük seti): her örnek #510 ile açıklanabilir.
  Satır: ders / konu / alt konu, `mistakeCount`, `trapCount`, `strengthPct`, en çok 3 örnek (soru id, öğrencinin seçtiği şıkkın
  harfi ve metni, görsel / metin). **Doğru şık dönmez.** Sıra: güç, sonra tuzak sayısı.
- **Tool** `get_repeated_trap_answers` (`take` 1–5, varsayılan 3): model alt konuyu, `trapCount` / `mistakeCount` sayılarını (yüzde verilmez: canlı denemede doğruluk sanıldı) ve öğrencinin seçimini görür (şık metni
  `<untrusted>`), görsel ve soru metni yalnız `questions` kartında (`solvable:false`: sonraki adım alt konu pratiği, "Bunları çöz" düğmesi yok). Prompt r12: kavram yanılgısını yalnız bir örnekte
  `explain_question` çağırdıktan sonra adlandırır (doğru şık oradan), sonra o alt konudan pratik önerisi; boşsa desen uydurmaz. Altın sete `repeated-trap`.

## En çok vakit harcadığım / acele edip yanlış yaptığım sorular (#527, Faz 2)

- **Exam API:** `GET /api/student/me/time-struggles?kind=slow|rushed&subjectId&topicId&subTopicId&days=90&take=10`
  (`StudentQuestionExplainController`, yalnız `Student`, konu performansı hız sınırı; `kind` dışı, `days` 1–90, `take` 1–30 dışı 400).
  Soru başına öğrencinin **son** cevabı (boş değil, süresi 1 sn – 30 dk) sorunun ortalama süresiyle karşılaştırılır:
  - `slow`: süre ≥ `HardQuestions:TimeSlowFactor` (2) × ortalama; doğru da yanlış da girer. Sıra: en yüksek oran önce.
  - `rushed`: yalnız yanlış; süre ≤ `TimeRushedFactor` (0,25) × ortalama **ve** ≤ `TimeRushedMaxSeconds` (15). Sıra: en düşük oran önce.
  - **Ortalama** istek anında, **diğer öğrencilerin** geçerli süreli cevaplarından (kapanmış / süresi dolmuş test, pratik; süren test
    girmez). Soruda `TimeMinAnswers` (10) cevaptan azsa alt konularındaki soruların ortalaması (`averageScope=subTopic`), orada da
    azsa soru atlanır. Gece işi tablosuna taşımak sonraki iş.
  - Kapsam #526 `mine` ile aynı: pratik havuzundaki sorular, yanlış defteri gibi dışlama (süren / bitmemiş atanmış test, bugünün seti).
  - Satır: soru id, ders / konu / alt konu, `timeTakenSeconds`, `averageSeconds`, `averageScope`, `correct` (öğrencinin sonucu), görsel
    (imzalı) / metin. Doğru şık dönmez.
- **Pratik:** `POST /api/practice/sessions` `questionIds` artık bu iki listedeki id'leri de kabul eder (#526 kuralı genişledi).
- **Tool** `get_time_struggle_questions(kind, subjectId?, topicId?, subTopicId?, take?)`: model id, konu ve iki süreyi görür (saniye ve hazır metin `3 dk 20 sn`: canlıda model 200 sn'yi "3,5 dakika" dedi), görsel ve
  metin yalnız `questions` kartında ("Bunları çöz" var); `start_hard_question_practice` bu id'leri de alır. Prompt r13: süreyi
  ortalamayla sade söyler; slow'da strateji (atla ve dön), rushed'da dikkat (sonuna kadar oku) önerir, yargılamaz. Altın sete
  `time-slow`, `time-rushed`.

## Konu bazında akran karşılaştırması (#533, Faz 2)

- **Gece işi:** `QuestionAnswerStatsJob` artık `TopicAnswerStats` de yazar (migration `AddTopicAnswerStats`): sınıf düzeyi
  (öğrencinin şu anki) + konu (alt konusuz sorunun konusu, yoksa ilk alt konusunun konusu) başına, son `HardQuestions:PeerWindowDays`
  (90) günde o konuda en az `PeerMinAnswers` (5) boş olmayan cevabı olan öğrencilerin sayısı, doğruluk ortalaması ve medyanı. Tablo
  her gece bütünüyle yenilenir; öğrenci satırı tutulmaz.
- **Exam API:** `GET /api/student/me/topic-comparison?subjectId` (yalnız `Student`, konu performansı hız sınırı). Öğrencinin tarafı
  istek anında aynı kuralla (`StudentAnswerReader`, en az 5 cevaplı konular); akran tarafı **yalnız** tablodan ve yalnız en az
  `MinStudents` (20; ayar ne olursa olsun en az 10) öğrenci varsa, yoksa `null` (k-anonimlik). Satır: ders / konu, `myAnswered`, `myAccuracyPct`,
  `peerAccuracyPct`, `peerMedianPct`. Öğrenci sayısı dönmez (güvenlik incelemesi: geceden geceye tek öğrencinin katıldığını gösterirdi). Sıra: akranın en çok gerisinde olunan konu önce, akran verisi olmayan
  sonda. Öğrenci kimliği, sıralama ya da yüzdelik dilim yok. Akran kümesi aynı sınıf düzeyi, okuldan bağımsız (tenant sınırı, #160).
- **KVKK (#522):** kullanıcı kararıyla şimdilik ele alınmadı; yalnız toplu değer döndüğü için rıza gerektirmeyen tasarım seçildi.
- **Tool** `compare_my_topics_with_peers(subjectId?)` (kart yok): model en çok 15 konu ve akran verisi olmayan konu sayısını görür. Prompt r14: önce güçlü yan, sonra en geride bir iki konu ve pratik önerisi; sıralama /
  yüzdelik uydurmaz, başka öğrenci hakkında konuşmaz, `null` ise veri yetmediğini söyler. Altın sete `peer-compare`.

## Proaktif asistan (#519, Faz 4)

- **Haftalık veli raporu** zaten #1112 ile var (`ParentWeeklyReportJob`, Pazartesi; uygulama içi + e-posta, e-posta ayarlardan
  kapanır, yalnız Active bağ). Bu iş yalnız öğrenci tarafını ekler.
- **Öğrenci dürtmesi:** `StudentStudyNudgeJob` (Hangfire `student-study-nudge`, varsayılan her gün 14:00 UTC = 17:00 İstanbul; ayarlar
  `StudentStudyNudge`). Son cevabı (pratik cevabı ya da başlatılan test) `InactiveDays` (3) ile `MaxInactiveDays` (30) gün arasında
  olan öğrenciye bir `StudentStudyNudgeEvent` (outbox) yazar. Sıklık: hareketsizlik dönemi başına bir kez (son dürtme son cevaptan
  eskiyse) ve en çok `MinDaysBetween` (7) günde bir. Hiç cevabı olmayan öğrenciye gitmez. `Students.LastStudyNudgeAtUtc` koşullu
  güncellemeyle outbox satırıyla aynı transaction'da: ikinci tur ikinci bildirim üretmez. Migration `AddStudentStudyNudge`.
- **BadgeService** `StudentStudyNudgeConsumer`: şablon metin (LLM yok), tip `StudyNudge` ("4 gündür soru çözmedin …"), idempotency
  `(Type, SourceEventId)`, 3 immediate retry sonra `badge-service_error`. RabbitMQ izni `definitions.json`, `rabbitmq-init.sh`,
  GKE `stateful-services.yaml`. Bildirime tıklayınca `/practice`.
- **Kapatma:** `GET/PUT /api/student/me/notification-settings` (`studyNudge`), öğrenci Ayarlar → Bildirimler anahtarı
  (`Students.StudyNudgeOptOut`).

## Planlama tool'ları (#515, Faz 3 — birinci dilim)

- Okuma: `list_my_programs`, `get_program` (önümüzdeki 14 günün ders oturumları + çalışma sayfaları, `scheduleId`),
  `search_study_pages` (`api/study-items`), `search_worksheets` (`api/worksheet/list`, yalnız test türü, durum filtresi).
- Yazma (hepsi onay kartı; önizleme yalnız okur):
  - `create_study_program`: program sihirbazının seçimleri (adım 1/2/3/5/6 + `difficultSubjectIds`); önizleme ders listesini ve
    aktif programın yerine geçip geçmediğini söyler. Başlangıç bugün ya da sonrası, en çok 180 gün.
  - `schedule_study_pages`, `mark_study_page_done`, `set_worksheet_reminder` / `delete_worksheet_reminder`.
  - `start_test`: onaydan sonra `/testsolve/{instanceId}` açılır; asistan test sırasında kapalı (layout gizler, sohbet
    `AssistantClosedDuringTest`). Bu rota `ui_open_screen` anahtarı değildir, yalnız onaydan sonra sunucu üretir.
- Tarihler Türkiye yerel saati (sabit UTC+3); çalışma sayfası günleri ekrandaki gibi yerel gece yarısının UTC'si gönderilir.
- Onay kartı (UI): bilinen eylem başlığı + ayrıntı satırları; adlar sunucuda `<untrusted>` işaretli, panelde düz metin.
  Başlatan eylemlerde "Başlat", diğerlerinde "Onayla".
- Prompt r15 "Planning:" kuralı; golden `reminder-test`, `create-program`.
- İkinci dilim (ayrı PR): `plan_my_week`, `ui_highlight`, özel ders / mesaj tool'ları.

## Haftalık plan, özel ders ve mesaj tool'ları (#515, Faz 3 — ikinci dilim)

- **`get_my_week`**: önümüzdeki 7 yerel gün; açık ödevler (`api/worksheet/assignments/active`) ve takvim
  (`api/worksheet/calendar/me`, İstanbul gece yarısı UTC aralığı: hatırlatma, ödev bitişi, program sayfası, ders). Model
  zayıf konu ve günlük set tool'larıyla birlikte gün gün bir öneri yapar; her değişiklik ayrı onay kartı.
- **Özel ders:** `search_tutors` (ders adı → `api/subject/search` → `api/Teacher/search`), `get_tutor_profile`,
  `list_tutor_slots` (yalnız gelecekteki boş slotlar), `request_lesson` (yazma; önizleme slotun hâlâ boş olduğunu kontrol
  eder, kart öğretmen, tarih-saat, saat ücreti ve "öğretmen onaylayınca kesinleşir" der; commit `POST api/booking/requests`,
  sonra `/my-bookings`). Veli onayı (#439) ve paket kapısı exam API'de kalır.
- **Mesaj:** `list_my_conversations` (yalnız son mesaj önizlemesi). `draft_message_to_teacher` hiçbir şey göndermez: `draft`
  kartı metni gösterir, "Kopyala" ve "Mesajlara git" düğmeleri vardır. Kartın rotası yalnız `/teacher-messages` (isteğe bağlı
  `?conversation=<id>`) olabilir; panel başka rotayı açmaz.
- **`summarize_worksheet_comments`**: testin yorum dizileri (soru no, yazar rolü, metin; yazar adı yok), her metin
  `<untrusted>`; model yorumlardaki talimatlara uymaz.
- **`ui_highlight(elementKey)`**: izinli anahtarlar `programs.create`, `calendar.view`, `homework.assignments`,
  `tutors.subjects`, `messages.search`; her anahtar kendi ekranına bağlı. Panel ekran farklıysa önce açar, öğeyi
  (`data-assistant-key`) 3 sn bekler, kaydırır ve `.assistant-highlight` ile 3 sn çerçeveler. Yeni anahtar: şablona öznitelik
  + `UiHighlightTool.Keys` + açıklama.
- `SystemPrompt.Revision` 16.


## Sunucu tarafı sertleştirme (#1353)

- **Test sırasında asistan:** `GET api/student/assistant-access` artık `testInProgress` da döner (başlamış, bitmemiş, süresi
  dolmamış süreli test). `ParentControl` bu durumda sohbeti ve onayı rota ne olursa olsun 409 `AssistantClosedDuringTest` ile
  reddeder; test sürerken yanıt önbelleğe alınmaz (test biter bitmez soru sorulabilsin). Süresiz açık test ve süresi dolmuş test
  sayılmaz.
- **Programa çalışma sayfası ekleme:** `POST api/program/{id}/study-pages` yalnız yayımlanmış, silinmemiş ve öğrencinin plan
  dersi (#1109) kapsamındaki sayfaları ekler; diğerleri eksik sayfa gibi atlanır.
