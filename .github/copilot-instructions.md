---
# Copilot Coding Agent Instructions for ExamApp

## Project Overview

ExamApp is a microservices-based online examination platform. It includes:
- Angular 19 frontends (`ui/`, `auth-ui/`, `finance-app/`)
- .NET Core backend APIs (`api/`, `auth-api/`, `finance-api/`)
- AI-powered question detection (`question-detector/`)
- Infrastructure: MinIO, RabbitMQ, Keycloak, PostgreSQL, Ocelot Gateway

## Architecture & Service Boundaries

- **Frontend**: Angular 19 apps in `ui/`, `auth-ui/`, `finance-app/`. Each has its own `src/` and follows Angular CLI conventions. Shared UI logic is not cross-imported between apps.
- **Backend**: Each API (e.g., `ExamApp.Api`, `AuthService`, `BadgeService`) is a separate .NET Core project with its own `Controllers/`, `Services/`, `Data/`, and `Models/`.
- **Communication**: Services communicate via REST APIs and RabbitMQ for event-driven flows. MinIO is used for file storage. Keycloak provides authentication/authorization (OIDC/JWT).
- **Database**: Each service has its own PostgreSQL schema/context. Migrations are managed via EF Core.

## Developer Workflows

- **Start All Services**: `docker-compose up` (root) spins up all services and dependencies.
- **Frontend Dev**: `cd ui && npm install && ng serve` (or `auth-ui/`, `finance-app/`)
- **Backend Dev**: `cd api && dotnet run --project ExamApp.Api` (or other API folders)
- **Database Migrations**: `cd api/ExamApp.Api && dotnet ef migrations add <Name> && dotnet ef database update`
- **Unit Tests**:
  - Frontend: `cd ui && ng test`
  - Backend: `cd api/ExamApp.Tests && dotnet test`
- **Load Testing**: `cd k6 && k6 run k6-script.js` (see also `api/k6-test.js`)

## Project-Specific Conventions

- **Angular**: Use standalone components, signals, and Angular Material. Prefer feature modules for logical grouping. Use `Mat*` modules for UI. Test files use `TestBed` and `ComponentFixture`.
- **.NET Core**: Use dependency injection for all services. Controllers are thin; business logic is in `Services/`. Use `AppDbContext`/`FinanceDbContext` for EF Core. Exception handling via middleware. Swagger is enabled in development.
- **Auth**: All APIs expect JWT tokens from Keycloak. User roles: Student, Teacher, Parent. Role-based access enforced in controllers/services.
- **File Storage**: Use MinIO via `IMinIoService` for file operations.
- **Events**: Use RabbitMQ for async/event-driven flows (see commented `AddMassTransit` in `Program.cs`).
- **Configuration**: Each service has its own `appsettings.json`/`appsettings.Development.json`. Update connection strings and Keycloak/Redis settings as needed.

## Integration Points

- **API Gateway**: Ocelot routes requests to backend APIs.
- **Keycloak**: Handles authentication/authorization for all user-facing services.
- **MinIO**: Used for file and image storage (e.g., question images, avatars).
- **RabbitMQ**: Used for event publishing (e.g., OutboxPublisher, BadgeService).

## Examples & References

- See `api/README.md` for API endpoints, business logic, and architecture diagrams.
- See `README.md` (root) for system architecture and developer workflow.
- See `api/nuget-commands.txt` for NuGet packaging/publishing.
- See `docker-compose.yaml` for service orchestration and dependencies.

---

**When generating code or documentation, always follow these conventions and reference the above files for patterns.**
