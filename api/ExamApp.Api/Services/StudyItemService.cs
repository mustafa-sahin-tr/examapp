using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Storage;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services;

public class StudyItemService : IStudyItemService
{
    private readonly AppDbContext _context;
    private readonly IMinIoService _minioService;
    private readonly ILogger<StudyItemService> _logger;
    private readonly StorageAreaPolicy _storagePolicy;
    private readonly IQuestionOwnershipGuard _questionGuard;

    // Client'a ulaşan hata/başarı metinleri buradan gelir (issue #184). DI her zaman gerçek
    // localizer'ı verir; parametre yalnızca DI'sız (birim test) senaryolar için opsiyonel.
    private readonly IStringLocalizer<Messages> _localizer;

    public StudyItemService(
        AppDbContext context,
        IMinIoService minioService,
        ILogger<StudyItemService>? logger = null,
        IStringLocalizer<Messages>? localizer = null,
        StorageAreaPolicy? storagePolicy = null,
        IQuestionOwnershipGuard? questionGuard = null)
    {
        _context = context;
        _minioService = minioService;
        _logger = logger ?? NullLogger<StudyItemService>.Instance;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
        _storagePolicy = storagePolicy ?? new StorageAreaPolicy((string?)null);
        _questionGuard = questionGuard ?? new QuestionOwnershipGuard(context);
    }

