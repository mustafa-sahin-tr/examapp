---
name: build-dll-lock-workaround
description: dotnet build fails with MSB3027 when the API is running locally (bin/Debug dll locked); build to scratchpad OutDir to verify compilation
metadata:
  type: project
---

`cd api/ExamApp.Api && dotnet build` can fail with MSB3021/MSB3027 ("file is locked by .NET Host")
even when the code compiles cleanly — the user often has the API running locally and it locks
`bin/Debug/net10.0/*.dll`.

**Why:** The failure is in the copy step, not compilation. Killing the user's running process is not
acceptable just to verify a build.

**How to apply:** Re-run with an alternate output dir, e.g.
`dotnet build -p:OutDir="<scratchpad>/build/"`, and grep for `error CS` / `Build succeeded`.
Report the lock in the Doğrulama section so the user knows the normal command will pass once the
API is stopped. Baseline warning count is ~187 (pre-existing, not ours).
