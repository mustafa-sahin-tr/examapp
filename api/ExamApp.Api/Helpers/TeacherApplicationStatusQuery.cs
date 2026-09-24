using System;
using ExamApp.Api.Data;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #187: <c>GET api/admin/teacher-applications?status=</c> değerinin çözümü. Controller (400 kararı) ve rate limit
/// reddinin audit'i (<see cref="AdminUserListRateLimitPolicy"/>) aynı kuralı kullanır.
/// </summary>
public static class TeacherApplicationStatusQuery
{
    public const string Pending = "pending";
    public const string All = "all";

    /// <summary>
    /// Parametre yok/boş → <see cref="TeacherApplicationStatusFilter.Pending"/> (eski davranış). <c>pending</c> | <c>all</c>
    /// büyük/küçük harf duyarsız kabul edilir; başka her değer false (çağıran 400 döner). Sayısal enum değerleri
    /// (<c>1</c>, <c>2</c>) bilinçli olarak KABUL EDİLMEZ.
    /// </summary>
    public static bool TryParse(string? raw, out TeacherApplicationStatusFilter filter)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value) || value.Equals(Pending, StringComparison.OrdinalIgnoreCase))
        {
            filter = TeacherApplicationStatusFilter.Pending;
            return true;
        }

        if (value.Equals(All, StringComparison.OrdinalIgnoreCase))
        {
            filter = TeacherApplicationStatusFilter.All;
            return true;
        }

        filter = default;
        return false;
    }
}
