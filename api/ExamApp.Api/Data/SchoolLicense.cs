using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #1092 (epic #1085): özel anlaşmayla okulun bütün üyelerine plan. Okul başına tek satır; uzatma aynı satırı günceller,
/// bitirme <see cref="ValidToUtc"/>'yi şimdiye çeker. Kimseye atama yazılmaz: plan çözümü (<c>EntitlementService.ResolvePlanAsync</c>)
/// kullanıcının okuluna (öğretmende <c>Teachers.SchoolId</c>, öğrencide doğrulanmış üyelik) bakar; okula sonradan katılan da
/// lisansı alır, ayrılan düşer.
/// </summary>
public class SchoolLicense : BaseEntity
{
    public const int NoteMaxLength = 200;

    public int Id { get; set; }
    public int SchoolId { get; set; }
    public School School { get; set; } = null!;

    /// <summary>Okulun öğretmenlerinin planı (varsayılan Teacher Pro).</summary>
    public int TeacherPlanId { get; set; }
    public SubscriptionPlan TeacherPlan { get; set; } = null!;

    /// <summary>Okulun öğrencilerinin planı (varsayılan Student Pro).</summary>
    public int StudentPlanId { get; set; }
    public SubscriptionPlan StudentPlan { get; set; } = null!;

    public DateTime ValidFromUtc { get; set; }

    /// <summary>Hariç üst sınır; null = süresiz.</summary>
    public DateTime? ValidToUtc { get; set; }

    /// <summary>Anlaşma referansı (sözleşme no, yazışma); serbest metin.</summary>
    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }
}

/// <summary>Issue #1092: lisanstaki her değişikliğin kaydı (kim, ne zaman, yeni durum). Silinmez.</summary>
public class SchoolLicenseAudit
{
    public long Id { get; set; }
    public int SchoolId { get; set; }
    public int ActorUserId { get; set; }
    public SchoolLicenseAuditAction Action { get; set; }

    [MaxLength(SubscriptionPlan.KeyMaxLength)]
    public string? TeacherPlanKey { get; set; }

    [MaxLength(SubscriptionPlan.KeyMaxLength)]
    public string? StudentPlanKey { get; set; }

    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ValidToUtc { get; set; }

    [MaxLength(SchoolLicense.NoteMaxLength)]
    public string? Note { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}

public enum SchoolLicenseAuditAction
{
    Created = 1,
    Updated = 2,
    Ended = 3,
}
