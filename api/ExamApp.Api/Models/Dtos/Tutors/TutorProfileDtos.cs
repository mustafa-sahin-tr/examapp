using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos.Tutors;

/// <summary>Öğretmenin verdiği ders (Subject tablosundan).</summary>
public class TutorSubjectDto
{
    public int SubjectId { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Issue #95: bağımsız öğretmenin kendi tutor profili (GET /api/Teacher/tutor-profile).
/// Sadece kaydın sahibi görür; ApprovalStatus onay beklerken de döner ki UI durumu gösterebilsin.
/// </summary>
public class TutorProfileDto
{
    public int TeacherId { get; set; }
    public TeacherApprovalStatus ApprovalStatus { get; set; }
    public List<TutorSubjectDto> Subjects { get; set; } = new();
    public decimal? HourlyRate { get; set; }
    public bool TeachesOnline { get; set; }
    public bool TeachesInPerson { get; set; }
    public string? Bio { get; set; }
}

/// <summary>PUT /api/Teacher/tutor-profile isteği. İş kuralı validasyonu serviste (en az 1 ders, en az 1 mod, ücret &gt; 0).</summary>
public class UpdateTutorProfileDto
{
    [Required(ErrorMessage = "En az bir ders seçilmelidir.")]
    public List<int> SubjectIds { get; set; } = new();

    [Range(0.01, 1_000_000, ErrorMessage = "Saatlik ücret 0'dan büyük olmalıdır.")]
    public decimal HourlyRate { get; set; }

    public bool TeachesOnline { get; set; }

    public bool TeachesInPerson { get; set; }

    [MaxLength(500, ErrorMessage = "Tanıtım metni en fazla 500 karakter olabilir.")]
    public string? Bio { get; set; }
}

/// <summary>
/// Servisten controller'a tutor profil okuma/yazma sonucu. ResponseBaseDto bayrakları
/// (NotFound / Forbidden) controller'da HTTP koduna eşlenir; Profile başarıda dolu.
/// </summary>
public class TutorProfileResultDto : ResponseBaseDto
{
    public TutorProfileDto? Profile { get; set; }
}

/// <summary>GET /api/Teacher/search filtreleri. Tüm alanlar opsiyonel; boşsa tüm onaylı bağımsız öğretmenler döner.</summary>
public class TeacherSearchFilterDto
{
    public int? SubjectId { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }

    /// <summary>true ise sadece online ders verenler; null/false filtre uygulamaz.</summary>
    public bool? Online { get; set; }

    /// <summary>true ise sadece yüz yüze ders verenler; null/false filtre uygulamaz.</summary>
    public bool? InPerson { get; set; }

    public int Skip { get; set; } = 0;

    /// <summary>Varsayılan 20, üst sınır serviste 100'e kırpılır.</summary>
    public int Take { get; set; } = 20;
}

/// <summary>Öğrenci arama sonucu satırı (issue #95). Sadece ApprovalStatus=Approved bağımsız öğretmenler.</summary>
public class TeacherSearchResultDto
{
    public int TeacherId { get; set; }

    /// <summary>auth-api'den çözümlenir; erişilemezse "Öğretmen #{TeacherId}" fallback'i.</summary>
    public string FullName { get; set; } = string.Empty;

    public List<TutorSubjectDto> Subjects { get; set; } = new();
    public decimal? HourlyRate { get; set; }
    public bool TeachesOnline { get; set; }
    public bool TeachesInPerson { get; set; }

    /// <summary>Kısa tanıtım (ilk 160 karakter). Tam metin public-profile'da.</summary>
    public string? Bio { get; set; }
}

/// <summary>Tekil öğretmen public profili (GET /api/Teacher/{id}/public-profile). Arama satırı + tam Bio.</summary>
public class TeacherPublicProfileDto
{
    public int TeacherId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public List<TutorSubjectDto> Subjects { get; set; } = new();
    public decimal? HourlyRate { get; set; }
    public bool TeachesOnline { get; set; }
    public bool TeachesInPerson { get; set; }
    public string? Bio { get; set; }
}
