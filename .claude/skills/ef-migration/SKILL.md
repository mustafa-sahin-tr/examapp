---
name: ef-migration
description: EF Core migration üretme, uygulama ve geri alma prosedürü. Entity, DbContext veya veritabanı şeması değişen her işte bu adımları izle — migration dosyalarını elle düzenlemeden önce mutlaka oku.
---

# EF Core migration

Komutlar **her zaman** `api/ExamApp.Api/` dizininden çalıştırılır.

```bash
cd api/ExamApp.Api
dotnet ef migrations add <MigrationName>
dotnet ef database update
dotnet ef migrations remove   # sadece henüz uygulanmamış son migration için
```

## Kurallar

- `Migrations/` altındaki üretilmiş dosyaları **elle düzenleme**. Yanlışsa `remove` et, entity'yi düzelt,
  yeniden `add` et.
- Migration adı ne yaptığını anlatsın: `AddQuestionClassificationSource`, `MakeExamSubtitleNullable`.
  `Update1`, `Fix` gibi isimler yasak.
- Zaten `database update` ile uygulanmış bir migration'ı `remove` etme — geri almak için yeni migration üret.

## Yıkıcı değişiklik (dikkat)

Kolon silme, kolon tipi daraltma ve rename veri kaybettirir. Böyle bir değişiklik gerekiyorsa:

1. Durumu raporla ve onay iste, sessizce üretme.
2. Mümkünse iki adıma böl: önce yeni kolonu nullable ekle + veriyi taşı, sonra ayrı bir migration'da eskiyi düşür.
3. Rename için EF'in drop+create üretip üretmediğini `Up()` içinde kontrol et.

## Kontrol listesi

- [ ] Yeni kolon mevcut satırlar için nullable veya default değerli mi?
- [ ] Sık filtrelenen kolona index gerekiyor mu?
- [ ] Üretilen `Up()` beklediğin SQL'i mi içeriyor? (okumadan `database update` çalıştırma)
- [ ] `Down()` mantıklı mı?
