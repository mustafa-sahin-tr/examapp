using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #1022 / #1023 (epic #1017, karar 3): kitaptan aktarılan <see cref="Question"/> ve <see cref="Worksheet"/> taslak
/// (<c>IsDraft</c>) olarak oluşur; ADLI global filtre (<see cref="Key"/>) taslakları her sorgudan gizler. Unutulmuş bir sorgu
/// taslak sızdıramaz. Soft-delete filtresi de bu iki varlıkta adlıdır (<see cref="SoftDeleteKey"/>; EF Core 10 aynı varlıkta
/// adlı ve adsız filtreyi birlikte kabul etmez).
/// <para>
/// <see cref="WithDrafts{T}"/> YALNIZ taslak filtresini kapatır, sorgu geneline uygulanır. Yalnız aktarım / inceleme kodu ve
/// yetkisi önceden denetlenmiş sınıflandırıcı yolları (servis hesabı) kullanır. Tüm filtreleri kapatan
/// <c>IgnoreQueryFilters()</c> taslakları da gösterir: yeni bir kullanımda taslağın görünmesi gerekip gerekmediğine bakın.
/// </para>
/// </summary>
public static class DraftQueryFilter
{
    public const string Key = "Draft";

    /// <summary>Question ve Worksheet'in soft-delete filtresinin anahtarı.</summary>
    public const string SoftDeleteKey = "DraftableSoftDelete";

    private static readonly string[] Keys = [Key];

    public static IQueryable<T> WithDrafts<T>(this IQueryable<T> source) where T : class
        => source.IgnoreQueryFilters(Keys);
}
