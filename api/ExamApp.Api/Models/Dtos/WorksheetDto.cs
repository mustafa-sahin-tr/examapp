using System;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

public class WorksheetDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int GradeId { get; set; }
    public int? SubjectId { get; set; }

    public int? TopicId { get; set; } // Konu ID'si (isteğe bağlı)
    public int? SubTopicId { get; set; } // Alt konu ID'si (isteğe bağlı)
    public int MaxDurationSeconds { get; set; }
    public bool IsPracticeTest { get; set; }
    public string? Subtitle { get; set; }
    public string? ImageUrl { get; set; }
    public string? BadgeText { get; set; }
    public int? BookTestId { get; set; }
    public int? BookId { get; set; }
    public int QuestionCount { get; set; } // ✅ Eklenen alan

    public InstanceSummaryDto? Instance { get; set; }

    public int InstanceCount { get; set; } = 0;// ✅ Eklenen alan = 0

    // Yetki modeli: "sahibi VEYA admin". CanEdit istek sahibine göre hesaplanır.
    public bool CanEdit { get; set; }

    // worksheet.CreateUserId — legacy kayıtlarda null olabilir.
    public int? CreatedByUserId { get; set; }

    // Sadece istek sahibi admin ise doldurulur; aksi halde null.
    public string? CreatedByName { get; set; }

    // --- Görünürlük eksenleri (issue #9). Davranış değişmez; UI için taşınır. ---

    // Öğretmenler arası paylaşım ekseni. Şu an her kayıt Private.
    public WorksheetTeacherSharing TeacherSharing { get; set; } = WorksheetTeacherSharing.Private;

    // Öğrenciye görünürlük ekseni. Şu an her kayıt Normal.
    public WorksheetStudentVisibility StudentVisibility { get; set; } = WorksheetStudentVisibility.Normal;

    // İstek sahibi bu worksheet'in sahibi mi (legacy CreateUserId null/0 => false).
    public bool IsOwner { get; set; }

    // Tekil detay (GetWorksheetByIdAsync) akışında admin VEYA sahip için dolu.
    // Liste akışlarında (N+1'den kaçınmak için) yalnız admin için dolu; aksi halde null.
    public string? OwnerName { get; set; }

    // İstek sahibi bu worksheet'i öğrenciye atayabilir mi (şimdilik CanEdit ile aynı).
    public bool CanAssign { get; set; }

    // Öğrenci akışı (issue #14): bu sınav istek sahibi öğrenciye/sınıfına aktif olarak
    // atanmış mı (true), yoksa yalnızca keşfet listesinden mi görünüyor (false).
    // Öğretmen akışlarında anlamsız, her zaman false döner.
    public bool IsAssigned { get; set; }
}

public class WorksheetWithInstanceDto
{
    public WorksheetDto Worksheet { get; set; } = default!;
    public WorksheetInstance? Instance { get; set; }
}

public class WorksheetInstanceDto
{
    public int Id { get; set; }
    public string TestName { get; set; } = default!;
    public WorksheetInstanceStatus Status { get; set; }
    public int MaxDurationSeconds { get; set; }
    public bool IsPracticeTest { get; set; }

    public List<WorksheetInstanceQuestionDto> TestInstanceQuestions { get; set; } = new();
}

public class WorksheetInstanceQuestionDto
{
    public int Id { get; set; }
    public int Order { get; set; }
    public QuestionDto Question { get; set; } = default!;
    public int? SelectedAnswerId { get; set; }
    public string? AnswerPayload { get; set; }
    public int? TimeTaken { get; set; }
}

public class WorksheetInstanceResultDto
{
    public int Id { get; set; }
    public string TestName { get; set; } = default!;
    public WorksheetInstanceStatus Status { get; set; }
    public int MaxDurationSeconds { get; set; }
    public bool IsPracticeTest { get; set; }
    public List<WorksheetInstanceQuestionDto> TestInstanceQuestions { get; set; } = new();
}
