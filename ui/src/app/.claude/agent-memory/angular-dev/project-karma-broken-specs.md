---
name: karma-broken-specs-workaround
description: ui/ karma test run fails at compile because of 3 pre-existing broken specs; how to run a single spec in isolation
metadata:
  type: project
---

`cd ui && npx ng test` fails before any test runs: three pre-existing specs do not compile
(`app.component.spec.ts` → `app.title` missing, `teacher-register.component.spec.ts` → imports a
non-existent `student-register.component`, `ms-checkbox.component.spec.ts` → wrong class name).
Observed 2026-09-08; they are untouched by feature work, so `--include` alone does not help
because `tsconfig.spec.json` still compiles every `*.spec.ts`.

**Why:** Nobody has fixed those specs yet; the karma builder (Angular 19, `@angular-devkit/build-angular:karma`)
does not accept `--tsConfig`, so the only way to run one spec is to narrow `tsconfig.spec.json` `include`
temporarily AND pass `--include=<spec path>`, then `git checkout -- tsconfig.spec.json`.

**How to apply:** When asked to run/verify a component spec in `ui/`, use that temporary-narrowing approach
in a single shell command (restore in the same command so the file never stays modified). Mention the
three broken specs in "Açık kalanlar" rather than fixing them unless asked. If a future `ng test` passes cleanly,
delete this memory.
