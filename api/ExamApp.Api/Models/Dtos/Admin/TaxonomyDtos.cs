using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos.Admin;

// ---- Read: full tree ----

public class TaxonomyTreeDto
{
    public List<TaxonomySubjectDto> Subjects { get; set; } = new();
    public List<TaxonomyGradeDto> Grades { get; set; } = new();
}

public class TaxonomyGradeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TaxonomySubjectDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Grades this subject is linked to via GradeSubject. Empty = "Sınıf atanmamış".</summary>
    public List<int> GradeIds { get; set; } = new();

    public List<TaxonomyTopicDto> Topics { get; set; } = new();
}

public class TaxonomyTopicDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SubjectId { get; set; }
    public int GradeId { get; set; }
    public string? GradeName { get; set; }
    public List<TaxonomySubTopicDto> SubTopics { get; set; } = new();
}

public class TaxonomySubTopicDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TopicId { get; set; }
    public int QuestionCount { get; set; }
}

// ---- Write ----

public class UpsertSubjectDto
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Create: required — at least one grade (issue #249: a subject with no GradeSubject link is
    /// unreachable from the grade-filtered admin screen). Null/empty → 400 <see cref="TaxonomyErrorCodes.GradeRequired"/>.
    /// Update: optional. When provided (non-null, non-empty) the subject's GradeSubject links are
    /// synchronised to exactly this set (missing ones added, extra ones removed); a link that still
    /// has topics is not removed (<see cref="TaxonomyErrorCodes.SubjectGradeHasTopics"/>, nothing is written).
    /// When null or empty the existing links are left untouched.
    /// </summary>
    public List<int>? GradeIds { get; set; }
}

public class UpsertTopicDto
{
    public string Name { get; set; } = string.Empty;
    public int SubjectId { get; set; }
    public int GradeId { get; set; }
}

public class UpsertSubTopicDto
{
    public string Name { get; set; } = string.Empty;
    public int TopicId { get; set; }
}

/// <summary>Taksonomi yazma uçlarının yanıtı: ResponseBaseDto + makine tarafından okunabilir hata kodu.</summary>
public class TaxonomyResponseDto : ResponseBaseDto
{
    /// <summary>Hata durumunda kod — bkz. <see cref="TaxonomyErrorCodes"/>. Başarıda ve kodsuz hatalarda null.</summary>
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Issue #249: taksonomide "sahipsiz" kayıt (hiç sınıfa bağlı olmayan ders ya da dersi kendi sınıfına
/// bağlı olmayan konu) oluşmasını engelleyen kuralların kodları. Hepsi 400.
/// </summary>
public static class TaxonomyErrorCodes
{
    /// <summary>Konunun (SubjectId, GradeId) çifti için aktif GradeSubject bağı yok.</summary>
    public const string SubjectGradeNotLinked = "SubjectGradeNotLinked";

    /// <summary>Kaldırılmak istenen ders–sınıf bağında hâlâ aktif konu var.</summary>
    public const string SubjectGradeHasTopics = "SubjectGradeHasTopics";

    /// <summary>Ders oluştururken en az bir sınıf seçilmedi.</summary>
    public const string GradeRequired = "GradeRequired";

    /// <summary>Dersin kalan son aktif sınıf bağı kaldırılmak istendi (ders sınıfsız kalırdı).</summary>
    public const string LastGradeLink = "LastGradeLink";
}

// ---- Classifier cache ----

public class ClassifierCacheStatusDto
{
    public string? CachedContentName { get; set; }
    public string? Model { get; set; }
    public DateTime? RefreshedAt { get; set; }
    public int SubTopicCount { get; set; }
    public bool ConfiguredInSettings { get; set; }

    /// <summary>True when the current taxonomy no longer matches what the cache was built from.</summary>
    public bool Stale { get; set; }
}

public class ClassifierCacheRefreshResultDto : ResponseBaseDto
{
    public string? CachedContentName { get; set; }
    public int SubTopicCount { get; set; }
    public DateTime RefreshedAt { get; set; }
}
