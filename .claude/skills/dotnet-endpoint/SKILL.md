---
name: dotnet-endpoint
description: exam-dotnet-api'ye yeni REST endpoint ekleme prosedürü — controller, service, DTO, DI kaydı ve gateway route'u. Backend'e yeni endpoint, yeni iş kuralı veya yeni domain işlemi eklenecek her durumda bu adımları izle.
---

# Yeni .NET endpoint ekleme

## Sıra

1. **Domain'i bul.** `api/ExamApp.Api/Controllers/` altında ilgili controller var mı?
   Varsa oraya ekle, yoksa `<Domain>Controller.cs` aç.
2. **DTO'ları yaz.** `Models/<Domain>/` altında request ve response tipleri.
   Entity'yi request veya response olarak kullanma — validation attribute'ları DTO'ya konur.
3. **Service'i yaz.** `Services/I<Domain>Service.cs` + implementation. İş kuralı buraya.
   Metot imzasına `CancellationToken ct = default` koy ve EF çağrılarına geçir.
4. **Controller'ı ince tut.** Sadece: model binding, yetki attribute'u, service çağrısı, HTTP status eşlemesi.
5. **DI kaydı.** `Program.cs` içinde `builder.Services.AddScoped<I...Service, ...Service>();`
6. **Yetkilendirme.** Endpoint public değilse `[Authorize]` ve gerekiyorsa rol kontrolü.
   Kaynak sahipliği gerekiyorsa (öğretmen sadece kendi testini görmeli) bunu service içinde doğrula —
   sadece `[Authorize]` yeterli değil.
7. **Şema değişikliği varsa** `ef-migration` skill'ine geç.
8. **Gateway.** Endpoint dışarıdan çağrılacaksa `gateway-route` skill'i ile `ocelot.json`'a route ekle.

## Dönüş tipleri

| Durum | Dönüş |
|---|---|
| Başarılı okuma | `200 Ok(dto)` |
| Oluşturma | `201 CreatedAtAction` |
| Doğrulama hatası | `400` + alan bazlı hata |
| Yetkisiz / yasak | `401` / `403` |
| Kayıt yok | `404` |

Beklenmeyen hataları controller'da yakalayıp string olarak dönme; exception mesajını istemciye sızdırma.

## Sorgu hijyeni

- Liste dönen okuma sorgularında `AsNoTracking()`.
- İlişkili veri lazımsa `Include` — döngü içinde ayrı sorgu (N+1) yazma.
- Sayfalama olmadan sınırsız liste dönme; `skip/take` parametreleri ekle.

## Doğrulama

```bash
cd api/ExamApp.Api && dotnet build
```
