namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// issue #396: sunucu tarafı süre sınırı. Sınır, instance'a start-test anında kopyalanan
/// <c>WorksheetInstance.MaxDurationSeconds</c>'tır (öğretmen test süresini sonradan değiştirse de açık oturum etkilenmez).
/// Started bir instance <c>StartTime + sınır + </c><see cref="Tolerance"/> geçtikten sonra cevap kabul etmez ve Expired
/// sayılır. Tolerans, sayaç sıfırlandığı anda gönderilen son cevabın/end-test'in ağ gecikmesini karşılar.
/// Sınır null ya da &lt;= 0 → süre sınırı yok.
/// </summary>
public static class TestTimeLimit
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(30);

    /// <summary>Testin süresinin bittiği an (toleranssız); süre sınırı yoksa null. Expired instance'ın EndTime'ı budur.</summary>
    public static DateTime? EndsAt(DateTime startTime, int? maxDurationSeconds) =>
        maxDurationSeconds is > 0 ? startTime.AddSeconds(maxDurationSeconds.Value) : null;

    /// <summary>Tolerans dahil son kabul anı geçti mi.</summary>
    public static bool IsOver(DateTime startTime, int? maxDurationSeconds, DateTime nowUtc) =>
        EndsAt(startTime, maxDurationSeconds) is { } endsAt && nowUtc > endsAt + Tolerance;

    /// <summary>
    /// Yanıtta gösterilecek durum: veritabanında hâlâ Started olan ama süresi (+ tolerans) dolmuş instance Expired görünür.
    /// Yazma yapmaz — GET uçları saftır; kalıcı Expired yazımı save/end/start ve süpürücüdedir.
    /// </summary>
    public static WorksheetInstanceStatus EffectiveStatus(
        WorksheetInstanceStatus stored, DateTime startTime, int? maxDurationSeconds, DateTime nowUtc) =>
        stored == WorksheetInstanceStatus.Started && IsOver(startTime, maxDurationSeconds, nowUtc)
            ? WorksheetInstanceStatus.Expired
            : stored;

    /// <summary>
    /// İstemci sayacı için kalan süre (saniye, yukarı yuvarlanmış, en az 0; toleranssız). Süre sınırı yoksa null; oturum
    /// Started değilse 0. İstemci bunu kendi saatine göre bir bitiş anına çevirir — sayfa yenilense ya da sekme arka
    /// planda kalsa da süre baştan başlamaz, sunucu/istemci saat farkı da etkilemez.
    /// </summary>
    public static int? RemainingSeconds(
        WorksheetInstanceStatus effectiveStatus, DateTime startTime, int? maxDurationSeconds, DateTime nowUtc)
    {
        if (EndsAt(startTime, maxDurationSeconds) is not { } endsAt)
            return null;
        if (effectiveStatus != WorksheetInstanceStatus.Started)
            return 0;
        return (int)Math.Max(0, Math.Ceiling((endsAt - nowUtc).TotalSeconds));
    }
}
