using System;
using System.Linq.Expressions;
using ExamApp.Api.Data;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// Öğrencinin bir worksheet atamasını görüp göremeyeceğine dair ortak predikat.
/// Hem <see cref="WorksheetReminderService"/> hem <see cref="WorksheetCalendarService"/>
/// (ve WorksheetDetailService.BuildRankAsync) aynı kuralı kullanır ki
/// "bana atanmış" tanımı tek yerde kalsın. issue #236: aktif-pencere sürümü
/// <see cref="ExamApp.Api.Helpers.WorksheetAccess.ActiveAssignmentsFor"/> da bu ifadeyi kullanır — öğrenci listesi,
/// keşfet görünürlüğü, "atanan testler" (GetActiveAssignmentsForStudentAsync) ve test başlatma kapısı dahil.
/// </summary>
public static class WorksheetStudentAccess
{
    /// <summary>
    /// Öğrenciye doğrudan atanmış (StudentId eşleşiyor) VEYA öğrencinin sınıfına hedeflenmiş
    /// (StudentId null + GradeId eşleşiyor) atamalar. Grade global bir seviyedir ("9. Sınıf"),
    /// okula özel değildir; bu yüzden sınıf-hedefli atamalarda atamanın <c>SchoolId</c>'si varsa
    /// öğrencinin okuluyla eşleşmelidir (okullar arası sızıntı olmasın).
    /// Öğrencinin GradeId'si null ise sınıf-hedefli atamalar dışarıda kalır.
    /// <para>
    /// issue #277 (madde 7): okul koşulu fail-CLOSED — sınıf ataması yalnızca açıkça platform geneli işaretliyse
    /// (<see cref="WorksheetAssignment.IsPlatformWide"/>, yalnızca admin yazar) ya da atamanın okulu öğrencinin okuluyla
    /// EŞİTSE görünür. <c>SchoolId == null</c> tek başına artık "herkese açık" demek değildir; okulsuz öğrenci
    /// (<paramref name="schoolId"/> null) yalnızca platform geneli sınıf atamalarını görür (<c>SchoolId != null</c> koşulu
    /// SQL'de "NULL = NULL" eşleşmesini kapatır).
    /// </para>
    /// </summary>
    public static Expression<Func<WorksheetAssignment, bool>> AssignmentVisibleTo(int studentId, int? gradeId, int? schoolId)
        => a => a.StudentId == studentId
            || (a.StudentId == null && a.GradeId != null && a.GradeId == gradeId
                && (a.IsPlatformWide || (a.SchoolId != null && a.SchoolId == schoolId)));

    /// <summary>
    /// <see cref="AssignmentVisibleTo"/>'nun bellek içi (SQL'siz) karşılığı — issue #105: ilgili öğretmen toplu çözülürken
    /// worksheet'in aktif atamaları tek sorguda projeksiyonla çekilir, öğrenci eşleşmesi burada yapılır (expression
    /// <c>Compile</c> maliyeti yok). İki tanım AYNI kuralı taşımalı; eşdeğerlik
    /// <c>WorksheetStudentAccessTests</c>'te tüm kombinasyonlar üzerinden doğrulanır — birini değiştiren diğerini de değiştirir.
    /// </summary>
    public static bool IsAssignmentVisibleTo(int? assignmentStudentId, int? assignmentGradeId, int? assignmentSchoolId,
        bool assignmentIsPlatformWide, int studentId, int? gradeId, int? schoolId)
        => assignmentStudentId == studentId
            || (assignmentStudentId == null && assignmentGradeId != null && assignmentGradeId == gradeId
                && (assignmentIsPlatformWide || (assignmentSchoolId != null && assignmentSchoolId == schoolId)));
}
