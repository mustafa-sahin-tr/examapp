---
name: question-link-legacy-ownership
description: Linking questions into worksheets (WorksheetQuestion rows) can grant modify rights on legacy (CreateUserId null/0) questions; check every new link path, incl. admin
metadata:
  type: project
---

Legacy question ownership (QuestionOwnershipGuard.CanModifyQuestionAsync, CanUsePassageAsync, bank legacyOwnedIds) is derived from "owner of a non-copy worksheet containing the question". So any code path that inserts a WorksheetQuestion row is a potential privilege grant.

**Why:** #472 (bank-questions link, 2026-10-22) blocked foreign legacy links for teachers, but admin path allowed linking any legacy question into another teacher's original worksheet, silently granting that teacher modify rights. Copies (SourceWorksheetId != null) are excluded from the rule; copies can be re-published (no visibility restriction on copies), so re-sharing SchoolOnly content via link/copy is accepted model, not a new finding.

**How to apply:** When reviewing new WorksheetQuestion insert paths, check (1) teacher foreign-legacy rejection, (2) admin acting on someone else's original worksheet, (3) CanUsePassage legacy rule. Related: [[practice-review-answer-gating]].
