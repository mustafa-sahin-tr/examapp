---
description: Key directory layout inside each service — where to find and add code
alwaysApply: true
---

## Backend — api/ExamApp.Api/

```
Controllers/     # REST endpoints, one file per domain (e.g. QuestionController.cs)
Services/        # business logic, injected via DI
Data/            # DbContext + entity models (EF Core)
Models/          # DTOs and request/response shapes (subfolders per domain)
Helpers/         # utility/extension classes
Migrations/      # EF Core migrations (auto-generated, do not edit by hand)
Program.cs       # service registration + middleware pipeline
appsettings.json # config (connection strings, Keycloak, Redis)
```

### Shared foundation project

`api/ExamApp.Foundation/` — shared interfaces and base types used across services.

## Frontend — ui/src/app/

```
pages/           # routed feature components (one folder per page)
services/        # Angular services (API calls, state)
models/          # TypeScript interfaces/types
components/      # shared/reusable UI components
```

## Other services

| Path | What lives there |
|---|---|
| Services/Gateway/ | Ocelot gateway — `ocelot.json` is the route config |
| Services/BadgeService/ | Event consumers (all outbox events, not just badges) |
| Services/OutboxPublisher/ | Polls outbox table, publishes to RabbitMQ |
| question-detector/ | Python YOLO service — `main.py` is the entry point |
| auth-api/ | Keycloak-backed auth API |
| auth-ui/ | Angular app managing Keycloak login flow |
