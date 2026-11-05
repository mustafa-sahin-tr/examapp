using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Flashcards;

/// <summary>
/// Issue #699: deste ve kart yazımı (öğretmen ve öğrenci aynı kurallarla, kendi desteleri üzerinde) ve öğretmen destesinin atanması.
/// Atama hedefi kuralları worksheet atamasıyla aynı (<see cref="WorksheetAssignmentService"/>): öğrenci kapsamı
/// <see cref="ISchoolAccessPolicy.ApplyScope"/>, sınıf ataması öğretmenin doğrulanmış okuluna damgalanır, bağımsız öğretmen sınıfa atayamaz.
/// </summary>
public class FlashcardDeckService(
    AppDbContext context,
    IMinIoService storage,
    ISchoolAccessPolicy schoolAccess,
    IAuthApiClient? authApiClient = null) : IFlashcardDeckService
{
    private const int MaxDecksPerOwner = 100;
    private const int MaxCardsPerDeck = 300;
    private const int MaxImagesPerOwner = 300;
    private const int MaxImageBytes = 2 * 1024 * 1024;
    private const string ImageDataPrefix = "data:image/";
    private const string Base64Marker = ";base64,";

    public async Task<IReadOnlyList<FlashcardDeckSummaryDto>> ListOwnedAsync(FlashcardOwner owner, CancellationToken ct = default) =>
        await Owned(owner)
            .AsNoTracking()
            .OrderByDescending(d => d.CreateTime)
            .Take(MaxDecksPerOwner)
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
                IsOwner = true,
            })
            .ToListAsync(ct);

    public Task<FlashcardDeckDetailDto?> GetOwnedAsync(FlashcardOwner owner, int deckId, CancellationToken ct = default) =>
        Owned(owner)
            .AsNoTracking()
            .Where(d => d.Id == deckId)
            .Select(FlashcardProjections.DetailFor(isOwner: true))
            .FirstOrDefaultAsync(ct);

    public async Task<FlashcardDeckDetailDto> CreateAsync(FlashcardOwner owner, SaveFlashcardDeckDto request, CancellationToken ct = default)
    {
        if (await Owned(owner).CountAsync(ct) >= MaxDecksPerOwner)
            throw new FlashcardRuleException("DeckLimitReached");

        var deck = new FlashcardDeck { OwnerUserId = owner.UserId, OwnerKind = owner.Kind };
        await ApplyAsync(deck, owner, request, ct);
        context.FlashcardDecks.Add(deck);
        await context.SaveChangesAsync(ct);
        return (await GetOwnedAsync(owner, deck.Id, ct))!;
    }

    public async Task<FlashcardDeckDetailDto?> UpdateAsync(FlashcardOwner owner, int deckId, SaveFlashcardDeckDto request, CancellationToken ct = default)
    {
        var deck = await Owned(owner).FirstOrDefaultAsync(d => d.Id == deckId, ct);
        if (deck == null)
            return null;

        await ApplyAsync(deck, owner, request, ct);
        await context.SaveChangesAsync(ct);
        return await GetOwnedAsync(owner, deck.Id, ct);
    }

    public async Task<bool> DeleteAsync(FlashcardOwner owner, int deckId, CancellationToken ct = default)
    {
        var deck = await Owned(owner).FirstOrDefaultAsync(d => d.Id == deckId, ct);
        if (deck == null)
            return false;

        context.FlashcardDecks.Remove(deck);
        await context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<FlashcardDto?> AddCardAsync(FlashcardOwner owner, int deckId, SaveFlashcardDto request, CancellationToken ct = default)
    {
        if (!await Owned(owner).AnyAsync(d => d.Id == deckId, ct))
            return null;

        var cards = context.Flashcards.Where(c => c.DeckId == deckId);
        if (await cards.CountAsync(ct) >= MaxCardsPerDeck)
            throw new FlashcardRuleException("CardLimitReached");

        var card = new Flashcard { DeckId = deckId, Order = (await cards.MaxAsync(c => (int?)c.Order, ct) ?? 0) + 1 };
        await ApplyAsync(card, owner, request, ct);
        context.Flashcards.Add(card);
        await context.SaveChangesAsync(ct);
        return ToDto(card);
    }

    public async Task<FlashcardDto?> UpdateCardAsync(FlashcardOwner owner, int cardId, SaveFlashcardDto request, CancellationToken ct = default)
    {
        var card = await OwnedCard(owner, cardId, ct);
        if (card == null)
            return null;

        await ApplyAsync(card, owner, request, ct);
        await context.SaveChangesAsync(ct);
        return ToDto(card);
    }

    public async Task<bool> DeleteCardAsync(FlashcardOwner owner, int cardId, CancellationToken ct = default)
    {
        var card = await OwnedCard(owner, cardId, ct);
        if (card == null)
            return false;

        context.Flashcards.Remove(card);
        await context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<FlashcardDeckAssignmentDto>?> ListAssignmentsAsync(SchoolScope requester, int deckId, CancellationToken ct = default)
    {
        if (!await Owned(TeacherOwner(requester)).AnyAsync(d => d.Id == deckId, ct))
            return null;

        // Kapsamdan çıkan öğrenci (okul değişimi, #194) listede görünmez; sınıf ataması kendi okuluyla kalır.
        var studentsInScope = schoolAccess.ApplyScope(context.Students.AsNoTracking(), requester);
        var rows = await context.FlashcardDeckAssignments
            .AsNoTracking()
            .Where(a => a.DeckId == deckId && (a.StudentId == null || studentsInScope.Any(s => s.Id == a.StudentId)))
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.StudentId, StudentUserId = (int?)a.Student!.UserId, a.GradeId, GradeName = a.Grade != null ? a.Grade.Name : null })
            .ToListAsync(ct);

        var names = await LoadNamesAsync(rows.Where(r => r.StudentUserId.HasValue).Select(r => r.StudentUserId!.Value).ToList());
        return rows.Select(r => new FlashcardDeckAssignmentDto
        {
            Id = r.Id,
            StudentId = r.StudentId,
            StudentName = r.StudentUserId is { } userId ? names.GetValueOrDefault(userId) : null,
            GradeId = r.GradeId,
            GradeName = r.GradeName,
        }).ToList();
    }

    public async Task<FlashcardDeckAssignmentDto?> AssignAsync(SchoolScope requester, int deckId, AssignFlashcardDeckDto request, CancellationToken ct = default)
    {
        if (!await Owned(TeacherOwner(requester)).AnyAsync(d => d.Id == deckId, ct))
            return null;

        if (request.StudentId.HasValue == request.GradeId.HasValue)
            throw new FlashcardRuleException("TargetRequired");

        // Issue #222: çok rollü hesapta okul öğretmen kaydından doğrulanır.
        var teacher = await context.ResolveTeacherRecordAsync(requester.UserId, ct);
        if (!teacher.Exists || teacher.SchoolId != requester.SchoolId)
            throw new FlashcardRuleException("OnlyOwnStudents");

        var assignment = new FlashcardDeckAssignment { DeckId = deckId, SchoolId = requester.SchoolId };
        string? studentName = null;
        string? gradeName = null;
        FlashcardDeckAssignment? existing;
        if (request.StudentId is { } studentId)
        {
            var student = await schoolAccess.ApplyScope(context.Students.AsNoTracking(), requester)
                .Where(s => s.Id == studentId)
                .Select(s => new { s.UserId })
                .FirstOrDefaultAsync(ct);
            if (student == null)
                return null;

            assignment.StudentId = studentId;
            studentName = (await LoadNamesAsync([student.UserId])).GetValueOrDefault(student.UserId);
            existing = await context.FlashcardDeckAssignments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.DeckId == deckId && a.StudentId == studentId, ct);
        }
        else
        {
            // Issue #222: bağımsız öğretmenin sınıf ataması başka okulların öğrencilerine açılırdı.
            if (requester.SchoolId == null)
                throw new FlashcardRuleException("IndependentGradeForbidden");

            var gradeId = request.GradeId!.Value;
            gradeName = await context.Grades.Where(g => g.Id == gradeId).Select(g => g.Name).FirstOrDefaultAsync(ct);
            if (gradeName == null)
                throw new FlashcardRuleException("InvalidGrade");

            assignment.GradeId = gradeId;
            existing = await context.FlashcardDeckAssignments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.DeckId == deckId && a.StudentId == null && a.GradeId == gradeId && a.SchoolId == requester.SchoolId, ct);
        }

        if (existing == null)
        {
            context.FlashcardDeckAssignments.Add(assignment);
            try
            {
                await context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
            {
                context.Entry(assignment).State = EntityState.Detached;
                throw new FlashcardRuleException("AlreadyAssigned");
            }
        }

        return new FlashcardDeckAssignmentDto
        {
            Id = (existing ?? assignment).Id,
            StudentId = request.StudentId,
            StudentName = studentName,
            GradeId = request.GradeId,
            GradeName = gradeName,
        };
    }

    public async Task<bool> UnassignAsync(SchoolScope requester, int deckId, int assignmentId, CancellationToken ct = default)
    {
        if (!await Owned(TeacherOwner(requester)).AnyAsync(d => d.Id == deckId, ct))
            return false;

        var assignment = await context.FlashcardDeckAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId && a.DeckId == deckId, ct);
        if (assignment == null)
            return false;

        context.FlashcardDeckAssignments.Remove(assignment);
        await context.SaveChangesAsync(ct);
        return true;
    }

    private static FlashcardOwner TeacherOwner(SchoolScope requester) => new(requester.UserId, FlashcardDeckOwnerKind.Teacher);

    private IQueryable<FlashcardDeck> Owned(FlashcardOwner owner) =>
        context.FlashcardDecks.Where(d => d.OwnerUserId == owner.UserId && d.OwnerKind == owner.Kind);

    private Task<Flashcard?> OwnedCard(FlashcardOwner owner, int cardId, CancellationToken ct)
    {
        var decks = Owned(owner);
        return context.Flashcards.FirstOrDefaultAsync(c => c.Id == cardId && decks.Any(d => d.Id == c.DeckId), ct);
    }

    private static FlashcardDto ToDto(Flashcard card) =>
        new() { Id = card.Id, Front = card.Front, Back = card.Back, ImageUrl = card.ImageUrl, Order = card.Order };

    private async Task ApplyAsync(FlashcardDeck deck, FlashcardOwner owner, SaveFlashcardDeckDto request, CancellationToken ct)
    {
        var title = request.Title.Trim();
        if (title.Length == 0)
            throw new FlashcardRuleException("TitleRequired");

        if (owner.Kind == FlashcardDeckOwnerKind.Teacher && (request.GradeId == null || request.SubjectId == null))
            throw new FlashcardRuleException("TaxonomyRequired");

        if (request.GradeId is { } gradeId && !await context.Grades.AnyAsync(g => g.Id == gradeId, ct))
            throw new FlashcardRuleException("InvalidGrade");
        if (request.SubjectId is { } subjectId && !await context.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            throw new FlashcardRuleException("InvalidSubject");
        if (request.TopicId is { } topicId
            && !await context.Topics.AnyAsync(t => t.Id == topicId && t.SubjectId == request.SubjectId && t.GradeId == request.GradeId, ct))
            throw new FlashcardRuleException("InvalidTopic");

        deck.Title = title;
        deck.GradeId = request.GradeId;
        deck.SubjectId = request.SubjectId;
        deck.TopicId = request.TopicId;
    }

    private async Task ApplyAsync(Flashcard card, FlashcardOwner owner, SaveFlashcardDto request, CancellationToken ct)
    {
        var imageUrl = request.RemoveImage ? null : card.ImageUrl;
        var hasNewImage = !string.IsNullOrEmpty(request.Image);

        // Temizlikten sonra boş kalan metin (yalnız betik vb.) yok sayılır. Asıl temizlik kayıt anında (AppDbContext).
        var front = RichTextSanitizer.Sanitize(request.Front?.Trim()) ?? string.Empty;
        var back = RichTextSanitizer.Sanitize(request.Back.Trim()) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(back) || (string.IsNullOrWhiteSpace(front) && imageUrl == null && !hasNewImage))
            throw new FlashcardRuleException("CardTextRequired");

        if (hasNewImage)
            imageUrl = await StoreImageAsync(request.Image!, owner, ct);

        card.Front = front;
        card.Back = back;
        card.ImageUrl = imageUrl;
    }

    /// <summary>
    /// Görsel çözülüp yeniden yazılır (üstveri ve görsel olmayan içerik atılır). Anahtar <c>questions/</c> altında: imza
    /// allowlist'i (<see cref="Storage.StorageArea.QuestionImage"/>) ve presign politikası değişmeden çalışır.
    /// </summary>
    private async Task<string> StoreImageAsync(string dataUri, FlashcardOwner owner, CancellationToken ct)
    {
        var marker = dataUri.IndexOf(Base64Marker, StringComparison.Ordinal);
        if (!dataUri.StartsWith(ImageDataPrefix, StringComparison.Ordinal) || marker < 0)
            throw new FlashcardRuleException("InvalidImage");

        var base64 = dataUri.AsSpan(marker + Base64Marker.Length);
        if (base64.Length / 4 * 3 > MaxImageBytes + 3)
            throw new FlashcardRuleException("ImageTooLarge");

        var bytes = new byte[base64.Length / 4 * 3 + 3];
        if (!Convert.TryFromBase64Chars(base64, bytes, out var written))
            throw new FlashcardRuleException("InvalidImage");
        if (written > MaxImageBytes)
            throw new FlashcardRuleException("ImageTooLarge");

        var images = await context.Flashcards.CountAsync(
            c => c.ImageUrl != null && c.Deck.OwnerUserId == owner.UserId && c.Deck.OwnerKind == owner.Kind, ct);
        if (images >= MaxImagesPerOwner)
            throw new FlashcardRuleException("ImageLimitReached");

        byte[] encoded;
        try
        {
            using var bitmap = SkiaImaging.DecodeOriented(bytes[..written], out var format);
            encoded = SkiaImaging.Encode(bitmap, format, ImageHelper.JpegQuality);
        }
        catch (ImageDecodeException)
        {
            throw new FlashcardRuleException("InvalidImage");
        }

        using var stream = new MemoryStream(encoded);
        return await storage.UploadFileAsync(stream, $"questions/flashcards/{Guid.NewGuid()}.jpg");
    }

    private async Task<IReadOnlyDictionary<int, string>> LoadNamesAsync(IReadOnlyCollection<int> userIds)
    {
        if (authApiClient is null || userIds.Count == 0)
            return new Dictionary<int, string>();

        var users = await authApiClient.GetUsersByIdsAsync(userIds.Distinct());
        return users
            .Where(u => !string.IsNullOrWhiteSpace(u.FullName))
            .ToDictionary(u => u.Id, u => u.FullName);
    }
}
