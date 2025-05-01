using System;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExamApp.Api.Helpers;

public class TestInstanceQuestionSaveChangesInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context == null) return result;

        var modifiedEntries = context.ChangeTracker.Entries<TestInstanceQuestion>()
            .Where(e => e.State == EntityState.Modified &&
                        (e.Property("TimeTaken").IsModified || e.Property("SelectedAnswerId").IsModified))
            .ToList();

        foreach (var entry in modifiedEntries)
        {
            var question = entry.Entity;

            // Önce süre farkını bul
            var originalTime = entry.Property("TimeTaken").OriginalValue as int? ?? 0;
            var newTime = question.TimeTaken ?? 0;
            var delta = newTime - originalTime;

            var shouldIncrementQuestionCount = false;

            // Eğer daha önce hiç cevap verilmemişse (yani ilk defa cevaplandıysa)
            if (entry.Property("SelectedAnswerId").OriginalValue == null && question.SelectedAnswerId != null)
            {
                shouldIncrementQuestionCount = true;
            }

            // Öğrenci ID’sini bulalım
            var studentId = await context.Set<TestInstance>()
                .Where(ti => ti.Id == question.WorksheetInstanceId)
                .Select(ti => ti.StudentId)
                .FirstOrDefaultAsync(cancellationToken);

            var profile = await context.Set<StudentProfile>()
                .FirstOrDefaultAsync(p => p.StudentId == studentId, cancellationToken);

            if (profile != null)
            {
                if (shouldIncrementQuestionCount)
                    profile.TotalSolvedQuestions += 1;

                if (delta > 0)
                    profile.TotalStudySeconds += delta;

                profile.LastSolvedDate = DateTime.UtcNow;
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

