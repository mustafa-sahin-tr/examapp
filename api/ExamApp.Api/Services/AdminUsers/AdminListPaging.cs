using System;
using System.Collections.Generic;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// Admin kullanıcı listeleri (öğretmen #152, öğrenci #153) için ortak sayfa hesabı: page/pageSize kırpma ve
/// taşma-güvenli offset. pageSize üst sınırı auth-api users/lookup IncludeAccountStatus limitine (100 id) bağlıdır.
/// </summary>
public static class AdminListPaging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>page &lt; 1 → 1; pageSize [1, <see cref="MaxPageSize"/>] aralığına kırpılır.</summary>
    public static (int Page, int PageSize) Normalize(int page, int pageSize)
        => (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));

    /// <summary>
    /// (page - 1) * pageSize int'te taşabilir (page=int.MaxValue → negatif Skip → 500); long'da hesaplanır.
    /// Toplamı aşan sayfa için false döner (çağıran boş sayfa döner; ikinci sorgu ve auth-api çağrısı yapılmaz).
    /// true dönerse offset &lt; totalCount ≤ int.MaxValue olduğundan int'e sığar.
    /// </summary>
    public static bool TryGetOffset(int page, int pageSize, int totalCount, out int offset)
    {
        var longOffset = (long)(page - 1) * pageSize;
        if (longOffset >= totalCount)
        {
            offset = 0;
            return false;
        }

        offset = (int)longOffset;
        return true;
    }

    public static Paged<T> EmptyPage<T>(int page, int pageSize, int totalCount) => new()
    {
        PageNumber = page,
        PageSize = pageSize,
        TotalCount = totalCount,
        Items = new List<T>()
    };
}
