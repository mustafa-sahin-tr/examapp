using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;

namespace ExamApp.Api.Services;

public class ExamService : IExamService
{
    private readonly AppDbContext _context;

    private readonly ImageHelper _imageHelper;
    private readonly IMinIoService _minioService;
    private readonly IAuthApiClient _authApiClient;
    private readonly ILogger<ExamService>? _logger;
    public ExamService(AppDbContext context, ImageHelper imageHelper, IMinIoService minioService,
        IAuthApiClient authApiClient, ILogger<ExamService>? logger = null)
    {
        _imageHelper = imageHelper;
        _context = context;
        _minioService = minioService;
        _authApiClient = authApiClient;
        _logger = logger;
    }

    /// <summary>
    /// Admin isteklerinde worksheet listesindeki CreateUserId'leri isimlere çözer.
    /// StudentService.GetStudentLookupsAsync ile aynı desen.
    /// </summary>
    private async Task<Dictionary<int, string>> ResolveCreatorNamesAsync(IEnumerable<int?> creatorIds)
    {
        var ids = creatorIds.Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(ids);
            return users
                .Where(u => !string.IsNullOrWhiteSpace(u.FullName))
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First().FullName);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Auth API erişilemez/yavaş — isim çözümü best-effort, istek düşmesin ama sessiz de kalmasın.
            _logger?.LogWarning(ex, "Worksheet oluşturan kullanıcı adları auth-api'den çözülemedi ({Count} id).", ids.Count);
            return new Dictionary<int, string>();
        }
    }

    /// <summary>
    /// WorksheetDto'nun görünürlük + sahiplik alanlarını tek yerden doldurur; tüm liste/detay
    /// projeksiyonları bunu çağırır ki endpoint'ler arası tutarlı olsun (issue #9).
    /// </summary>
    /// <param name="requesterUserId">İstek sahibinin user id'si; öğrenci akışında null.</param>
    /// <param name="ownerName">Çözülmüş sahip/creator adı. Liste akışlarında normalde null geçilir.</param>
    /// <param name="populateOwnerName">
    /// True ise (admin/sahip dışında da) <paramref name="ownerName"/> DTO'ya yazılır. Issue #11 ile
    /// eklendi: bir öğretmen başkasının PublicView/PublicAssignable worksheet'ini görüntülediğinde
    /// (CanView true ama IsOwner/isAdmin false) sahibinin adını yine de görmesi gerekiyor.
    /// </param>
    private static void ApplyOwnershipAndVisibility(
        WorksheetDto dto, Worksheet w, int? requesterUserId, bool isAdmin, string? ownerName = null,
        bool populateOwnerName = false)
    {
        dto.TeacherSharing = w.TeacherSharing;
        dto.StudentVisibility = w.StudentVisibility;

        var isOwner = w.CreateUserId.HasValue && w.CreateUserId.Value > 0
            && requesterUserId.HasValue && w.CreateUserId.Value == requesterUserId.Value;
        dto.IsOwner = isOwner;
        dto.OwnerName = (isAdmin || isOwner || populateOwnerName) ? ownerName : null;
        // TODO(#12/#13): CanAssign public/restricted dallarını da dikkate alacak — şimdilik sahibi/admin dışında hep false.
        dto.CanAssign = WorksheetAccess.CanModify(w.CreateUserId, requesterUserId ?? 0, isAdmin);
    }

    private static IQueryable<Worksheet> ApplyCommonFilters(IQueryable<Worksheet> query, ExamFilterDto dto)
    {
        if (dto.minQuestionCount.HasValue)
            query = query.Where(t => t.WorksheetQuestions.Count() >= dto.minQuestionCount.Value);
        if (dto.maxQuestionCount.HasValue)
            query = query.Where(t => t.WorksheetQuestions.Count() <= dto.maxQuestionCount.Value);
        if (dto.minDurationSeconds.HasValue)
            query = query.Where(t => t.MaxDurationSeconds >= dto.minDurationSeconds.Value);
        if (dto.maxDurationSeconds.HasValue)
            query = query.Where(t => t.MaxDurationSeconds <= dto.maxDurationSeconds.Value);
        if (dto.isPracticeTest.HasValue)
            query = query.Where(t => t.IsPracticeTest == dto.isPracticeTest.Value);
        if (dto.bookIds != null && dto.bookIds.Any())
            query = query.Where(t => t.BookTest != null && dto.bookIds.Contains(t.BookTest.BookId));
        return query;
    }

    private static IQueryable<Worksheet> ApplySort(
        IQueryable<Worksheet> query, AppDbContext context,
        WorksheetSortBy sortBy, bool descending, int? studentId)
    {
        IOrderedQueryable<Worksheet> ordered = sortBy switch
        {
            WorksheetSortBy.Popular => descending
                ? query.OrderByDescending(t => context.TestInstances.Count(ti => ti.WorksheetId == t.Id))
                : query.OrderBy(t => context.TestInstances.Count(ti => ti.WorksheetId == t.Id)),
            WorksheetSortBy.Duration => descending
                ? query.OrderByDescending(t => t.MaxDurationSeconds)
                : query.OrderBy(t => t.MaxDurationSeconds),
            WorksheetSortBy.QuestionCount => descending
                ? query.OrderByDescending(t => t.WorksheetQuestions.Count())
                : query.OrderBy(t => t.WorksheetQuestions.Count()),
            WorksheetSortBy.Alphabetical => descending
                ? query.OrderByDescending(t => t.Name)
                : query.OrderBy(t => t.Name),
            WorksheetSortBy.Recent when studentId.HasValue => descending
                ? query.OrderByDescending(t => context.TestInstances
                    .Where(ti => ti.StudentId == studentId.Value && ti.WorksheetId == t.Id)
                    .Max(ti => (DateTime?)ti.StartTime))
                : query.OrderBy(t => context.TestInstances
                    .Where(ti => ti.StudentId == studentId.Value && ti.WorksheetId == t.Id)
                    .Max(ti => (DateTime?)ti.StartTime)),
            WorksheetSortBy.Recent => descending
                ? query.OrderByDescending(t => t.UpdateTime ?? t.CreateTime)
                : query.OrderBy(t => t.UpdateTime ?? t.CreateTime),
            _ => descending
                ? query.OrderByDescending(t => t.CreateTime)
                : query.OrderBy(t => t.CreateTime),
        };

        return ordered.ThenBy(t => t.Id); // stable tiebreaker
    }

    public async Task<Paged<WorksheetDto>> GetWorksheetsForTeacherAsync(ExamFilterDto dto, UserProfileDto userProfile, bool isAdmin)
    {
        var query = _context.Worksheets.AsQueryable();

        if (dto.id > 0)
        {
            query = query.Where(t => t.Id == dto.id);
        }
        else
        {
            if (dto.gradeIds != null && dto.gradeIds.Any())
                query = query.Where(t => dto.gradeIds.Contains(t.GradeId));

            if (dto.subjectIds != null && dto.subjectIds.Any())
                query = query.Where(t => t.SubjectId.HasValue && dto.subjectIds.Contains(t.SubjectId.Value));

            if (dto.bookTestId > 0)
                query = query.Where(t => t.BookTestId == dto.bookTestId);

            if (!string.IsNullOrEmpty(dto.search))
            {
                var normalizedSearch = dto.search.ToLower(new CultureInfo("tr-TR"));
                query = query.Where(t =>
                    EF.Functions.Like(t.Name.ToLower(), $"%{normalizedSearch}%") ||
                    (t.Subtitle != null && EF.Functions.Like(t.Subtitle.ToLower(), $"%{normalizedSearch}%")) ||
                    EF.Functions.Like(t.Description.ToLower(), $"%{normalizedSearch}%"));
            }
        }

        // Öğretmen yalnızca kendi worksheet'lerini görür; admin hepsini.
        // Legacy (CreateUserId null/0) kayıtlar yalnızca admin'e görünür.
        // dto.id > 0 dalı da buna tabidir — başkasının id'siyle çekilemez.
        // includeShared=true ise (issue #11) sahiplik kapısına ek olarak başka öğretmenlerin
        // PublicView/PublicAssignable worksheet'leri de dahil edilir.
        if (!isAdmin)
        {
            if (dto.includeShared)
            {
                query = query.Where(t =>
                    (t.CreateUserId != null && t.CreateUserId > 0 && t.CreateUserId == userProfile.Id)
                    || (t.CreateUserId != null && t.CreateUserId > 0
                        && (t.TeacherSharing == WorksheetTeacherSharing.PublicView
                            || t.TeacherSharing == WorksheetTeacherSharing.PublicAssignable)));
            }
            else
            {
                query = query.Where(t => t.CreateUserId != null && t.CreateUserId > 0 && t.CreateUserId == userProfile.Id);
            }
        }

        query = ApplyCommonFilters(query, dto);

        var totalCount = await query.CountAsync(); // Toplam kayıt sayısı
        var tests = await ApplySort(query, _context, dto.SortByParsed, dto.SortDescending, studentId: null)
            .Include(t => t.BookTest)
                .ThenInclude(bt => bt.Book)
            .Include(t => t.WorksheetQuestions)
                .ThenInclude(tq => tq.Question)
            .Skip((dto.pageNumber - 1) * dto.pageSize) // Sayfalama için
            .Take(dto.pageSize)
            .ToListAsync();

        // İsim çözümü: admin için her zaman; öğretmen için yalnız includeShared=true iken (kendi
        // kayıtları zaten adını bildiği için normalde auth-api'ye gitmeye gerek yok).
        var creatorNames = (isAdmin || dto.includeShared)
            ? await ResolveCreatorNamesAsync(tests.Select(t => t.CreateUserId))
            : new Dictionary<int, string>();

        var worksheetDtos = tests.Select(t =>
        {
            // Liste akışı: OwnerName admin için, ya da includeShared ile görünür kılınan paylaşılan
            // satırlar için çözülür (issue #11 — kimin worksheet'i olduğu bu özelliğin amacı).
            var isOwnRow = t.CreateUserId.HasValue && t.CreateUserId.Value > 0 && t.CreateUserId.Value == userProfile.Id;
            var isSharedRow = dto.includeShared && !isOwnRow;
            string? ownerName = t.CreateUserId.HasValue
                && creatorNames.TryGetValue(t.CreateUserId.Value, out var on) ? on : null;
            var d = new WorksheetDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                GradeId = t.GradeId,
                SubjectId = t.SubjectId,
                TopicId = t.TopicId,
                SubTopicId = t.SubTopicId,
                MaxDurationSeconds = t.MaxDurationSeconds,
                IsPracticeTest = t.IsPracticeTest,
                Subtitle = t.Subtitle,
                ImageUrl = t.ImageUrl,
                BadgeText = t.BadgeText,
                BookTestId = t.BookTestId,
                BookId = t.BookTest?.BookId,
                QuestionCount = t.WorksheetQuestions.Count(),
                CanEdit = WorksheetAccess.CanModify(t.CreateUserId, userProfile.Id, isAdmin),
                // İç user id enumerasyonunu önlemek için sadece admin veya sahibine dön.
                CreatedByUserId = (isAdmin || t.CreateUserId == userProfile.Id) ? t.CreateUserId : null,
                CreatedByName = isAdmin && t.CreateUserId.HasValue && creatorNames.TryGetValue(t.CreateUserId.Value, out var name)
                    ? name
                    : null
            };
            ApplyOwnershipAndVisibility(d, t, userProfile.Id, isAdmin, ownerName, populateOwnerName: isSharedRow);
            return d;
        }).ToList();

        return new Paged<WorksheetDto>
        {
            PageNumber = dto.pageNumber,
            PageSize = dto.pageSize,
            TotalCount = totalCount,
            Items = worksheetDtos
        };
    }

    public async Task<Paged<WorksheetDto>> GetWorksheetsForStudentsAsync(ExamFilterDto dto, StudentProfileDto studentProfile)
    {
        var instanceQuery = _context.TestInstances
           .Where(ti => ti.StudentId == studentProfile.Id)
           .Include(ti => ti.WorksheetInstanceQuestions)
           .Include(ti => ti.Worksheet)
           .AsQueryable();


        var query = _context.Worksheets.AsQueryable();

        if (dto.id > 0)
        {
            query = query.Where(t => t.Id == dto.id);
            instanceQuery = instanceQuery.Where(ti => ti.WorksheetId == dto.id);
        }
        else
        {
            if (dto.gradeIds != null && dto.gradeIds.Any())
                query = query.Where(t => dto.gradeIds.Contains(t.GradeId));
            else if (studentProfile != null && studentProfile.GradeId.HasValue)
                query = query.Where(t => t.GradeId == studentProfile.GradeId);

            if (dto.subjectIds != null && dto.subjectIds.Any())
                query = query.Where(t => t.SubjectId.HasValue && dto.subjectIds.Contains(t.SubjectId.Value));

            if (dto.bookTestId > 0)
                query = query.Where(t => t.BookTestId == dto.bookTestId);

            if (!string.IsNullOrEmpty(dto.search))
            {
                var normalizedSearch = dto.search.ToLower(new CultureInfo("tr-TR"));
                query = query.Where(t =>
                    EF.Functions.Like(t.Name.ToLower(), $"%{normalizedSearch}%") ||
                    (t.Subtitle != null && EF.Functions.Like(t.Subtitle.ToLower(), $"%{normalizedSearch}%")) ||
                    EF.Functions.Like(t.Description.ToLower(), $"%{normalizedSearch}%"));
            }
        }

        query = ApplyCommonFilters(query, dto);

        var now = DateTime.UtcNow;

        // Görünürlük kapısı (issue #14): öğrenci yalnızca (a) kendisine/sınıfına aktif ataması
        // olan, VEYA (b) grade uyumlu + StudentVisibility=Normal olan (keşfedilebilir) sınavları
        // görebilir. TeacherSharing bu eksenle ilgisizdir — öğretmenler arası paylaşım öğrencinin
        // kendi öğretmeninin worksheet'lerini görmesini etkilemez.
        query = query.Where(t =>
            _context.ActiveAssignmentsFor(studentProfile.Id, studentProfile.GradeId, now)
                .Any(a => a.WorksheetId == t.Id)
            || (t.StudentVisibility == WorksheetStudentVisibility.Normal
                && studentProfile.GradeId.HasValue && t.GradeId == studentProfile.GradeId.Value));

        if (dto.statuses != null && dto.statuses.Any())
        {
            var studentInstances = _context.TestInstances.Where(ti => ti.StudentId == studentProfile.Id);
            var wantNotStarted = dto.statuses.Contains(-1);
            var wantInProgress = dto.statuses.Contains(0);
            var wantCompleted = dto.statuses.Contains(1);

            query = query.Where(t =>
                (wantNotStarted && !studentInstances.Any(ti => ti.WorksheetId == t.Id)) ||
                (wantInProgress && studentInstances.Any(ti => ti.WorksheetId == t.Id
                    && ti.Status == WorksheetInstanceStatus.Started)) ||
                (wantCompleted && studentInstances.Any(ti => ti.WorksheetId == t.Id
                    && (ti.Status == WorksheetInstanceStatus.Completed
                        || ti.Status == WorksheetInstanceStatus.Expired))));
        }

        // Her worksheet için kaç benzersiz öğrenci instance oluşturmuş
        var worksheetStudentCounts = await _context.TestInstances
            .GroupBy(ti => ti.WorksheetId)
            .Select(g => new
            {
                WorksheetId = g.Key,
                UniqueStudentCount = g.Select(ti => ti.StudentId).Distinct().Count()
            })
            .ToDictionaryAsync(x => x.WorksheetId, x => x.UniqueStudentCount);

        var instances = await instanceQuery.ToListAsync(); // 🔥 Burada `ToListAsync()` çağırarak veriyi hafızaya alıyoruz

        var totalCount = await query.CountAsync(); // Toplam kayıt sayısı
        var tests = await ApplySort(query, _context, dto.SortByParsed, dto.SortDescending, studentProfile.Id)
            .Include(t => t.BookTest)
                .ThenInclude(bt => bt.Book)
            .Include(t => t.WorksheetQuestions)
                .ThenInclude(tq => tq.Question)
            .Skip((dto.pageNumber - 1) * dto.pageSize) // Sayfalama için
            .Take(dto.pageSize)
            .ToListAsync();

        // Bu sayfadaki worksheet'lerden hangileri öğrenciye/sınıfına aktif olarak atanmış
        // (IsAssigned) — kalanlar yalnızca keşfet ile görünüyor demektir.
        var pageWorksheetIds = tests.Select(t => t.Id).ToList();
        var assignedWorksheetIds = (await _context.ActiveAssignmentsFor(studentProfile.Id, studentProfile.GradeId, now)
            .Where(a => pageWorksheetIds.Contains(a.WorksheetId))
            .Select(a => a.WorksheetId)
            .Distinct()
            .ToListAsync())
            .ToHashSet();

        var worksheetDtos = tests.Select(t =>
        {
            var instance = instances.FirstOrDefault(i => i.WorksheetId == t.Id);

            InstanceSummaryDto? instanceDto = null;
            if (instance != null)
            {
                var correct = instance.WorksheetInstanceQuestions.Count(wiq =>
                    wiq.SelectedAnswerId != null &&
                    t.WorksheetQuestions.Any(wq => wq.Id == wiq.WorksheetQuestionId
                        && wq.Question.CorrectAnswerId == wiq.SelectedAnswerId));

                var wrong = instance.WorksheetInstanceQuestions.Count(wiq =>
                    wiq.SelectedAnswerId != null &&
                    t.WorksheetQuestions.Any(wq => wq.Id == wiq.WorksheetQuestionId
                        && wq.Question.CorrectAnswerId != wiq.SelectedAnswerId));
                instanceDto = new InstanceSummaryDto
                {
                    Id = instance.Id,
                    Name = t.Name,
                    Status = (int)instance.Status,
                    ImageUrl = t.ImageUrl,
                    CompletedDate = instance.EndTime ?? DateTime.UtcNow,
                    DurationMinutes = instance.EndTime.HasValue ?
                (int)(instance.EndTime.Value - instance.StartTime).TotalMinutes : 0,
                    TotalQuestions = instance.WorksheetInstanceQuestions.Count,
                    CorrectAnswers = correct,
                    WrongAnswers = wrong,
                    Score = (correct * 100) / (instance.WorksheetInstanceQuestions.Count > 0 ? instance.WorksheetInstanceQuestions.Count : 1)
                };
            }

            var d = new WorksheetDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                GradeId = t.GradeId,
                SubjectId = t.SubjectId,
                TopicId = t.TopicId,
                SubTopicId = t.SubTopicId,
                MaxDurationSeconds = t.MaxDurationSeconds,
                IsPracticeTest = t.IsPracticeTest,
                Subtitle = t.Subtitle,
                ImageUrl = t.ImageUrl,
                BadgeText = t.BadgeText,
                BookTestId = t.BookTestId,
                BookId = t.BookTest?.BookId,
                QuestionCount = t.WorksheetQuestions.Count(),
                Instance = instanceDto, // 💡 Eklenen alan
                InstanceCount = worksheetStudentCounts.TryGetValue(t.Id, out var count) ? count : 0, // 💡 Yeni eklenen alan
                IsAssigned = assignedWorksheetIds.Contains(t.Id)
            };
            // Öğrenci akışı: sahiplik alanları anlamsız (requesterUserId=null, isAdmin=false)
            // → IsOwner=false, OwnerName=null, CanAssign=false; TeacherSharing/StudentVisibility yine set edilir.
            ApplyOwnershipAndVisibility(d, t, requesterUserId: null, isAdmin: false);
            return d;
        }).ToList();

        return new Paged<WorksheetDto>
        {
            PageNumber = dto.pageNumber,
            PageSize = dto.pageSize,
            TotalCount = totalCount,
            Items = worksheetDtos
        };
    }

    public async Task<List<WorksheetDto>> GetLatestWorksheetsAsync(int pageNumber, int pageSize, int? ownerUserId = null)
    {
        var query = _context.Worksheets.AsQueryable();
        // Öğretmen (admin değil) yalnızca kendi worksheet'lerini görür.
        if (ownerUserId.HasValue)
            query = query.Where(t => t.CreateUserId == ownerUserId.Value);

        var worksheets = await query
            .OrderByDescending(t => t.CreateTime)
            .ThenByDescending(t => t.Id) // stable tiebreaker when CreateTime collides
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Include(t => t.BookTest)
            .Include(t => t.WorksheetQuestions)
            .ToListAsync();

        return worksheets.Select(t =>
        {
            var d = new WorksheetDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                GradeId = t.GradeId,
                SubjectId = t.SubjectId,
                MaxDurationSeconds = t.MaxDurationSeconds,
                IsPracticeTest = t.IsPracticeTest,
                Subtitle = t.Subtitle,
                ImageUrl = t.ImageUrl,
                BadgeText = t.BadgeText,
                BookTestId = t.BookTestId,
                BookId = t.BookTest?.BookId,
                QuestionCount = t.WorksheetQuestions.Count()
            };
            // ownerUserId yalnız admin olmayan öğretmen için dolu → o durumda tüm satırlar sahibinin.
            // Admin/öğrenci için ownerUserId null: IsOwner/CanAssign false (keşif listesi; /tests ile
            // admin farkı kabul edilebilir). OwnerName liste akışında çözülmez.
            ApplyOwnershipAndVisibility(d, t, requesterUserId: ownerUserId, isAdmin: false);
            return d;
        }).ToList();
    }

    public async Task<List<WorksheetDto>> GetPopularWorksheetsAsync(int? gradeId, int pageNumber, int pageSize, int sinceDays, int? ownerUserId = null)
    {
        if (sinceDays <= 0) sinceDays = 30;
        var since = DateTime.UtcNow.AddDays(-sinceDays);

        // Son zamanlarda öğrenciler tarafından çözülen (instance oluşturulan) worksheet'ler
        var popularity = await _context.TestInstances
            .Where(ti => ti.StartTime >= since)
            .GroupBy(ti => ti.WorksheetId)
            .Select(g => new
            {
                WorksheetId = g.Key,
                SolveCount = g.Count(),
                UniqueStudents = g.Select(ti => ti.StudentId).Distinct().Count(),
                LastSolvedAt = g.Max(ti => ti.StartTime)
            })
            .ToListAsync();

        if (popularity.Count == 0)
            return new List<WorksheetDto>();

        var popMap = popularity.ToDictionary(p => p.WorksheetId);
        var worksheetIds = popMap.Keys.ToList();

        var worksheetQuery = _context.Worksheets.Where(w => worksheetIds.Contains(w.Id));
        if (gradeId.HasValue)
            worksheetQuery = worksheetQuery.Where(w => w.GradeId == gradeId.Value);
        // Öğretmen (admin değil) yalnızca kendi worksheet'lerini görür.
        if (ownerUserId.HasValue)
            worksheetQuery = worksheetQuery.Where(w => w.CreateUserId == ownerUserId.Value);

        var worksheets = await worksheetQuery
            .Include(t => t.BookTest)
            .Include(t => t.WorksheetQuestions)
            .ToListAsync();

        return worksheets
            .OrderByDescending(w => popMap[w.Id].SolveCount)
            .ThenByDescending(w => popMap[w.Id].LastSolvedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(t =>
            {
                var d = new WorksheetDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Description = t.Description,
                    GradeId = t.GradeId,
                    SubjectId = t.SubjectId,
                    TopicId = t.TopicId,
                    SubTopicId = t.SubTopicId,
                    MaxDurationSeconds = t.MaxDurationSeconds,
                    IsPracticeTest = t.IsPracticeTest,
                    Subtitle = t.Subtitle,
                    ImageUrl = t.ImageUrl,
                    BadgeText = t.BadgeText,
                    BookTestId = t.BookTestId,
                    BookId = t.BookTest?.BookId,
                    QuestionCount = t.WorksheetQuestions.Count(),
                    InstanceCount = popMap[t.Id].UniqueStudents
                };
                // ownerUserId yalnız admin olmayan öğretmen için dolu (bkz. GetLatestWorksheetsAsync).
                ApplyOwnershipAndVisibility(d, t, requesterUserId: ownerUserId, isAdmin: false);
                return d;
            })
            .ToList();
    }

    public async Task<List<QuestionDto>> GetExamQuestionsAsync()
    {
        return await _context.Questions
            .Include(q => q.Subject)
            .Select(q => new QuestionDto
            {
                Id = q.Id,
                Text = q.Text,
                SubText = q.SubText,
                ImageUrl = q.ImageUrl,
                CategoryName = q.Subject.Name,
                Point = q.Point
            })
            .ToListAsync();
    }

    public async Task<WorksheetDto?> GetWorksheetByIdAsync(int id, UserProfileDto userProfile, bool isAdmin)
    {
        var worksheet = await _context.Worksheets
            .Include(t => t.BookTest)
                .ThenInclude(bt => bt.Book)
            .Include(t => t.WorksheetQuestions)
                .ThenInclude(tq => tq.Question)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (worksheet == null)
            return null;

        // Öğretmen yalnızca kendi worksheet'ini görebilir; admin hepsini. Yetkisizse "yok" gibi davran.
        // Öğrenci akışı değişmez (öğrenci çözüm için worksheet detayına erişebilir).
        var isStudent = string.Equals(userProfile.Role, UserRole.Student.ToString(), StringComparison.OrdinalIgnoreCase);
        // Legacy (CreateUserId null/0) kayıtlar Public* işaretli olsa bile admin dışında kimseye
        // görünmez — bu kural WorksheetAccess.CanView içinde merkezi olarak uygulanır (issue #11 AC).
        if (!isStudent && !WorksheetAccess.CanView(worksheet.CreateUserId, userProfile.Id, isAdmin, worksheet.TeacherSharing, worksheet.StudentVisibility))
            return null;

        var isOwner = worksheet.CreateUserId.HasValue && worksheet.CreateUserId.Value > 0
            && worksheet.CreateUserId.Value == userProfile.Id;
        // Bu satır sahiplik/admin dışında yalnızca Public* paylaşım sayesinde görünüyorsa (issue #11),
        // görüntüleyen öğretmenin kimin worksheet'i olduğunu bilmesi gerekir.
        var isSharedRow = !isAdmin && !isOwner && !isStudent;

        string? createdByName = null;
        string? ownerName = null;
        if ((isAdmin || isOwner || isSharedRow) && worksheet.CreateUserId.HasValue && worksheet.CreateUserId.Value > 0)
        {
            var names = await ResolveCreatorNamesAsync(new[] { worksheet.CreateUserId });
            names.TryGetValue(worksheet.CreateUserId.Value, out ownerName);
            if (isAdmin) createdByName = ownerName;
        }

        var result = new WorksheetDto
        {
            Id = worksheet.Id,
            Name = worksheet.Name,
            Description = worksheet.Description,
            GradeId = worksheet.GradeId,
            SubjectId = worksheet.SubjectId,
            MaxDurationSeconds = worksheet.MaxDurationSeconds,
            IsPracticeTest = worksheet.IsPracticeTest,
            Subtitle = worksheet.Subtitle,
            ImageUrl = worksheet.ImageUrl,
            BadgeText = worksheet.BadgeText,
            BookTestId = worksheet.BookTestId,
            BookId = worksheet.BookTest?.BookId,
            QuestionCount = worksheet.WorksheetQuestions.Count(),
            CanEdit = WorksheetAccess.CanModify(worksheet.CreateUserId, userProfile.Id, isAdmin),
            // İç user id enumerasyonunu önlemek için sadece admin veya sahibine dön.
            CreatedByUserId = (isAdmin || worksheet.CreateUserId == userProfile.Id) ? worksheet.CreateUserId : null,
            CreatedByName = createdByName
        };
        // Tekil detay: OwnerName admin, sahip, veya Public* paylaşım sayesinde görülen satır için doldurulur.
        ApplyOwnershipAndVisibility(result, worksheet, userProfile.Id, isAdmin, ownerName, populateOwnerName: isSharedRow);
        return result;
    }

    public async Task<List<WorksheetWithInstanceDto>> GetWorksheetAndInstancesAsync(StudentProfileDto student, int gradeId)
    {
        // Student-scoped worksheet'ler (örn. "Yanlışlarım" practice test'i) yalnızca ilgili
        // öğrenciye görünmeli — sınıf listesine sızmamalı.
        var worksheets = await _context.Worksheets
            .Include(w => w.WorksheetQuestions)
            .Include(w => w.BookTest)
            .Where(w => w.GradeId == gradeId)
            .Where(w => !_context.WorksheetAssignments.Any(a => a.WorksheetId == w.Id && a.StudentId != null)
                || _context.WorksheetAssignments.Any(a => a.WorksheetId == w.Id && a.StudentId == student.Id))
            .ToListAsync();

        var worksheetIds = worksheets.Select(w => w.Id).ToList();

        var instances = await _context.TestInstances
            .Where(i => worksheetIds.Contains(i.WorksheetId) && i.StudentId == student.Id)
            .ToListAsync();

        var result = worksheets.Select(w =>
        {
            var wsDto = new WorksheetDto
            {
                Id = w.Id,
                Name = w.Name,
                Description = w.Description,
                GradeId = w.GradeId,
                SubjectId = w.SubjectId,
                MaxDurationSeconds = w.MaxDurationSeconds,
                IsPracticeTest = w.IsPracticeTest,
                Subtitle = w.Subtitle,
                ImageUrl = w.ImageUrl,
                BadgeText = w.BadgeText,
                BookTestId = w.BookTestId,
                BookId = w.BookTest?.BookId,
                QuestionCount = w.WorksheetQuestions.Count
            };
            // Öğrenci akışı: sahiplik alanları anlamsız → IsOwner=false, OwnerName=null, CanAssign=false.
            ApplyOwnershipAndVisibility(wsDto, w, requesterUserId: null, isAdmin: false);
            return new WorksheetWithInstanceDto
            {
                Worksheet = wsDto,
                Instance = instances.FirstOrDefault(i => i.WorksheetId == w.Id)
            };
        }).ToList();

        return result;
    }

    public async Task<List<WorksheetInstanceQuestionDto>> GetAllCanvasQuestions(bool includeAnswers = false, int maxId = 0)
    {
        var questions = await _context.Questions
            .Include(q => q.Answers)
            .Where(q => q.IsCanvasQuestion && q.Id > maxId)
            .Select(q => new WorksheetInstanceQuestionDto
            {
                Question = new QuestionDto
                {
                    Id = q.Id,
                    X = q.X,
                    Y = q.Y,
                    Width = q.Width,
                    Height = q.Height,
                    SanitizedHeight = q.SanitizedHeight,
                    ImageUrl = q.ImageUrl,
                    Answers = includeAnswers ? q.Answers.Select(a => new AnswerDto
                    {
                        X = a.X,
                        Y = a.Y,
                        Width = a.Width,
                        Height = a.Height
                    }).ToList() : new List<AnswerDto>(),
                }
            }
            )
            .ToListAsync();

        foreach (var question in questions)
        {
            var dto = question.Question;
            var fallbackColumns = dto.AnswerColCount > 0
                ? dto.AnswerColCount
                : Math.Max(1, Math.Min(dto.Answers?.Count ?? 0, 4));
        }

        return questions;
    }

    public async Task<ExamAllStatisticsDto> GetGroupedStudentStatistics(int studentId)
    {
        var testInstances = await _context.TestInstances
            .Where(ti => ti.StudentId == studentId)
            .Include(ti => ti.Worksheet)
            .Include(ti => ti.WorksheetInstanceQuestions)
                .ThenInclude(wiq => wiq.WorksheetQuestion)
                    .ThenInclude(wq => wq.Question)
            .ToListAsync();

        // 🔹 Total verileri için
        int totalSolved = testInstances.Count;
        var completedTests = testInstances.Where(ti => ti.Status == WorksheetInstanceStatus.Completed).ToList();
        int completedCount = completedTests.Count;
        int totalCorrect = 0;
        int totalWrong = 0;
        int totalTimeTaken = 0;

        foreach (var instance in completedTests)
        {
            foreach (var question in instance.WorksheetInstanceQuestions)
            {
                totalTimeTaken += question.TimeTaken / 60;

                if (question.SelectedAnswerId.HasValue)
                {
                    var correctAnswerId = question.WorksheetQuestion.Question.CorrectAnswerId;
                    if (correctAnswerId.HasValue)
                    {
                        if (question.SelectedAnswerId == correctAnswerId)
                            totalCorrect++;
                        else
                            totalWrong++;
                    }
                }
            }
        }

        // 🔹 Gruplama verileri
        var grouped = testInstances
            .GroupBy(ti => new { ti.Worksheet.GradeId, ti.Worksheet.Name })
            .Select(group =>
            {
                var allTests = group.ToList();
                var completed = allTests.Where(ti => ti.Status == WorksheetInstanceStatus.Completed).ToList();

                int groupCorrect = 0;
                int groupWrong = 0;
                int groupTimeTaken = 0;

                foreach (var instance in completed)
                {
                    foreach (var question in instance.WorksheetInstanceQuestions)
                    {
                        groupTimeTaken += question.TimeTaken / 60;

                        if (question.SelectedAnswerId.HasValue)
                        {
                            var correctAnswerId = question.WorksheetQuestion.Question.CorrectAnswerId;
                            if (correctAnswerId.HasValue)
                            {
                                if (question.SelectedAnswerId == correctAnswerId)
                                    groupCorrect++;
                                else
                                    groupWrong++;
                            }
                        }
                    }
                }

                return new
                {
                    GradeId = group.Key.GradeId,
                    TestName = group.Key.Name,
                    TotalSolvedTests = allTests.Count,
                    CompletedTests = completed.Count,
                    TotalTimeSpentMinutes = groupTimeTaken,
                    TotalCorrectAnswers = groupCorrect,
                    TotalWrongAnswers = groupWrong
                };
            })
            .ToList();

        // 🔹 Response
        var response = new ExamAllStatisticsDto
        {
            Total = new ExamStatisticsDto
            {
                TotalSolvedTests = totalSolved,
                CompletedTests = completedCount,
                TotalTimeSpentMinutes = totalTimeTaken,
                TotalCorrectAnswers = totalCorrect,
                TotalWrongAnswers = totalWrong
            },
            Grouped = grouped.Select(g => new ExamStatisticsDto
            {
                TotalSolvedTests = g.TotalSolvedTests,
                CompletedTests = g.CompletedTests,
                TotalTimeSpentMinutes = g.TotalTimeSpentMinutes,
                TotalCorrectAnswers = g.TotalCorrectAnswers,
                TotalWrongAnswers = g.TotalWrongAnswers
            }).ToList()
        }; return response;
    }

    public async Task<List<Grade>> GetGradesAsync()
    {
        return await _context.Grades.ToListAsync();
    }

}
