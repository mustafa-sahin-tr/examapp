using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ExamApp.Api.Services.Schools;

/// <summary>
/// <see cref="Data.School.SearchText"/> kuralı: okul adı + ilçe + il, Türkçe harf ve büyük/küçük farkı katlanmış.
/// Arama metni de aynı katlamadan geçer ("kadikoy ataturk" → "Bostancı Atatürk Ortaokulu, Kadıköy").
/// Kural değişirse <c>AddSchoolSearchText</c> migration'ındaki SQL gibi mevcut satırlar yeniden doldurulmalı.
/// </summary>
public static class SchoolSearchText
{
    /// <summary>Kolon sınırı; ad (200) + ilçe + il buna sığar.</summary>
    public const int MaxLength = 500;

    /// <summary>Elle yazılmış absürt aramalar sorguyu şişirmesin.</summary>
    private const int MaxQueryLength = 100;
    private const int MaxTokens = 6;

    public static string Build(string name, string? districtName, string? provinceName)
    {
        var parts = new[] { name, districtName, provinceName }.Where(p => !string.IsNullOrWhiteSpace(p));
        return Fold(string.Join(' ', parts));
    }

    /// <summary>Arama metnini katlanmış kelimelere ayırır; her kelime <c>SearchText</c> içinde geçmelidir (AND).</summary>
    public static IReadOnlyList<string> Tokens(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var text = query.Length > MaxQueryLength ? query[..MaxQueryLength] : query;
        return Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().Take(MaxTokens).ToList();
    }

    public static string Fold(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Replace('İ', 'i').Replace('I', 'i').Replace('ı', 'i').Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.ToLowerInvariant(c));
        }
        return string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
