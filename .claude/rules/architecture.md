---
description: Service architecture overview — what each service does and how they relate
alwaysApply: true
---

- **exam-dotnet-api**: ana backend (api/) + Angular frontend (ui/)
- **Services/Gateway**: Ocelot API Gateway, tüm istekler buradan geçer
- **Services/OutboxPublisher**: outbox mesajlarını RabbitMQ'ya publish eder
- **Services/BadgeService**: outbox mesajlarını consume eder — İSİM YANILTICI, sadece badge değil TÜM outbox event'lerini handle eder (örn: soru çözüldüğünde puan hesaplama)
- **auth-api + auth-ui**: Keycloak entegrasyonu; auth-ui Angular, Keycloak login akışını yönetir
- **Python servisi**: YOLO tabanlı — yaprak test/soru bankası görsellerinden soru ve şık sınırlarını tespit edip crop eder
- **n8n workflow**: öğretmen soru oluşturduğunda tetiklenir; AI'a sorarak dersi/konuyu/zorluğu belirler ve günceller
