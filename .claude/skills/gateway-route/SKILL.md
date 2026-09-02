---
name: gateway-route
description: Ocelot gateway'e yeni route ekleme veya mevcut route'u değiştirme prosedürü. Yeni bir backend endpoint dışarıdan erişilecekse veya 404/401 gateway sorunu araştırılıyorsa bu adımları izle.
---

# Ocelot route ekleme

Tüm client trafiği `localhost:5678` üzerinden geçer. Angular hiçbir zaman servis portuna doğrudan gitmez.

Route tanımları: `Services/Gateway/ocelot.json`

## Sıra

1. Mevcut bir route'u kopyala; downstream host/port'u hedef servisten al:
   exam-dotnet-api `5079`, auth-api `6079`, badge `5080`, outbox-publisher `5081`, question-detector `8080`.
2. `UpstreamPathTemplate` ile `DownstreamPathTemplate` arasındaki placeholder'ların (`{id}`) birebir eşleştiğini
   doğrula.
3. `UpstreamHttpMethod` dizisine gerçekten gereken metotları yaz — `GET` eklerken `DELETE` sızdırma.
4. Endpoint korumalıysa authentication ayarını mevcut korumalı route'larla aynı şekilde kur.
   Yeni route'u yanlışlıkla public bırakma.
5. Gateway'i yeniden başlat ve gateway üzerinden çağırarak doğrula:

```bash
curl -i http://localhost:5678/<upstream-path>
```

## Sık görülen hatalar

| Belirti | Olası neden |
|---|---|
| Gateway'den 404, servisten doğrudan 200 | Route eklenmemiş veya path template uyuşmuyor |
| 401 (token doğru görünüyor) | Route'un auth ayarı eksik/farklı, veya Keycloak issuer uyuşmazlığı |
| 502 | Downstream host/port yanlış; container ağında servis adı ile host adı karışmış |

Yeni route eklediğinde `local-dev.md` port tablosunu değiştirmen gerekiyorsa onu da güncelle.
