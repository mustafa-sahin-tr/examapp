using System;

namespace ExamApp.Api.Helpers;

/// <summary>Sorgu parametresinden gelen <see cref="DateTime"/>'ı UTC'ye çevirir (issue #424).</summary>
public static class UtcDateTimes
{
    /// <summary>
    /// Model binding <c>...Z</c> ile gelen değeri <see cref="DateTimeKind.Local"/>'e çevirir, saat dilimsiz değer
    /// <see cref="DateTimeKind.Unspecified"/> kalır; Npgsql <c>timestamptz</c> parametresi UTC ister. Saat dilimsiz değer UTC sayılır.
    /// </summary>
    public static DateTime? AsUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        { } v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
    };
}
