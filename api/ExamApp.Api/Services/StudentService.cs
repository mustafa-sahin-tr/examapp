using System;
using System.Collections.Generic;
using System.Linq;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Services;

public class StudentService : IStudentService
{
    /// <summary>Lookup listesi üst sınırı — sınırsız liste dönülmez (issue #190).</summary>
    public const int LookupMaxTake = 500;

    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApiClient;

    // issue #190: okul izolasyonu kararı burada değil, merkezi policy'de verilir.
    private readonly ISchoolAccessPolicy _schoolAccessPolicy;

    // Client'a ulaşan ResponseBaseDto.Message metinleri buradan gelir (issue #184).
    // DI her zaman gerçek localizer'ı verir; parametre yalnızca DI'sız kurulan (birim test)
    // senaryolarda varsayılan dile düşebilmek için opsiyonel.
    private readonly IStringLocalizer<Messages> _localizer;

    public StudentService(
        AppDbContext context,
        IAuthApiClient authApiClient,
        ISchoolAccessPolicy schoolAccessPolicy,
        IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _authApiClient = authApiClient;
        _schoolAccessPolicy = schoolAccessPolicy;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }
    public async Task<List<GradeDto>> GetGradesAsync(CancellationToken ct = default)
    {
        return await _context.Grades
            .AsNoTracking()
            .OrderBy(g => g.Id)
            .Select(g => new GradeDto { Id = g.Id, Name = g.Name })
            .ToListAsync(ct);
    }
    public async Task<StudentProfileDto> GetStudentProfile(int userId)
    {
        var student = await _context.Students
            .Where(s => s.UserId == userId)
            .Select(s => new StudentProfileDto
            {
                Id = s.Id,
                // FullName = s.User.FullName,
                // AvatarUrl = s.User.AvatarUrl,
                GradeId = s.GradeId,
                SchoolName = s.SchoolName,
                SchoolId = s.SchoolId,
                XP = s.StudentPoints.Sum(sp => sp.XP),
                Level = s.StudentPoints.OrderByDescending(sp => sp.LastUpdated).Select(sp => sp.Level).FirstOrDefault(), // 🟢 En son seviye
                TotalQuestionsSolved = _context.StudentPointHistories.Count(p => p.StudentId == s.Id),
                CorrectAnswers = _context.StudentPointHistories.Count(p => p.StudentId == s.Id && p.Reason == "Doğru Cevap"),
                WrongAnswers = _context.StudentPointHistories.Count(p => p.StudentId == s.Id && p.Reason == "Yanlış Cevap"),
                TestsCompleted = _context.TestInstances.Count(t => t.StudentId == s.Id),
                TotalRewards = _context.StudentRewards.Count(r => r.StudentId == s.Id),
                Badges = _context.StudentBadges
                    .Where(sb => sb.StudentId == s.Id)
                    .ToList(),
                LeaderboardRank = _context.Leaderboards
                    .Where(lb => lb.StudentId == s.Id)
                    .OrderByDescending(lb => lb.RecordedAt)
                    .Select(lb => lb.Rank)
                    .FirstOrDefault(),
                RecentTests = _context.TestInstances
                    .Where(ti => ti.StudentId == s.Id)
                    .OrderByDescending(ti => ti.StartTime)
                    .Take(5)
                    .Select(ti => new StudentWorkSheetSummaryDto
                    {
                        Id = ti.Id,
                        Name = ti.Worksheet.Name,
                        StartTime = ti.StartTime,
                        Score = ti.WorksheetInstanceQuestions.Count(tiq => tiq.IsCorrect) * 10, // 10 puan üzerinden hesaplama
                        TotalQuestions = ti.WorksheetInstanceQuestions.Count()
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        return student;

    }

    public async Task<ResponseBaseDto> Save(int userId, RegisterStudentDto dto)
    {
        if (dto.SchoolId.HasValue &&
            !await _context.Schools.AnyAsync(s => s.Id == dto.SchoolId.Value))
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = _localizer["student.schoolNotFound"]
            };
        }

        // issue #234 (security): öğretmen ve öğrenci kaydı birbirini dışlar. Öğretmen kaydı olan kullanıcı
        // student/register ile kendine Students.SchoolId yazıp (önbellekte Role=Student) başka bir okulun
        // kapsamına giremez.
        if (await _context.Teachers.AnyAsync(t => t.UserId == userId))
        {
            return new ResponseBaseDto
            {
                Success = false,
                Conflict = true,
                Message = _localizer["student.teacherRecordExists"]
            };
        }

        // Canlı satır tektir (#259 filtreli unique index); OrderBy yalnız deterministiklik için.
        var student = await _context.Students.OrderBy(s => s.Id).FirstOrDefaultAsync(s => s.UserId == userId);
        if (student != null)
        {
            // issue #259 (security): okul kilidi. Okul bir kez atandıktan sonra bu uçla DEĞİŞTİRİLEMEZ — aksi halde
            // öğrenci register'ı tekrar çağırıp istediği okulun sınıf atamalarını görüp başlatabilirdi. Okulu
            // boşaltmak (null) da değişikliktir: X → null → Y ile kilit aşılamasın. Okulsuz kayıtta ilk atama serbest;
            // aynı okul idempotent (diğer alanlar güncellenir).
            if (student.SchoolId.HasValue && dto.SchoolId != student.SchoolId)
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Conflict = true,
                    Message = _localizer["student.schoolLocked"]
                };
            }

            // issue #259 (security review): okulsuz kayda İLK okul ataması koşullu UPDATE ile yapılır — yukarıdaki okuma
            // ile yazma arasında eşzamanlı bir istek başka bir okul yazdıysa ("son yazan kazanır") kilit aşılmasın.
            // SchoolId tracked entity üzerinden YAZILMAZ; yalnız bu atomik ifade yazar.
            if (!student.SchoolId.HasValue && dto.SchoolId.HasValue)
            {
                var requestedSchoolId = dto.SchoolId.Value;
                var claimed = await _context.Students
                    .Where(s => s.Id == student.Id && s.SchoolId == null)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.SchoolId, requestedSchoolId));

                if (claimed == 0)
                {
                    var currentSchoolId = await _context.Students.AsNoTracking()
                        .Where(s => s.Id == student.Id)
                        .Select(s => s.SchoolId)
                        .FirstOrDefaultAsync();
                    if (currentSchoolId != requestedSchoolId)
                    {
                        return new ResponseBaseDto
                        {
                            Success = false,
                            Conflict = true,
                            Message = _localizer["student.schoolLocked"]
                        };
                    }
                }

                // Tracked kopyayı DB ile hizala ama değiştirilmiş işaretleme — SaveChanges SchoolId'yi tekrar yazmasın.
                var schoolEntry = _context.Entry(student).Property(s => s.SchoolId);
                schoolEntry.CurrentValue = requestedSchoolId;
                schoolEntry.OriginalValue = requestedSchoolId;
                schoolEntry.IsModified = false;
            }

