---
name: practice-review-answer-gating
description: Practice ("Soru Çöz") review endpoint answer-leak fix verified sound as of 2026-09-08 rework; explains the intentional PracticeCorrectAnswer/IsExample exception so it isn't misflagged as a leak again.
metadata:
  type: project
---

`GET /api/practice/sessions/{id}/review` (api/ExamApp.Api/Services/Practice/PracticeSessionService.cs,
`GetReviewAsync` + `MapQuestion(Question, bool revealCorrectAnswer)`) correctly gates
`QuestionDto.CorrectAnswerId` and `AnswerDto.IsCorrect` on `PracticeSessionQuestion.AnsweredAt != null`
— verified directly in code (not just trusted from prior agent reports) as of the issue #62/#63
review-screen rework (branch `feature/issue-62-63-soru-coz-pratigi`). The 2-arg overload is the only
path used by both the review endpoint (reveal = `AnsweredAt != null`) and the live `/next` feed
(reveal = `false` via the 1-arg overload `MapQuestion(q) => MapQuestion(q, false)`), so there's no
ambiguous-default risk. Controller (`PracticeController.cs`) funnels both `ListSessions` and
`GetSessionReview` through `ResolveStudentAsync()` + `StudentId == studentId` ownership filter — no
IDOR. Frontend (`practice-solve.component.ts` `toReviewEntry`) derives `correctAnswerId`/`correctChoice`
purely from the server DTO field, no local cache/fallback that could reconstruct a hidden answer.
xUnit tests (`tests/ExamApp.Api.Tests/Services/PracticeSessionServiceTests.cs`,
`Review_PendingQuestion_NeverExposesCorrectAnswerId` and similar) assert this explicitly.

**Non-obvious exception, do not re-flag as a bug:** `Question.PracticeCorrectAnswer` (aka
`practiceCorrectAnswer` in the DTO) IS mapped unconditionally in `MapQuestion`, regardless of
`revealCorrectAnswer` — this is by design, not a gating miss. It's only populated
(`QuestionService.cs`: `PracticeCorrectAnswer = questionDto.IsExample ? questionDto.ExampleAnswer : null`)
for `IsExample == true` "worked example" questions, whose whole point is that the answer is shown
immediately regardless of answered state (`Question.cs` comment: "Eğer true ise ... cevabı otomatik
gösterilir"). Frontend mirrors this consistently (`practice.service.ts` `toQuestionRegion`:
`exampleAnswer: question.isExample ? question.practiceCorrectAnswer : null`). Pre-existing pattern,
untouched by this diff — confirmed via `git diff HEAD` that the `PracticeCorrectAnswer =
q.PracticeCorrectAnswer` line in `MapQuestion` was not touched.

**Why:** third security-review pass explicitly asked to re-verify this exact leak after a response-shape
rework (flat fields → embedded QuestionDto); worth confirming, but not re-litigating each time this file
is touched unless the gating logic (`revealCorrectAnswer` derivation or the overloads) actually changes.
**How to apply:** if reviewing further changes to `PracticeSessionService.cs`/`PracticeController.cs`,
only re-verify the `revealCorrectAnswer` boolean derivation and overload call sites — treat
`PracticeCorrectAnswer` unconditional exposure as known/intentional unless `IsExample` gating itself
changes.
