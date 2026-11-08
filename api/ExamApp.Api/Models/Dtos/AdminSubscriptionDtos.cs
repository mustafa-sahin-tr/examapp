using System.Text.Json.Serialization;

namespace ExamApp.Api.Models.Dtos;

/// <summary>Issue #1030: admin plan ataması aramasında bir kullanıcı. E-posta maskelidir (#246).</summary>
public class AdminSubscriptionUserDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? PlanKey { get; set; }
    public string? PlanName { get; set; }
    public DateTime? ValidToUtc { get; set; }
}

/// <summary>Issue #1030: kullanıcının şimdiki planı, atamaları ve değişiklik geçmişi (en yeni önce).</summary>
public class AdminSubscriptionUserDetailDto : AdminSubscriptionUserDto
{
    public List<AdminSubscriptionAssignmentDto> Assignments { get; set; } = new();
    public List<AdminSubscriptionChangeDto> Changes { get; set; } = new();
}

public class AdminSubscriptionAssignmentDto
{
    public int Id { get; set; }
    public string PlanKey { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public DateTime ValidFromUtc { get; set; }
    public DateTime? ValidToUtc { get; set; }

    /// <summary><c>Active</c> | <c>Scheduled</c> | <c>Ended</c>.</summary>
    public string Status { get; set; } = string.Empty;
}

public class AdminSubscriptionChangeDto
{
    public DateTime OccurredAtUtc { get; set; }

    /// <summary><c>Assigned</c> | <c>Ended</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary><c>null</c> = atama yoktu (Free).</summary>
    public string? FromPlanKey { get; set; }
    public string? ToPlanKey { get; set; }
    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ValidToUtc { get; set; }

    /// <summary>Değişikliği yapan (admin ya da kendi başlattığında kullanıcının kendisi); çözülemezse boş.</summary>
    public string ActorName { get; set; } = string.Empty;
    public bool BySelf { get; set; }

    /// <summary>Okula toplu atamada okul; tekil atamada null.</summary>
    public int? SchoolId { get; set; }
}

/// <summary>Issue #1030: okula toplu atamada hedef kitle (JSON'da ad: <c>Teachers</c> | <c>Students</c> | <c>All</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SchoolPlanAudience>))]
public enum SchoolPlanAudience
{
    Teachers,
    Students,
    All,
}

/// <summary>Issue #1030: okulun plan atanabilecek üyeleri (öğretmen: bağlı okul; öğrenci: doğrulanmış üyelik).</summary>
public class SchoolSubscriptionMembersDto
{
    public int SchoolId { get; set; }
    public int Teachers { get; set; }
    public int Students { get; set; }
}

public class AssignSchoolSubscriptionPlanDto : AssignSubscriptionPlanDto
{
    public SchoolPlanAudience Audience { get; set; } = SchoolPlanAudience.All;
}

public class AssignSchoolSubscriptionPlanResultDto
{
    public int Assigned { get; set; }
}
