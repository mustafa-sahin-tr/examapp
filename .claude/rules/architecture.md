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
- **Soru sınıflandırma**: öğretmen soru oluşturunca `QuestionCreatedEvent` → BadgeService `QuestionCreatedConsumer` → `GeminiQuestionClassifier` soru görselini Gemini'ye verip ders/konu/alt konu/zorluğu belirler ve exam API'ye geri yazar (`ClassificationSource=AI`). (Eskiden n8n yapıyordu; kaldırıldı.)
- **İstisna — exam API consumer'ı**: `StudentPointsChangedEvent` (#225) BadgeService yerine exam API'de `StudentPointsChangedConsumer` ile tüketilir; yazılan `StudentPoints` tablosu exam DB'de ve servisler arası HTTP/DB paylaşımı yasak. Kural: event'in yazdığı veri hangi serviste ise consumer orada olur; diğer tüm outbox event'leri BadgeService'e eklenir. BadgeService'in kendi outbox'ı `badge-outbox-publisher` ile yayınlanır.
