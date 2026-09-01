Genel tablo

Sistem, "mikroservis" olarak tasarlanmış ama pratikte dağıtık monolit (distributed monolith): 4 .NET servisi + 2 Angular + Python + n8n, hepsi tek PostgreSQL instance'ına bağlı. Servis sınırları veri katmanında değil, sadece process düzeyinde ayrılmış.

Pozitif tarafta: tüm .NET projeleri net10.0, outbox pattern var, Aspire migrasyonu başlamış (AppHost + ServiceDefaults), EF retry-resilience yakı

Kritik sorunlar

1. Paylaşılan veritabanı = sahte servis sı
- OutboxPublisher doğrudan ana API'nin OutboxMessages tablosunu okuyor.
- BadgeService ayrı DbContext kullanıyor aa.
- Sonuç: bir servisin şeması diğerini kırar, bağımsız deploy edilemez, "mikroservis" faydası yok ama dağıtık
  sistem maliyeti var.
- docker-compose.yml healthcheck'i examination DB'sini kontrol ediyor, oysa DB adı worksheet — tutarsızlık.

2. Outbox implementasyonu kırılgan
Services/OutboxPublisher/Publishers/Outbox
- Type.GetType(message.Type) — assembly-qualified tip adı DB'de saklanıyor; namespace/rename refactor'ünde eski
  mesajlar sessizce ölür (continue ile atlk).
- 5 sn polling, Take(10), tek instance varsayımı — FOR UPDATE SKIP LOCKED yok, ikinci replica çalışırsa çift
  publish.
- ProcessedAt işaretleniyor ama temizlik yok → tablo sonsuza kadar büyür.
- Poison message / retry stratejisi yok.
- Consumer tarafında idempotency/dedup görünmüyor.

3. Çok fazla async mekanizma, örtüşen sorumluluk
Outbox + Hangfire + n8n + in-process Backg kuyruğu modeli. Hangi işin nereye aitolduğu net değil.

4. Auth dağınık ve zayıf
- KeycloakService hem auth-api'de hem ana
- Servis-servis yetkilendirme: preferred_username == "exam-admin" string kontrolü ("god user"). Rol/scope tabanlı değil, tek client tüm yetkiye sahip.
- Audience = "account" hardcoded — realm'deki herhangi bir token bu API'yi çağırabilir, API'ye özel scope yok.
- Issuer (Server:BaseUrl) ve MetadataAddrealhost/internal problemini elle çözüyor —çalışıyor ama kırılgan.

5. "Big ball of mud" servisler
- ExamService.cs 1758 satır, QuestionServi sınav+worksheet+puan+outbox mantığı içiçe.
- 44 DbSet, 105 migration (squash edilmemiaget_v2).

6. Gateway teknik borç
ocelot.json sabit host/port; ölü rotalar (question-detector-dev, finance-api, comment'li catalog). CatalogService
klasörü var ama solution'da yok, compose'ds.

7. İşletim riskleri
- Program.cs'te startup migration try/catch ile yutuluyor → yanlış şemayla açılır.
- Console.WriteLine ile loglama (ILogger ystdout'a yazılıyor.
- Compose'da düz metin secret (exampass, minioadmin).
- Container'lara kaynak kod volume mount eler gerçekte kullanılmıyor, dev/prodparitesi yok.
- finance-api/app/ios aynı repoda ve gatewnmış.

Öncelik sırası (öneri)

┌─────────┬───────────────────────────────────────────────────────────────────────┐
│ Öncelik │                                              Aksiyon                                               │
├─────────┼───────────────────────────────────────────────────────────────────────┤
│ Yüksek  │ Outbox'a SKIP LOCKED + retry/dead-letter + processed temizliği; tip çözümlemeyi string map'e çevir │
├─────────┼───────────────────────────────────────────────────────────────────────┤
│ Yüksek  │ Audience'ı API'ye özgü client'a bağla; servis-servis auth'u client-credentials + role'e taşı       │
├─────────┼───────────────────────────────────────────────────────────────────────┤
│ Orta    │ BadgeService için ayrı DB (şema değil, veritabanı); OutboxPublisher'ı ana API ile aynı repo/DB     │
│         │ context'i paylaşacak şekilde n                                        │
├─────────┼────────────────────────────────────────────────────────────────────────────────────────────────────┤
│ Orta    │ ExamService/QuestionService böil-fast yap                             │
├─────────┼────────────────────────────────────────────────────────────────────────────────────────────────────┤
│ Düşük   │ Ölü Ocelot rotalarını ve Catalkanizmaları 2'ye indir (Hangfire =      │
│         │ scheduled, Outbox = domain event)                                                                  │
├─────────┼───────────────────────────────────────────────────────────────────────┤
│ Düşük   │ Loglamayı ILogger'a taşı, secret'ları user-secrets/parametreye al (Aspire brief'i bunu zaten       │
│         │ istiyor)                                                              │
└─────────┴────────────────────────────────────────────────────────────────────────────────────────────────────┘
