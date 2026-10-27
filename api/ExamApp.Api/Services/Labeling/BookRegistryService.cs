using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Labeling;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExamApp.Api.Services.Labeling;

/// <summary>Kitap kaydı çakışması → 409 (ad kullanımda, kitap kullanımda). <see cref="Exception.Message"/> yerelleştirme anahtarı.</summary>
public sealed class BookRegistryConflictException(string messageKey, string errorCode) : Exception(messageKey)
{
    public string ErrorCode { get; } = errorCode;
}

/// <summary>
/// Paylaşılan kitabın adını / asıl kaydını değiştirme yetkisi yok → 403 (öğretmenlerin testleri, çalışma öğeleri ya da başka
/// kullanıcının soruları ona bağlı; yalnız Admin). <see cref="Exception.Message"/> yerelleştirme anahtarı.
/// </summary>
public sealed class BookRegistryForbiddenException(string messageKey, string errorCode) : Exception(messageKey)
{
    public string ErrorCode { get; } = errorCode;
}

/// <summary>Kitabı düzenleyen: iç kullanıcı id'si ve Admin mi.</summary>
public sealed record BookEditor(int UserId, bool IsAdmin);

/// <summary>
/// Bankaya kayıt için çözülmüş kitap sayfası (issue #577). <see cref="TocStale"/>: sayfanın içindekiler satırı var ama
/// silinmiş bir sınıf/ders/konu/alt konuya işaret ediyor — <see cref="Toc"/> null döner, kayıt içindekileri uygulamaz.
/// </summary>
public sealed record BookPageContext(
    int BookId, string BookName, int FilePage, int? PrintedPage, bool IsAnswerKey, BookTocEntry? Toc, bool TocStale = false);

public interface IBookRegistryService
{
    Task<IReadOnlyList<LabelingBookDto>> ListAsync(CancellationToken ct = default);

    Task<LabelingBookDto?> GetAsync(int id, CancellationToken ct = default);

    Task<LabelingBookDto> CreateAsync(LabelingBookSaveRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// Kümeleri bütün olarak değiştirir; kitap yoksa null. Ad ya da asıl kayıt değişiyorsa ve kitap paylaşımlıysa (kitap testi,
    /// çalışma öğesi ya da başka kullanıcının aynı adlı sorusu) yalnız Admin → aksi halde <see cref="BookRegistryForbiddenException"/>.
    /// </summary>
    Task<LabelingBookDto?> UpdateAsync(int id, LabelingBookSaveRequestDto request, BookEditor editor, CancellationToken ct = default);

    /// <summary>Soft-delete; kitap yoksa false. Kullanımdaysa <see cref="BookRegistryConflictException"/>.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    Task<LabelingBookPageDto?> GetPageAsync(int id, int filePage, CancellationToken ct = default);

    Task<LabelingBookPrintedPageDto?> FindFilePagesAsync(int id, int printedPage, int? near, CancellationToken ct = default);

    /// <summary>Bankaya kayıt: kitap adı, bastırılmış sayfa, cevap anahtarı mı, içindekiler satırı. Kitap yoksa null.</summary>
    Task<BookPageContext?> ResolvePageAsync(int id, int filePage, CancellationToken ct = default);
}

/// <summary>
/// Issue #577: kitap kaydı — ofset aralıkları, cevap anahtarı aralıkları, içindekiler (dosya aralığı → sınıf / ders / konu /
/// alt konu). Kümeler PUT'ta tek transaction'da bütün olarak değiştirilir; eşzamanlı iki PUT kitap satırının kilidiyle
/// sıraya girer (ilk ifade kitap satırını günceller), sonuç ikisinden birinin kümeleridir, karışım değil.
/// </summary>
public sealed class BookRegistryService(AppDbContext db, TimeProvider timeProvider) : IBookRegistryService
{
    public const int MaxNameLength = 200;

    /// <summary>
    /// Ad tekilliği için <c>pg_advisory_xact_lock(int4, int4)</c> sınıf anahtarı (ikinci anahtar 0: tüm kitaplar tek kilit —
    /// oluşturma/yeniden adlandırma seyrek). Unique index yok: mevcut veride aynı adlı kitaplar olabilir.
    /// </summary>
    internal const int NameLockClass = 577_001;

