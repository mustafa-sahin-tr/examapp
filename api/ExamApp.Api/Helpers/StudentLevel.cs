using System;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Öğrenci seviyesi (issue #243): <c>Level = 1 + floor(sqrt(XP / 50))</c>.
/// XP 0→1, 50→2, 200→3, 450→4, 800→5, 5000→11.
///
/// Seviye saklanmaz, OKUMA ANINDA XP'den (StudentPoints.XP) hesaplanır; <c>StudentPoints.Level</c> kolonu artık
/// okunmaz (ayrı işte düşürülecek). Saf fonksiyon: EF sorgusuna çevrilmez, projeksiyon sonrası bellekte uygulanır.
///
/// Güvenlik: XP &lt; 50 (negatif dahil) → 1. Hesap tamamen tamsayı (long) aritmetiğiyle yapılır:
/// floor(sqrt(x / 50)) == isqrt(floor(x / 50)) (x ≥ 0), böylece büyük XP'lerde double yuvarlaması seviye sınırını
/// kaydıramaz; long.MaxValue'da bile sonuç int'e sığar (~4.3e8).
/// </summary>
public static class StudentLevel
{
    public const int XpPerLevelUnit = 50;
    public const int MinLevel = 1;

    public static int FromXp(long xp)
    {
        if (xp < XpPerLevelUnit)
            return MinLevel;

        var units = xp / XpPerLevelUnit;
        return MinLevel + (int)IntegerSqrt(units);
    }

    /// <summary>floor(sqrt(n)), n ≥ 0 — double tahmini tamsayı düzeltmesiyle kesinleştirilir.</summary>
    private static long IntegerSqrt(long n)
    {
        var r = (long)Math.Sqrt(n);
        while (r * r > n) r--;
        while ((r + 1) * (r + 1) <= n) r++;
        return r;
    }
}