    public async Task<Paged<StudyItemDto>> GetPagedAsync(StudyItemFilterDto filter, UserProfileDto user)
    {
        var query = _context.StudyItems
            .Include(p => p.Images)
            .Include(p => p.Book)
            .Include(p => p.BookTest)
            .Where(p => !p.IsDeleted);

        if (user.Role == UserRole.Student.ToString())
        {
            query = query.Where(p => p.IsPublished);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLowerInvariant();
            query = query.Where(p => p.Title.ToLower().Contains(search) || p.Description.ToLower().Contains(search));
        }

        if (filter.SubjectId.HasValue && filter.SubjectId.Value > 0)
        {
            query = query.Where(p => p.SubjectId == filter.SubjectId.Value);
        }

        if (filter.TopicId.HasValue && filter.TopicId.Value > 0)
        {
            query = query.Where(p => p.TopicId == filter.TopicId.Value);
        }

        if (filter.SubTopicId.HasValue && filter.SubTopicId.Value > 0)
        {
            query = query.Where(p => p.SubTopicId == filter.SubTopicId.Value);
        }

        if (filter.ContentType.HasValue)
        {
            query = query.Where(p => p.ContentType == filter.ContentType.Value);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.CreateTime)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        var itemIds = items.Select(p => p.Id).ToList();
        var blockCounts = await _context.StudyItemBlocks.AsNoTracking()
            .Where(b => itemIds.Contains(b.StudyItemId) && !b.IsDeleted)
            .GroupBy(b => b.StudyItemId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var dtos = items.Select(MapToDto).ToList();
        foreach (var dto in dtos)
        {
            dto.BlockCount = blockCounts.GetValueOrDefault(dto.Id);
        }

        return new Paged<StudyItemDto>
        {
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize,
            TotalCount = totalCount,
            Items = dtos
        };
    }

    public async Task<StudyItemDto?> GetByIdAsync(int id, UserProfileDto user)
    {
        var item = await _context.StudyItems
            .Include(p => p.Images)
            .Include(p => p.Book)
            .Include(p => p.BookTest)
            .Include(p => p.Blocks.Where(b => !b.IsDeleted))
                .ThenInclude(b => b.Question)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

        if (item == null)
        {
            return null;
        }

        var isStudent = user.Role == UserRole.Student.ToString();
        if (isStudent && !item.IsPublished)
        {
            return null;
        }

        var dto = MapToDto(item);
        if (isStudent)
        {
            // Issue #933: silinmiş soruya bakan blok öğrenciye gösterilmez; öğretmen editörde "soru kaldırıldı" görür.
            dto.Blocks.RemoveAll(b => b.Type == StudyItemBlockType.Question && b.QuestionId == null);
        }

        return dto;
    }

    public async Task<StudyItemMutationResult> CreateAsync(CreateStudyItemRequestDto request, List<IFormFile> images, UserProfileDto user)
    {
        if (!TryParseMinioImages(request.MinioImages, out var minioImages))
        {
            return StudyItemMutationResult.Fail(_localizer["study.image.invalidUrl"]);
        }

        var (blocks, blockError) = await ParseBlocksAsync(request.Blocks, user, new HashSet<int>());
        if (blockError != null)
        {
            return StudyItemMutationResult.Fail(blockError);
        }

        // Issue #933: blokları olan sayfada görsel seti zorunlu değil.
        var validationError = ValidateContent(
            request.ContentType, request.Url,
            request.BookId, request.NewBookName, request.BookTestId, request.NewBookTestName,
            request.StartPage, request.EndPage,
            hasAnyImage: HasAnyImage(images, minioImages) || blocks is { Count: > 0 });

        if (validationError != null)
        {
            return StudyItemMutationResult.Fail(validationError);
        }

        var entity = new StudyItem
        {
            Title = request.Title.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            GradeId = request.GradeId,
            SubjectId = request.SubjectId,
            TopicId = request.TopicId,
            SubTopicId = request.SubTopicId,
            IsPublished = request.IsPublished,
            ContentType = request.ContentType,
            CreatedByUserId = user.Id,
            CreatedByName = user.FullName ?? string.Empty,
            CreatedByRole = user.Role ?? string.Empty
        };

        var contentError = await ApplyTypedContentAsync(
            entity, request.ContentType, request.Url, request.Platform,
            request.BookId, request.NewBookName, request.BookTestId, request.NewBookTestName,
            request.StartPage, request.EndPage);

        if (contentError != null)
        {
            return StudyItemMutationResult.Fail(contentError);
        }

        _context.StudyItems.Add(entity);
        await _context.SaveChangesAsync();

        if (request.ContentType == StudyItemContentType.Image)
        {
            await AddImagesAsync(entity.Id, images, minioImages, startSortOrder: 1);
        }

        ApplyBlocks(entity, blocks);
        await _context.SaveChangesAsync();

        var dto = await GetByIdAsync(entity.Id, user) ?? MapToDto(entity);
        return StudyItemMutationResult.Ok(dto);
    }

    public async Task<StudyItemMutationResult> UpdateAsync(int id, UpdateStudyItemRequestDto request, List<IFormFile> newImages, UserProfileDto user)
    {
        var item = await _context.StudyItems
            .Include(p => p.Images)
            .Include(p => p.Blocks.Where(b => !b.IsDeleted))
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

        if (item == null)
        {
            return StudyItemMutationResult.Missing();
        }

        if (item.CreatedByUserId != user.Id && user.Role != "Service")
        {
            return StudyItemMutationResult.Missing();
        }

        if (!TryParseMinioImages(request.MinioImages, out var minioImages))
        {
            return StudyItemMutationResult.Fail(_localizer["study.image.invalidUrl"]);
        }

        // Image tipinde: mevcut (silinmeyen) resimler + yeni gelenler toplamda en az bir olmalı.
        var remainingImageCount = item.Images.Count(i => !i.IsDeleted && !request.RemovedImageIds.Contains(i.Id));
        var hasAnyImage = remainingImageCount > 0 || HasAnyImage(newImages, minioImages);

        var pageQuestionIds = item.Blocks.Where(b => b.QuestionId != null).Select(b => b.QuestionId!.Value).ToHashSet();
        var (blocks, blockError) = await ParseBlocksAsync(request.Blocks, user, pageQuestionIds);
        if (blockError != null)
        {
            return StudyItemMutationResult.Fail(blockError);
        }

        // Issue #933: blokları olan sayfada görsel seti zorunlu değil (blocks null → mevcut bloklar kalır).
        hasAnyImage = hasAnyImage || (blocks?.Count ?? item.Blocks.Count) > 0;

        var validationError = ValidateContent(
            request.ContentType, request.Url,
            request.BookId, request.NewBookName, request.BookTestId, request.NewBookTestName,
            request.StartPage, request.EndPage,
            hasAnyImage);

        if (validationError != null)
        {
            return StudyItemMutationResult.Fail(validationError);
        }

        item.Title = request.Title.Trim();
        item.Description = request.Description?.Trim() ?? string.Empty;
        item.GradeId = request.GradeId;
        item.SubjectId = request.SubjectId;
        item.TopicId = request.TopicId;
        item.SubTopicId = request.SubTopicId;
        item.IsPublished = request.IsPublished;
        item.ContentType = request.ContentType;

        var contentError = await ApplyTypedContentAsync(
            item, request.ContentType, request.Url, request.Platform,
            request.BookId, request.NewBookName, request.BookTestId, request.NewBookTestName,
            request.StartPage, request.EndPage);

        if (contentError != null)
        {
            return StudyItemMutationResult.Fail(contentError);
        }

        if (request.RemovedImageIds.Count > 0)
        {
            var toRemove = item.Images.Where(i => request.RemovedImageIds.Contains(i.Id)).ToList();
            _context.StudyItemImages.RemoveRange(toRemove);
        }

        if (request.ContentType == StudyItemContentType.Image)
        {
            var sortOrder = item.Images.Count == 0 ? 1 : item.Images.Max(i => i.SortOrder) + 1;
            await AddImagesAsync(item.Id, newImages, minioImages, sortOrder);
        }

        ApplyBlocks(item, blocks);
        await _context.SaveChangesAsync();

        var dto = await GetByIdAsync(item.Id, user);
        return dto == null ? StudyItemMutationResult.Missing() : StudyItemMutationResult.Ok(dto);
    }

    public async Task<AttachStudyItemImageBySubTopicsResultDto> AttachImageBySubTopicsAsync(AttachStudyItemImageBySubTopicsRequestDto request, UserProfileDto user)
    {
        var result = new AttachStudyItemImageBySubTopicsResultDto();

        if (string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            result.Success = false;
            result.Message = _localizer["study.image.urlRequired"];
            return result;
        }

        // issue #365 (S2): yalnız study-pages/{books|pages}/... kabul edilir; imzalı URL'nin query'si atılır. Aksi halde
        // saklanan alan üzerinden question-transfer/ gibi bir nesne imzalatılabilirdi (imza kâhini).
        if (!_storagePolicy.TryNormalizeRequiredClientUrl(request.ImageUrl, StorageArea.StudyPage, out var imageUrl))
        {
            result.Success = false;
            result.Message = _localizer["study.image.invalidUrl"];
            return result;
        }

        var subTopicIds = request.SubTopicIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (subTopicIds.Count == 0)
        {
            result.Success = false;
            result.Message = _localizer["study.image.noValidSubTopicIds"];
            return result;
        }

        var subTopics = await _context.SubTopics
            .Include(st => st.Topic)
            .Where(st => subTopicIds.Contains(st.Id) && !st.IsDeleted)
            .ToListAsync();

        var subTopicById = subTopics.ToDictionary(st => st.Id, st => st);
        var missingSubTopicIds = subTopicIds.Where(id => !subTopicById.ContainsKey(id)).ToList();
        if (missingSubTopicIds.Count > 0)
        {
            result.Success = false;
            result.Message = _localizer["study.image.someSubTopicsMissing"];
            result.MissingSubTopicIds = missingSubTopicIds;
            return result;
        }

        var existingItems = await _context.StudyItems
            .Include(p => p.Images)
            .Where(p => !p.IsDeleted && p.SubTopicId.HasValue && subTopicIds.Contains(p.SubTopicId.Value))
            .ToListAsync();

        var itemBySubTopicId = existingItems
            .GroupBy(p => p.SubTopicId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreateTime).First());

        var processedItems = new List<StudyItem>();

        foreach (var subTopicId in subTopicIds)
        {
            if (!itemBySubTopicId.TryGetValue(subTopicId, out var item))
            {
                var subTopic = subTopicById[subTopicId];

                item = new StudyItem
                {
                    Title = subTopic.Name.Trim(),
                    Description = string.Empty,
                    GradeId = subTopic.Topic?.GradeId,
                    SubjectId = subTopic.Topic?.SubjectId,
                    TopicId = subTopic.TopicId,
                    SubTopicId = subTopic.Id,
                    IsPublished = true,
                    ContentType = StudyItemContentType.Image,
                    CreatedByUserId = user.Id,
                    CreatedByName = user.FullName ?? string.Empty,
                    CreatedByRole = user.Role ?? string.Empty
                };

                _context.StudyItems.Add(item);
                itemBySubTopicId[subTopicId] = item;
                result.CreatedStudyItemCount += 1;
            }
            else
            {
                result.UpdatedStudyItemCount += 1;
            }

            var nextSortOrder = item.Images
                .Where(i => !i.IsDeleted)
                .Select(i => i.SortOrder)
                .DefaultIfEmpty(0)
                .Max() + 1;

            item.Images.Add(new StudyItemImage
            {
                ImageUrl = imageUrl,
                SortOrder = nextSortOrder,
                FileName = GetFileNameFromUrl(imageUrl)
            });

            processedItems.Add(item);
        }

        await _context.SaveChangesAsync();

        result.StudyItems = processedItems
            .GroupBy(p => p.Id)
            .Select(g => MapToDto(g.First()))
            .ToList();
        result.Success = true;
        result.Message = _localizer["study.image.attached"];

        return result;
    }

