---
name: aspire-migration
description: Bu platformu docker-compose'dan .NET Aspire AppHost'a taşıma stratejisi, fazları ve bilinen kırılma noktaları. Aspire, AppHost, service discovery veya orchestration konuşulan her işte bu dosyayı oku.
---

# Aspire'a taşıma

Hedef: `docker-compose.yml` ile yönetilen yerel topolojiyi Aspire AppHost altında toplamak.
Taşıma **fazlı** yapılır; iki yol bir süre paralel çalışır ve compose yolu bozulmaz.

## Fazlar

**Faz 1 — Altyapı.** PostgreSQL, Redis, RabbitMQ, MinIO, Keycloak AppHost'ta container resource olarak
tanımlanır. .NET servisleri hâlâ compose'da veya elle çalışır. Bağlantı stringleri Aspire'dan gelir.

**Faz 2 — .NET servisleri.** `ExamDotnetApi`, `BadgeService`, `OutboxPublisher` AppHost'a proje olarak eklenir.
`WithReference` ile altyapıya bağlanır. Gateway henüz taşınmaz.

**Faz 3 — Gateway + auth.** En riskli faz. Ocelot ve Keycloak burada devreye girer; aşağıdaki iki tuzak
büyük ihtimalle burada patlar.

**Faz 4 — Frontend ve Python servisi.** Angular uygulamaları ve `question-detector` AppHost'a bağlanır,
compose yolu emekliye ayrılır.

Her fazın sonunda: tüm servisler ayağa kalkıyor + gateway üzerinden bir smoke request başarılı.
Bu sağlanmadan sonraki faza geçme.

## Bilinen kırılma noktaları

### 1. Ocelot ile service discovery çakışması (birincil risk)

Ocelot `ocelot.json` içinde downstream host/port'u statik tutar; Aspire adresleri dinamik atar.
Seçenekler:

- Aspire'ın atadığı endpoint'leri environment variable olarak Gateway'e geçirip `ocelot.json`'da
  placeholder ile okumak (en az invaziv).
- Ocelot'un service discovery provider'ını Aspire'ın çözümlediği adreslerle beslemek.
- Gateway'i YARP'a taşımak (Aspire ile daha doğal çalışır, ama ayrı ve büyük bir iş — ayrı karar olarak ele al).

Hangi seçenek seçilirse seçilsin, karar gerekçesiyle yazılsın; sessizce değiştirme.

### 2. Keycloak issuer URL uyuşmazlığı (en olası Faz 3 hatası)

Container içinden görünen issuer (`http://keycloak:8080`) ile tarayıcının gördüğü issuer
(`http://localhost:8081`) farklı olunca token doğrulaması `invalid issuer` ile sessizce başarısız olur.
Belirti: login akışı çalışır ama API 401 döner.

Kontrol edilecekler: Keycloak `hostname` / frontend URL ayarı, API tarafındaki `Authority` ve
`ValidIssuer`, Angular'daki issuer. Üçü tutarlı olmalı.

## Kural

Aspire çalıştı diye compose dosyasını silme. Taşıma tamamlanıp ekip doğrulayana kadar ikisi de kalır.
