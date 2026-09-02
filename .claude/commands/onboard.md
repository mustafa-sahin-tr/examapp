---
description: Bir servisin nasıl çalıştığını agent ekibine ve sana özetler
argument-hint: <servis veya klasör adı>
---

**$ARGUMENTS** için mimari brifing çıkar. Keşfi `Explore` agent'ına yaptır ki ana context şişmesin.

Şunları raporla:

1. **Sorumluluk** — bu servis ne yapıyor, ne yapmıyor
2. **Giriş noktaları** — controller/endpoint, consumer, entry script
3. **Bağımlılıklar** — DB, Redis, RabbitMQ, MinIO, Keycloak, dış servisler
4. **Veri akışı** — bir tipik istek/olay baştan sona hangi dosyalardan geçiyor
5. **Değiştirmesi riskli yerler** — kırılgan noktalar, isimlendirme tuzakları, gizli varsayımlar
6. **Bu servise dokunacak agent'ın bilmesi gerekenler** — 3-5 madde

Son maddeyi, kalıcı hale getirmeye değerse `.claude/rules/` altındaki ilgili dosyaya ekleme olarak öner.
