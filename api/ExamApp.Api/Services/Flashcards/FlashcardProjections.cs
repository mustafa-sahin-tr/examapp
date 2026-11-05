using System;
using System.Linq;
using System.Linq.Expressions;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Flashcards;

internal static class FlashcardProjections
{
    /// <summary>Öğrencinin destesi her zaman kendisinindir (<see cref="FlashcardAccess.VisibleDecks"/>); öğretmen destesi öğrenciye salt okunur.</summary>
    internal static Expression<Func<FlashcardDeck, FlashcardDeckDetailDto>> DetailFor(bool isOwner)
        => d => new FlashcardDeckDetailDto
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
            IsOwner = isOwner || d.OwnerKind == FlashcardDeckOwnerKind.Student,
            Cards = d.Cards
                .OrderBy(c => c.Order).ThenBy(c => c.Id)
                .Select(c => new FlashcardDto { Id = c.Id, Front = c.Front, Back = c.Back, ImageUrl = c.ImageUrl, Order = c.Order })
                .ToList(),
        };
}
