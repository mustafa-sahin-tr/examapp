using System.Linq.Expressions;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #396: test oturumu durum kuralları tek yerde. Sunucu süre sınırıyla Expired artık gerçekten yazılıyor; iki ayrı
/// soru vardır ve karıştırılmamalıdır:
/// <list type="bullet">
/// <item><see cref="IsFinished(WorksheetInstanceStatus)"/> — oturum bitti mi (Completed VEYA Expired): devam/tekrar yok,
/// doğru cevaplar/yanlışlar gösterilebilir, atama "yapıldı/süresi doldu" sayılır, takvim hatırlatmaz.</item>
/// <item><c>Status == Completed</c> — "puanlı tamamlandı": ortalama, sıralama, istatistik, öğretmen içgörüleri yalnız
/// öğrencinin kendisinin bitirdiği oturumları sayar (süresi dolan yarım oturum kitle ortalamasını bozmasın).</item>
/// </list>
/// </summary>
public static class WorksheetInstanceStatusRules
{
    public static bool IsFinished(WorksheetInstanceStatus status) =>
        status is WorksheetInstanceStatus.Completed or WorksheetInstanceStatus.Expired;

    public static bool IsFinished(WorksheetInstanceStatus? status) =>
        status is WorksheetInstanceStatus.Completed or WorksheetInstanceStatus.Expired;

    /// <summary>EF sorgularında <see cref="IsFinished(WorksheetInstanceStatus)"/> karşılığı.</summary>
    public static readonly Expression<Func<WorksheetInstance, bool>> Finished =
        ti => ti.Status == WorksheetInstanceStatus.Completed || ti.Status == WorksheetInstanceStatus.Expired;
}
