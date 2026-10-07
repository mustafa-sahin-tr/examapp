using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #420 (epic #407 V2): velinin çocuğuna ait veriye her erişiminin hafif kaydı — "hangi veli, hangi çocuğun, hangi
/// ucunu, ne zaman gördü". Yalnızca ekleme yapılır (append-only; <see cref="AdminDataAccessLog"/> deseni):
/// <see cref="BaseEntity"/>'den türemez, soft delete / update alanı yoktur. Yalnızca erişim VERİLEN (Active bağlantı)
/// istekler yazılır; 404'e düşen denemeler yazılmaz. PII yok: yalnızca exam DB id'leri.
/// FK yok — kayıt, veli/öğrenci satırı ya da bağlantı sonradan silinse/koparılsa da kalmalı. Saklama süresi ve
/// raporlama V6 (#424) kapsamında.
/// </summary>
public class ParentAccessAudit
{
    public long Id { get; set; }

    /// <summary>Erişen velinin <see cref="Parent.Id"/>'si.</summary>
    public int ParentId { get; set; }

    /// <summary>Verisi görülen öğrencinin <see cref="Student.Id"/>'si.</summary>
    public int StudentId { get; set; }

    /// <summary>Erişilen uç (<see cref="ParentAccessEndpoints"/>); kalıcı değer, yeniden adlandırma geçmiş kayıtları bozar.</summary>
    [MaxLength(64)]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Erişim anı (UTC).</summary>
    public DateTime At { get; set; }
}

/// <summary>Audit'lenen veli uçları (<see cref="ParentAccessAudit.Endpoint"/> değerleri).</summary>
public static class ParentAccessEndpoints
{
    /// <summary><c>GET api/parent/children/{studentId}/summary</c> (issue #420).</summary>
    public const string ChildSummary = "children.summary";
}
