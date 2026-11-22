using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.Tenancy;

public enum SchoolHomeworkStatus
{
    Success,

    /// <summary>Oluşturan okulun onaylı öğretmeni değil.</summary>
    Forbidden,

    /// <summary>Ödev / şube / öğrenci kapsamda değil (varlık sızdırılmaz).</summary>
    NotFound,

    Invalid,

    /// <summary>Öğretmen kontrol ettikten sonra öğrenci işaretini değiştiremez.</summary>
    Locked,

    /// <summary>Öğretmenin son 24 saatteki ödev sınırı doldu.</summary>
    RateLimited
}

public sealed record SchoolHomeworkResult<T>(SchoolHomeworkStatus Status, T? Value = default);

/// <summary>
/// issue #1282 (#1071 S7): düz ödev. Oluşturan okulun onaylı, askısız, bağımsız olmayan öğretmenidir (okul DB'den,
/// <see cref="ISchoolSectionService.ResolveTeacherSchoolIdAsync"/>); hedef okulun sınıfı, şubesi ya da doğrulanmış öğrencileri.
/// Alıcılar okunurken çözülür (okul değişimi matrisi "Sınıf atamaları" satırı): sınıf hedefi öğrencinin güncel okulu + sınıfıyla,
/// şube hedefi aktif üyelikle, seçili öğrenci hedefi doğrudan (okul değişse de kalır). Öğretmen yalnız kendi ödevlerini ve
/// yalnız güncel okulunun doğrulanmış öğrencilerinin durumunu görür / kontrol eder. Puan ve bildirim yok.
/// </summary>
public interface ISchoolHomeworkService
{
    Task<SchoolHomeworkResult<SchoolHomeworkDto>> CreateAsync(int teacherUserId, SchoolHomeworkCreateRequestDto request,
        CancellationToken ct = default);

    /// <summary>Öğretmenin kendi ödevleri (okul değişse de), yeniden eskiye.</summary>
    Task<Paged<SchoolHomeworkDto>> ListOwnAsync(int teacherUserId, int page, int pageSize, CancellationToken ct = default);

    Task<SchoolHomeworkResult<SchoolHomeworkDetailDto>> GetAsync(int teacherUserId, int homeworkId, CancellationToken ct = default);

    Task<SchoolHomeworkResult<SchoolHomeworkStudentStatusDto>> CheckAsync(int teacherUserId, int homeworkId, int studentId,
        SchoolHomeworkCheckRequestDto request, CancellationToken ct = default);

    Task<SchoolHomeworkStatus> DeleteAsync(int teacherUserId, int homeworkId, CancellationToken ct = default);

    /// <summary>Öğrencinin görebildiği ödevler: teslim günü son <see cref="SchoolHomeworkService.StudentHistoryDays"/> gün içinde ya da ileride.</summary>
    Task<IReadOnlyList<StudentHomeworkDto>?> ListForStudentAsync(int studentUserId, CancellationToken ct = default);

    Task<SchoolHomeworkResult<StudentHomeworkDto>> MarkDoneAsync(int studentUserId, int homeworkId, bool done,
        CancellationToken ct = default);
}

public class SchoolHomeworkService : ISchoolHomeworkService
{
    public const int MaxStudentIds = 200;
    public const int DailyLimit = 50;
    public const int MaxDueDays = 180;
    public const int StudentHistoryDays = 30;

    /// <summary>Ayrıntıda dönen en çok öğrenci (bir sınıf hedefi bile bunu nadiren aşar).</summary>
    public const int MaxDetailStudents = 500;

    private readonly AppDbContext _context;
    private readonly ISchoolSectionService _sections;
    private readonly ILocalDayCalendar _calendar;
    private readonly IAuthApiClient? _authApi;
    private readonly ILogger<SchoolHomeworkService> _logger;
    private readonly TimeProvider _time;

