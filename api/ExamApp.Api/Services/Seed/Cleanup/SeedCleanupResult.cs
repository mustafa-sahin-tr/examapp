using System.Collections.Generic;

namespace ExamApp.Api.Services.Seed.Cleanup;

/// <summary><c>seed-cleanup</c> raporu (issue #218): sistem başına silinen / atlanan / hata + envanter (özet rapor).</summary>
public sealed class SeedCleanupResult
{
    public bool Applied { get; set; }
    public bool Force { get; set; }

    // ---- Envanter (dry-run = özet rapor) ----
    public int SeedSchools { get; set; }
    public int SeedSchoolTeachers { get; set; }
    public int SeedTutors { get; set; }
    public int SeedTutorsPending { get; set; }

    /// <summary>İl × tür (İlkokul/Ortaokul/Bilinmeyen) okul sayıları.</summary>
    public List<SeedCleanupSchoolGroup> SchoolGroups { get; set; } = new();
    /// <summary>İl × branş okul öğretmeni ve bağımsız öğretmen sayıları. Tutor ili e-postadan (auth-api listesi) çıkarılır; yoksa "?".</summary>
    public List<SeedCleanupTeacherGroup> TeacherGroups { get; set; } = new();

    // ---- Exam: öğretmenler ----
    public int TeachersDeleted { get; set; }
    /// <summary>Gerçek öğrenci randevusu (Booking) var → HER modda atlandı (seed-dışı satıra dokunulmaz).</summary>
    public int TeachersSkippedRealStudentBooking { get; set; }
    /// <summary>Atlanan öğretmenlerdeki gerçek öğrenci randevusu sayısı.</summary>
    public int RealStudentBookings { get; set; }
    /// <summary>Müsaitlik verisi (slot/kural) var, --force yok → atlandı.</summary>
    public int TeachersSkippedScheduling { get; set; }
    /// <summary>Worksheet/soru yazmış, --force yok → atlandı.</summary>
    public int TeachersSkippedContent { get; set; }
    /// <summary>--force ile silinen ve müsaitlik satırları (slot/kural) da silinen öğretmen.</summary>
    public int TeachersForceDeleted { get; set; }
    /// <summary>--force ile silinen ama yazdığı worksheet/soru yerinde kalan (sahipsiz) öğretmen.</summary>
    public int TeachersOrphanedContent { get; set; }
    public int SlotsDeleted { get; set; }
    public int RulesDeleted { get; set; }
    public int TeacherSubjectsDeleted { get; set; }
    public int WorksheetsOrphaned { get; set; }
    public int QuestionsOrphaned { get; set; }

    // ---- Exam: okullar ----
    public int SchoolsDeleted { get; set; }
    public int SchoolsSkippedNonSeedTeacher { get; set; }
    public int SchoolsSkippedStudent { get; set; }
    public int SchoolsSkippedAssignment { get; set; }
    public int SchoolsSkippedSeedTeacherKept { get; set; }

    // ---- auth-api (Keycloak + identity) ----
    public bool AuthApiCalled { get; set; }
    public string? AuthApiError { get; set; }
    public int KeycloakDeleted { get; set; }
    public int KeycloakMissing { get; set; }
    public int KeycloakExcluded { get; set; }
    public int KeycloakSkippedForeign { get; set; }
    public int KeycloakFailed { get; set; }
    public int IdentityDeleted { get; set; }
    public int IdentityExcluded { get; set; }
    public int IdentityFailed { get; set; }
    /// <summary>auth-api dry-run'da silinecek olarak listelenen (Planned) hesap sayısı.</summary>
    public int AuthPlanned { get; set; }

    public long ExamDbElapsedMs { get; set; }
    public long AuthApiElapsedMs { get; set; }
    public long TotalElapsedMs { get; set; }

    /// <summary>Atlanan öğretmen/okul örnekleri (ilk N) — neden ile.</summary>
    public List<string> Skipped { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public const int SampleLimit = 50;

    public int TotalFailed => KeycloakFailed + IdentityFailed + Errors.Count;
}

public sealed class SeedCleanupSchoolGroup
{
    public string Province { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class SeedCleanupTeacherGroup
{
    public string Province { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public int SchoolTeachers { get; set; }
    public int Tutors { get; set; }
}
