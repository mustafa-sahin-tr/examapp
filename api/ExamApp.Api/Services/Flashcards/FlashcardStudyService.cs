using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Flashcards;

/// <summary>Issue #699: öğrencinin kart çalışması. Puan, rozet ve liderlik yok; outbox event'i yazılmaz.</summary>
public class FlashcardStudyService(AppDbContext context, ILocalDayCalendar calendar) : IFlashcardStudyService
{
    private const int MaxDecksListed = 200;

    public async Task<IReadOnlyList<FlashcardDeckSummaryDto>> ListDecksAsync(FlashcardLearner learner, CancellationToken ct = default)
    {
        var today = calendar.Today;
        var studentId = learner.StudentId;
        return await FlashcardAccess.VisibleDecks(context, learner)
            .AsNoTracking()
            .OrderByDescending(d => d.CreateTime)
            .Take(MaxDecksListed)
            .Select(d => new FlashcardDeckSummaryDto
            {
                Id = d.Id,
                Title = d.Title,
                GradeId = d.GradeId,
                GradeName = d.Grade != null ? d.Grade.Name : null,
                SubjectId = d.SubjectId,
                SubjectName = d.Subject != null ? d.Subject.Name : null,
                TopicId = d.TopicId,
                TopicName = d.Topic != null ? d.Topic.Name : null,
                CardCount = d.Cards.Count(),
                DueCount = d.Cards.Count(c => !context.FlashcardReviews.Any(r => r.FlashcardId == c.Id && r.StudentId == studentId && r.DueDay > today)),
                IsOwner = d.OwnerKind == FlashcardDeckOwnerKind.Student,
            })
            .ToListAsync(ct);
    }

    public async Task<FlashcardDeckDetailDto?> GetDeckAsync(FlashcardLearner learner, int deckId, CancellationToken ct = default)
    {
        return await FlashcardAccess.VisibleDecks(context, learner)
            .AsNoTracking()
            .Where(d => d.Id == deckId)
            .Select(FlashcardProjections.DetailFor(isOwner: false))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<FlashcardDueCardDto>> GetDueCardsAsync(FlashcardLearner learner, int? deckId, int take, CancellationToken ct = default)
    {
        var today = calendar.Today;
        var studentId = learner.StudentId;
        var decks = FlashcardAccess.VisibleDecks(context, learner);
        if (deckId.HasValue)
            decks = decks.Where(d => d.Id == deckId.Value);

        // Sıra: önce günü en eski olan, sonra yeni kartlar.
        return await (
                from card in context.Flashcards.AsNoTracking()
                join deck in decks on card.DeckId equals deck.Id
                from review in context.FlashcardReviews.Where(r => r.FlashcardId == card.Id && r.StudentId == studentId).DefaultIfEmpty()
                where review == null || review.DueDay <= today
                orderby review == null, review!.DueDay, card.DeckId, card.Order, card.Id
                select new FlashcardDueCardDto
                {
                    Id = card.Id,
                    Front = card.Front,
                    Back = card.Back,
                    ImageUrl = card.ImageUrl,
                    Order = card.Order,
                    DeckId = deck.Id,
                    DeckTitle = deck.Title,
                    Box = review == null ? null : review.Box,
                })
            .Take(take)
            .ToListAsync(ct);
    }

    public Task<int> GetDueCountAsync(FlashcardLearner learner, CancellationToken ct = default)
    {
        var today = calendar.Today;
        var studentId = learner.StudentId;
        var decks = FlashcardAccess.VisibleDecks(context, learner);
        return context.Flashcards
            .Where(c => decks.Any(d => d.Id == c.DeckId))
            .CountAsync(c => !context.FlashcardReviews.Any(r => r.FlashcardId == c.Id && r.StudentId == studentId && r.DueDay > today), ct);
    }

    public async Task<FlashcardReviewResult> ReviewAsync(FlashcardLearner learner, int cardId, FlashcardRecall recall, CancellationToken ct = default)
    {
        var decks = FlashcardAccess.VisibleDecks(context, learner);
        var visible = await context.Flashcards
            .Where(c => c.Id == cardId)
            .AnyAsync(c => decks.Any(d => d.Id == c.DeckId), ct);
        if (!visible)
            return new(FlashcardReviewOutcome.NotFound);

        var today = calendar.Today;
        var studentId = learner.StudentId;
        var current = await context.FlashcardReviews
            .AsNoTracking()
            .Where(r => r.StudentId == studentId && r.FlashcardId == cardId)
            .Select(r => new { r.Box, r.DueDay })
            .FirstOrDefaultAsync(ct);
        if (current != null && current.DueDay > today)
            return new(FlashcardReviewOutcome.NotDue);

        var (box, dueDay) = FlashcardSchedule.Next(current?.Box, recall, today);
        var now = DateTime.UtcNow;

        if (current == null)
        {
            context.FlashcardReviews.Add(new FlashcardReview
            {
                StudentId = studentId,
                FlashcardId = cardId,
                Box = box,
                DueDay = dueDay,
                LastReviewedAtUtc = now,
                ReviewCount = 1,
            });
            try
            {
                await context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
            {
                return new(FlashcardReviewOutcome.NotDue);
            }
        }
        else
        {
            // Koşullu güncelleme: aynı anda gelen ikinci istek satırı bulamaz.
            var updated = await context.FlashcardReviews
                .Where(r => r.StudentId == studentId && r.FlashcardId == cardId && r.DueDay <= today)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Box, box)
                    .SetProperty(r => r.DueDay, dueDay)
                    .SetProperty(r => r.LastReviewedAtUtc, now)
                    .SetProperty(r => r.ReviewCount, r => r.ReviewCount + 1), ct);
            if (updated == 0)
                return new(FlashcardReviewOutcome.NotDue);
        }

        return new(FlashcardReviewOutcome.Recorded, new FlashcardReviewResultDto { CardId = cardId, Box = box, DueDay = dueDay });
    }
}
