using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Tenancy;

namespace ExamApp.Api.Services.Flashcards;

/// <summary>İstemcinin metne çevirdiği iş kuralı hatası; mesaj hata kodudur.</summary>
public sealed class FlashcardRuleException(string code) : Exception(code);

/// <summary>Null dönüş: kayıt yok ya da çağıranın değil (varlık sızdırılmaz).</summary>
public interface IFlashcardDeckService
{
    Task<IReadOnlyList<FlashcardDeckSummaryDto>> ListOwnedAsync(FlashcardOwner owner, CancellationToken ct = default);
    Task<FlashcardDeckDetailDto?> GetOwnedAsync(FlashcardOwner owner, int deckId, CancellationToken ct = default);
    Task<FlashcardDeckDetailDto> CreateAsync(FlashcardOwner owner, SaveFlashcardDeckDto request, CancellationToken ct = default);
    Task<FlashcardDeckDetailDto?> UpdateAsync(FlashcardOwner owner, int deckId, SaveFlashcardDeckDto request, CancellationToken ct = default);
    Task<bool> DeleteAsync(FlashcardOwner owner, int deckId, CancellationToken ct = default);

    Task<FlashcardDto?> AddCardAsync(FlashcardOwner owner, int deckId, SaveFlashcardDto request, CancellationToken ct = default);
    Task<FlashcardDto?> UpdateCardAsync(FlashcardOwner owner, int cardId, SaveFlashcardDto request, CancellationToken ct = default);
    Task<bool> DeleteCardAsync(FlashcardOwner owner, int cardId, CancellationToken ct = default);

    Task<IReadOnlyList<FlashcardDeckAssignmentDto>?> ListAssignmentsAsync(SchoolScope requester, int deckId, CancellationToken ct = default);
    Task<FlashcardDeckAssignmentDto?> AssignAsync(SchoolScope requester, int deckId, AssignFlashcardDeckDto request, CancellationToken ct = default);
    Task<bool> UnassignAsync(SchoolScope requester, int deckId, int assignmentId, CancellationToken ct = default);
}

public enum FlashcardReviewOutcome
{
    Recorded,
    NotFound,

    /// <summary>Kartın günü gelmedi; çift dokunuş kutuyu iki kez ilerletmesin.</summary>
    NotDue,
}

public sealed record FlashcardReviewResult(FlashcardReviewOutcome Outcome, FlashcardReviewResultDto? Review = null);

public interface IFlashcardStudyService
{
    Task<IReadOnlyList<FlashcardDeckSummaryDto>> ListDecksAsync(FlashcardLearner learner, CancellationToken ct = default);
    Task<FlashcardDeckDetailDto?> GetDeckAsync(FlashcardLearner learner, int deckId, CancellationToken ct = default);
    Task<IReadOnlyList<FlashcardDueCardDto>> GetDueCardsAsync(FlashcardLearner learner, int? deckId, int take, CancellationToken ct = default);
    Task<int> GetDueCountAsync(FlashcardLearner learner, CancellationToken ct = default);
    Task<FlashcardReviewResult> ReviewAsync(FlashcardLearner learner, int cardId, FlashcardRecall recall, CancellationToken ct = default);
}
