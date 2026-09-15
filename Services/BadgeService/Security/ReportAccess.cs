namespace BadgeService.Security;

/// <summary>
/// Rapor görüntüleme yetki modeli: "kendi verisi VEYA admin" (IDOR koruması, issue #165).
///
/// Saf/statik tutuldu ki controller'dan bağımsız olarak unit test edilebilsin —
/// <c>api/ExamApp.Api/Helpers/WorksheetAccess.cs</c> ile aynı desen.
/// </summary>
public static class ReportAccess
{
    /// <summary>
    /// <paramref name="callerUserId"/> (çözümlenemediyse <c>null</c>) <paramref name="requestedUserId"/>
    /// kullanıcısının raporunu görebilir mi. Admin her kullanıcıyı görebilir; diğer herkes sadece
    /// kendi verisini. Çağıran kimliği çözümlenemediyse erişim yoktur (fail-closed → 403).
    /// </summary>
    public static bool CanView(int? callerUserId, int requestedUserId, bool isAdmin)
    {
        if (isAdmin)
            return true;

        return callerUserId.HasValue && callerUserId.Value == requestedUserId;
    }
}