    public SchoolHomeworkService(
        AppDbContext context,
        ISchoolSectionService sections,
        ILocalDayCalendar calendar,
        IAuthApiClient? authApi = null,
        ILogger<SchoolHomeworkService>? logger = null,
        TimeProvider? time = null)
    {
        _context = context;
        _sections = sections;
        _calendar = calendar;
        _authApi = authApi;
        _logger = logger ?? NullLogger<SchoolHomeworkService>.Instance;
        _time = time ?? TimeProvider.System;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public async Task<SchoolHomeworkResult<SchoolHomeworkDto>> CreateAsync(int teacherUserId, SchoolHomeworkCreateRequestDto request,
        CancellationToken ct = default)
    {
        if (await _sections.ResolveTeacherSchoolIdAsync(teacherUserId, ct) is not int schoolId)
            return new(SchoolHomeworkStatus.Forbidden);

        var book = Clean(request.BookName);
        var pages = Clean(request.Pages);
        var note = Clean(request.Note);
        var today = _calendar.Today;
        if (request.SubjectId is not int subjectId
            || request.DueDate is not DateOnly due || due < today || due > today.AddDays(MaxDueDays)
            || (pages == null && note == null)
            || book?.Length > SchoolHomework.BookNameMaxLength
            || pages?.Length > SchoolHomework.PagesMaxLength
            || note?.Length > SchoolHomework.NoteMaxLength
            || !Enum.TryParse<SchoolHomeworkTarget>(request.Target, ignoreCase: false, out var target)
            || !Enum.IsDefined(target))
            return new(SchoolHomeworkStatus.Invalid);

        var subjectName = await _context.Subjects.AsNoTracking().Where(s => s.Id == subjectId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        if (subjectName == null)
            return new(SchoolHomeworkStatus.Invalid);

        var homework = new SchoolHomework
        {
            SchoolId = schoolId,
            TeacherUserId = teacherUserId,
            SubjectId = subjectId,
            BookName = book,
            Pages = pages,
            Note = note,
            DueDate = due,
            Target = target,
            CreatedAt = UtcNow
        };
        var verified = VerifiedStudents(schoolId);
        switch (target)
        {
            case SchoolHomeworkTarget.Grade:
                if (request.GradeId is not int g)
                    return new(SchoolHomeworkStatus.Invalid);
                var gradeName = await _context.Grades.AsNoTracking().Where(x => x.Id == g).Select(x => x.Name).FirstOrDefaultAsync(ct);
                if (gradeName == null)
                    return new(SchoolHomeworkStatus.Invalid);
                homework.TargetGradeId = g;
                homework.TargetLabel = Truncate(gradeName);
                break;
            case SchoolHomeworkTarget.Section:
                if (request.SectionId is not int sid)
                    return new(SchoolHomeworkStatus.Invalid);
                var section = await _context.SchoolSections.AsNoTracking()
                    .Where(s => s.Id == sid && s.SchoolId == schoolId)
                    .Select(s => new { s.Id, s.GradeId, s.Name, GradeName = s.Grade.Name })
                    .FirstOrDefaultAsync(ct);
                if (section == null)
                    return new(SchoolHomeworkStatus.NotFound);
                homework.TargetGradeId = section.GradeId;
                homework.TargetSectionId = section.Id;
                homework.TargetLabel = Truncate($"{section.GradeName} / {section.Name}");
                break;
            default: // Students
                var ids = request.StudentIds?.Distinct().ToList() ?? [];
                if (ids.Count == 0 || ids.Count > MaxStudentIds || ids.Any(id => id <= 0))
                    return new(SchoolHomeworkStatus.Invalid);
                if (await verified.CountAsync(s => ids.Contains(s.Id), ct) != ids.Count)
                    return new(SchoolHomeworkStatus.NotFound); // hiçbiri yazılmaz
                foreach (var id in ids)
                    homework.Students.Add(new SchoolHomeworkStudent { StudentId = id });
                break;
        }

        var since = UtcNow.AddDays(-1);
        if (await _context.SchoolHomeworks.CountAsync(h => h.TeacherUserId == teacherUserId && h.CreatedAt > since, ct) >= DailyLimit)
            return new(SchoolHomeworkStatus.RateLimited);

        _context.SchoolHomeworks.Add(homework);
        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Ödev oluşturuldu. HomeworkId={Id}, SchoolId={SchoolId}, Target={Target}, DueDate={Due}",
            homework.Id, schoolId, target, due);
        return new(SchoolHomeworkStatus.Success, await SummarizeAsync(homework, subjectName, schoolId, ct));
    }

    public async Task<Paged<SchoolHomeworkDto>> ListOwnAsync(int teacherUserId, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = AdminListPaging.Normalize(page, pageSize);
        var query = _context.SchoolHomeworks.AsNoTracking().Where(h => h.TeacherUserId == teacherUserId && h.DeletedAt == null);
        var totalCount = await query.CountAsync(ct);
        if (!AdminListPaging.TryGetOffset(page, pageSize, totalCount, out var offset))
            return AdminListPaging.EmptyPage<SchoolHomeworkDto>(page, pageSize, totalCount);
        var rows = await query.OrderByDescending(h => h.CreatedAt).ThenByDescending(h => h.Id).Skip(offset).Take(pageSize)
            .Select(h => new { Homework = h, SubjectName = h.Subject.Name })
            .ToListAsync(ct);
        var schoolId = await _sections.ResolveTeacherSchoolIdAsync(teacherUserId, ct);
        var items = new List<SchoolHomeworkDto>(rows.Count);
        foreach (var r in rows)
            items.Add(await SummarizeAsync(r.Homework, r.SubjectName, schoolId, ct));
        return new Paged<SchoolHomeworkDto> { PageNumber = page, PageSize = pageSize, TotalCount = totalCount, Items = items };
    }

    public async Task<SchoolHomeworkResult<SchoolHomeworkDetailDto>> GetAsync(int teacherUserId, int homeworkId, CancellationToken ct = default)
    {
        var row = await OwnHomework(teacherUserId, homeworkId)
            .Select(h => new { Homework = h, SubjectName = h.Subject.Name })
            .FirstOrDefaultAsync(ct);
        if (row == null)
            return new(SchoolHomeworkStatus.NotFound);
        var schoolId = await _sections.ResolveTeacherSchoolIdAsync(teacherUserId, ct);
        var students = await ScopedStudentRowsAsync(row.Homework, schoolId, ct);
        var names = await FullNamesAsync(students.Select(s => s.UserId), ct);
        var pastDue = IsPastDue(row.Homework.DueDate);
        return new(SchoolHomeworkStatus.Success, new SchoolHomeworkDetailDto
        {
            Homework = Summarize(row.Homework, row.SubjectName, students),
            Students = students
                .Select(s => ToStatusDto(s, names.GetValueOrDefault(s.UserId) ?? string.Empty, pastDue))
                .OrderBy(s => s.FullName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(s => s.StudentId)
                .ToList()
        });
    }

    public async Task<SchoolHomeworkResult<SchoolHomeworkStudentStatusDto>> CheckAsync(int teacherUserId, int homeworkId, int studentId,
        SchoolHomeworkCheckRequestDto request, CancellationToken ct = default)
    {
        SchoolHomeworkCheckStatus? status = null;
        if (request.Status != null)
        {
            if (!Enum.TryParse<SchoolHomeworkCheckStatus>(request.Status, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
                return new(SchoolHomeworkStatus.Invalid);
            status = parsed;
        }

        var note = Clean(request.Note);
        if (note?.Length > SchoolHomeworkProgress.TeacherNoteMaxLength || (status == null && note != null))
            return new(SchoolHomeworkStatus.Invalid);

        var homework = await OwnHomework(teacherUserId, homeworkId).FirstOrDefaultAsync(ct);
        if (homework == null)
            return new(SchoolHomeworkStatus.NotFound);
        var schoolId = await _sections.ResolveTeacherSchoolIdAsync(teacherUserId, ct);
        var student = await TargetStudents(homework, schoolId).Where(s => s.Id == studentId)
            .Select(s => new { s.Id, s.UserId, s.StudentNumber })
            .FirstOrDefaultAsync(ct);
        if (student == null)
            return new(SchoolHomeworkStatus.NotFound);

        var progress = await UpsertProgressAsync(homeworkId, studentId, p =>
        {
            p.TeacherStatus = status;
            p.TeacherNote = note;
            p.CheckedAt = status == null ? null : UtcNow;
            p.CheckedByUserId = status == null ? null : teacherUserId;
            return true;
        }, ct);

        var names = await FullNamesAsync([student.UserId], ct);
        return new(SchoolHomeworkStatus.Success, ToStatusDto(
            new StudentRow(student.Id, student.UserId, student.StudentNumber, progress),
            names.GetValueOrDefault(student.UserId) ?? string.Empty,
            IsPastDue(homework.DueDate)));
    }

    public async Task<SchoolHomeworkStatus> DeleteAsync(int teacherUserId, int homeworkId, CancellationToken ct = default)
    {
        var homework = await _context.SchoolHomeworks
            .FirstOrDefaultAsync(h => h.Id == homeworkId && h.TeacherUserId == teacherUserId && h.DeletedAt == null, ct);
        if (homework == null)
            return SchoolHomeworkStatus.NotFound;
        homework.DeletedAt = UtcNow;
        await _context.SaveChangesAsync(ct);
        return SchoolHomeworkStatus.Success;
    }

    public async Task<IReadOnlyList<StudentHomeworkDto>?> ListForStudentAsync(int studentUserId, CancellationToken ct = default)
    {
        var student = await StudentOf(studentUserId, ct);
        if (student == null)
            return null;
        var from = _calendar.Today.AddDays(-StudentHistoryDays);
        var rows = await VisibleTo(student)
            .Where(h => h.DueDate >= from)
            .OrderBy(h => h.DueDate)
            .ThenBy(h => h.Id)
            .Select(h => new
            {
                Homework = h,
                SubjectName = h.Subject.Name,
                Progress = _context.SchoolHomeworkProgress.FirstOrDefault(p => p.HomeworkId == h.Id && p.StudentId == student.Id)
            })
            .ToListAsync(ct);
        var names = await ParentNotificationSupport.LookupAsync(_authApi, rows.Select(r => r.Homework.TeacherUserId), _logger, ct);
        return rows.Select(r => ToStudentDto(r.Homework, r.SubjectName, r.Progress,
            ParentNotificationSupport.Of(names, r.Homework.TeacherUserId).DisplayName)).ToList();
    }

    public async Task<SchoolHomeworkResult<StudentHomeworkDto>> MarkDoneAsync(int studentUserId, int homeworkId, bool done,
        CancellationToken ct = default)
    {
        var student = await StudentOf(studentUserId, ct);
        if (student == null)
            return new(SchoolHomeworkStatus.NotFound);
        var row = await VisibleTo(student).Where(h => h.Id == homeworkId)
            .Select(h => new { Homework = h, SubjectName = h.Subject.Name })
            .FirstOrDefaultAsync(ct);
        if (row == null)
            return new(SchoolHomeworkStatus.NotFound);

        var locked = false;
        var progress = await UpsertProgressAsync(homeworkId, student.Id, p =>
        {
            if (p.TeacherStatus != null)
            {
                locked = true;
                return false;
            }

            p.StudentDoneAt = done ? p.StudentDoneAt ?? UtcNow : null;
            return true;
        }, ct);
        if (locked)
            return new(SchoolHomeworkStatus.Locked);

        var names = await ParentNotificationSupport.LookupAsync(_authApi, [row.Homework.TeacherUserId], _logger, ct);
        return new(SchoolHomeworkStatus.Success, ToStudentDto(row.Homework, row.SubjectName, progress,
            ParentNotificationSupport.Of(names, row.Homework.TeacherUserId).DisplayName));
    }

    // ---- kapsam ----

    private IQueryable<Student> VerifiedStudents(int schoolId) =>
        _context.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && s.SchoolVerifiedAt != null);

    private IQueryable<SchoolHomework> OwnHomework(int teacherUserId, int homeworkId) =>
        _context.SchoolHomeworks.AsNoTracking().Where(h => h.Id == homeworkId && h.TeacherUserId == teacherUserId && h.DeletedAt == null);

    /// <summary>
    /// Ödevin hedef öğrencileri, öğretmenin güncel okulunun doğrulanmış öğrencileriyle kesişimi (<paramref name="teacherSchoolId"/> null
    /// → boş): okul değiştiren öğretmen eski okulun öğrencilerini görmez.
    /// </summary>
    private IQueryable<Student> TargetStudents(SchoolHomework h, int? teacherSchoolId)
    {
        if (teacherSchoolId is not int schoolId)
            return _context.Students.AsNoTracking().Where(_ => false);
        var scoped = VerifiedStudents(schoolId);
        return h.Target switch
        {
            SchoolHomeworkTarget.Grade => scoped.Where(s => s.SchoolId == h.SchoolId && s.GradeId == h.TargetGradeId),
            SchoolHomeworkTarget.Section => scoped.Where(s => _context.SchoolSectionMemberships
                .Any(m => m.SectionId == h.TargetSectionId && m.StudentId == s.Id && m.EndedAt == null)),
            _ => scoped.Where(s => _context.SchoolHomeworkStudents.Any(x => x.HomeworkId == h.Id && x.StudentId == s.Id))
        };
    }

    /// <summary>Öğrencinin güncel durumuna göre görünen ödevler (okul değişimi matrisi, "Sınıf atamaları").</summary>
    private IQueryable<SchoolHomework> VisibleTo(StudentScope st)
    {
        var verifiedSchool = st.Verified ? st.SchoolId : null;
        return _context.SchoolHomeworks.AsNoTracking().Where(h => h.DeletedAt == null && (
            (h.Target == SchoolHomeworkTarget.Grade && verifiedSchool != null && h.SchoolId == verifiedSchool
             && st.GradeId != null && h.TargetGradeId == st.GradeId)
            || (h.Target == SchoolHomeworkTarget.Section && _context.SchoolSectionMemberships
                .Any(m => m.SectionId == h.TargetSectionId && m.StudentId == st.Id && m.EndedAt == null))
            || (h.Target == SchoolHomeworkTarget.Students && _context.SchoolHomeworkStudents
                .Any(x => x.HomeworkId == h.Id && x.StudentId == st.Id))));
    }

    private sealed record StudentScope(int Id, int? SchoolId, bool Verified, int? GradeId);

    private Task<StudentScope?> StudentOf(int userId, CancellationToken ct) =>
        _context.Students.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new StudentScope(s.Id, s.SchoolId, s.SchoolVerifiedAt != null, s.GradeId))
            .FirstOrDefaultAsync(ct);

    // ---- durum ----

    private sealed record StudentRow(int Id, int UserId, string StudentNumber, SchoolHomeworkProgress? Progress);

    private async Task<List<StudentRow>> ScopedStudentRowsAsync(SchoolHomework h, int? teacherSchoolId, CancellationToken ct)
    {
        var students = await TargetStudents(h, teacherSchoolId)
            .OrderBy(s => s.Id)
            .Take(MaxDetailStudents)
            .Select(s => new { s.Id, s.UserId, s.StudentNumber })
            .ToListAsync(ct);
        var ids = students.Select(s => s.Id).ToList();
        var progress = ids.Count == 0
            ? new Dictionary<int, SchoolHomeworkProgress>()
            : await _context.SchoolHomeworkProgress.AsNoTracking()
                .Where(p => p.HomeworkId == h.Id && ids.Contains(p.StudentId))
                .ToDictionaryAsync(p => p.StudentId, ct);
        return students.Select(s => new StudentRow(s.Id, s.UserId, s.StudentNumber, progress.GetValueOrDefault(s.Id))).ToList();
    }

    private async Task<SchoolHomeworkDto> SummarizeAsync(SchoolHomework h, string subjectName, int? teacherSchoolId, CancellationToken ct) =>
        Summarize(h, subjectName, await ScopedStudentRowsAsync(h, teacherSchoolId, ct));

    private SchoolHomeworkDto Summarize(SchoolHomework h, string subjectName, List<StudentRow> students)
    {
        var pastDue = IsPastDue(h.DueDate);
        return new SchoolHomeworkDto
        {
            Id = h.Id,
            Target = h.Target.ToString(),
            TargetLabel = h.TargetLabel,
            SubjectId = h.SubjectId,
            SubjectName = subjectName,
            BookName = h.BookName,
            Pages = h.Pages,
            Note = h.Note,
            DueDate = h.DueDate,
            CreatedAt = h.CreatedAt,
            IsPastDue = pastDue,
            StudentCount = students.Count,
            StudentDoneCount = students.Count(s => s.Progress is { TeacherStatus: null, StudentDoneAt: not null }),
            DoneCount = students.Count(s => s.Progress?.TeacherStatus == SchoolHomeworkCheckStatus.Done),
            PartialCount = students.Count(s => s.Progress?.TeacherStatus == SchoolHomeworkCheckStatus.Partial),
            NotDoneCount = students.Count(s => s.Progress?.TeacherStatus == SchoolHomeworkCheckStatus.NotDone),
            OverdueCount = students.Count(s => IsOverdue(pastDue, s.Progress))
        };
    }

    private static SchoolHomeworkStudentStatusDto ToStatusDto(StudentRow s, string name, bool pastDue) => new()
    {
        StudentId = s.Id,
        FullName = name,
        StudentNumber = StudentNumberMask.Apply(s.StudentNumber),
        StudentDoneAt = s.Progress?.StudentDoneAt,
        TeacherStatus = s.Progress?.TeacherStatus?.ToString(),
        TeacherNote = s.Progress?.TeacherNote,
        CheckedAt = s.Progress?.CheckedAt,
        IsOverdue = IsOverdue(pastDue, s.Progress)
    };

    private StudentHomeworkDto ToStudentDto(SchoolHomework h, string subjectName, SchoolHomeworkProgress? p, string teacherName) => new()
    {
        Id = h.Id,
        SubjectName = subjectName,
        BookName = h.BookName,
        Pages = h.Pages,
        Note = h.Note,
        DueDate = h.DueDate,
        CreatedAt = h.CreatedAt,
        TeacherName = teacherName,
        StudentDoneAt = p?.StudentDoneAt,
        TeacherStatus = p?.TeacherStatus?.ToString(),
        TeacherNote = p?.TeacherNote,
        IsOverdue = IsOverdue(IsPastDue(h.DueDate), p)
    };

    /// <summary>Teslim günü geçmiş; ne öğrenci "yaptım" demiş ne öğretmen kontrol etmiş.</summary>
    private static bool IsOverdue(bool pastDue, SchoolHomeworkProgress? p) =>
        pastDue && p?.StudentDoneAt == null && p?.TeacherStatus == null;

    private bool IsPastDue(DateOnly due) => _calendar.Today > due;

    /// <summary>
    /// (HomeworkId, StudentId) satırını açar ya da günceller. <paramref name="apply"/> false dönerse yazılmaz. Eşzamanlı ilk yazımda
    /// tekil indeks çakışırsa bir kez yeniden okuyup güncellenir.
    /// </summary>
    private async Task<SchoolHomeworkProgress?> UpsertProgressAsync(int homeworkId, int studentId,
        Func<SchoolHomeworkProgress, bool> apply, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var progress = await _context.SchoolHomeworkProgress
                .FirstOrDefaultAsync(p => p.HomeworkId == homeworkId && p.StudentId == studentId, ct);
            var isNew = progress == null;
            progress ??= new SchoolHomeworkProgress { HomeworkId = homeworkId, StudentId = studentId };
            if (!apply(progress))
            {
                if (!isNew)
                    _context.Entry(progress).State = EntityState.Unchanged;
                return isNew ? null : progress;
            }

            if (isNew)
                _context.SchoolHomeworkProgress.Add(progress);
            try
            {
                await _context.SaveChangesAsync(ct);
                return progress;
            }
            catch (DbUpdateException) when (isNew && attempt == 0)
            {
                _context.Entry(progress).State = EntityState.Detached;
            }
        }
    }

    /// <summary>Öğretmenin öğrenci listesi için tam ad (şube listesiyle aynı); auth-api yoksa / düşerse adsız döner.</summary>
    private async Task<Dictionary<int, string>> FullNamesAsync(IEnumerable<int> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => id > 0).Distinct().ToList();
        if (_authApi == null || ids.Count == 0)
            return new Dictionary<int, string>();
        try
        {
            var users = await _authApi.GetUsersByIdsAsync(ids, ct);
            return users.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First().FullName ?? string.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "[Homework] Ad çözümü başarısız ({Count} kullanıcı); liste adsız döner", ids.Count);
            return new Dictionary<int, string>();
        }
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string Truncate(string label) =>
        label.Length > SchoolHomework.TargetLabelMaxLength ? label[..SchoolHomework.TargetLabelMaxLength] : label;
}
