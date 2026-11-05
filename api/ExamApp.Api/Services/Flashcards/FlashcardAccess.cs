using System;
using System.Linq;
using System.Linq.Expressions;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Worksheets;

namespace ExamApp.Api.Services.Flashcards;

/// <summary>Kartları çalışan öğrenci. Okul profildeki doğrulanmış okuldur (#361), istemci girdisi değil.</summary>
public sealed record FlashcardLearner(int StudentId, int UserId, int? GradeId, int? SchoolId);

public sealed record FlashcardOwner(int UserId, FlashcardDeckOwnerKind Kind);

internal static class FlashcardAccess
{
    /// <summary>
    /// <see cref="WorksheetStudentAccess.AssignmentVisibleTo"/> ile aynı kural, platform geneli olmadan (deste atamasında yok):
    /// doğrudan atama ya da okulun eşleştiği sınıf ataması. Eşdeğerlik <c>FlashcardAccessTests</c>'te doğrulanır.
    /// </summary>
    internal static Expression<Func<FlashcardDeckAssignment, bool>> AssignedTo(int studentId, int? gradeId, int? schoolId)
        => a => a.StudentId == studentId
            || (a.StudentId == null && a.GradeId != null && a.GradeId == gradeId
                && a.SchoolId != null && a.SchoolId == schoolId);

    /// <summary>Öğrencinin kendi destesi ve kendisine atanmış öğretmen desteleri.</summary>
    internal static IQueryable<FlashcardDeck> VisibleDecks(AppDbContext context, FlashcardLearner learner)
    {
        var assignedDeckIds = context.FlashcardDeckAssignments
            .Where(AssignedTo(learner.StudentId, learner.GradeId, learner.SchoolId))
            .Select(a => a.DeckId);

        return context.FlashcardDecks.Where(d =>
            (d.OwnerKind == FlashcardDeckOwnerKind.Student && d.OwnerUserId == learner.UserId)
            || (d.OwnerKind == FlashcardDeckOwnerKind.Teacher && assignedDeckIds.Contains(d.Id)));
    }
}
