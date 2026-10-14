# Security Policy

## Reporting a vulnerability

Please **do not** open a public issue, pull request or discussion for security problems.

Report it privately through GitHub instead:
**Security** tab → **Report a vulnerability**
(<https://github.com/mustafa-sahin-tr/examapp/security/advisories/new>).

Please include:

- the affected component (e.g. `api/`, `auth-api/`, `ui/`, `Services/Gateway`, `question-detector/`),
- steps to reproduce or a proof of concept,
- the impact you expect (data exposure, privilege escalation, …).

You should get a first response within 7 days. Please give us a reasonable time to fix the
issue before disclosing it publicly.

## Supported versions

Only the latest `master` branch is supported; fixes are not backported.

## Scope

In scope: the code in this repository.

Out of scope:

- the dev-only credentials in `.env.example`, `AppHost/appsettings.json` and
  `deploy/keycloak/dev-import/` (they are intentionally public and must never be used outside
  local development),
- vulnerabilities in third-party dependencies without a demonstrated impact on this project
  (report those upstream),
- denial-of-service, spam and social-engineering reports.
