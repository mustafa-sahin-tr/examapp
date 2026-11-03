using ExamApp.Api.Services.Storage;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ExamApp.Api.Models.Dtos;

public class StartQuestionExportDto
{
    public List<int> QuestionIds { get; set; } = new();

    // Used for idempotent imports: (SourceKey, ExternalQuestionKey)
    public string? SourceKey { get; set; }

    // Kapsam: biri verilirse yalnız eşleşen sorular alınır. Sınıf, konudan ya da sorunun etkinliğinden gelir.
    public int? GradeId { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public int? BookId { get; set; }

    public bool HasScope => GradeId.HasValue || SubjectId.HasValue || TopicId.HasValue || BookId.HasValue;
}

public class StartQuestionImportDto
{
    // Used for idempotency. Must match the export SourceKey if provided.
    public string? SourceKey { get; set; }
}

public class StartQuestionImportFormDto
{
    public string? SourceKey { get; set; }

    [Required]
    public IFormFile File { get; set; } = default!;
}

public class QuestionTransferJobDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SourceKey { get; set; } = "default";
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    [NotStorageUrl("question-transfer/* paketleri: asla imzalanmaz, yalnız Admin API akışıyla indirilir (#365)")]
    public string? FileUrl { get; set; }
    public string? Message { get; set; }
    public QuestionTransferImportResultDto? Result { get; set; }
}

public class QuestionTransferImportResultDto
{
    public int Imported { get; set; }
    public int Matched { get; set; }
    public int AlreadyImported { get; set; }
    public int Conflicts { get; set; }
    public int Failed { get; set; }
    public int MissingTaxonomy { get; set; }

    /// <summary>Paket yüzünden burada yayından kaldırılan sorular: sayı sınırı yok, geri almak için hepsi gerekir.</summary>
    public int Unpublished { get; set; }
    public List<int> UnpublishedQuestionIds { get; set; } = new();
    public List<string> ConflictSamples { get; set; } = new();
    public List<string> FailedSamples { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class QuestionTransferExportBundleDto
{
    public string SourceKey { get; set; } = "default";
    public int BundleNo { get; set; }
    public int QuestionCount { get; set; }
    [NotStorageUrl("question-transfer/* paketleri: asla imzalanmaz, yalnız Admin API akışıyla indirilir (#365)")]
    public string? FileUrl { get; set; }
}

public class QuestionTransferImportPreviewDto
{
    public string SourceKey { get; set; } = "default";
    public int QuestionCount { get; set; }
    public int AlreadyImportedCount { get; set; }
    public int NewCount { get; set; }
    public int MatchedCount { get; set; }
    public int ConflictCount { get; set; }
    public int MissingTaxonomyCount { get; set; }

    /// <summary>Görseli pakette bulunmayan soru sayısı: içe aktarımda alınmaz, hata olarak yazılır.</summary>
    public int MissingImageCount { get; set; }

    /// <summary>İçe alınırsa burada yayından kalkacak soru sayısı.</summary>
    public int UnpublishCount { get; set; }

    /// <summary>Hedefte hash'i henüz hesaplanmamış soru sayısı: sıfır değilse eşleşme sayıları eksik olabilir.</summary>
    public int TargetUnhashedCount { get; set; }
    public List<string> ConflictSamples { get; set; } = new();
}
