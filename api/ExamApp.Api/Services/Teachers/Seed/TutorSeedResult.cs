using System.Collections.Generic;

namespace ExamApp.Api.Services.Teachers.Seed;

/// <summary><c>seed-tutors</c> özet raporu (issue #218). HTTP yüzeyi yok.</summary>
public sealed class TutorSeedResult
{
    public bool DryRun { get; set; }
    public string KeycloakMode { get; set; } = string.Empty;
    public double PendingRatio { get; set; }

    /// <summary>Tabana sayılan seed okul öğretmeni (il+branş toplamı).</summary>
    public int SchoolTeachersCounted { get; set; }
    public int SchoolsCounted { get; set; }
    public int SchoolsSkippedByLimit { get; set; }

    public int Planned { get; set; }
    public int PlannedPending { get; set; }
    public int TutorsCreated { get; set; }
    public int TutorsExisting { get; set; }
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

    /// <summary>İl × branş kırılımı (yalnızca eşleşen iller; taban 0 olan gruplar da listelenir).</summary>
    public List<TutorSeedGroupSummary> Groups { get; set; } = new();
    public List<string> UnmatchedProvinces { get; set; } = new();

    public List<TutorSeedAccount> Accounts { get; set; } = new();
    public const int AccountSampleLimit = 200;

    public List<string> Errors { get; set; } = new();
    public const int ErrorSampleLimit = 50;
}

public sealed class TutorSeedGroupSummary
{
    public string Province { get; set; } = string.Empty;
    public TeacherSeedBranch Branch { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    /// <summary>Tabandaki seed okul öğretmeni sayısı.</summary>
    public int SchoolTeachers { get; set; }
    /// <summary><c>floor(SchoolTeachers / 2)</c>.</summary>
    public int Planned { get; set; }
    public int PlannedPending { get; set; }
    public int Created { get; set; }
    public int Existing { get; set; }
    public int Failed { get; set; }
}

public sealed class TutorSeedAccount
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public TeacherSeedBranch Branch { get; set; }
    public decimal HourlyRate { get; set; }
    public bool TeachesInPerson { get; set; }
    public bool Pending { get; set; }
    /// <summary>Planned (dry-run) | Created | Existing | Failed</summary>
    public string Status { get; set; } = string.Empty;
}