            student.StudentNumber = dto.StudentNumber;
            student.GradeId = dto.GradeId;
        }
        else
        {
            // 🔹 Yeni öğrenci kaydını ekle
            student = new Student
            {
                UserId = userId,
                StudentNumber = dto.StudentNumber,
                SchoolId = dto.SchoolId,
                GradeId = dto.GradeId
            };
            _context.Students.Add(student);
        }

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
        {
            // issue #259: eşzamanlı ilk kayıt — diğer istek aynı kullanıcı için canlı satırı önce yazdı. İkinci satır
            // açılmaz (kilit çift satırla aşılamaz); istemci tekrar denerse mevcut satır üzerinden kilit kuralı işler.
            return new ResponseBaseDto
            {
                Success = false,
                Conflict = true,
                Message = _localizer["student.registrationConflict"]
            };
        }

        return new ResponseBaseDto
        {
            Success = true,
            Message = _localizer["student.registered"],
            ObjectId = student.Id
        };
    }

    public async Task<ResponseBaseDto> UpdateStudentGrade(int userId, int gradeId)
    {
        var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == userId);
        if (student == null)
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = _localizer["student.notFound"]
            };
        }

        student.GradeId = gradeId;
        await _context.SaveChangesAsync();
        return new ResponseBaseDto
        {
            Success = true,
            Message = _localizer["student.gradeUpdated"]
        };

    }

    // public async Task<Dictionary<DateTime, int>> GetStudentActivityHeatmap(int studentId)
    // {
    //     var activityData = await _context.StudentActivities
    //         .Where(sa => sa.StudentId == studentId)
    //         .GroupBy(sa => sa.ActivityDate.Date)
    //         .Select(g => new
    //         {
    //             Date = g.Key,
    //             ActivityCount = g.Count()
    //         })
    //         .ToDictionaryAsync(x => x.Date, x => x.ActivityCount);

    //     return activityData;
    // }

    public async Task<List<StudentLookupDto>> GetStudentLookupsAsync(SchoolScope requester, CancellationToken ct = default)
    {
        // issue #190: okul filtresi projeksiyondan/sıralamadan ÖNCE, SQL düzeyinde uygulanır.
        // Soft-delete edilmiş öğrenciler AppDbContext global query filter (!IsDeleted) ile dışarıda.
        var scoped = _schoolAccessPolicy.ApplyScope(_context.Students.AsNoTracking(), requester);

        // Sınırsız liste dönülmez (admin/servis tüm okulları görür); sıralama deterministik.
        var students = await scoped
            .OrderBy(s => s.StudentNumber)
            .ThenBy(s => s.Id)
            .Take(LookupMaxTake)
            .Select(s => new StudentLookupDto
            {
                Id = s.Id,
                UserId = s.UserId,
                StudentNumber = s.StudentNumber,
                SchoolName = s.SchoolName,
                SchoolId = s.SchoolId,
                GradeId = s.GradeId
            })
            .ToListAsync(ct);

        if (students.Count == 0)
        {
            return students;
        }

        var userIds = students.Select(s => s.UserId).Distinct().ToList();

        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(userIds, ct);
            var userLookup = users.ToDictionary(u => u.Id);

            foreach (var student in students)
            {
                if (userLookup.TryGetValue(student.UserId, out var user))
                {
                    student.FullName = user.FullName ?? string.Empty;
                    student.Email = user.Email ?? string.Empty;
                    student.AvatarUrl = user.Avatar ?? string.Empty;
                }
            }
        }
        catch
        {
            // Auth API erişilemezse mevcut listeyi base bilgilerle döndür.
        }

        return students;
    }

    public async Task<UpdateThemeDto> UpdateStudentTheme(int userId, string themePreset, string? themeCustomConfig)
    {
        var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == userId);
        if (student == null)
        {
            return new UpdateThemeDto
            {
                Success = false,
                Message = _localizer["student.notFound"]
            };
        }

        student.ThemePreset = themePreset;
        student.ThemeCustomConfig = themeCustomConfig;

        await _context.SaveChangesAsync();

        return new UpdateThemeDto
        {
            Success = true,
            Message = _localizer["student.theme.updated"],
            ThemePreset = student.ThemePreset,
            ThemeCustomConfig = student.ThemeCustomConfig
        };
    }
}
