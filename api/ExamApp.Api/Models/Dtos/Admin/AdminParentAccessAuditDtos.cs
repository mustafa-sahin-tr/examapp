using System;

namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// Issue #424 (epic #407 V6): admin veli erişim kaydı listesinin bir satırı — <c>ParentAccessAudits</c>'in salt okunur izdüşümü.
/// Yanıt zarfı <see cref="Paged{T}"/>: <c>{ pageNumber, pageSize, totalCount, items: [...] }</c>. PII yok: yalnızca exam DB
/// id'leri (kaynak tablo da yalnızca id tutar); ad/e-posta çözülmez.
/// </summary>
public sealed class AdminParentAccessAuditItemDto
{
    /// <summary>Audit satırının id'si (sayfalar arası kararlı sıralama anahtarı).</summary>
    public long Id { get; set; }

    /// <summary>Erişen velinin <c>Parent.Id</c>'si.</summary>
    public int ParentId { get; set; }

    /// <summary>Verisi görülen öğrencinin <c>Student.Id</c>'si.</summary>
    public int StudentId { get; set; }

    /// <summary>Erişilen uç (<c>ParentAccessEndpoints</c> değerleri, örn. <c>children.summary</c>).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Görülen kaynağın id'si (test sonucu ucunda test oturumu); kaynak tekil olmayan uçlarda null.</summary>
    public int? ResourceId { get; set; }

    /// <summary>Erişim anı (UTC).</summary>
    public DateTime At { get; set; }
}

/// <summary>
/// Admin veli erişim kaydı filtresi (issue #424). Hepsi opsiyonel; <see cref="From"/> dahil, <see cref="To"/> hariç (UTC).
/// İkisi birlikte verilirse <c>From &lt; To</c> ve aralık en fazla <see cref="MaxRangeDays"/> gün.
/// </summary>
public sealed record AdminParentAccessAuditQuery(int? ParentId, int? StudentId, DateTime? From, DateTime? To)
{
    /// <summary>Tek sorguda en geniş tarih aralığı (saklama süresiyle aynı: 180 gün).</summary>
    public const int MaxRangeDays = 180;
}

/// <summary>Liste sorgusunun sonucu: doğrulama hatası ya da sayfa.</summary>
public enum AdminParentAccessAuditListStatus
{
    Ok = 0,

    /// <summary><c>from</c> &gt;= <c>to</c>.</summary>
    InvalidRange = 1,

    /// <summary><c>to - from</c> &gt; <see cref="AdminParentAccessAuditQuery.MaxRangeDays"/> gün.</summary>
    RangeTooLong = 2
}

/// <summary>Liste sonucu; <see cref="Page"/> yalnızca <see cref="AdminParentAccessAuditListStatus.Ok"/>'de dolu.</summary>
public sealed record AdminParentAccessAuditListResult(AdminParentAccessAuditListStatus Status, Paged<AdminParentAccessAuditItemDto>? Page);
