namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// issue #572: etkinlik (<c>Worksheet.Kind = Activity</c>) tamamlama kuralları.
/// </summary>
public static class ActivityOptions
{
    /// <summary>
    /// Bir etkinlik denemesinin süre rozetine katabileceği en fazla etkin süre (sn, 30 dk). Bildirilen süre ayrıca oturum
    /// başlangıcından beri geçen süreyle sınırlanır.
    /// </summary>
    public const int MaxActiveSecondsPerActivity = 1800;
}
