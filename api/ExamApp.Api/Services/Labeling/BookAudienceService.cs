using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Issue #1259: kitabın erişim listesi (okullar + öğretmenler). <see cref="WorksheetTeacherSharing.BookAudience"/> paylaşımlı
/// testler bu listeye görünür ve atanabilir. Liste yalnız Admin tarafından değiştirilir; kitap aktarımı onayında seçilenler
/// mevcut listeye eklenir (<see cref="AddAsync"/>), kitap ekranında bütün olarak değiştirilir (<see cref="ReplaceAsync"/>).
/// </summary>
public interface IBookAudienceService
{
    /// <summary>Kitabın listesi; kitap yoksa null.</summary>
    Task<BookAudienceDto?> GetAsync(int bookId, CancellationToken ct = default);

    /// <summary>Listeyi bütün olarak değiştirir; kitap yoksa null. Geçersiz okul / öğretmen → <see cref="LabelingValidationException"/>.</summary>
    Task<BookAudienceDto?> ReplaceAsync(int bookId, BookAudienceSaveDto request, CancellationToken ct = default);

    /// <summary>Doğrulama anahtarı (geçerliyse null): okullar ve öğretmenler var, en az biri seçili, sınır içinde.</summary>
    Task<string?> ValidateAsync(BookAudienceSaveDto request, CancellationToken ct = default);

    /// <summary>Listede olmayanları ekler (kaydetmez; çağıranın SaveChanges'ine girer).</summary>
    Task AddAsync(int bookId, BookAudienceSaveDto request, CancellationToken ct = default);
}

public sealed class BookAudienceService(AppDbContext db, IAuthApiClient? authApi = null) : IBookAudienceService
{
    /// <summary>Listedeki okul ve öğretmen sayısı üst sınırı (her biri için).</summary>
    public const int MaxMembers = 500;

    public async Task<BookAudienceDto?> GetAsync(int bookId, CancellationToken ct = default)
    {
        if (!await db.Books.AnyAsync(b => b.Id == bookId, ct))
            return null;

        var schools = await db.BookAudienceMembers.AsNoTracking()
            .Where(m => m.BookId == bookId && m.SchoolId != null)
            .Join(db.Schools, m => m.SchoolId, s => s.Id, (m, s) => s)
            .OrderBy(s => s.Name)
            .Select(s => new BookAudienceSchoolDto(s.Id, s.Name))
            .ToListAsync(ct);
        var teacherUserIds = await db.BookAudienceMembers.AsNoTracking()
            .Where(m => m.BookId == bookId && m.TeacherUserId != null)
            .Select(m => m.TeacherUserId!.Value)
            .ToListAsync(ct);
        var teacherRows = await db.Teachers.AsNoTracking()
            .Where(t => teacherUserIds.Contains(t.UserId))
            .Select(t => new { t.Id, t.UserId, SchoolName = t.School != null ? t.School.Name : t.SchoolName })
            .ToListAsync(ct);
        var names = await ResolveNamesAsync(teacherRows.Select(t => t.UserId).Distinct().ToList(), ct);

        var teachers = teacherRows
            .Select(t => new BookAudienceTeacherDto(t.Id, names.GetValueOrDefault(t.UserId), t.SchoolName))
            .OrderBy(t => t.Name ?? string.Empty, StringComparer.CurrentCulture)
            .ToList();
        return new BookAudienceDto(schools, teachers);
    }

    public async Task<BookAudienceDto?> ReplaceAsync(int bookId, BookAudienceSaveDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await db.Books.AnyAsync(b => b.Id == bookId, ct))
            return null;
        // Boş liste geçerli: kitabın BookAudience testleri yalnız sahibine kalır.
        var normalized = Normalize(request);
        if (await ValidateMembersAsync(normalized, ct) is { } invalid)
            throw new LabelingValidationException(invalid);

        var teacherUserIds = await TeacherUserIdsAsync(normalized.TeacherIds, ct);
        await db.BookAudienceMembers.Where(m => m.BookId == bookId).ExecuteDeleteAsync(ct);
        db.BookAudienceMembers.AddRange(Members(bookId, normalized.SchoolIds, teacherUserIds));
        await db.SaveChangesAsync(ct);
        return await GetAsync(bookId, ct);
    }

    public async Task<string?> ValidateAsync(BookAudienceSaveDto request, CancellationToken ct = default)
    {
        var normalized = Normalize(request);
        if (normalized.SchoolIds.Count == 0 && normalized.TeacherIds.Count == 0)
            return "labeling.books.audienceEmpty";
        return await ValidateMembersAsync(normalized, ct);
    }

    public async Task AddAsync(int bookId, BookAudienceSaveDto request, CancellationToken ct = default)
    {
        var (schoolIds, teacherRecordIds) = Normalize(request);
        var teacherIds = await TeacherUserIdsAsync(teacherRecordIds, ct);
        var existing = await db.BookAudienceMembers.AsNoTracking().Where(m => m.BookId == bookId).ToListAsync(ct);
        var newSchools = schoolIds.Where(id => existing.All(m => m.SchoolId != id)).ToList();
        var newTeachers = teacherIds.Where(id => existing.All(m => m.TeacherUserId != id)).ToList();
        db.BookAudienceMembers.AddRange(Members(bookId, newSchools, newTeachers));
    }

    private async Task<string?> ValidateMembersAsync((List<int> SchoolIds, List<int> TeacherIds) request, CancellationToken ct)
    {
        if (request.SchoolIds.Count > MaxMembers || request.TeacherIds.Count > MaxMembers)
            return "labeling.books.audienceTooMany";
        var schools = await db.Schools.CountAsync(s => request.SchoolIds.Contains(s.Id), ct);
        var teachers = await db.Teachers.CountAsync(t => request.TeacherIds.Contains(t.Id), ct);
        return schools != request.SchoolIds.Count || teachers != request.TeacherIds.Count ? "labeling.books.audienceUnknownMember" : null;
    }

    /// <summary>Öğretmen kaydı kimlikleri → kullanıcı kimlikleri (liste kullanıcı kimliğiyle tutulur; testin sahiplik ekseniyle aynı).</summary>
    private async Task<List<int>> TeacherUserIdsAsync(List<int> teacherIds, CancellationToken ct) =>
        teacherIds.Count == 0
            ? []
            : await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id)).Select(t => t.UserId).Distinct().ToListAsync(ct);

    private static (List<int> SchoolIds, List<int> TeacherIds) Normalize(BookAudienceSaveDto request) =>
        ((request.SchoolIds ?? []).Distinct().ToList(), (request.TeacherIds ?? []).Distinct().ToList());

    private static IEnumerable<BookAudienceMember> Members(int bookId, IEnumerable<int> schoolIds, IEnumerable<int> teacherIds) =>
        schoolIds.Select(id => new BookAudienceMember { BookId = bookId, SchoolId = id })
            .Concat(teacherIds.Select(id => new BookAudienceMember { BookId = bookId, TeacherUserId = id }));

    /// <summary>Öğretmen adları auth-api'den; ulaşılamazsa adsız döner (liste yine düzenlenebilir).</summary>
    private async Task<Dictionary<int, string>> ResolveNamesAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0 || authApi is null)
            return new();
        try
        {
            return (await authApi.GetUsersByIdsAsync(ids, ct))
                .Where(u => !string.IsNullOrWhiteSpace(u.FullName))
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First().FullName);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new();
        }
    }
}