    public async Task<IReadOnlyList<LabelingBookDto>> ListAsync(CancellationToken ct = default)
    {
        var books = await db.Books.AsNoTracking().OrderBy(b => b.Name).ThenBy(b => b.Id)
            .Select(b => new { b.Id, b.Name, b.CanonicalBookId, b.Notes })
            .ToListAsync(ct);
        if (books.Count == 0)
            return [];

        var ids = books.Select(b => b.Id).ToList();
        var sets = await LoadSetsAsync(ids, ct);
        var names = books.ToDictionary(b => b.Id, b => b.Name);
        var canonicalIds = books.Where(b => b.CanonicalBookId is { } c && !names.ContainsKey(c))
            .Select(b => b.CanonicalBookId!.Value).Distinct().ToList();
        if (canonicalIds.Count > 0)
        {
            // Soft-delete edilmiş asıl kayıt (normalde silme engellenir; eski veri için).
            foreach (var c in await db.Books.IgnoreQueryFilters().AsNoTracking().Where(b => canonicalIds.Contains(b.Id))
                         .Select(b => new { b.Id, b.Name }).ToListAsync(ct))
                names[c.Id] = c.Name;
        }

        return books.Select(b => ToDto(b.Id, b.Name, b.CanonicalBookId,
                b.CanonicalBookId is { } c ? names.GetValueOrDefault(c) : null, b.Notes, sets))
            .ToList();
    }

    public async Task<LabelingBookDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var book = await db.Books.AsNoTracking().Where(b => b.Id == id)
            .Select(b => new { b.Id, b.Name, b.CanonicalBookId, CanonicalName = b.CanonicalBook != null ? b.CanonicalBook.Name : null, b.Notes })
            .FirstOrDefaultAsync(ct);
        if (book is null)
            return null;
        var sets = await LoadSetsAsync([id], ct);
        return ToDto(book.Id, book.Name, book.CanonicalBookId, book.CanonicalName, book.Notes, sets);
    }