    public async Task<ResponseBaseDto> DeleteAsync(int id, UserProfileDto user)
    {
        var item = await _context.StudyItems
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

        if (item == null)
        {
            return new ResponseBaseDto { Success = false, Message = _localizer["study.notFound"] };
        }

        if (item.CreatedByUserId != user.Id && user.Role != "Service")
        {
            return new ResponseBaseDto { Success = false, Message = _localizer["study.deleteForbidden"] };
        }

        _context.StudyItems.Remove(item);
        await _context.SaveChangesAsync();

        return new ResponseBaseDto { Success = true, ObjectId = item.Id };
    }

    // ---- ContentType doğrulama ----

    /// <summary>
    /// ContentType'a göre alan doğrulaması. Hata mesajı döner; null ise geçerli.
    /// </summary>
    private string? ValidateContent(
        StudyItemContentType contentType, string? url,
        int? bookId, string? newBookName, int? bookTestId, string? newBookTestName,
        int? startPage, int? endPage, bool hasAnyImage)
    {
        switch (contentType)
        {
            case StudyItemContentType.Image:
                return hasAnyImage ? null : _localizer["study.content.imageRequired"].Value;

            case StudyItemContentType.Link:
                if (string.IsNullOrWhiteSpace(url))
                {
                    return _localizer["study.content.urlRequired"];
                }
                if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return _localizer["study.content.invalidUrl"];
                }
                return null;

            case StudyItemContentType.BookPageRange:
                if ((bookId is null || bookId <= 0) && string.IsNullOrWhiteSpace(newBookName))
                {
                    return _localizer["study.content.bookRequired"];
                }
                if ((bookTestId is null || bookTestId <= 0) && string.IsNullOrWhiteSpace(newBookTestName))
                {
                    return _localizer["study.content.bookTestRequired"];
                }
                if (startPage is null || endPage is null)
                {
                    return _localizer["study.content.pageRangeRequired"];
                }
                if (startPage <= 0 || endPage <= 0)
                {
                    return _localizer["study.content.pagesMustBePositive"];
                }
                if (endPage < startPage)
                {
                    return _localizer["study.content.endPageBeforeStartPage"];
                }
                return null;

            default:
                return _localizer["study.content.invalidContentType"];
        }
    }

    /// <summary>
    /// Tipe özel alanları entity'ye yazar; diğer tiplerin alanlarını temizler.
    /// BookPageRange için Book/BookTest find-or-create yapar (WorksheetAuthoringService.CreateOrUpdateAsync deseni).
    /// </summary>
    private async Task<string?> ApplyTypedContentAsync(
        StudyItem entity, StudyItemContentType contentType, string? url, StudyItemLinkPlatform? platform,
        int? bookId, string? newBookName, int? bookTestId, string? newBookTestName,
        int? startPage, int? endPage)
    {
        // Önce hepsini sıfırla; sadece seçili tipin alanları dolu kalır.
        entity.Url = null;
        entity.Platform = null;
        entity.BookId = null;
        entity.BookTestId = null;
        entity.StartPage = null;
        entity.EndPage = null;

        switch (contentType)
        {
            case StudyItemContentType.Link:
                entity.Url = url!.Trim();
                entity.Platform = platform ?? StudyItemLinkPlatform.Other;
                return null;

            case StudyItemContentType.BookPageRange:
            {
                var resolved = await ResolveBookAndTestAsync(bookId, newBookName, bookTestId, newBookTestName);
                if (resolved.Error != null)
                {
                    return resolved.Error;
                }

                entity.BookId = resolved.BookId;
                entity.BookTestId = resolved.BookTestId;
                entity.StartPage = startPage;
                entity.EndPage = endPage;
                return null;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Kitap ve testi ID veya isimle bulur, yoksa oluşturur. Aynı isimli kayıt varsa yeniden oluşturmaz.
    /// </summary>
    private async Task<(int? BookId, int? BookTestId, string? Error)> ResolveBookAndTestAsync(
        int? bookId, string? newBookName, int? bookTestId, string? newBookTestName)
    {
        Book? book;

        if (bookId is > 0)
        {
            book = await _context.Books
                .Include(b => b.BookTests)
                .FirstOrDefaultAsync(b => b.Id == bookId.Value);

            if (book == null)
            {
                return (null, null, "Kitap bulunamadi.");
            }
        }
        else
        {
            var name = newBookName!.Trim();
            book = await _context.Books
                .Include(b => b.BookTests)
                .FirstOrDefaultAsync(b => b.Name == name);

            if (book == null)
            {
                book = new Book { Name = name };
                _context.Books.Add(book);
            }
        }

        BookTest? bookTest;

        if (bookTestId is > 0)
        {
            bookTest = book.BookTests.FirstOrDefault(bt => bt.Id == bookTestId.Value);
            if (bookTest == null)
            {
                return (null, null, "Kitap testi bulunamadi veya secilen kitaba ait degil.");
            }
        }
        else
        {
            var testName = newBookTestName!.Trim();
            bookTest = book.BookTests.FirstOrDefault(bt => bt.Name == testName);
            if (bookTest == null)
            {
                bookTest = new BookTest { Name = testName, Book = book };
                book.BookTests.Add(bookTest);
            }
        }

        // Yeni oluşturulan Book/BookTest için ID'leri almak üzere kaydet.
        if (book.Id == 0 || bookTest.Id == 0)
        {
            await _context.SaveChangesAsync();
        }

        return (book.Id, bookTest.Id, null);
    }

    // ---- Kitap sayfası sorgusu (issue #365 S3) ----

    /// <summary>İstek başına eş zamanlı StatObject üst sınırı (200 sayfalık istek MinIO'yu boğmasın).</summary>
    internal const int BookPageLookupConcurrency = 8;

    /// <summary>Süreç geneli eş zamanlı StatObject üst sınırı (aynı anda gelen çok sayıda sorgu toplamda MinIO'yu boğmasın).</summary>
    internal const int BookPageLookupGlobalConcurrency = 32;

    private static readonly SemaphoreSlim BookPageLookupGlobalGate = new(BookPageLookupGlobalConcurrency);

    public async Task<StudyBookPageLookupResult> LookupBookPagesAsync(IReadOnlyList<StudyBookPageRefDto> pages,
        CancellationToken ct = default)
    {
        if (pages is null || pages.Count == 0)
            return StudyBookPageLookupResult.Fail(_localizer["study.bookPages.pagesRequired"].Value);
        if (pages.Count > StudyBookPageLookupRequestDto.MaxPages)
            return StudyBookPageLookupResult.Fail(_localizer["study.bookPages.tooManyPages", StudyBookPageLookupRequestDto.MaxPages].Value);

        // Önce hepsi doğrulanır: tek bir geçersiz kitap adı bile varsa MinIO'ya hiç gidilmez.
        var unique = new List<(string Book, int Page, string Key)>();
        var seen = new HashSet<(string, int)>();
        foreach (var page in pages)
        {
            if (page is null || page.PageNumber < 1 || page.PageNumber > StudyBookPageRefDto.MaxPageNumber)
                return StudyBookPageLookupResult.Fail(_localizer["study.bookPages.invalidPage", StudyBookPageRefDto.MaxPageNumber].Value);
            if (!TryBuildBookPageKey(page.Book, page.PageNumber, out var key))
                return StudyBookPageLookupResult.Fail(_localizer["study.bookPages.invalidBook"].Value);
            if (seen.Add((page.Book, page.PageNumber)))
                unique.Add((page.Book, page.PageNumber, key));
        }

        var exists = new bool[unique.Count];
        using var gate = new SemaphoreSlim(BookPageLookupConcurrency);
        // İlk altyapı hatasında kalan StatObject'ler iptal edilir: 503 tüm sayfaların zaman aşımını beklemeden döner.
        using var failFast = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = failFast.Token;
        try
        {
            await Task.WhenAll(unique.Select(async (entry, index) =>
            {
                await gate.WaitAsync(token);
                try
                {
                    await BookPageLookupGlobalGate.WaitAsync(token);
                    try
                    {
                        exists[index] = await _minioService.ObjectExistsAsync(MinioBucketPolicies.StudyPagesBucket, entry.Key, token);
                    }
                    finally
                    {
                        BookPageLookupGlobalGate.Release();
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await failFast.CancelAsync();
                    throw;
                }
                finally
                {
                    gate.Release();
                }
            }));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[MinIO] Study book page lookup failed for {Count} page(s)", unique.Count);
            return StudyBookPageLookupResult.Unavailable(_localizer["study.bookPages.storageUnavailable"].Value);
        }

        var results = unique.Select((entry, index) =>
        {
            var url = exists[index] ? MinioObjectUrl.Build(MinioBucketPolicies.StudyPagesBucket, entry.Key) : null;
            return new StudyBookPageLookupResultDto
            {
                Book = entry.Book,
                PageNumber = entry.Page,
                Exists = exists[index],
                MinioUrl = url,
                PreviewUrl = url, // MVC JSON çıktısında imzalanır ([StorageUrl])
            };
        }).ToList();
        return StudyBookPageLookupResult.Ok(results);
    }

    /// <summary>
    /// Kitap klasörü adı → <c>books/{book}/page_{n}.webp</c>. Sıkı allowlist: harf, rakam, boşluk ve <c>- _ . ( ) , ' +</c>;
    /// baş/son boşluk, <c>.</c> ile başlama, <c>..</c>, yol ayırıcı (<c>/</c> <c>\</c>), <c>&amp;</c>/<c>%</c> (gateway imzayı
    /// bozar), kontrol ve birleştirici işaretler reddedilir. Son kontrol olarak anahtar
    /// <see cref="StorageAreaPolicy"/> (StudyPage alanı) ve <see cref="MinioObjectUrl.IsGatewaySafeKey"/>'den geçmelidir.
    /// </summary>
    internal bool TryBuildBookPageKey(string? book, int pageNumber, out string key)
    {
        key = string.Empty;
        if (string.IsNullOrEmpty(book) || book.Length > StudyBookPageRefDto.MaxBookLength ||
            book != book.Trim() || book.StartsWith('.') || book.Contains("..", StringComparison.Ordinal) ||
            pageNumber < 1 || pageNumber > StudyBookPageRefDto.MaxPageNumber)
            return false;

        foreach (var c in book)
        {
            if (!(char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' or '(' or ')' or ',' or '\'' or '+'))
                return false;
        }

        var candidate = $"books/{book}/page_{pageNumber}.webp";
        if (!_storagePolicy.IsAllowed(MinioBucketPolicies.StudyPagesBucket, candidate, [StorageArea.StudyPage]) ||
            !MinioObjectUrl.IsGatewaySafeKey(candidate))
            return false;

        key = candidate;
        return true;
    }

    // ---- Resim yardımcıları ----

    private static bool HasAnyImage(List<IFormFile>? images, IReadOnlyList<MinioImageInfo> minioImages)
    {
        var hasFiles = images != null && images.Any(i => i != null && i.Length > 0);
        return hasFiles || minioImages.Count > 0;
    }

    /// <summary>
    /// issue #365 (S2): <c>MinioImages</c> JSON'u (<c>[{bookName, pageNumber, minioUrl}]</c>) kaydetmeden ÖNCE ayrıştırılır;
    /// her <c>minioUrl</c> <see cref="StorageArea.StudyPage"/> allowlist'ine normalize edilir (query atılır). Geçersiz JSON,
    /// eksik alan ya da allowlist dışı adres → false (400); hiçbir görsel kısmen eklenmez. Boş/null → boş liste.
    /// </summary>
    private bool TryParseMinioImages(string? minioImages, out IReadOnlyList<MinioImageInfo> parsed)
    {
        parsed = Array.Empty<MinioImageInfo>();
        if (string.IsNullOrWhiteSpace(minioImages))
        {
            return true;
        }

        var list = new List<MinioImageInfo>();
        try
        {
            using var jsonDoc = JsonDocument.Parse(minioImages);
            if (jsonDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in jsonDoc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("minioUrl", out var urlElement) || urlElement.ValueKind != JsonValueKind.String ||
                    !_storagePolicy.TryNormalizeRequiredClientUrl(urlElement.GetString(), StorageArea.StudyPage, out var normalized))
                {
                    _logger.LogWarning("Rejected study item MinioImages entry: minioUrl missing or outside the study-pages allowlist");
                    return false;
                }

                var bookName = item.TryGetProperty("bookName", out var b) && b.ValueKind == JsonValueKind.String
                    ? b.GetString() ?? string.Empty
                    : string.Empty;
                var pageNumber = item.TryGetProperty("pageNumber", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var pn)
                    ? pn
                    : 0;

                list.Add(new MinioImageInfo { BookName = bookName, PageNumber = pageNumber, MinioUrl = normalized });
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid MinioImages JSON on study item");
            return false;
        }

        parsed = list;
        return true;
    }

    // ---- Bloklar (issue #933) ----

    /// <summary>
    /// <c>Blocks</c> JSON'unu ayrıştırır ve doğrular. null/boş → (null, null): bloklara dokunulmaz. Hata varsa hiçbir blok
    /// yazılmaz. Görsel adresi <see cref="StorageArea.StudyPage"/> allowlist'ine normalize edilir. Soru silinmemiş olmalı ve
    /// sayfaya yeni eklenen soru öğretmene görünür olmalı (soru bankası <c>GET {id}</c> kuralı,
    /// <see cref="IQuestionOwnershipGuard.CanViewQuestionAsync"/>); sayfada zaten olan soru yeniden denetlenmez.
    /// </summary>
    private async Task<(List<StudyItemBlockRequestDto>? Blocks, string? Error)> ParseBlocksAsync(
        string? json, UserProfileDto user, IReadOnlySet<int> existingQuestionIds)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, null);
        }

        List<StudyItemBlockRequestDto>? blocks;
        try
        {
            blocks = JsonSerializer.Deserialize<List<StudyItemBlockRequestDto>>(json, BlockJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid Blocks JSON on study item");
            return (null, _localizer["study.block.invalidJson"]);
        }

        if (blocks == null || blocks.Any(b => b == null))
        {
            return (null, _localizer["study.block.invalidJson"]);
        }

        if (blocks.Count > StudyItemBlock.MaxBlocksPerItem)
        {
            return (null, _localizer["study.block.tooMany", StudyItemBlock.MaxBlocksPerItem]);
        }

        foreach (var block in blocks)
        {
            block.Text = string.IsNullOrWhiteSpace(block.Text) ? null : block.Text.Trim();
            block.BackText = string.IsNullOrWhiteSpace(block.BackText) ? null : block.BackText.Trim();
            if (block.Text?.Length > StudyItemBlock.MaxTextLength || block.BackText?.Length > StudyItemBlock.MaxTextLength)
            {
                return (null, _localizer["study.block.textTooLong", StudyItemBlock.MaxTextLength]);
            }

            switch (block.Type)
            {
                case StudyItemBlockType.Text:
                    if (block.Text == null)
                    {
                        return (null, _localizer["study.block.textRequired"]);
                    }
                    break;

                case StudyItemBlockType.Image:
                    if (!_storagePolicy.TryNormalizeRequiredClientUrl(block.ImageUrl, StorageArea.StudyPage, out var normalized))
                    {
                        return (null, _localizer["study.image.invalidUrl"]);
                    }
                    block.ImageUrl = normalized;
                    break;

                case StudyItemBlockType.Link:
                    if (!Uri.TryCreate(block.Url?.Trim(), UriKind.Absolute, out var uri)
                        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    {
                        return (null, _localizer["study.content.invalidUrl"]);
                    }
                    block.Url = block.Url!.Trim();
                    break;

                case StudyItemBlockType.Question:
                    if (block.QuestionId is null or <= 0)
                    {
                        return (null, _localizer["study.block.questionNotFound"]);
                    }
                    break;

                case StudyItemBlockType.Flashcard:
                    if (block.Text == null || block.BackText == null)
                    {
                        return (null, _localizer["study.block.flashcardRequired"]);
                    }
                    break;

                default:
                    return (null, _localizer["study.block.invalidType"]);
            }
        }

        var questionIds = blocks.Where(b => b.Type == StudyItemBlockType.Question).Select(b => b.QuestionId!.Value).Distinct().ToList();
        if (questionIds.Count > 0)
        {
            var found = await _context.Questions.AsNoTracking()
                .CountAsync(q => questionIds.Contains(q.Id) && !q.IsDeleted);
            if (found != questionIds.Count)
            {
                return (null, _localizer["study.block.questionNotFound"]);
            }

            var isService = user.Role == "Service";
            foreach (var questionId in questionIds.Where(id => !existingQuestionIds.Contains(id)))
            {
                if (!isService && await _questionGuard.CanViewQuestionAsync(questionId, user.Id, isAdmin: false) != QuestionAccessResult.Allowed)
                {
                    return (null, _localizer["study.block.questionNotFound"]);
                }
            }
        }

        return (blocks, null);
    }

    private static readonly JsonSerializerOptions BlockJsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Doğrulanmış blok listesini sayfaya yazar: id'si eşleşen blok yerinde güncellenir, listede olmayan silinir
    /// (soft delete), yeni blok eklenir. Sıra listedeki sıradır. <paramref name="blocks"/> null → dokunulmaz.
    /// </summary>
    private void ApplyBlocks(StudyItem item, List<StudyItemBlockRequestDto>? blocks)
    {
        if (blocks == null)
        {
            return;
        }

        var existing = item.Blocks.Where(b => !b.IsDeleted).ToDictionary(b => b.Id);
        var kept = new HashSet<int>();
        var sortOrder = 1;

        foreach (var request in blocks)
        {
            if (request.Id is not { } id || !existing.TryGetValue(id, out var block) || !kept.Add(id))
            {
                block = new StudyItemBlock();
                item.Blocks.Add(block);
            }

            block.SortOrder = sortOrder++;
            block.Type = request.Type;
            block.Text = request.Type is StudyItemBlockType.Question ? null : request.Text;
            block.BackText = request.Type == StudyItemBlockType.Flashcard ? request.BackText : null;
            block.ImageUrl = request.Type == StudyItemBlockType.Image ? request.ImageUrl : null;
            block.Url = request.Type == StudyItemBlockType.Link ? request.Url : null;
            block.Platform = request.Type == StudyItemBlockType.Link ? request.Platform ?? StudyItemLinkPlatform.Other : null;
            block.QuestionId = request.Type == StudyItemBlockType.Question ? request.QuestionId : null;
        }

        foreach (var removed in existing.Values.Where(b => !kept.Contains(b.Id)))
        {
            _context.StudyItemBlocks.Remove(removed);
        }
    }

    /// <summary>Issue #933: görsel bloğu dosyasını yükler; dönen değer kayıtta bloğun <c>imageUrl</c>'i olur.</summary>
    public async Task<(StudyItemBlockImageUploadDto? Image, string? Error)> UploadBlockImageAsync(IFormFile? image, UserProfileDto user)
    {
        if (image == null || image.Length == 0 || image.Length > MaxBlockImageBytes
            || !AllowedBlockImageTypes.Contains(image.ContentType ?? string.Empty))
        {
            return (null, _localizer["study.block.invalidImageFile"]);
        }

        var extension = Path.GetExtension(image.FileName) ?? string.Empty;
        if (extension.Length > 8 || extension.Any(c => !char.IsLetterOrDigit(c) && c != '.'))
        {
            extension = string.Empty;
        }

        var objectName = $"pages/blocks/{user.Id}/{Guid.NewGuid()}{extension.ToLowerInvariant()}";
        using var stream = image.OpenReadStream();
        var url = await _minioService.UploadFileAsync(stream, objectName, "study-pages", image.ContentType);
        return (new StudyItemBlockImageUploadDto { ImageUrl = url }, null);
    }

    private const long MaxBlockImageBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedBlockImageTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp", "image/gif" };

    private static StudyItemBlockDto MapBlock(StudyItemBlock block)
    {
        var question = block.Question is { IsDeleted: false } q ? q : null;
        return new StudyItemBlockDto
        {
            Id = block.Id,
            SortOrder = block.SortOrder,
            Type = block.Type,
            Text = block.Text,
            BackText = block.BackText,
            ImageUrl = block.ImageUrl,
            Url = block.Url,
            Platform = block.Platform,
            QuestionId = block.Type == StudyItemBlockType.Question ? question?.Id : null,
            QuestionText = question?.Text,
            QuestionImageUrl = question?.ImageUrl,
        };
    }

    private async Task AddImagesAsync(int studyItemId, List<IFormFile>? images, IReadOnlyList<MinioImageInfo> minioImages, int startSortOrder)
    {
        var sortOrder = startSortOrder;

        foreach (var image in images ?? new List<IFormFile>())
        {
            if (image == null || image.Length == 0)
            {
                continue;
            }

            var extension = Path.GetExtension(image.FileName) ?? string.Empty;
            var objectName = $"pages/{studyItemId}/{Guid.NewGuid()}{extension}";

            using var stream = image.OpenReadStream();
            var url = await _minioService.UploadFileAsync(stream, objectName, "study-pages", image.ContentType);

            _context.StudyItemImages.Add(new StudyItemImage
            {
                StudyItemId = studyItemId,
                ImageUrl = url,
                SortOrder = sortOrder,
                FileName = image.FileName
            });

            sortOrder += 1;
        }

        foreach (var image in minioImages)
        {
            _context.StudyItemImages.Add(new StudyItemImage
            {
                StudyItemId = studyItemId,
                ImageUrl = image.MinioUrl,
                SortOrder = sortOrder,
                FileName = $"{image.BookName}/page_{image.PageNumber}.webp"
            });

            sortOrder += 1;
        }
    }

    private static StudyItemDto MapToDto(StudyItem item)
    {
        var images = item.Images
            .Where(i => !i.IsDeleted)
            .OrderBy(i => i.SortOrder)
            .Select(i => new StudyItemImageDto
            {
                Id = i.Id,
                ImageUrl = i.ImageUrl,
                SortOrder = i.SortOrder,
                FileName = i.FileName
            })
            .ToList();

        var dto = new StudyItemDto
        {
            Id = item.Id,
            Title = item.Title,
            Description = item.Description,
            GradeId = item.GradeId,
            SubjectId = item.SubjectId,
            TopicId = item.TopicId,
            SubTopicId = item.SubTopicId,
            IsPublished = item.IsPublished,
            CreatedByUserId = item.CreatedByUserId,
            CreatedByName = item.CreatedByName,
            CreatedByRole = item.CreatedByRole,
            CreateTime = item.CreateTime,
            ContentType = item.ContentType,
            Url = item.Url,
            Platform = item.Platform,
            BookId = item.BookId,
            BookName = item.Book?.Name,
            BookTestId = item.BookTestId,
            BookTestName = item.BookTest?.Name,
            StartPage = item.StartPage,
            EndPage = item.EndPage,
            ImageCount = images.Count,
            CoverImageUrl = images.FirstOrDefault()?.ImageUrl,
            Images = images,
            Blocks = item.Blocks
                .Where(b => !b.IsDeleted)
                .OrderBy(b => b.SortOrder)
                .Select(MapBlock)
                .ToList()
        };
        dto.BlockCount = dto.Blocks.Count;
        return dto;
    }

    private static string GetFileNameFromUrl(string imageUrl)
    {
        if (MinioObjectUrl.TryParse(imageUrl, out _, out var key))
        {
            return Path.GetFileName(key);
        }

        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
        {
            var fileName = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        return "external-image";
    }
}

public class MinioImageInfo
{
    public string BookName { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public string MinioUrl { get; set; } = string.Empty;
}
