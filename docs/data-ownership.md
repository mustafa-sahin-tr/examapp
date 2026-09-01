# Data ownership

Which service owns which database, and where the coupling actually is.

## Databases (all on one Postgres instance)

| Database | Owner | DbContext / migrations |
|---|---|---|
| `worksheet` | exam API (`api/ExamApp.Api`) | `AppDbContext` — the domain schema + the `OutboxMessages` table + the `hangfire` schema |
| `badge` | BadgeService | `BadgeDbContext` — badge/aggregate schema, fully independent |
| `identity` | auth-api | its own `AppDbContext` |
| `keycloak` | Keycloak container | Keycloak-managed |

Each service reads its connection string from `ConnectionStrings:DefaultConnection`
and can be repointed independently. Separate databases (not schemas) already give
schema and migration isolation.

## The one shared-table relationship: OutboxPublisher ↔ exam API

`OutboxPublisher` connects to the **`worksheet`** database and reads the
`OutboxMessages` table that the exam API writes. This is intentional and correct
for the transactional outbox pattern: the producer writes outbox rows in the same
transaction as the business data, and the relay must read from that same table.
They are one bounded context deployed as two processes. `OutboxMessage` lives in
`ExamApp.Foundation` so both sides share one definition.

Do not "fix" this by giving OutboxPublisher its own database.

## What is *not* done (deliberately)

Running `badge` on a separate Postgres **instance** would isolate the blast radius
(a badge-DB outage couldn't affect exam). It is not worth the extra container,
volume and backup target at this scale. Revisit if BadgeService's load or
availability requirements diverge from the exam API's.

## BadgeService → exam API

BadgeService never touches the exam database. It calls the exam API over HTTP
(`ExamApi:BaseUrl`) for question images and to write back classifications, and
consumes domain events from RabbitMQ.
