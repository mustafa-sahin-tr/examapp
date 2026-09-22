using System.Collections.Generic;

namespace ExamApp.Api.Services.Teachers.Seed;

/// <summary><c>seed-teachers</c> özet raporu (issue #217). HTTP yüzeyi yok — servisin yanında.</summary>
public sealed class TeacherSeedResult
{
    public bool DryRun { get; set; }
    public string KeycloakMode { get; set; } = string.Empty;

    public int SchoolsSelected { get; set; }
    public int SchoolsIlkokul { get; set; }
    public int SchoolsOrtaokul { get; set; }
    /// <summary>Adından türü çıkarılamayan (ne "İlkokulu" ne "Ortaokulu") seed okullar — atlandı.</summary>
    public int SchoolsUnknownKind { get; set; }
    public int SchoolsSkippedByLimit { get; set; }

    /// <summary>Plan: kaç hesap üretilmesi gerekiyor (tüm okullar × branş dağılımı).</summary>
    public int Planned { get; set; }
    /// <summary>Exam DB'de yeni Teacher satırı (dry-run'da 0).</summary>
    public int TeachersCreated { get; set; }
    /// <summary>Exam DB'de zaten vardı (idempotent koşu).</summary>
    public int TeachersExisting { get; set; }
    /// <summary>Herhangi bir katmanda başarısız (Keycloak/identity) — Teacher yazılmadı.</summary>
    public int Failed { get; set; }

    public int KeycloakCreated { get; set; }
    public int KeycloakExisting { get; set; }
    public int IdentityCreated { get; set; }
    public int IdentityExisting { get; set; }

    public long KeycloakElapsedMs { get; set; }
    public long IdentityDbElapsedMs { get; set; }
    public long ExamDbElapsedMs { get; set; }
    public long TotalElapsedMs { get; set; }
    public int Batches { get; set; }

    public List<TeacherSeedProvinceSummary> Provinces { get; set; } = new();
    public List<TeacherSeedBranchSummary> Branches { get; set; } = new();

    /// <summary>İstenen ama Province tablosunda bulunmayan iller.</summary>
    public List<string> UnmatchedProvinces { get; set; } = new();

    /// <summary>Oluşturulan/mevcut/başarısız hesaplar — ilk <see cref="AccountSampleLimit"/> tanesi.</summary>
    public List<TeacherSeedAccount> Accounts { get; set; } = new();
    public const int AccountSampleLimit = 200;

    /// <summary>Hata mesajları (e-posta + neden) — ilk <see cref="ErrorSampleLimit"/> tanesi.</summary>
    public List<string> Errors { get; set; } = new();
    public const int ErrorSampleLimit = 50;
}

public sealed class TeacherSeedProvinceSummary
{
    public string Province { get; set; } = string.Empty;
    public bool ProvinceMatched { get; set; }
    public int SchoolsIlkokul { get; set; }
    public int SchoolsOrtaokul { get; set; }
    public int SchoolsUnknownKind { get; set; }
    public int SkippedByLimit { get; set; }
    public int Planned { get; set; }
    public int Created { get; set; }
    public int Existing { get; set; }
    public int Failed { get; set; }
}

public sealed class TeacherSeedBranchSummary
{
    public TeacherSeedBranch Branch { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public int Planned { get; set; }
    public int Created { get; set; }
    public int Existing { get; set; }
    public int Failed { get; set; }
}

public sealed class TeacherSeedAccount
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string School { get; set; } = string.Empty;
    public int SchoolId { get; set; }
    public string Province { get; set; } = string.Empty;
    public TeacherSeedBranch Branch { get; set; }
    /// <summary>Planned (dry-run) | Created | Existing | Failed</summary>
    public string Status { get; set; } = string.Empty;
}
