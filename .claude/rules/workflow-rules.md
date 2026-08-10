---
description: Development rules — patterns to follow when adding features or making changes
alwaysApply: true
---

- Yeni async iş akışları **outbox pattern** ile yapılır; direkt servis-to-servis çağrı yapılmaz.
- **BadgeService**'e yeni event handler eklerken ismi yanıltıcı olsa da oraya eklenir (henüz refactor edilmedi).
- EF migration komutları — `api/ExamApp.Api/` dizininden çalıştırılır:
  ```bash
  cd api/ExamApp.Api
  dotnet ef migrations add <MigrationName>
  dotnet ef database update
  dotnet ef migrations remove   # son migration'ı geri al
  ```
- Gateway'e yeni route eklerken **Ocelot config dosyası** (`Services/Gateway/ocelot.json`) güncellenir.
