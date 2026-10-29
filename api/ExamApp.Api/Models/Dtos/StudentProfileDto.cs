using ExamApp.Api.Services.Storage;
using System;
using System.Linq.Expressions;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

public class StudentProfileDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    [NotStorageUrl("Avatar ön ayar kimliği/dış adres; student-avatars bucket'ı özel ve URL'si sunulmuyor")]
    public string AvatarUrl { get; set; } = string.Empty;
    public int? GradeId { get; set; }
    public string? SchoolName { get; set; }
    /// <summary>issue #361: DOĞRULANMIŞ okul (yetki kararları buna bakar); beklemedeki üyelikte null.</summary>
    public int? SchoolId { get; set; }
    /// <summary>issue #361: öğrencinin kendi kaydında seçtiği, onay bekleyen okul (doğrulanmışsa null).</summary>
    public int? PendingSchoolId { get; set; }
    /// <summary>issue #361: <see cref="PendingSchoolId"/> okulunun adı (yalnız gösterim).</summary>
    public string? PendingSchoolName { get; set; }
    /// <summary>
    /// issue #438: hesap veli tarafından açıldı (kullanıcı adı + şifre). UI "Hesabım / şifre değiştir" kartını gizler; şifreyi veli
    /// sıfırlar.
    /// </summary>
    public bool ParentManaged { get; set; }
    public int XP { get; set; }
    public int Level { get; set; } // 🟢 En son seviy

    // issue #501: aşağıdaki sayılar yalnız GET api/student/profile'da dolar (istatistik kuralı: tamamlanmış, etkinlik olmayan
    // oturumlar; StudentController.FillProfileStatsAsync). Diğer uçların döndüğü profilde 0'dır.
    public int TotalQuestionsSolved { get; set; }
    public int CorrectAnswers { get; set; }
    public int WrongAnswers { get; set; }
    public int TestsCompleted { get; set; }
    /// <summary>issue #501: genel liderlik tablosundaki sıra (#193 kuralı); profil ucu dışında 0.</summary>
    public int LeaderboardRank { get; set; }
    public List<StudentWorkSheetSummaryDto> RecentTests { get; set; } = new List<StudentWorkSheetSummaryDto>();
}

public class StudentWorkSheetSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public int Score { get; set; } // 10 puan üzerinden hesaplama
    public int TotalQuestions { get; set; }

}

public class StudentDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    [NotStorageUrl("Avatar ön ayar kimliği/dış adres; student-avatars bucket'ı özel ve URL'si sunulmuyor")]
    public string AvatarUrl { get; set; } = string.Empty;
    public int? GradeId { get; set; }
    public int XP { get; set; }
    public int Level { get; set; } // 🟢 En son seviy
    public string? SchoolName { get; set; }
    /// <summary>issue #361: DOĞRULANMIŞ okul; beklemedeki üyelikte null (bkz. <see cref="PendingSchoolId"/>).</summary>
    public int? SchoolId { get; set; }
    /// <summary>issue #361: onay bekleyen okul üyeliği (öğrencinin kendi seçtiği okul); yoksa null. UI "Okul onayı bekleniyor".</summary>
    public int? PendingSchoolId { get; set; }
    /// <summary>issue #361: <see cref="PendingSchoolId"/> okulunun adı.</summary>
    public string? PendingSchoolName { get; set; }
    public string StudentNumber { get; set; } = string.Empty;
    public string? ThemePreset { get; set; } = "standard"; // 🎨 Theme tercihi
    public string? ThemeCustomConfig { get; set; } // 🎨 Custom theme config (JSON)

    /// <summary>
    /// Issue #440 / #441: öğrencinin veli durumu (yalnız profil tazeleme <c>POST api/auth/refresh</c> yanıtında; UI banner/modal/route guard).
    /// </summary>
    public ExamApp.Api.Models.Dtos.ParentInvitations.ParentStatusDto? ParentStatus { get; set; }
}

public class TeacherDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    [NotStorageUrl("Avatar ön ayar kimliği/dış adres; student-avatars bucket'ı özel ve URL'si sunulmuyor")]
    public string AvatarUrl { get; set; } = string.Empty;
    public string? SchoolName { get; set; }
    public int? SchoolId { get; set; }

    /// <summary>issue #384: bağımsız öğretmen mi — tutor profili kuralının (TeacherService) UI'daki karşılığı.</summary>
    public bool IsIndependentTutor { get; set; }
    public string? ThemePreset { get; set; } = "standard"; // 🎨 Theme tercihi
    public string? ThemeCustomConfig { get; set; } // 🎨 Custom theme config (JSON)

    /// <summary>issue #287: öğretmen hesabı admin tarafından onaylandı mı — false iken öğretmen özellikleri 403 TeacherNotApproved.</summary>
    public bool TeacherAccountApproved { get; set; }

    /// <summary>issue #289: öğretmen hesap onayı admin tarafından askıya alındı (neden dönülmez). true iken <see cref="TeacherAccountApproved"/> false.</summary>
    public bool TeacherAccountSuspended { get; set; }

    /// <summary>issue #287: mevcut başvurunun durumu: "Pending" | "Approved" | "Rejected".</summary>
    public string TeacherApplicationStatus { get; set; } = string.Empty;

    /// <summary>issue #287: yalnızca <see cref="TeacherApplicationStatus"/> == "Rejected" iken dolu.</summary>
    public string? RejectionReason { get; set; }
}

public class StudentLookupDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string StudentNumber { get; set; } = string.Empty;
    public string? SchoolName { get; set; }
    public int? SchoolId { get; set; }
    public int? GradeId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    [NotStorageUrl("Avatar ön ayar kimliği/dış adres; student-avatars bucket'ı özel ve URL'si sunulmuyor")]
    public string AvatarUrl { get; set; } = string.Empty;
}