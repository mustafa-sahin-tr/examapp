---
name: outbox-event
description: Yeni async akış (domain event) ekleme prosedürü — outbox kaydı, OutboxPublisher, RabbitMQ ve BadgeService consumer'ı. Servisler arası iletişim, arka plan işi veya event tabanlı bir özellik gerektiğinde bu adımları izle.
---

# Yeni outbox event akışı

Bu platformda servisler birbirini **doğrudan çağırmaz**. Yeni async iş = outbox pattern.

```
Producer servis → outbox tablosu → OutboxPublisher (poll) → RabbitMQ → BadgeService consumer
```

## Sıra

1. **Event sınıfı.** Geçmiş zaman kipiyle adlandır: `QuestionCreatedEvent`, `ExamPublishedEvent`.
   Referans olarak mevcut `QuestionCreatedEvent`'i oku ve aynı yerde/şekilde tanımla.
2. **Payload'ı küçük tut.** Id + akışı yönlendirmek için gereken minimum alan. Tüm entity'yi serialize etme,
   hassas veri (mail, token, kişisel bilgi) taşıma. Consumer detayı kendisi okur.
3. **Producer tarafı.** İş işlemiyle **aynı transaction içinde** outbox satırını yaz.
   Ayrı transaction = kaybolan event.
4. **Consumer.** `Services/BadgeService/` altına `<Event>Consumer` ekle.
   İsim yanıltıcı olsa da tüm outbox event'leri burada handle edilir; bu bilinçli bir karar.
5. **Idempotency.** Aynı mesaj tekrar gelebilir. Şunlardan birini uygula ve hangisini seçtiğini yaz:
   işlenmiş event id kontrolü, doğal unique key, veya upsert.
6. **Hata yolu.** Consumer patlarsa ne olacak? Retry sayısı, dead-letter ve loglanacak korelasyon id'si belirle.
   Sessizce yutma.

## Referans akış

Öğretmen soru oluşturur → `QuestionCreatedEvent` → `QuestionCreatedConsumer` →
`GeminiQuestionClassifier` soru görselini Gemini'ye verir → ders/konu/alt konu/zorluk belirlenir →
exam API'ye `ClassificationSource=AI` ile geri yazılır.

Yeni bir sınıflandırma/zenginleştirme akışı yazacaksan bu akışı birebir örnek al.

## Doğrulama

`docker-compose up -d` sonrası RabbitMQ UI'da (`localhost:15672`) mesajın kuyruğa düştüğünü ve
consumer'ın tükettiğini gör. Publisher poll aralığını unutma — mesaj anında görünmeyebilir.
