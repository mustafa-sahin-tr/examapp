using ExamApp.Api.Data;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Bir atama penceresi (<c>StartAt</c>/<c>EndAt</c>) için öğrencinin "ilgili" test instance'ını seçen tek kural —
/// öğretmen ilerleme ekranı (<c>WorksheetAssignmentService</c>) ile öğretmen özet/geride kalan öğrenci hesapları
/// (<c>TeacherService</c>) aynı yerden okur, birbirinden ayrışmaz.
/// <para>
/// issue #367: öğrenci başına worksheet başına tek canlı instance var (tekrar çözüm yok). Atamadan ÖNCE çözülmüş bir
/// test ikinci kez başlatılamadığından, pencereden önce başlamış ama <b>Completed</b> instance atamayı karşılar.
/// Tamamlanmamış (Started) instance ise hâlâ pencere içinde başlamış olmalıdır (<c>StartTime &gt;= StartAt</c>).
/// Her iki durumda da <c>EndAt</c> varsa instance ondan sonra başlamış olamaz.
/// </para>
/// </summary>
public static class AssignmentInstanceWindow
{
    /// <summary>Instance bu atama penceresinde sayılır mı?</summary>
    public static bool Counts(DateTime instanceStartTime, WorksheetInstanceStatus status, DateTime windowStartAt, DateTime? windowEndAt)
        => (!windowEndAt.HasValue || instanceStartTime <= windowEndAt.Value)
           && (instanceStartTime >= windowStartAt || status == WorksheetInstanceStatus.Completed);

    /// <summary>Pencerede sayılan instance'lardan en son başlayanı; yoksa null.</summary>
    public static T? SelectRelevant<T>(
        IEnumerable<T> instances,
        DateTime windowStartAt,
        DateTime? windowEndAt,
        Func<T, DateTime> startTime,
        Func<T, WorksheetInstanceStatus> status) where T : class
        => instances
            .Where(i => Counts(startTime(i), status(i), windowStartAt, windowEndAt))
            .OrderByDescending(startTime)
            .FirstOrDefault();
}
