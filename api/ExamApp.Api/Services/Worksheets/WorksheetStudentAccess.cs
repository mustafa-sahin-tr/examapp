using System;
using System.Linq.Expressions;
using ExamApp.Api.Data;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// Öğrencinin bir worksheet atamasını görüp göremeyeceğine dair ortak predikat.
/// Hem <see cref="WorksheetReminderService"/> hem <see cref="WorksheetCalendarService"/>
/// (ve WorksheetDetailService.BuildRankAsync) aynı kuralı kullanır ki
/// "bana atanmış" tanımı tek yerde kalsın (bkz. WorksheetAssignmentService.GetActiveAssignmentsForStudentAsync).
/// </summary>
public static class WorksheetStudentAccess
{
    /// <summary>
    /// Öğrenciye doğrudan atanmış (StudentId eşleşiyor) VEYA öğrencinin sınıfına hedeflenmiş
    /// (StudentId null + GradeId eşleşiyor) atamalar. Grade global bir seviyedir ("9. Sınıf"),
    /// okula özel değildir; bu yüzden sınıf-hedefli atamalarda atamanın <c>SchoolId</c>'si varsa
    /// öğrencinin okuluyla eşleşmelidir (okullar arası sızıntı olmasın).
    /// Öğrencinin GradeId'si null ise sınıf-hedefli atamalar dışarıda kalır.
    /// </summary>
    public static Expression<Func<WorksheetAssignment, bool>> AssignmentVisibleTo(int studentId, int? gradeId, int? schoolId)
        => a => a.StudentId == studentId
            || (a.StudentId == null && a.GradeId != null && a.GradeId == gradeId
                && (a.SchoolId == null || a.SchoolId == schoolId));
}
