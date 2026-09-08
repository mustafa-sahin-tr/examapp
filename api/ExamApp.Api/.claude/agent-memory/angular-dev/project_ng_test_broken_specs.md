---
name: ng-test-broken-specs
description: ui/ `ng test` fails to compile because of three stale pre-existing spec files; run a single spec with a temporary tsconfig limited to it
metadata:
  type: project
---

`cd ui && ng test` does not start: `app.component.spec.ts`, `pages/teacher-register/teacher-register.component.spec.ts` and `shared/components/ms-checkbox/ms-checkbox.component.spec.ts` reference symbols that no longer exist (last touched in commit dc88ead). Karma aborts on the TS errors before any test runs.

**Why:** Stale specs on master, not caused by feature branches; nobody has fixed them yet (as of 2026-09-08).

**How to apply:** To verify a single spec, write a temporary `ui/tsconfig.spec.tmp.json` that extends `./tsconfig.json` with `types: ["jasmine"]` and `include` limited to the target spec + `src/**/*.d.ts`, then run
`npx ng test --watch=false --browsers=ChromeHeadless --ts-config=tsconfig.spec.tmp.json --include=<spec path>` and delete the temp file afterwards. Re-check whether the three specs are still broken before relying on this.
