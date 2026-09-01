# Tests

Unit tests for the .NET services. **xUnit v3** + **NSubstitute** + **Shouldly**.
Common package versions and `global using`s live in `tests/Directory.Build.props`.

## Running

```bash
# whole suite
dotnet test ExamApp.slnx

# one project
dotnet test tests/ExamApp.Api.Tests
```

> The service projects lock their output DLLs while running under the AppHost.
> Stop the relevant service (or the whole AppHost) before `dotnet test`, or run
> the suite in CI where nothing else is running.

## Coverage

```bash
dotnet test ExamApp.slnx --collect:"XPlat Code Coverage" \
  --settings tests/coverage.runsettings --results-directory coverage

# one-time: dotnet tool install --global dotnet-reportgenerator-globaltool
reportgenerator -reports:"coverage/**/coverage.cobertura.xml" \
  -targetdir:coverage/report -reporttypes:"TextSummary;Html"

cat coverage/report/Summary.txt          # numbers
open coverage/report/index.html          # per-file drill-down
```

`coverage.runsettings` excludes EF migrations, generated code and entry points
from the denominator (otherwise the ~240k lines of auto-generated migration code
drown out everything). `coverage/` is gitignored.

## Layout (96 tests)

| Project | Covers |
|---|---|
| `ExamApp.Foundation.Tests` | `ServicePrincipal` (service-vs-user decision), `OutboxEventRegistry` (name ↔ type, legacy formats) |
| `ExamApp.Api.Tests` | `TaxonomyService` (CRUD + guards + soft-delete + cache-reconcile scheduling), `ClassifierCacheService` (stale detection, Gemini call, pointer persistence), `KeycloakRoleTransformer` (realm role mapping, malformed-token tolerance), `ExamService.SaveAnswer` (scoring, outbox event, cross-student isolation, dragDrop), `QuestionService.UpdateQuestionClassification` (subtopic validation, topic/subject derivation, source/difficulty guards), `QuestionService.CreateOrUpdateQuestion` (QuestionCreated outbox event on create only) |
| `OutboxPublisher.Tests` | `OutboxOptions.ComputeBackoff` (exponential + cap + overflow) |
| `BadgeService.Tests` | `ActivityAnalytics` (active days, current/best streak), `BadgeEvaluator` (rule parsing, progress tracking, earning, idempotency, bad-config tolerance) |

## Test doubles

- **Database** — `ExamApp.Api.Tests/Support/TestDb.cs`: an isolated in-memory SQLite
  `AppDbContext` (real relational behaviour, query filters, soft delete). One
  `TestDb` per test class; `NewContext()` per logical request.
- **HTTP** — `Support/StubHttp.cs`: an `IHttpClientFactory` driven by a response
  function, recording every request.
- **Hangfire / other collaborators** — NSubstitute.

## Not covered here (needs integration tests — Testcontainers for PostgreSQL)

- `OutboxProcessor`'s `FOR UPDATE SKIP LOCKED` batch claim and its dead-letter
  transitions (SQLite has no `SKIP LOCKED`).
- Hangfire job execution end to end.
- The full `ExamService` / `QuestionService` transactional flows.
