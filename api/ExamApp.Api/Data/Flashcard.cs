using System;
using System.Collections.Generic;

namespace ExamApp.Api.Data;

public enum FlashcardDeckOwnerKind
{
    Teacher = 1,

    /// <summary>İlk sürümde özel: yalnız sahibi görür, atanamaz, paylaşılamaz.</summary>
    Student = 2,
}

/// <summary>Issue #699: bilgi kartı destesi. Sahibi kullanıcı kimliğiyle tutulur; çok rollü hesapta tür ayrımı <see cref="OwnerKind"/>.</summary>
public class FlashcardDeck : BaseEntity
{
    public int Id { get; set; }

    public int OwnerUserId { get; set; }
    public FlashcardDeckOwnerKind OwnerKind { get; set; }

    public string Title { get; set; } = string.Empty;

    public int? GradeId { get; set; }
    public Grade? Grade { get; set; }

    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public int? TopicId { get; set; }
    public Topic? Topic { get; set; }

    public ICollection<Flashcard> Cards { get; set; } = new List<Flashcard>();
}

public class Flashcard : BaseEntity
{
    public const int MaxTextLength = 2000;

    public int Id { get; set; }

    public int DeckId { get; set; }
    public FlashcardDeck Deck { get; set; } = default!;

    public string Front { get; set; } = string.Empty;
    public string Back { get; set; } = string.Empty;

    /// <summary>Ön yüz görseli; <c>/img/...</c> değeri, yanıtta imzalanır.</summary>
    public string? ImageUrl { get; set; }

    public int Order { get; set; }
}

/// <summary>
/// Öğretmen destesinin hedefi: tek öğrenci ya da okulun bir sınıfı. Görünürlük kuralı
/// <see cref="ExamApp.Api.Services.Flashcards.FlashcardAccess.AssignedTo"/>; platform geneli hedef yok.
/// </summary>
public class FlashcardDeckAssignment : BaseEntity
{
    public int Id { get; set; }

    public int DeckId { get; set; }
    public FlashcardDeck Deck { get; set; } = default!;

    public int? StudentId { get; set; }
    public Student? Student { get; set; }

    public int? GradeId { get; set; }
    public Grade? Grade { get; set; }

    public int? SchoolId { get; set; }
    public School? School { get; set; }
}

/// <summary>Öğrenci + kart başına tekrar durumu. Satır yoksa kart yenidir ve bugün gelir. Sıfırlamada gerçekten silinir.</summary>
public class FlashcardReview
{
    public long Id { get; set; }

    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;

    public int FlashcardId { get; set; }
    public Flashcard Flashcard { get; set; } = default!;

    public int Box { get; set; }
    public DateOnly DueDay { get; set; }
    public DateTime LastReviewedAtUtc { get; set; }
    public int ReviewCount { get; set; }
}
