using System;
using System.Net.Http;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services;

public class TeacherService : ITeacherService
{
    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApiClient;

    public TeacherService(AppDbContext context, IAuthApiClient authApiClient)
    {
        _context = context;
        _authApiClient = authApiClient;
    }

    public async Task<Teacher?> GetTeacher(int userId)
    {
        return await _context.Teachers
            .Where(t => t.UserId == userId)
            .FirstOrDefaultAsync();
    }

    public async Task<ResponseBaseDto> Save(int userId, RegisterTeacherDto dto)
    {
        if (dto.SchoolId.HasValue &&
            !await _context.Schools.AnyAsync(s => s.Id == dto.SchoolId.Value))
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = "Seçilen okul bulunamadı."
            };
        }

        // Bağımsız öğretmen (issue #92): admin onayı bekler; okula bağlı öğretmen doğrudan onaylı.
        var approvalStatus = dto.IsIndependentTutor
            ? TeacherApprovalStatus.Pending
            : TeacherApprovalStatus.Approved;

        var existingTeacher = await _context.Teachers.FirstOrDefaultAsync(s => s.UserId == userId);
        if (existingTeacher != null)
        {
            existingTeacher.SchoolId = dto.SchoolId;

            // ApprovalStatus yalnızca IsIndependentTutor gerçekten değişince yeniden hesaplanır.
            // Aksi halde admin'in verdiği Approved/Rejected kararı tekrar register çağrısıyla
            // sessizce Pending'e dönebilir ya da Pending kayıt IsIndependentTutor=false göndererek
            // kendini Approved'a yükseltebilirdi.
            var wasIndependent = existingTeacher.IsIndependentTutor;
            existingTeacher.IsIndependentTutor = dto.IsIndependentTutor;
            var becamePendingOnTransition = false;
            if (dto.IsIndependentTutor != wasIndependent)
            {
                existingTeacher.ApprovalStatus = approvalStatus;
                becamePendingOnTransition = approvalStatus == TeacherApprovalStatus.Pending;
            }

            if (becamePendingOnTransition)
            {
                await AddTeacherApplicationSubmittedOutboxAsync(existingTeacher.Id, userId);
            }

            await _context.SaveChangesAsync();
            return new ResponseBaseDto
            {
                Success = true,
                Message = "Öğretmen başarıyla güncellendi..",
                ObjectId = existingTeacher.Id
            };
        }

        // 🔹 Yeni öğretmen kaydını ekle
        var teacher = new Teacher
        {
            UserId = userId,
            SchoolId = dto.SchoolId,
            IsIndependentTutor = dto.IsIndependentTutor,
            ApprovalStatus = approvalStatus
        };

        if (!dto.IsIndependentTutor)
        {
            // Okula bağlı öğretmen: Pending'e düşmüyor, outbox gerekmiyor — tek SaveChanges yeterli.
            _context.Teachers.Add(teacher);
            await _context.SaveChangesAsync();
            return new ResponseBaseDto
            {
                Success = true,
                Message = "Öğretmen başarıyla kaydedildi.",
                ObjectId = teacher.Id
            };
        }

        // Bağımsız öğretmen: Teacher.Id identity ile üretildiği için (SaveChanges'ten önce 0),
        // event içeriği TeacherId'ye ihtiyaç duyar. WorksheetAccessRequestService ile aynı desen:
        // teacher INSERT + outbox INSERT aynı execution-strategy transaction'ında, iki SaveChanges ile.
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            _context.Teachers.Add(teacher);
            await _context.SaveChangesAsync();

            await AddTeacherApplicationSubmittedOutboxAsync(teacher.Id, userId);
            await _context.SaveChangesAsync();

            await tx.CommitAsync();
        });

        return new ResponseBaseDto
        {
            Success = true,
            Message = "Öğretmen başarıyla kaydedildi.",
            ObjectId = teacher.Id
        };
    }

    /// <summary>
    /// Bağımsız öğretmen onay akışı (issue #94): yeni bir Pending başvuru oluştuğunda
    /// (yeni kayıt VEYA IsIndependentTutor=false→true geçişi ile Pending'e düşen kayıt)
    /// <see cref="TeacherApplicationSubmittedEvent"/>'i outbox'a ekler. Çağıranın, teacherId'yi
    /// zaten bilmesi gerekir — yeni kayıt yolunda bu yalnızca ilk SaveChangesAsync (identity insert)
    /// tamamlandıktan sonra mümkündür, bkz. çağrı yeri.
    /// </summary>
    private async Task AddTeacherApplicationSubmittedOutboxAsync(int teacherId, int userId)
    {
        var applicantName = await ResolveApplicantNameAsync(userId);
        _context.OutboxMessages.Add(new OutboxMessage
        {
            Type = OutboxEventRegistry.NameFor<TeacherApplicationSubmittedEvent>(),
            Content = JsonSerializer.Serialize(new TeacherApplicationSubmittedEvent
            {
                TeacherId = teacherId,
                UserId = userId,
                ApplicantName = applicantName,
                SubmittedAt = DateTime.UtcNow
            }),
            CreatedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// UserId'yi isme çevirir (WorksheetAccessRequestService ile aynı best-effort desen).
    /// auth-api erişilemezse null döner — event yine de yazılır, consumer "Bir öğretmen" fallback'i kullanır.
    /// </summary>
    private async Task<string?> ResolveApplicantNameAsync(int userId)
    {
        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(new[] { userId });
            return users.FirstOrDefault(u => u.Id == userId)?.FullName;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    public async Task<UpdateThemeDto> UpdateTeacherTheme(int userId, string themePreset, string? themeCustomConfig)
    {
        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == userId);
        if (teacher == null)
        {
            return new UpdateThemeDto
            {
                Success = false,
                Message = "Öğretmen bulunamadı."
            };
        }

        teacher.ThemePreset = themePreset;
        teacher.ThemeCustomConfig = themeCustomConfig;

        await _context.SaveChangesAsync();

        return new UpdateThemeDto
        {
            Success = true,
            Message = "Theme tercihi güncellendi.",
            ObjectId = teacher.Id,
            ThemePreset = teacher.ThemePreset,
            ThemeCustomConfig = teacher.ThemeCustomConfig
        };
    }

    public async Task<TeacherDashboardSummaryDto> GetDashboardSummaryAsync(int teacherId, CancellationToken ct = default)
    {
        // Sahiplik: sadece CreateUserId == teacherId olan worksheet'ler; paylaşılanlar hariç.
        // Soft-delete edilmiş kayıtlar AppDbContext global query filter ile zaten dışarıda.
        var ownedWorksheets = _context.Worksheets
            .AsNoTracking()
            .Where(w => w.CreateUserId == teacherId);

        var totalWorksheets = await ownedWorksheets.CountAsync(ct);

        if (totalWorksheets == 0)
        {
            return new TeacherDashboardSummaryDto { TotalWorksheets = 0, TotalUniqueStudents = 0 };
        }

        // Sahip olunan worksheet'lerin atamaları; sadece hedefleme alanları projeksiyonlanır.
        var assignmentTargets = await _context.WorksheetAssignments
            .AsNoTracking()
            .Where(wa => ownedWorksheets.Any(w => w.Id == wa.WorksheetId))
            .Select(wa => new { wa.StudentId, wa.GradeId, wa.SchoolId })
            .ToListAsync(ct);

        if (assignmentTargets.Count == 0)
        {
            return new TeacherDashboardSummaryDto { TotalWorksheets = totalWorksheets, TotalUniqueStudents = 0 };
        }

        // WorksheetAssignmentService ile aynı genişletme mantığı:
        //  - StudentId dolu -> direkt öğrenci
        //  - GradeId dolu, StudentId boş -> o sınıftaki tüm öğrenciler (SchoolId doluysa o okulla sınırlı)
        var directStudentIds = assignmentTargets
            .Where(a => a.StudentId.HasValue)
            .Select(a => a.StudentId!.Value)
            .Distinct()
            .ToList();

        var gradeTargets = assignmentTargets
            .Where(a => a.GradeId.HasValue && !a.StudentId.HasValue)
            .Select(a => new { GradeId = a.GradeId!.Value, a.SchoolId })
            .Distinct()
            .ToList();

        var gradeIds = gradeTargets.Select(g => g.GradeId).Distinct().ToList();

        // Direkt StudentId atamaları da Students tablosu üzerinden doğrulanır ki
        // soft-delete edilmiş öğrenciler sınıf-bazlı yolla tutarlı biçimde dışlansın.
        var existingDirectStudentIds = directStudentIds.Count > 0
            ? await _context.Students
                .AsNoTracking()
                .Where(s => directStudentIds.Contains(s.Id))
                .Select(s => s.Id)
                .ToListAsync(ct)
            : new List<int>();

        var targetStudentIds = new HashSet<int>(existingDirectStudentIds);

        if (gradeIds.Count > 0)
        {
            var gradeStudents = await _context.Students
                .AsNoTracking()
                .Where(s => s.GradeId.HasValue && gradeIds.Contains(s.GradeId.Value))
                .Select(s => new { s.Id, GradeId = s.GradeId!.Value, s.SchoolId })
                .ToListAsync(ct);

            foreach (var student in gradeStudents)
            {
                var matches = gradeTargets.Any(g =>
                    g.GradeId == student.GradeId &&
                    (!g.SchoolId.HasValue || g.SchoolId == student.SchoolId));

                if (matches)
                {
                    targetStudentIds.Add(student.Id);
                }
            }
        }

        return new TeacherDashboardSummaryDto
        {
            TotalWorksheets = totalWorksheets,
            TotalUniqueStudents = targetStudentIds.Count
        };
    }

    public async Task<List<TeacherWorksheetOverviewDto>> GetWorksheetsOverviewAsync(int teacherId, CancellationToken ct = default)
    {
        // 1) Sahip olunan worksheet'ler (GetDashboardSummaryAsync ile aynı sahiplik kuralı).
        var ownedWorksheets = await _context.Worksheets
            .AsNoTracking()
            .Where(w => w.CreateUserId == teacherId)
            .OrderBy(w => w.Name)
            .Select(w => new { w.Id, w.Name })
            .ToListAsync(ct);

        if (ownedWorksheets.Count == 0)
        {
            return new List<TeacherWorksheetOverviewDto>();
        }

        var worksheetIds = ownedWorksheets.Select(w => w.Id).ToList();

        // 2) Bu worksheet'lere ait TÜM atamalar tek sorguda (N+1 yok).
        var assignments = await _context.WorksheetAssignments
            .AsNoTracking()
            .Where(wa => worksheetIds.Contains(wa.WorksheetId))
            .Select(wa => new
            {
                wa.WorksheetId,
                wa.StudentId,
                wa.GradeId,
                wa.SchoolId,
                wa.StartAt,
                wa.EndAt
            })
            .ToListAsync(ct);

        // 3) Hedef öğrenciler tek sorguda: direkt atananlar + ilgili sınıflardaki tüm öğrenciler.
        var directStudentIds = assignments
            .Where(a => a.StudentId.HasValue)
            .Select(a => a.StudentId!.Value)
            .Distinct()
            .ToList();

        var gradeIds = assignments
            .Where(a => a.GradeId.HasValue && !a.StudentId.HasValue)
            .Select(a => a.GradeId!.Value)
            .Distinct()
            .ToList();

        var students = (directStudentIds.Count == 0 && gradeIds.Count == 0)
            ? new List<StudentTarget>()
            : await _context.Students
                .AsNoTracking()
                .Where(s => directStudentIds.Contains(s.Id)
                            || (s.GradeId.HasValue && gradeIds.Contains(s.GradeId.Value)))
                .Select(s => new StudentTarget(s.Id, s.GradeId, s.SchoolId))
                .ToListAsync(ct);

        var studentsById = students.ToDictionary(s => s.Id);
        var studentsByGrade = students
            .Where(s => s.GradeId.HasValue)
            .GroupBy(s => s.GradeId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 4) Worksheet bazında hedef öğrenci kümesi + her öğrencinin ilgili atama pencereleri.
        //    Aynı öğrenci bir worksheet'e birden fazla atamayla hedeflenebilir; distinct sayılır.
        var targetsByWorksheet = new Dictionary<int, Dictionary<int, List<AssignmentWindow>>>();

        foreach (var assignment in assignments)
        {
            if (!targetsByWorksheet.TryGetValue(assignment.WorksheetId, out var studentWindows))
            {
                studentWindows = new Dictionary<int, List<AssignmentWindow>>();
                targetsByWorksheet[assignment.WorksheetId] = studentWindows;
            }

            var window = new AssignmentWindow(assignment.StartAt, assignment.EndAt);

            if (assignment.StudentId.HasValue)
            {
                // Direkt atama: öğrenci Students tablosunda mevcutsa (soft-delete dışlanır) hedeftir.
                if (studentsById.ContainsKey(assignment.StudentId.Value))
                {
                    AddWindow(studentWindows, assignment.StudentId.Value, window);
                }
            }
            else if (assignment.GradeId.HasValue
                     && studentsByGrade.TryGetValue(assignment.GradeId.Value, out var gradeStudents))
            {
                foreach (var student in gradeStudents)
                {
                    if (!assignment.SchoolId.HasValue || assignment.SchoolId == student.SchoolId)
                    {
                        AddWindow(studentWindows, student.Id, window);
                    }
                }
            }
        }

        // 5) İlgili test instance'ları tek sorguda (worksheet + hedef öğrenci filtresiyle).
        var allTargetStudentIds = targetsByWorksheet.Values
            .SelectMany(d => d.Keys)
            .Distinct()
            .ToList();

        var instances = allTargetStudentIds.Count == 0
            ? new List<InstanceSnapshot>()
            : await _context.TestInstances
                .AsNoTracking()
                .Where(ti => worksheetIds.Contains(ti.WorksheetId) && allTargetStudentIds.Contains(ti.StudentId))
                .Select(ti => new InstanceSnapshot(ti.WorksheetId, ti.StudentId, ti.StartTime, ti.EndTime, ti.Status))
                .ToListAsync(ct);

        var instancesByWorksheetStudent = instances
            .GroupBy(i => (i.WorksheetId, i.StudentId))
            .ToDictionary(g => g.Key, g => g.ToList());

        // 6) Bellekte hesapla.
        var result = new List<TeacherWorksheetOverviewDto>(ownedWorksheets.Count);

        foreach (var worksheet in ownedWorksheets)
        {
            var assignedCount = 0;
            var completedCount = 0;

            if (targetsByWorksheet.TryGetValue(worksheet.Id, out var studentWindows))
            {
                assignedCount = studentWindows.Count;

                foreach (var (studentId, windows) in studentWindows)
                {
                    instancesByWorksheetStudent.TryGetValue((worksheet.Id, studentId), out var studentInstances);

                    if (IsCompletedInAnyWindow(windows, studentInstances))
                    {
                        completedCount++;
                    }
                }
            }

            result.Add(new TeacherWorksheetOverviewDto
            {
                WorksheetId = worksheet.Id,
                Name = worksheet.Name,
                AssignedStudentCount = assignedCount,
                CompletionPercentage = assignedCount == 0
                    ? 0
                    : Math.Round(completedCount * 100.0 / assignedCount, 2)
            });
        }

        return result;
    }

    public async Task<List<TeacherLaggingStudentDto>> GetLaggingStudentsAsync(int teacherId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // 1) Sahip olunan worksheet'ler (GetWorksheetsOverviewAsync ile aynı sahiplik kuralı).
        var ownedWorksheets = await _context.Worksheets
            .AsNoTracking()
            .Where(w => w.CreateUserId == teacherId)
            .Select(w => new { w.Id, w.Name })
            .ToListAsync(ct);

        if (ownedWorksheets.Count == 0)
        {
            return new List<TeacherLaggingStudentDto>();
        }

        var worksheetNameById = ownedWorksheets.ToDictionary(w => w.Id, w => w.Name);
        var worksheetIds = ownedWorksheets.Select(w => w.Id).ToList();

        // 2) Bu worksheet'lere ait TÜM atamalar tek sorguda (N+1 yok).
        var assignments = await _context.WorksheetAssignments
            .AsNoTracking()
            .Where(wa => worksheetIds.Contains(wa.WorksheetId))
            .Select(wa => new
            {
                wa.WorksheetId,
                wa.StudentId,
                wa.GradeId,
                wa.SchoolId,
                wa.StartAt,
                wa.EndAt
            })
            .ToListAsync(ct);

        if (assignments.Count == 0)
        {
            return new List<TeacherLaggingStudentDto>();
        }

        // 3) Hedef öğrenciler tek sorguda: direkt atananlar + ilgili sınıflardaki tüm öğrenciler.
        var directStudentIds = assignments
            .Where(a => a.StudentId.HasValue)
            .Select(a => a.StudentId!.Value)
            .Distinct()
            .ToList();

        var gradeIds = assignments
            .Where(a => a.GradeId.HasValue && !a.StudentId.HasValue)
            .Select(a => a.GradeId!.Value)
            .Distinct()
            .ToList();

        var students = (directStudentIds.Count == 0 && gradeIds.Count == 0)
            ? new List<LaggingStudentTarget>()
            : await _context.Students
                .AsNoTracking()
                .Where(s => directStudentIds.Contains(s.Id)
                            || (s.GradeId.HasValue && gradeIds.Contains(s.GradeId.Value)))
                .Select(s => new LaggingStudentTarget(s.Id, s.UserId, s.StudentNumber, s.GradeId, s.SchoolId))
                .ToListAsync(ct);

        if (students.Count == 0)
        {
            return new List<TeacherLaggingStudentDto>();
        }

        var studentsById = students.ToDictionary(s => s.Id);
        var studentsByGrade = students
            .Where(s => s.GradeId.HasValue)
            .GroupBy(s => s.GradeId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 4) (worksheet, öğrenci) çifti bazında atama pencereleri (GetWorksheetsOverviewAsync ile aynı genişletme).
        var windowsByPair = new Dictionary<(int WorksheetId, int StudentId), List<AssignmentWindow>>();

        foreach (var assignment in assignments)
        {
            var window = new AssignmentWindow(assignment.StartAt, assignment.EndAt);

            if (assignment.StudentId.HasValue)
            {
                if (studentsById.ContainsKey(assignment.StudentId.Value))
                {
                    AddPairWindow(windowsByPair, assignment.WorksheetId, assignment.StudentId.Value, window);
                }
            }
            else if (assignment.GradeId.HasValue
                     && studentsByGrade.TryGetValue(assignment.GradeId.Value, out var gradeStudents))
            {
                foreach (var student in gradeStudents)
                {
                    if (!assignment.SchoolId.HasValue || assignment.SchoolId == student.SchoolId)
                    {
                        AddPairWindow(windowsByPair, assignment.WorksheetId, student.Id, window);
                    }
                }
            }
        }

        if (windowsByPair.Count == 0)
        {
            return new List<TeacherLaggingStudentDto>();
        }

        // 5) İlgili test instance'ları tek sorguda.
        var targetStudentIds = windowsByPair.Keys.Select(k => k.StudentId).Distinct().ToList();

        var instances = await _context.TestInstances
            .AsNoTracking()
            .Where(ti => worksheetIds.Contains(ti.WorksheetId) && targetStudentIds.Contains(ti.StudentId))
            .Select(ti => new InstanceSnapshot(ti.WorksheetId, ti.StudentId, ti.StartTime, ti.EndTime, ti.Status))
            .ToListAsync(ct);

        var instancesByPair = instances
            .GroupBy(i => (i.WorksheetId, i.StudentId))
            .ToDictionary(g => g.Key, g => g.ToList());

        // 6) Bellekte hesapla: çift başına "en ilgili" atama = başlamış (StartAt <= now) olanlar arasında en son başlayan.
        //    Henüz başlamamış (Scheduled) atamalar geride kalma sayılmaz.
        var laggingRows = new List<(int WorksheetId, LaggingStudentTarget Student, bool IsCompleted, bool IsExpired)>();

        foreach (var ((worksheetId, studentId), windows) in windowsByPair)
        {
            var relevantWindow = windows
                .Where(w => w.StartAt <= now)
                .OrderByDescending(w => w.StartAt)
                .FirstOrDefault();

            if (relevantWindow == null)
            {
                continue;
            }

            instancesByPair.TryGetValue((worksheetId, studentId), out var pairInstances);

            var isCompleted = IsCompletedInWindow(relevantWindow, pairInstances);
            var isExpired = !isCompleted
                            && relevantWindow.EndAt.HasValue
                            && relevantWindow.EndAt.Value < now;

            // CompletionPercentage 0/100 olduğu için IsLowCompletion == !isCompleted; ikisi de false ise satır dahil edilmez.
            if (isCompleted && !isExpired)
            {
                continue;
            }

            laggingRows.Add((worksheetId, studentsById[studentId], isCompleted, isExpired));
        }

        if (laggingRows.Count == 0)
        {
            return new List<TeacherLaggingStudentDto>();
        }

        // 7) Öğrenci adları auth-api'den tek batch çağrıyla; erişilemezse StudentNumber fallback'i.
        var userIds = laggingRows.Select(r => r.Student.UserId).Distinct().ToList();
        var nameByUserId = await ResolveStudentNamesAsync(userIds, ct);

        return laggingRows
            .Select(r =>
            {
                var completionPercentage = r.IsCompleted ? 100d : 0d;

                return new TeacherLaggingStudentDto
                {
                    StudentId = r.Student.Id,
                    StudentName = nameByUserId.TryGetValue(r.Student.UserId, out var fullName)
                        ? fullName
                        : $"Öğrenci #{r.Student.StudentNumber}",
                    WorksheetId = r.WorksheetId,
                    WorksheetName = worksheetNameById[r.WorksheetId],
                    CompletionPercentage = completionPercentage,
                    IsLowCompletion = completionPercentage < 50,
                    IsExpired = r.IsExpired
                };
            })
            .OrderBy(dto => dto.StudentName)
            .ThenBy(dto => dto.WorksheetName)
            .ToList();
    }

    private static void AddPairWindow(
        Dictionary<(int WorksheetId, int StudentId), List<AssignmentWindow>> windowsByPair,
        int worksheetId,
        int studentId,
        AssignmentWindow window)
    {
        var key = (worksheetId, studentId);
        if (!windowsByPair.TryGetValue(key, out var windows))
        {
            windows = new List<AssignmentWindow>();
            windowsByPair[key] = windows;
        }

        windows.Add(window);
    }

    /// <summary>
    /// UserId'leri tek batch çağrıyla isme çevirir (WorksheetCalendarService.ResolveTeacherNamesAsync ile aynı desen).
    /// Auth-api erişilemezse boş sözlük döner — liste yine de dönmeli.
    /// </summary>
    private async Task<Dictionary<int, string>> ResolveStudentNamesAsync(List<int> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return new Dictionary<int, string>();

        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(userIds, ct);
            return users
                .Where(u => !string.IsNullOrWhiteSpace(u.FullName))
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First().FullName);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new Dictionary<int, string>();
        }
    }

    private static void AddWindow(Dictionary<int, List<AssignmentWindow>> studentWindows, int studentId, AssignmentWindow window)
    {
        if (!studentWindows.TryGetValue(studentId, out var windows))
        {
            windows = new List<AssignmentWindow>();
            studentWindows[studentId] = windows;
        }

        windows.Add(window);
    }

    /// <summary>
    /// WorksheetAssignmentService.ResolveStudentAssignmentStatus ile aynı "Completed" kuralı:
    /// atama penceresi içindeki en son instance Completed ise ya da Started olup EndTime dolmuşsa tamamlanmıştır.
    /// Öğrenci birden fazla atamayla hedeflenmişse herhangi birinde tamamlamış olması yeterlidir.
    /// </summary>
    private static bool IsCompletedInAnyWindow(List<AssignmentWindow> windows, List<InstanceSnapshot>? studentInstances)
    {
        if (studentInstances == null || studentInstances.Count == 0)
        {
            return false;
        }

        foreach (var window in windows)
        {
            if (IsCompletedInWindow(window, studentInstances))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tek atama penceresi için "Completed" kuralı: pencere içindeki en son instance
    /// Completed ise ya da Started olup EndTime dolmuşsa tamamlanmıştır.
    /// </summary>
    private static bool IsCompletedInWindow(AssignmentWindow window, List<InstanceSnapshot>? studentInstances)
    {
        if (studentInstances == null || studentInstances.Count == 0)
        {
            return false;
        }

        var relevantInstance = studentInstances
            .Where(ti => ti.StartTime >= window.StartAt
                         && (!window.EndAt.HasValue || ti.StartTime <= window.EndAt.Value))
            .OrderByDescending(ti => ti.StartTime)
            .FirstOrDefault();

        if (relevantInstance == null)
        {
            return false;
        }

        return relevantInstance.Status switch
        {
            WorksheetInstanceStatus.Completed => true,
            WorksheetInstanceStatus.Started => relevantInstance.EndTime.HasValue,
            _ => false
        };
    }

    private sealed record StudentTarget(int Id, int? GradeId, int? SchoolId);

    private sealed record LaggingStudentTarget(int Id, int UserId, string StudentNumber, int? GradeId, int? SchoolId);

    private sealed record AssignmentWindow(DateTime StartAt, DateTime? EndAt);

    private sealed record InstanceSnapshot(int WorksheetId, int StudentId, DateTime StartTime, DateTime? EndTime, WorksheetInstanceStatus Status);

}