    public async Task<LabelingBookDto> CreateAsync(LabelingBookSaveRequestDto request, CancellationToken ct = default)
    {
        var input = await ValidateAsync(null, request, ct);

        var strategy = db.Database.CreateExecutionStrategy();
        var id = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await AcquireNameLockAsync(ct);
            await EnsureNameFreeAsync(null, input.Name, ct);
            var book = new Book { Name = input.Name, CanonicalBookId = input.CanonicalBookId, Notes = input.Notes };
            db.Books.Add(book);
            await db.SaveChangesAsync(ct);
            AddSets(book.Id, input);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return book.Id;
        });

        return (await GetAsync(id, ct))!;
    }

    public async Task<LabelingBookDto?> UpdateAsync(
        int id, LabelingBookSaveRequestDto request, BookEditor editor, CancellationToken ct = default)
    {
        if (!await db.Books.AnyAsync(b => b.Id == id, ct))
            return null;
        var input = await ValidateAsync(id, request, ct);

        var strategy = db.Database.CreateExecutionStrategy();
        var updated = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await AcquireNameLockAsync(ct);
            var current = await db.Books.AsNoTracking().Where(b => b.Id == id)
                .Select(b => new { b.Name, b.CanonicalBookId }).FirstOrDefaultAsync(ct);
            if (current is null)
                return false;

            var renamed = !string.Equals(current.Name, input.Name, StringComparison.Ordinal);
            if ((renamed || current.CanonicalBookId != input.CanonicalBookId) && !editor.IsAdmin &&
                await IsSharedAsync(id, current.Name, editor.UserId, ct))
                throw new BookRegistryForbiddenException("labeling.books.renameForbidden", "LabelingBookRenameForbidden");
            if (renamed)
                await EnsureNameFreeAsync(id, input.Name, ct);

            var now = timeProvider.GetUtcNow().UtcDateTime;
            // İlk ifade kitap satırını kilitler: eşzamanlı PUT bu satırda bekler, sonra bizim kümelerimizi görüp siler.
            var rows = await db.Books.Where(b => b.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Name, input.Name)
                .SetProperty(b => b.CanonicalBookId, input.CanonicalBookId)
                .SetProperty(b => b.Notes, input.Notes)
                .SetProperty(b => b.UpdateTime, now), ct);
            if (rows == 0)
                return false;

            await db.BookPageOffsets.Where(o => o.BookId == id).ExecuteDeleteAsync(ct);
            await db.BookAnswerKeyRanges.Where(r => r.BookId == id).ExecuteDeleteAsync(ct);
            await db.BookTocEntries.Where(t => t.BookId == id).ExecuteDeleteAsync(ct);
            AddSets(id, input);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return true;
        });

        return updated ? await GetAsync(id, ct) : null;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var book = await db.Books.AsTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null)
            return false;

        var inUse = await db.QuestionSources.AnyAsync(s => s.BookId == id, ct)
                    || await db.BookTests.AnyAsync(t => t.BookId == id, ct)
                    || await db.StudyItems.AnyAsync(i => i.BookId == id, ct)
                    || await db.Books.AnyAsync(b => b.CanonicalBookId == id, ct)
                    || await db.Questions.AnyAsync(q => q.BookName == book.Name, ct);
        if (inUse)
            throw new BookRegistryConflictException("labeling.books.inUse", "LabelingBookInUse");

        db.Books.Remove(book); // BaseEntity → soft-delete (AppDbContext.ApplyAuditInfo)
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<LabelingBookPageDto?> GetPageAsync(int id, int filePage, CancellationToken ct = default)
    {
        if (filePage is < 1 or > BookPageMap.MaxFilePage)
            throw new LabelingValidationException("labeling.books.invalidPage");
        if (!await db.Books.AnyAsync(b => b.Id == id, ct))
            return null;

        var sets = await LoadSetsAsync([id], ct);
        var map = sets.MapFor(id);
        var toc = map.TocFor(filePage);
        var stale = toc is not null && !await IsTocLiveAsync(toc, ct);
        return new LabelingBookPageDto(filePage, map.ToPrinted(filePage), map.IsAnswerKey(filePage),
            toc is null || stale ? null : sets.TocDto(toc, map), stale);
    }

    public async Task<LabelingBookPrintedPageDto?> FindFilePagesAsync(
        int id, int printedPage, int? near, CancellationToken ct = default)
    {
        if (printedPage is < 1 or > BookPageMap.MaxFilePage || near is < 1 or > BookPageMap.MaxFilePage)
            throw new LabelingValidationException("labeling.books.invalidPage");
        if (!await db.Books.AnyAsync(b => b.Id == id, ct))
            return null;

        var offsets = await db.BookPageOffsets.AsNoTracking().Where(o => o.BookId == id).ToListAsync(ct);
        return new LabelingBookPrintedPageDto(printedPage, new BookPageMap(offsets).ToFilePages(printedPage, near));
    }

    public async Task<BookPageContext?> ResolvePageAsync(int id, int filePage, CancellationToken ct = default)
    {
        if (filePage is < 1 or > BookPageMap.MaxFilePage)
            throw new LabelingValidationException("labeling.books.invalidPage");
        var name = await db.Books.AsNoTracking().Where(b => b.Id == id).Select(b => b.Name).FirstOrDefaultAsync(ct);
        if (name is null)
            return null;

        var offsets = await db.BookPageOffsets.AsNoTracking().Where(o => o.BookId == id).ToListAsync(ct);
        var answerKeys = await db.BookAnswerKeyRanges.AsNoTracking().Where(r => r.BookId == id).ToListAsync(ct);
        var toc = await db.BookTocEntries.AsNoTracking()
            .Where(t => t.BookId == id && t.FileFrom <= filePage && t.FileTo >= filePage).ToListAsync(ct);
        var map = new BookPageMap(offsets, answerKeys, toc);
        var entry = map.TocFor(filePage);
        var stale = entry is not null && !await IsTocLiveAsync(entry, ct);
        return new BookPageContext(id, name, filePage, map.ToPrinted(filePage), map.IsAnswerKey(filePage),
            stale ? null : entry, stale);
    }

    /// <summary>İçindekiler satırının sınıf/ders/konu/alt konusu hâlâ var mı (soft-delete filtresiyle).</summary>
    private async Task<bool> IsTocLiveAsync(BookTocEntry t, CancellationToken ct) =>
        await db.Grades.AnyAsync(g => g.Id == t.GradeId, ct)
        && await db.Subjects.AnyAsync(s => s.Id == t.SubjectId, ct)
        && (t.TopicId is not { } topicId || await db.Topics.AnyAsync(x => x.Id == topicId, ct))
        && (t.SubTopicId is not { } subTopicId || await db.SubTopics.AnyAsync(x => x.Id == subTopicId, ct));

    /// <summary>Öğretmenlerin içeriği kitaba bağlı mı: kitap testi, çalışma öğesi ya da başka kullanıcının aynı adlı sorusu.</summary>
    private async Task<bool> IsSharedAsync(int id, string name, int userId, CancellationToken ct) =>
        await db.BookTests.AnyAsync(t => t.BookId == id, ct)
        || await db.StudyItems.AnyAsync(i => i.BookId == id, ct)
        || await db.Questions.AnyAsync(q => q.BookName == name && (q.CreateUserId ?? 0) != userId, ct);

    /// <summary>
    /// Ad tekilliği (silinmemiş kitaplar). Karşılaştırma bellekte, kültürden bağımsız (<see cref="StringComparison.OrdinalIgnoreCase"/>;
    /// DB <c>lower()</c> collation'a bağlı olurdu, LIKE joker karakterleri de sorun). Kitap sayısı küçük; kilit altında çağrılır.
    /// </summary>
    private async Task EnsureNameFreeAsync(int? selfId, string name, CancellationToken ct)
    {
        var self = selfId ?? 0;
        var names = await db.Books.AsNoTracking().Where(b => b.Id != self).Select(b => b.Name).ToListAsync(ct);
        if (names.Any(n => string.Equals(n?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            throw new BookRegistryConflictException("labeling.books.nameTaken", "LabelingBookNameTaken");
    }

    /// <summary>Oluşturma / yeniden adlandırmayı sıraya sokar (transaction-kapsamlı). Postgres dışında (SQLite testleri) no-op.</summary>
    private async Task AcquireNameLockAsync(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
            return;
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({NameLockClass}, 0)", ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new BookRegistryConflictException("labeling.books.busy", "LabelingBookBusy");
        }
    }

    // ---------- doğrulama ----------

    private sealed record ValidatedInput(
        string Name,
        int? CanonicalBookId,
        string? Notes,
        IReadOnlyList<LabelingBookOffsetDto> Offsets,
        IReadOnlyList<LabelingBookPageRangeDto> AnswerKeys,
        IReadOnlyList<LabelingBookTocInputDto> Toc);

    private async Task<ValidatedInput> ValidateAsync(int? id, LabelingBookSaveRequestDto request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            throw new LabelingValidationException("labeling.books.nameRequired");
        if (name.Length > MaxNameLength)
            throw new LabelingValidationException("labeling.books.nameTooLong");
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is { Length: > Book.MaxNotesLength })
            throw new LabelingValidationException("labeling.books.notesTooLong");

        var offsets = (request.Offsets ?? []).Where(o => o is not null).ToList();
        var answerKeys = (request.AnswerKeyRanges ?? []).Where(r => r is not null).ToList();
        var toc = (request.Toc ?? []).Where(t => t is not null).ToList();
        if (offsets.Count > 200 || answerKeys.Count > 100 || toc.Count > 1000)
            throw new LabelingValidationException("labeling.books.tooManyRanges");

        if (BookPageMap.Validate(
                offsets.Select(o => (o.FileFrom, o.FileTo, o.Offset)).ToList(),
                answerKeys.Select(r => (r.FileFrom, r.FileTo)).ToList(),
                toc.Select(t => (t.FileFrom, t.FileTo)).ToList()) is { } rangeError)
            throw new LabelingValidationException(rangeError);

        // Ad tekilliği transaction içinde, kilit altında denetlenir (EnsureNameFreeAsync).

        if (request.CanonicalBookId is { } canonicalId)
        {
            // Tek seviye: asıl kayıt kendisi değil, var, kendisi başka kitaba bağlı değil; bu kitabın kopyaları varsa bu kitap
            // başka kitaba bağlanamaz (zincir/döngü olmasın).
            if (canonicalId == id)
                throw new LabelingValidationException("labeling.books.canonicalInvalid");
            var canonical = await db.Books.AsNoTracking().Where(b => b.Id == canonicalId)
                .Select(b => new { b.CanonicalBookId }).FirstOrDefaultAsync(ct);
            if (canonical is null || canonical.CanonicalBookId is not null)
                throw new LabelingValidationException("labeling.books.canonicalInvalid");
            if (id is { } self && await db.Books.AnyAsync(b => b.CanonicalBookId == self, ct))
                throw new LabelingValidationException("labeling.books.canonicalInvalid");
        }

        await ValidateTocTaxonomyAsync(toc, ct);

        return new ValidatedInput(name, request.CanonicalBookId, notes, offsets, answerKeys, toc);
    }

    /// <summary>
    /// Sınıf ve ders var; konu o derse ve o sınıfa ait; alt konu o konuya ait; alt konu konu ister. Toplu sorgu (N+1 yok).
    /// </summary>
    private async Task ValidateTocTaxonomyAsync(IReadOnlyList<LabelingBookTocInputDto> toc, CancellationToken ct)
    {
        if (toc.Count == 0)
            return;
        if (toc.Any(t => t.TopicId is <= 0 || t.SubTopicId is <= 0 || (t.SubTopicId is not null && t.TopicId is null)))
            throw new LabelingValidationException("labeling.books.subTopicMismatch");

        var gradeIds = toc.Select(t => t.GradeId).Distinct().ToList();
        var subjectIds = toc.Select(t => t.SubjectId).Distinct().ToList();
        var topicIds = toc.Where(t => t.TopicId is not null).Select(t => t.TopicId!.Value).Distinct().ToList();
        var subTopicIds = toc.Where(t => t.SubTopicId is not null).Select(t => t.SubTopicId!.Value).Distinct().ToList();

        var grades = (await db.Grades.AsNoTracking().Where(g => gradeIds.Contains(g.Id)).Select(g => g.Id).ToListAsync(ct)).ToHashSet();
        if (gradeIds.Any(g => !grades.Contains(g)))
            throw new LabelingValidationException("labeling.books.gradeUnknown");
        var subjects = (await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id)).Select(s => s.Id).ToListAsync(ct)).ToHashSet();
        if (subjectIds.Any(s => !subjects.Contains(s)))
            throw new LabelingValidationException("labeling.books.subjectUnknown");

        var topics = await db.Topics.AsNoTracking().Where(t => topicIds.Contains(t.Id))
            .Select(t => new { t.Id, t.SubjectId, t.GradeId }).ToDictionaryAsync(t => t.Id, ct);
        var subTopics = await db.SubTopics.AsNoTracking().Where(st => subTopicIds.Contains(st.Id))
            .Select(st => new { st.Id, st.TopicId }).ToDictionaryAsync(st => st.Id, ct);

        foreach (var entry in toc)
        {
            if (entry.TopicId is { } topicId &&
                (!topics.TryGetValue(topicId, out var topic) || topic.SubjectId != entry.SubjectId || topic.GradeId != entry.GradeId))
                throw new LabelingValidationException("labeling.books.topicMismatch");
            if (entry.SubTopicId is { } subTopicId &&
                (!subTopics.TryGetValue(subTopicId, out var subTopic) || subTopic.TopicId != entry.TopicId))
                throw new LabelingValidationException("labeling.books.subTopicMismatch");
        }
    }

    private void AddSets(int bookId, ValidatedInput input)
    {
        db.BookPageOffsets.AddRange(input.Offsets.Select(o =>
            new BookPageOffset { BookId = bookId, FileFrom = o.FileFrom, FileTo = o.FileTo, Offset = o.Offset }));
        db.BookAnswerKeyRanges.AddRange(input.AnswerKeys.Select(r =>
            new BookAnswerKeyRange { BookId = bookId, FileFrom = r.FileFrom, FileTo = r.FileTo }));
        db.BookTocEntries.AddRange(input.Toc.Select(t => new BookTocEntry
        {
            BookId = bookId,
            FileFrom = t.FileFrom,
            FileTo = t.FileTo,
            GradeId = t.GradeId,
            SubjectId = t.SubjectId,
            TopicId = t.TopicId,
            SubTopicId = t.SubTopicId,
        }));
    }

    // ---------- okuma ----------

    /// <summary>Kitapların kümeleri + içindekilerde geçen taksonomi adları (kitap sayısından bağımsız 7 sorgu).</summary>
    private sealed class BookSets
    {
        public required ILookup<int, BookPageOffset> Offsets { get; init; }
        public required ILookup<int, BookAnswerKeyRange> AnswerKeys { get; init; }
        public required ILookup<int, BookTocEntry> Toc { get; init; }
        public required IReadOnlyDictionary<int, string> GradeNames { get; init; }
        public required IReadOnlyDictionary<int, string> SubjectNames { get; init; }
        public required IReadOnlyDictionary<int, string> TopicNames { get; init; }
        public required IReadOnlyDictionary<int, string> SubTopicNames { get; init; }

        public BookPageMap MapFor(int bookId) => new(Offsets[bookId], AnswerKeys[bookId], Toc[bookId]);

        public LabelingBookTocDto TocDto(BookTocEntry t, BookPageMap map) => new(
            t.FileFrom, t.FileTo, map.ToPrinted(t.FileFrom), map.ToPrinted(t.FileTo),
            t.GradeId, GradeNames.GetValueOrDefault(t.GradeId),
            t.SubjectId, SubjectNames.GetValueOrDefault(t.SubjectId),
            t.TopicId, t.TopicId is { } topicId ? TopicNames.GetValueOrDefault(topicId) : null,
            t.SubTopicId, t.SubTopicId is { } subTopicId ? SubTopicNames.GetValueOrDefault(subTopicId) : null);
    }

    private async Task<BookSets> LoadSetsAsync(IReadOnlyCollection<int> bookIds, CancellationToken ct)
    {
        var ids = bookIds.ToList();
        var offsets = await db.BookPageOffsets.AsNoTracking().Where(o => ids.Contains(o.BookId)).ToListAsync(ct);
        var answerKeys = await db.BookAnswerKeyRanges.AsNoTracking().Where(r => ids.Contains(r.BookId)).ToListAsync(ct);
        var toc = await db.BookTocEntries.AsNoTracking().Where(t => ids.Contains(t.BookId)).ToListAsync(ct);

        var gradeIds = toc.Select(t => t.GradeId).Distinct().ToList();
        var subjectIds = toc.Select(t => t.SubjectId).Distinct().ToList();
        var topicIds = toc.Where(t => t.TopicId is not null).Select(t => t.TopicId!.Value).Distinct().ToList();
        var subTopicIds = toc.Where(t => t.SubTopicId is not null).Select(t => t.SubTopicId!.Value).Distinct().ToList();

        // Silinmiş taksonomi öğesinin adı da gösterilsin (satır geçersizleşir ama kayıt okunur kalır).
        return new BookSets
        {
            Offsets = offsets.ToLookup(o => o.BookId),
            AnswerKeys = answerKeys.ToLookup(r => r.BookId),
            Toc = toc.ToLookup(t => t.BookId),
            GradeNames = gradeIds.Count == 0 ? new Dictionary<int, string>() : await db.Grades.IgnoreQueryFilters().AsNoTracking()
                .Where(g => gradeIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct),
            SubjectNames = subjectIds.Count == 0 ? new Dictionary<int, string>() : await db.Subjects.IgnoreQueryFilters().AsNoTracking()
                .Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct),
            TopicNames = topicIds.Count == 0 ? new Dictionary<int, string>() : await db.Topics.IgnoreQueryFilters().AsNoTracking()
                .Where(t => topicIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct),
            SubTopicNames = subTopicIds.Count == 0 ? new Dictionary<int, string>() : await db.SubTopics.IgnoreQueryFilters().AsNoTracking()
                .Where(st => subTopicIds.Contains(st.Id)).ToDictionaryAsync(st => st.Id, st => st.Name, ct),
        };
    }

    private static LabelingBookDto ToDto(
        int id, string name, int? canonicalId, string? canonicalName, string? notes, BookSets sets)
    {
        var map = sets.MapFor(id);
        return new LabelingBookDto(
            id, name, canonicalId, canonicalName, notes,
            sets.Offsets[id].OrderBy(o => o.FileFrom)
                .Select(o => new LabelingBookOffsetDto { FileFrom = o.FileFrom, FileTo = o.FileTo, Offset = o.Offset }).ToList(),
            sets.AnswerKeys[id].OrderBy(r => r.FileFrom)
                .Select(r => new LabelingBookPageRangeDto { FileFrom = r.FileFrom, FileTo = r.FileTo }).ToList(),
            sets.Toc[id].OrderBy(t => t.FileFrom).Select(t => sets.TocDto(t, map)).ToList());
    }
}
