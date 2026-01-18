using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Layout;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace ExamApp.Api.Services;

public class ExamService : IExamService
{
    private readonly AppDbContext _context;

    private readonly ImageHelper _imageHelper;
    private readonly IMinIoService _minioService;
    public ExamService(AppDbContext context, ImageHelper imageHelper, IMinIoService minioService)
    {
        _imageHelper = imageHelper;
        _context = context;
        _minioService = minioService;
    }

    public async Task<Paged<WorksheetDto>> GetWorksheetsForTeacherAsync(ExamFilterDto dto, UserProfileDto userProfile)
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
        // Her worksheet için kaç benzersiz öğrenci instance oluşturmuş

        var totalCount = await query.CountAsync(); // Toplam kayıt sayısı
        var tests = await query
            .Include(t => t.BookTest)
                .ThenInclude(bt => bt.Book)
            .Include(t => t.WorksheetQuestions)
                .ThenInclude(tq => tq.Question)
            .OrderBy(t => t.Name) // Sıralama için
            .Skip((dto.pageNumber - 1) * dto.pageSize) // Sayfalama için
            .Take(dto.pageSize)
            .ToListAsync();

        var worksheetDtos = tests.Select(t =>
        {
            return new WorksheetDto
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
                QuestionCount = t.WorksheetQuestions.Count()
            };
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
        var tests = await query
            .Include(t => t.BookTest)
                .ThenInclude(bt => bt.Book)
            .Include(t => t.WorksheetQuestions)
                .ThenInclude(tq => tq.Question)
            .OrderBy(t => t.Name) // Sıralama için
            .Skip((dto.pageNumber - 1) * dto.pageSize) // Sayfalama için
            .Take(dto.pageSize)
            .ToListAsync();

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

            return new WorksheetDto
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
                InstanceCount = worksheetStudentCounts.TryGetValue(t.Id, out var count) ? count : 0 // 💡 Yeni eklenen alan
            };
        }).ToList();

        return new Paged<WorksheetDto>
        {
            PageNumber = dto.pageNumber,
            PageSize = dto.pageSize,
            TotalCount = totalCount,
            Items = worksheetDtos
        };
    }

    public async Task<Paged<InstanceSummaryDto>> GetCompletedTestsAsync(StudentProfileDto student, int pageNumber, int pageSize)
    {
        var query = await _context.TestInstances
            .Where(wi => wi.StudentId == student.Id && wi.Status == WorksheetInstanceStatus.Completed)
            .Select(wi => new
            {
                wi.Id,
                wi.Worksheet.Name,
                wi.Worksheet.ImageUrl,
                wi.EndTime,
                wi.StartTime,
                TotalQuestions = wi.WorksheetInstanceQuestions.Count(),

                CorrectAnswers = wi.WorksheetInstanceQuestions.Count(wiq =>
                    wiq.SelectedAnswerId != null &&
                    wi.Worksheet.WorksheetQuestions.Any(wq =>
                        wq.Id == wiq.WorksheetQuestionId &&
                        wq.Question.CorrectAnswerId == wiq.SelectedAnswerId)),

                WrongAnswers = wi.WorksheetInstanceQuestions.Count(wiq =>
                    wiq.SelectedAnswerId != null &&
                    wi.Worksheet.WorksheetQuestions.Any(wq =>
                        wq.Id == wiq.WorksheetQuestionId &&
                        wq.Question.CorrectAnswerId != wiq.SelectedAnswerId))
            })
            .ToListAsync();

        var results = query.Select(wi => new InstanceSummaryDto
        {
            Id = wi.Id,
            Name = wi.Name,
            ImageUrl = wi.ImageUrl,
            CompletedDate = wi.EndTime ?? DateTime.UtcNow,
            DurationMinutes = wi.EndTime.HasValue ?
                (int)(wi.EndTime.Value - wi.StartTime).TotalMinutes : 0,
            TotalQuestions = wi.TotalQuestions,
            CorrectAnswers = wi.CorrectAnswers,
            WrongAnswers = wi.WrongAnswers,
            Score = (wi.CorrectAnswers * 100) / (wi.TotalQuestions > 0 ? wi.TotalQuestions : 1)
        })
        .OrderByDescending(wi => wi.CompletedDate)
        .Skip((pageNumber - 1) * pageSize)
        .Take(pageSize)
        .ToList();

        return new Paged<InstanceSummaryDto>
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = query.Count,
            Items = results
        };
    }

    public async Task<List<WorksheetDto>> GetLatestWorksheetsAsync(int pageNumber, int pageSize)
    {
        return await _context.Worksheets
            .OrderByDescending(t => t.CreateTime)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new WorksheetDto
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
                BookId = t.BookTest != null ? t.BookTest.BookId : null,
                QuestionCount = t.WorksheetQuestions.Count()
            })
            .ToListAsync();
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

    public async Task<WorksheetDto?> GetWorksheetByIdAsync(int id)
    {
        var worksheet = await _context.Worksheets
            .Include(t => t.BookTest)
                .ThenInclude(bt => bt.Book)
            .Include(t => t.WorksheetQuestions)
                .ThenInclude(tq => tq.Question)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (worksheet == null)
            return null;

        return new WorksheetDto
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
            QuestionCount = worksheet.WorksheetQuestions.Count()
        };
    }

    public async Task<List<WorksheetWithInstanceDto>> GetWorksheetAndInstancesAsync(StudentProfileDto student, int gradeId)
    {
        var worksheets = await _context.Worksheets
            .Include(w => w.WorksheetQuestions)
            .Include(w => w.BookTest)
            .Where(w => w.GradeId == gradeId)
            .ToListAsync();

        var worksheetIds = worksheets.Select(w => w.Id).ToList();

        var instances = await _context.TestInstances
            .Where(i => worksheetIds.Contains(i.WorksheetId) && i.StudentId == student.Id)
            .ToListAsync();

        var result = worksheets.Select(w => new WorksheetWithInstanceDto
        {
            Worksheet = new WorksheetDto
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
            },
            Instance = instances.FirstOrDefault(i => i.WorksheetId == w.Id)
        }).ToList();

        return result;
    }

    public async Task<ResponseBaseDto> AssignWorksheetAsync(WorksheetAssignmentRequestDto request, int userId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.StudentId.HasValue && !request.GradeId.HasValue)
        {
            return new ResponseBaseDto { Success = false, Message = "En az bir hedef (öğrenci veya sınıf) seçilmelidir." };
        }

        if (request.StudentId.HasValue && request.GradeId.HasValue)
        {
            return new ResponseBaseDto { Success = false, Message = "Aynı atamada öğrenci ve sınıf birlikte seçilemez." };
        }

        var startAtUtc = NormalizeToUtc(request.StartAt);
        DateTime? endAtUtc = request.EndAt.HasValue ? NormalizeToUtc(request.EndAt.Value) : null;

        if (endAtUtc.HasValue && endAtUtc <= startAtUtc)
        {
            return new ResponseBaseDto { Success = false, Message = "Bitiş zamanı başlangıç zamanından sonra olmalıdır." };
        }

        var worksheet = await _context.Worksheets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.WorksheetId);

        if (worksheet == null)
        {
            return new ResponseBaseDto { Success = false, Message = "Worksheet bulunamadı." };
        }

        Student? student = null;
        if (request.StudentId.HasValue)
        {
            student = await _context.Students
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.StudentId.Value);

            if (student == null)
            {
                return new ResponseBaseDto { Success = false, Message = "Öğrenci bulunamadı." };
            }
        }

        Grade? grade = null;
        if (request.GradeId.HasValue)
        {
            grade = await _context.Grades
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == request.GradeId.Value);

            if (grade == null)
            {
                return new ResponseBaseDto { Success = false, Message = "Sınıf bulunamadı." };
            }
        }

        var overlapQuery = _context.WorksheetAssignments.AsQueryable();
        overlapQuery = overlapQuery.Where(wa => wa.WorksheetId == request.WorksheetId);

        if (student != null)
        {
            overlapQuery = overlapQuery.Where(wa => wa.StudentId == student.Id);
        }
        else if (grade != null)
        {
            overlapQuery = overlapQuery.Where(wa => wa.GradeId == grade.Id);
        }

        overlapQuery = overlapQuery.Where(wa => wa.StartAt < (endAtUtc ?? DateTime.MaxValue)
            && (wa.EndAt == null || wa.EndAt > startAtUtc));

        var hasOverlap = await overlapQuery.AnyAsync();
        if (hasOverlap)
        {
            return new ResponseBaseDto { Success = false, Message = "Seçilen aralıkta mevcut bir atama bulunuyor." };
        }

        _context.SetCurrentUser(userId);

        var assignment = new WorksheetAssignment
        {
            WorksheetId = worksheet.Id,
            StudentId = student?.Id,
            GradeId = grade?.Id,
            StartAt = startAtUtc,
            EndAt = endAtUtc
        };

        _context.WorksheetAssignments.Add(assignment);
        await _context.SaveChangesAsync();

        return new ResponseBaseDto
        {
            Success = true,
            Message = "Sınav başarıyla atandı.",
            ObjectId = assignment.Id
        };
    }

    public async Task<List<AssignedWorksheetDto>> GetActiveAssignmentsForStudentAsync(StudentProfileDto student)
    {
        ArgumentNullException.ThrowIfNull(student);

        var now = DateTime.UtcNow;
        var gradeId = student.GradeId;

        var assignmentsQuery = _context.WorksheetAssignments
            .Include(wa => wa.Worksheet)
                .ThenInclude(w => w.WorksheetQuestions)
            .Include(wa => wa.Worksheet)
                .ThenInclude(w => w.BookTest)
            .Where(wa => wa.StartAt <= now && (wa.EndAt == null || wa.EndAt > now));

        assignmentsQuery = assignmentsQuery.Where(wa =>
            (wa.StudentId.HasValue && wa.StudentId == student.Id) ||
            (gradeId.HasValue && wa.GradeId.HasValue && wa.GradeId == gradeId));

        var assignments = await assignmentsQuery
            .OrderBy(wa => wa.StartAt)
            .ToListAsync();

        if (!assignments.Any())
        {
            return new List<AssignedWorksheetDto>();
        }

        var worksheetIds = assignments
            .Select(wa => wa.WorksheetId)
            .Distinct()
            .ToList();

        var studentInstances = await _context.TestInstances
            .Where(ti => ti.StudentId == student.Id && worksheetIds.Contains(ti.WorksheetId))
            .OrderByDescending(ti => ti.StartTime)
            .ToListAsync();

        var latestInstances = studentInstances
            .GroupBy(ti => ti.WorksheetId)
            .ToDictionary(g => g.Key, g => g.First());

        var result = assignments
            .Select(wa =>
            {
                latestInstances.TryGetValue(wa.WorksheetId, out var instance);
                var worksheet = wa.Worksheet;
                var questionCount = worksheet?.WorksheetQuestions?.Count ?? 0;
                var bookId = worksheet?.BookTestId.HasValue == true
                    ? worksheet?.BookTest?.BookId
                    : null;

                return new AssignedWorksheetDto
                {
                    AssignmentId = wa.Id,
                    WorksheetId = wa.WorksheetId,
                    Name = worksheet?.Name ?? string.Empty,
                    Description = worksheet?.Description ?? string.Empty,
                    GradeId = worksheet?.GradeId ?? 0,
                    SubjectId = worksheet?.SubjectId,
                    TopicId = worksheet?.TopicId,
                    SubTopicId = worksheet?.SubTopicId,
                    MaxDurationSeconds = worksheet?.MaxDurationSeconds ?? 0,
                    IsPracticeTest = worksheet?.IsPracticeTest ?? false,
                    Subtitle = worksheet?.Subtitle,
                    ImageUrl = worksheet?.ImageUrl,
                    BadgeText = worksheet?.BadgeText,
                    BookTestId = worksheet?.BookTestId,
                    BookId = bookId,
                    QuestionCount = questionCount,
                    StartAt = wa.StartAt,
                    EndAt = wa.EndAt,
                    IsGradeAssignment = wa.GradeId.HasValue && !wa.StudentId.HasValue,
                    AssignedGradeId = wa.GradeId,
                    InstanceId = instance?.Id,
                    InstanceStatus = instance?.Status,
                    InstanceStartTime = instance?.StartTime,
                    InstanceEndTime = instance?.EndTime,
                    AssignmentStatus = ResolveAssignmentStatus(instance)
                };
            })
            .ToList();

        return result;
    }

    public async Task<TeacherWorksheetAssignmentsDto> GetWorksheetAssignmentsForTeacherAsync(int worksheetId, int teacherUserId)
    {
        var worksheet = await _context.Worksheets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == worksheetId);

        if (worksheet == null)
        {
            return new TeacherWorksheetAssignmentsDto
            {
                WorksheetId = worksheetId,
                WorksheetName = string.Empty
            };
        }

        var assignments = await _context.WorksheetAssignments
            .AsNoTracking()
            .Include(wa => wa.Grade)
            .Include(wa => wa.Student)
            .Where(wa => wa.WorksheetId == worksheetId && wa.CreateUserId == teacherUserId)
            .OrderByDescending(wa => wa.StartAt)
            .ToListAsync();

        if (!assignments.Any())
        {
            return new TeacherWorksheetAssignmentsDto
            {
                WorksheetId = worksheetId,
                WorksheetName = worksheet.Name
            };
        }

        var now = DateTime.UtcNow;

        var gradeIds = assignments
            .Where(wa => wa.GradeId.HasValue)
            .Select(wa => wa.GradeId!.Value)
            .Distinct()
            .ToList();

        var directStudentIds = assignments
            .Where(wa => wa.StudentId.HasValue)
            .Select(wa => wa.StudentId!.Value)
            .Distinct()
            .ToList();

        var studentsQuery = _context.Students
            .AsNoTracking()
            .Where(s => directStudentIds.Contains(s.Id) || (s.GradeId.HasValue && gradeIds.Contains(s.GradeId.Value)));

        var students = await studentsQuery.ToListAsync();
        var studentsById = students.ToDictionary(s => s.Id);

        var gradeMap = gradeIds.Any()
            ? await _context.Grades
                .AsNoTracking()
                .Where(g => gradeIds.Contains(g.Id))
                .ToDictionaryAsync(g => g.Id, g => g.Name)
            : new Dictionary<int, string>();

        var targetStudentIds = new HashSet<int>(students.Select(s => s.Id));
        foreach (var studentId in directStudentIds)
        {
            targetStudentIds.Add(studentId);
        }

        var instances = targetStudentIds.Count == 0
            ? new List<WorksheetInstance>()
            : await _context.TestInstances
                .AsNoTracking()
                .Where(ti => ti.WorksheetId == worksheetId && targetStudentIds.Contains(ti.StudentId))
                .ToListAsync();

        var assignmentDtos = new List<TeacherWorksheetAssignmentDto>();
        var summary = new AssignmentProgressSummaryDto();

        foreach (var assignment in assignments)
        {
            var targetStudents = assignment.StudentId.HasValue
                ? studentsById.TryGetValue(assignment.StudentId.Value, out var singleStudent)
                    ? new List<Student> { singleStudent }
                    : new List<Student>()
                : students.Where(s => s.GradeId.HasValue && assignment.GradeId.HasValue && s.GradeId.Value == assignment.GradeId.Value).ToList();

            var studentDtos = new List<TeacherAssignmentStudentDto>();

            var completedCount = 0;
            var inProgressCount = 0;
            var notStartedCount = 0;
            var scheduledCount = 0;
            var expiredCount = 0;

            foreach (var student in targetStudents)
            {
                var relevantInstance = instances
                    .Where(ti => ti.StudentId == student.Id && ti.StartTime >= assignment.StartAt
                        && (!assignment.EndAt.HasValue || ti.StartTime <= assignment.EndAt.Value))
                    .OrderByDescending(ti => ti.StartTime)
                    .FirstOrDefault();

                var status = ResolveStudentAssignmentStatus(assignment, relevantInstance, now);

                switch (status)
                {
                    case AssignmentStudentStatuses.Completed:
                        completedCount++;
                        break;
                    case AssignmentStudentStatuses.InProgress:
                        inProgressCount++;
                        break;
                    case AssignmentStudentStatuses.Scheduled:
                        scheduledCount++;
                        break;
                    case AssignmentStudentStatuses.Expired:
                        expiredCount++;
                        break;
                    default:
                        notStartedCount++;
                        break;
                }

                studentDtos.Add(new TeacherAssignmentStudentDto
                {
                    StudentId = student.Id,
                    UserId = student.UserId,
                    StudentNumber = student.StudentNumber,
                    GradeId = student.GradeId,
                    GradeName = student.GradeId.HasValue && gradeMap.TryGetValue(student.GradeId.Value, out var gradeName)
                        ? gradeName
                        : null,
                    Status = status,
                    InstanceId = relevantInstance?.Id,
                    LastActivity = relevantInstance?.EndTime ?? relevantInstance?.StartTime
                });
            }

            var isActive = assignment.StartAt <= now && (!assignment.EndAt.HasValue || assignment.EndAt.Value > now);

            var targetName = assignment.StudentId.HasValue
                ? studentDtos.FirstOrDefault()?.StudentNumber is { Length: > 0 } studentNumber
                    ? $"Öğrenci #{studentNumber}"
                    : $"Öğrenci {assignment.StudentId}"
                : assignment.GradeId.HasValue && gradeMap.TryGetValue(assignment.GradeId.Value, out var resolvedGradeName)
                    ? resolvedGradeName
                    : assignment.Grade?.Name ?? "Tanımlı Sınıf";

            var assignmentDto = new TeacherWorksheetAssignmentDto
            {
                AssignmentId = assignment.Id,
                TargetType = assignment.StudentId.HasValue ? "Student" : "Grade",
                TargetName = targetName,
                IsActive = isActive,
                StartAt = assignment.StartAt,
                EndAt = assignment.EndAt,
                StudentCount = studentDtos.Count,
                CompletedCount = completedCount,
                InProgressCount = inProgressCount,
                NotStartedCount = notStartedCount,
                ScheduledCount = scheduledCount,
                ExpiredCount = expiredCount,
                Students = studentDtos
            };

            assignmentDtos.Add(assignmentDto);

            summary.TotalAssignments++;
            summary.TotalStudents += assignmentDto.StudentCount;
            summary.CompletedCount += completedCount;
            summary.InProgressCount += inProgressCount;
            summary.NotStartedCount += notStartedCount;
            summary.ScheduledCount += scheduledCount;
            summary.ExpiredCount += expiredCount;

            if (assignmentDto.IsActive)
            {
                summary.ActiveAssignments++;
            }

            if (assignment.StartAt > now)
            {
                summary.UpcomingAssignments++;
            }
        }

        return new TeacherWorksheetAssignmentsDto
        {
            WorksheetId = worksheetId,
            WorksheetName = worksheet.Name,
            Summary = summary,
            Assignments = assignmentDtos
        };
    }

    private static DateTime NormalizeToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static string ResolveAssignmentStatus(WorksheetInstance? instance)
    {
        if (instance == null)
        {
            return "NotStarted";
        }

        return instance.Status switch
        {
            WorksheetInstanceStatus.Completed => "Completed",
            WorksheetInstanceStatus.Expired => "Expired",
            WorksheetInstanceStatus.Started => instance.EndTime.HasValue ? "Completed" : "InProgress",
            _ => instance.Status.ToString()
        };
    }

    private static string ResolveStudentAssignmentStatus(WorksheetAssignment assignment, WorksheetInstance? instance, DateTime now)
    {
        if (instance == null)
        {
            if (assignment.StartAt > now)
            {
                return AssignmentStudentStatuses.Scheduled;
            }

            if (assignment.EndAt.HasValue && assignment.EndAt.Value < now)
            {
                return AssignmentStudentStatuses.Expired;
            }

            return AssignmentStudentStatuses.NotStarted;
        }

        return instance.Status switch
        {
            WorksheetInstanceStatus.Completed => AssignmentStudentStatuses.Completed,
            WorksheetInstanceStatus.Expired => AssignmentStudentStatuses.Expired,
            WorksheetInstanceStatus.Started => instance.EndTime.HasValue
                ? AssignmentStudentStatuses.Completed
                : AssignmentStudentStatuses.InProgress,
            _ => AssignmentStudentStatuses.NotStarted
        };
    }

    public async Task<TestStartResultDto> StartTestAsync(int testId, StudentProfileDto student)
    {
        var existing = await _context.TestInstances
            .FirstOrDefaultAsync(ti => ti.StudentId == student.Id && ti.WorksheetId == testId
                && ti.EndTime == null);


        if (existing != null)
        {
            if (existing.Status == WorksheetInstanceStatus.Completed)
            {
                return new TestStartResultDto
                {
                    Success = false,
                    Message = "Bu test zaten tamamlanmış.",
                    InstanceId = existing.Id,
                    StartTime = existing.StartTime
                };
            }

            return new TestStartResultDto
            {
                Success = true,
                InstanceId = existing.Id,
                StartTime = existing.StartTime
            };
        }

        var instance = new WorksheetInstance
        {
            WorksheetId = testId,
            StudentId = student.Id,
            Status = WorksheetInstanceStatus.Started,
            WorksheetInstanceQuestions = new List<WorksheetInstanceQuestion>(),
            StartTime = DateTime.UtcNow
        };

        // Teste ait soruları TestQuestion tablosundan çekiyoruz
        var testQuestions = await _context.TestQuestions
            .Where(tq => tq.TestId == testId)
            .Include(tq => tq.Question)
                .ThenInclude(q => q.QuestionSubTopics)
            .OrderBy(tq => tq.Order)
            .ToListAsync();

        foreach (var tq in testQuestions)
        {
            instance.WorksheetInstanceQuestions.Add(new WorksheetInstanceQuestion
            {
                WorksheetQuestionId = tq.Id,
                IsCorrect = false,
                TimeTaken = 0
            });
        }

        _context.TestInstances.Add(instance);
        await _context.SaveChangesAsync(); // burada audit çalışır

        return new TestStartResultDto
        {
            Success = true,
            InstanceId = instance.Id,
            StartTime = instance.StartTime
        };
    }

    public async Task<WorksheetInstanceDto?> GetTestInstanceQuestionsAsync(int testInstanceId, int userId)
    {
        var instance = await _context.TestInstances
            .Include(ti => ti.Worksheet)
            .Include(ti => ti.WorksheetInstanceQuestions)
                .ThenInclude(tiq => tiq.WorksheetQuestion)
                .ThenInclude(wq => wq.Question)
                    .ThenInclude(q => q.Answers)
            .Include(ti => ti.WorksheetInstanceQuestions)
                .ThenInclude(tiq => tiq.WorksheetQuestion)
                .ThenInclude(wq => wq.Question)
                    .ThenInclude(q => q.Passage)
            .FirstOrDefaultAsync(ti => ti.Id == testInstanceId && ti.Student.UserId == userId);

        if (instance == null)
            return null;

        var worksheetInstanceDto = new WorksheetInstanceDto
        {
            Id = instance.Id,
            TestName = instance.Worksheet.Name,
            Status = instance.Status,
            MaxDurationSeconds = instance.Worksheet.MaxDurationSeconds,
            IsPracticeTest = instance.Worksheet.IsPracticeTest,
            TestInstanceQuestions = instance.WorksheetInstanceQuestions.Select(tiq => new WorksheetInstanceQuestionDto
            {
                Id = tiq.Id,
                Order = tiq.WorksheetQuestion.Order,
                SelectedAnswerId = tiq.SelectedAnswerId,
                Question = new QuestionDto
                {
                    Id = tiq.WorksheetQuestion.Question.Id,
                    Text = tiq.WorksheetQuestion?.Question?.Text ?? string.Empty,
                    SubText = tiq.WorksheetQuestion?.Question?.SubText,
                    ImageUrl = tiq.WorksheetQuestion?.Question?.ImageUrl,
                    IsExample = tiq.WorksheetQuestion?.Question?.IsExample ?? false,
                    PracticeCorrectAnswer = tiq.WorksheetQuestion?.Question?.PracticeCorrectAnswer,
                    AnswerColCount = tiq.WorksheetQuestion?.Question?.AnswerColCount ?? 0,
                    LayoutPlan = tiq.WorksheetQuestion.Question.LayoutPlan,
                    Passage = tiq.WorksheetQuestion?.Question?.PassageId != null
                        ? new PassageDto
                        {
                            Id = tiq.WorksheetQuestion.Question.Passage!.Id,
                            Title = tiq.WorksheetQuestion.Question.Passage.Title,
                            Text = tiq.WorksheetQuestion.Question.Passage.Text,
                            ImageUrl = tiq.WorksheetQuestion.Question.Passage.ImageUrl
                        }
                        : null,
                    Answers = tiq.WorksheetQuestion?.Question?.Answers?.Select(a => new AnswerDto
                    {
                        Id = a.Id,
                        Text = a.Text,
                        ImageUrl = a.ImageUrl,
                        Tag = a.Tag,
                        Order = a.Order
                    })?.ToList() ?? new List<AnswerDto>()
                }
            }).ToList()
        };

        foreach (var question in worksheetInstanceDto.TestInstanceQuestions)
        {
            var dto = question.Question;
            var fallbackColumns = dto.AnswerColCount > 0
                ? dto.AnswerColCount
                : Math.Max(1, Math.Min(dto.Answers?.Count ?? 0, 4));

            dto.LayoutPlan = QuestionLayoutAnalyzer.BuildUiPlanPayload(
                dto.LayoutPlan,
                fallbackColumns,
                dto.Passage != null);
        }

        return worksheetInstanceDto;
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
                    ImageUrl = q.ImageUrl,
                    LayoutPlan = q.LayoutPlan,
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

            dto.LayoutPlan = QuestionLayoutAnalyzer.BuildUiPlanPayload(
                dto.LayoutPlan,
                fallbackColumns,
                dto.Passage != null);
        }

        return questions;
    }

    public async Task<WorksheetInstanceResultDto?> GetCanvasTestResultAsync(int testInstanceId, int userId, bool includeCorrectAnswer = false)
    {
        var testInstance = await _context.TestInstances
            .Include(ti => ti.Worksheet)
            .Include(ti => ti.WorksheetInstanceQuestions)
                .ThenInclude(tiq => tiq.WorksheetQuestion)
                .ThenInclude(tq => tq.Question)
                .ThenInclude(q => q.Answers)
            .Include(ti => ti.WorksheetInstanceQuestions)
                .ThenInclude(tiq => tiq.WorksheetQuestion)
                .ThenInclude(tq => tq.Question)
                .ThenInclude(q => q.Passage)
            .FirstOrDefaultAsync(ti => ti.Id == testInstanceId &&
                    ti.Student.UserId == userId);

        if (testInstance == null)
        {
            return null;
        }

        if (includeCorrectAnswer && testInstance.Status != WorksheetInstanceStatus.Completed)
        {
            return null;
        }

        var response = new WorksheetInstanceResultDto
        {
            Id = testInstance.Id,
            TestName = testInstance.Worksheet.Name,
            Status = testInstance.Status,
            MaxDurationSeconds = testInstance.Worksheet.MaxDurationSeconds,
            IsPracticeTest = testInstance.Worksheet.IsPracticeTest,
            TestInstanceQuestions = testInstance.WorksheetInstanceQuestions.Select(tiq =>
            {
                var questionEntity = tiq.WorksheetQuestion.Question;
                var questionDto = new QuestionDto
                {
                    Id = questionEntity.Id,
                    Text = questionEntity?.Text ?? string.Empty,
                    SubText = questionEntity.SubText,
                    ImageUrl = questionEntity.ImageUrl,
                    IsExample = questionEntity.IsExample,
                    InteractionType = questionEntity.InteractionType,
                    InteractionPlan = questionEntity.InteractionPlan,
                    CorrectAnswerId = includeCorrectAnswer ? questionEntity.CorrectAnswerId : null,
                    Passage = questionEntity.PassageId.HasValue ? new PassageDto
                    {
                        Id = questionEntity.Passage?.Id,
                        Title = questionEntity.Passage?.Title,
                        Text = questionEntity.Passage?.Text,
                        ImageUrl = questionEntity.Passage?.ImageUrl,
                        X = questionEntity.Passage?.X,
                        Y = questionEntity.Passage?.Y,
                        Width = questionEntity.Passage?.Width,
                        Height = questionEntity.Passage?.Height
                    } : null,
                    PracticeCorrectAnswer = questionEntity.PracticeCorrectAnswer,
                    AnswerColCount = questionEntity.AnswerColCount,
                    IsCanvasQuestion = questionEntity.IsCanvasQuestion,
                    X = questionEntity.X,
                    Y = questionEntity.Y,
                    Width = questionEntity.Width,
                    Height = questionEntity.Height,
                    LayoutPlan = questionEntity.LayoutPlan,
                    Answers = questionEntity.Answers.Select(a => new AnswerDto
                    {
                        Id = a.Id,
                        Text = a.Text,
                        ImageUrl = a.ImageUrl,
                        X = a.X,
                        Y = a.Y,
                        Width = a.Width,
                        Height = a.Height,
                        Tag = a.Tag,
                        Order = a.Order
                    }).ToList()
                };

                var fallbackColumns = questionDto.AnswerColCount > 0
                    ? questionDto.AnswerColCount
                    : Math.Max(1, Math.Min(questionDto.Answers.Count, 4));

                questionDto.LayoutPlan = QuestionLayoutAnalyzer.BuildUiPlanPayload(
                    questionDto.LayoutPlan,
                    fallbackColumns,
                    questionDto.Passage != null);

                return new WorksheetInstanceQuestionDto
                {
                    Id = tiq.Id,
                    Order = tiq.WorksheetQuestion.Order,
                    Question = questionDto,
                    SelectedAnswerId = tiq.SelectedAnswerId,
                    AnswerPayload = tiq.AnswerPayload,
                    TimeTaken = tiq.TimeTaken
                };
            }).ToList()
        };


        return response;
    }

    public async Task<ResponseBaseDto> SaveAnswer(SaveAnswerDto dto, UserProfileDto user)
    {
        var testInstanceQuestion = await _context.TestInstanceQuestions
                    .Include(t => t.WorksheetQuestion)
                        .ThenInclude(wq => wq.Question)
                            .ThenInclude(q => q.Subject)
                    .Include(t => t.WorksheetQuestion)
                        .ThenInclude(wq => wq.Question)
                            .ThenInclude(q => q.QuestionSubTopics)
            .FirstOrDefaultAsync(tiq => tiq.WorksheetInstanceId == dto.TestInstanceId &&
                tiq.Id == dto.TestQuestionId
                && tiq.WorksheetInstance.Student.UserId == user.Id);

        if (testInstanceQuestion == null)
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = "Test instance question not found."
            };
        }
        // Store MCQ selection and/or structured answer payload
        testInstanceQuestion.SelectedAnswerId = dto.SelectedAnswerId > 0 ? dto.SelectedAnswerId : null;
        testInstanceQuestion.AnswerPayload = string.IsNullOrWhiteSpace(dto.AnswerPayload) ? null : dto.AnswerPayload;
        testInstanceQuestion.TimeTaken = dto.TimeTaken;

        var question = testInstanceQuestion.WorksheetQuestion?.Question;
        if (question == null)
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = "Question data not found for the worksheet."
            };
        }

        var interactionType = question.InteractionType ?? "mcq";
        var isDragDropLabeling = interactionType.Equals("dragDropLabeling", StringComparison.OrdinalIgnoreCase);

        bool isCorrect;
        if (isDragDropLabeling)
        {
            // For dragDropLabeling, correctness is determined client-side for practice (IsExample)
            // and server-side evaluation can be added later. For now, we store payload and mark unknown as false.
            isCorrect = false;
        }
        else
        {
            var correctAnswerId = question.CorrectAnswerId;
            isCorrect = correctAnswerId.HasValue && correctAnswerId.Value == dto.SelectedAnswerId;
        }
        testInstanceQuestion.IsCorrect = isCorrect;

        var primarySubTopicId = question.QuestionSubTopics?.FirstOrDefault()?.SubTopicId;

        _context.TestInstanceQuestions.Update(testInstanceQuestion);

        // 1. Event oluştur
        var evt = new AnswerSubmittedEvent
        {
            UserId = user.Id,
            QuestionId = question.Id,
            SubjectId = question.SubjectId,
            Subject = question.Subject?.Name ?? string.Empty,
            QuestionPoint = question.Point,
            DifficultyLevel = question.DifficultyLevel,
            SubmittedAt = DateTime.UtcNow,
            TimeTakenInSeconds = dto.TimeTaken,
            ClientId = user.KeycloakId,
            IsCorrect = isCorrect,
            SubTopicId = primarySubTopicId,
            TopicId = question.TopicId,
            TestInstanceId = dto.TestInstanceId,
            SelectedAnswerId = dto.SelectedAnswerId > 0 ? dto.SelectedAnswerId : null
        };

        // 2. Outbox'a yaz
        var outbox = new OutboxMessage
        {
            Type = typeof(AnswerSubmittedEvent).AssemblyQualifiedName ?? nameof(AnswerSubmittedEvent),
            Content = JsonSerializer.Serialize(evt),
            CreatedAt = DateTime.UtcNow
        };
        _context.OutboxMessages.Add(outbox);


        // Update Question Count
        await _context.SaveChangesAsync();
        return new ResponseBaseDto
        {
            Success = true,
            Message = "Answer saved successfully."
        };
    }

    public async Task<ResponseBaseDto> EndTest(int testInstanceId, int userId)
    {
        var testInstance = await _context.TestInstances
            .FirstOrDefaultAsync(ti => ti.Id == testInstanceId && ti.Student.UserId == userId);

        if (testInstance == null)
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = "Test instance not found."
            };
        }

        if (testInstance.Status != WorksheetInstanceStatus.Started)
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = $"Bu test zaten {testInstance.Status} durumunda."
            };
        }

        testInstance.Status = WorksheetInstanceStatus.Completed;
        testInstance.EndTime = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return new ResponseBaseDto
        {
            Success = true,
            Message = "Test ended successfully."
        };
    }

    public async Task<ExamSavedDto> CreateOrUpdateAsync(ExamDto examDto, int userId)
    {
        if (examDto == null)
        {
            // TODO: bu kontrolleri contrrollerda yapabilirsin
            // return BadRequest(new { error = "Sınav bilgileri eksik!" });
            return new ExamSavedDto
            {
                Success = false,
                Message = "Sınav bilgileri eksik!"
            };
        }

        // eper newBookName ve newBookTestName dolu ise ve db'de zaten varsa o halde sucess = true olarak devam et
        if (!string.IsNullOrWhiteSpace(examDto.NewBookName) && !string.IsNullOrWhiteSpace(examDto.NewBookTestName))
        {
            var existingBook = await _context.Books
                .Include(b => b.BookTests)
                .FirstOrDefaultAsync(b => b.Name == examDto.NewBookName);

            if (existingBook != null)
            {
                examDto.BookId = existingBook.Id;
                var existingBookTest = existingBook.BookTests
                    .FirstOrDefault(bt => bt.Name == examDto.NewBookTestName);

                if (existingBookTest != null)
                {
                    examDto.BookTestId = existingBookTest.Id;
                }
            }
        }

        try
        {
            if (examDto.BookId == 0 && string.IsNullOrWhiteSpace(examDto.NewBookName))
            {
                // return BadRequest(new { error = "Kitap seçilmedi!" });
                return new ExamSavedDto
                {
                    Success = false,
                    Message = "Kitap seçilmedi!"
                };
            }

            if (examDto.BookTestId == 0 && string.IsNullOrWhiteSpace(examDto.NewBookTestName))
            {
                // return BadRequest(new { error = "Kipta Test seçilmedi!" });
                return new ExamSavedDto
                {
                    Success = false,
                    Message = "Kipta Test seçilmedi!"
                };
            }

            Book? book = null;
            if (examDto.BookId is null || examDto.BookId == 0)
            {
                if (string.IsNullOrWhiteSpace(examDto.NewBookName))
                {
                    return new ExamSavedDto
                    {
                        Success = false,
                        Message = "Kitap seçilmedi!"
                    };
                }

                if (string.IsNullOrWhiteSpace(examDto.NewBookTestName))
                {
                    return new ExamSavedDto
                    {
                        Success = false,
                        Message = "Kipta Test seçilmedi!"
                    };
                }

                book = await _context.Books
                            .Include(b => b.BookTests)
                        .FirstOrDefaultAsync(b => b.Name == examDto.NewBookName);

                if (book == null)
                {
                    book = new Book
                    {
                        Name = examDto.NewBookName
                    };
                    book.BookTests =
                    [
                    new BookTest
                    {
                        Name = examDto.NewBookTestName,
                        BookId = book.Id
                    },
                    ];
                    _context.Books.Add(book);
                }

                await _context.SaveChangesAsync();
                examDto.BookId = book.Id;
                examDto.BookTestId = book.BookTests.First(bt => bt.Name == examDto.NewBookTestName).Id;
            }
            else
            {
                book = await _context.Books
                            .Include(b => b.BookTests)
                        .FirstOrDefaultAsync(b => b.Id == examDto.BookId);

                if (book == null)
                {
                    return new ExamSavedDto
                    {
                        Success = false,
                        Message = "Kitap bulunamadı!"
                    };

                }

                // Eğer zaten bu kitap için test zaten mevcutsa o halde success olarak devam et
                if (examDto.BookTestId is null || examDto.BookTestId == 0)
                {
                    if (string.IsNullOrWhiteSpace(examDto.NewBookTestName))
                    {
                        // return BadRequest(new { error = "Kipta Test seçilmedi!" });
                        return new ExamSavedDto
                        {
                            Success = false,
                            Message = "Kipta Test seçilmedi!"
                        };
                    }
                    else
                    {
                        book.BookTests.Add(new BookTest
                        {
                            Name = examDto.NewBookTestName,
                            BookId = book.Id
                        });
                    }
                    await _context.SaveChangesAsync();
                    examDto.BookTestId = book.BookTests.First(bt => bt.Name == examDto.NewBookTestName).Id;
                }
            }


            Worksheet? examination;
            var newId = 0;
            if (examDto.Id > 0)
            {
                examination = await _context.Worksheets.FindAsync(examDto.Id);

                if (examination == null)
                {
                    // return NotFound(new { error = "Test bulunamadı!" });
                    return new ExamSavedDto
                    {
                        Success = false,
                        Message = "Test bulunamadı!"
                    };
                }

                examination.Name = examDto.Name;
                examination.Description = examDto.Description;
                examination.GradeId = examDto.GradeId;
                examination.MaxDurationSeconds = examDto.MaxDurationSeconds;
                examination.IsPracticeTest = examDto.IsPracticeTest;
                examination.Subtitle = examDto.Subtitle;
                examination.BookTestId = book.BookTests.FirstOrDefault(bt => bt.Id == examDto.BookTestId)?.Id ?? book.BookTests.First().Id;
                examination.SubjectId = examDto.SubjectId;
                examination.TopicId = examDto.TopicId;
                examination.SubTopicId = examDto.SubTopicId;

                // 📌 Eğer yeni resim varsa, güncelle
                if (!string.IsNullOrEmpty(examDto.ImageUrl) &&
                    _imageHelper.IsBase64String(examDto.ImageUrl))
                {
                    byte[] imageBytes = Convert.FromBase64String(examDto.ImageUrl.Split(',')[1]);
                    await using var imageStream = new MemoryStream(imageBytes);
                    examination.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"{Guid.NewGuid()}.jpg", "exams");
                }

                _context.Worksheets.Update(examination);
            }
            else
            {
                // 📌 Yeni Soru Oluştur (INSERT)
                var bookTestId = book.BookTests.FirstOrDefault(bt => bt.Id == examDto.BookTestId)?.Id ?? book.BookTests.First().Id;
                var existingExam = await _context.Worksheets
                    .FirstOrDefaultAsync(e => e.Name == examDto.Name && e.BookTestId == bookTestId);
                // 📌 Eğer yeni resim varsa, güncelle
                var newImageUrl = string.Empty;
                if (!string.IsNullOrEmpty(examDto.ImageUrl) &&
                    _imageHelper.IsBase64String(examDto.ImageUrl))
                {
                    byte[] imageBytes = Convert.FromBase64String(examDto.ImageUrl.Split(',')[1]);
                    await using var imageStream = new MemoryStream(imageBytes);
                    newImageUrl = await _minioService.UploadFileAsync(imageStream, $"{Guid.NewGuid()}.jpg", "exams");
                }

                if (existingExam != null)
                {
                    // Update existing exam
                    existingExam.Name = examDto.Name;
                    existingExam.Description = examDto.Description;
                    existingExam.GradeId = examDto.GradeId;
                    existingExam.MaxDurationSeconds = examDto.MaxDurationSeconds;
                    existingExam.IsPracticeTest = examDto.IsPracticeTest;
                    existingExam.Subtitle = examDto.Subtitle;
                    existingExam.BookTestId = bookTestId;
                    existingExam.SubjectId = examDto.SubjectId;
                    existingExam.TopicId = examDto.TopicId;
                    existingExam.SubTopicId = examDto.SubTopicId;

                    if (!string.IsNullOrEmpty(newImageUrl))
                    {
                        existingExam.ImageUrl = newImageUrl;
                    }
                    examination = existingExam;
                    _context.Worksheets.Update(examination);
                }
                else
                {
                    // Create new exam
                    examination = new Worksheet
                    {
                        Name = examDto.Name,
                        Description = examDto.Description,
                        GradeId = examDto.GradeId,
                        MaxDurationSeconds = examDto.MaxDurationSeconds,
                        IsPracticeTest = examDto.IsPracticeTest,
                        Subtitle = examDto.Subtitle,
                        BookTestId = bookTestId,
                        SubjectId = examDto.SubjectId,
                        TopicId = examDto.TopicId,
                        SubTopicId = examDto.SubTopicId,
                    };
                    if (!string.IsNullOrEmpty(newImageUrl))
                    {
                        examination.ImageUrl = newImageUrl;
                    }
                    _context.Worksheets.Add(examination);

                }
            }

            await _context.SaveChangesAsync(); // burada audit çalışır
            return new ExamSavedDto
            {
                Message = examDto.Id > 0 ?
                            "Test başarıyla güncellendi!" : "Test başarıyla kaydedildi!",
                ExamId = examination.Id,
                BookId = book?.Id,
                BookTestId = examination.BookTestId
            };
        }
        catch (Exception ex)
        {
            return new ExamSavedDto
            {
                Success = false,
                Message = ex.Message
            };
            // return BadRequest(new { error = ex.Message });
        }
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

    public async Task<BulkExamResultDto> CreateBulkExamsAsync(BulkExamCreateDto bulkExamDto, int userId)
    {
        var result = new BulkExamResultDto
        {
            Success = true,
            Message = "Bulk exam creation completed"
        };

        var successfulExams = new List<ExamSavedDto>();
        var failedExams = new List<BulkExamErrorDto>();
        int rowNumber = 1;

        foreach (var examItem in bulkExamDto.Exams)
        {
            try
            {
                // Convert BulkExamItemDto to ExamDto
                var examDto = new ExamDto
                {
                    Name = examItem.Name,
                    Description = examItem.Description,
                    GradeId = examItem.GradeId,
                    MaxDurationSeconds = examItem.MaxDurationSeconds,
                    IsPracticeTest = examItem.IsPracticeTest,
                    Subtitle = examItem.Subtitle,
                    BadgeText = examItem.BadgeText,
                    BookTestId = examItem.BookTestId,
                    BookId = examItem.BookId,
                    NewBookName = examItem.NewBookName,
                    NewBookTestName = examItem.NewBookTestName,
                    SubjectId = examItem.SubjectId,
                    TopicId = examItem.TopicId,
                    SubTopicId = examItem.SubTopicId
                };

                // Use existing CreateOrUpdateAsync method
                var savedExam = await CreateOrUpdateAsync(examDto, userId);

                if (savedExam.Success)
                {
                    successfulExams.Add(savedExam);
                }
                else
                {
                    failedExams.Add(new BulkExamErrorDto
                    {
                        ExamName = examItem.Name,
                        ErrorMessage = savedExam.Message,
                        RowNumber = rowNumber
                    });
                }
            }
            catch (Exception ex)
            {
                failedExams.Add(new BulkExamErrorDto
                {
                    ExamName = examItem.Name,
                    ErrorMessage = ex.Message,
                    RowNumber = rowNumber
                });
            }

            rowNumber++;
        }

        result.SuccessfulExams = successfulExams;
        result.FailedExams = failedExams;
        result.TotalProcessed = bulkExamDto.Exams.Count;
        result.SuccessCount = successfulExams.Count;
        result.FailureCount = failedExams.Count;

        if (failedExams.Any())
        {
            result.Success = false;
            result.Message = $"Processed {result.TotalProcessed} exams: {result.SuccessCount} successful, {result.FailureCount} failed";
        }

        return result;
    }

    public async Task<List<Grade>> GetGradesAsync()
    {
        return await _context.Grades.ToListAsync();
    }

    public async Task<ResponseBaseDto> DeleteWorksheetAsync(int worksheetId, int userId)
    {
        var response = new ResponseBaseDto();

        var worksheet = await _context.Worksheets.FindAsync(worksheetId);

        if (worksheet == null || worksheet.IsDeleted)
        {
            response.Success = false;
            response.Message = "Worksheet bulunamadı.";
            return response;
        }

        worksheet.IsDeleted = true;
        worksheet.DeleteTime = DateTime.UtcNow;
        worksheet.DeleteUserId = userId;

        await _context.SaveChangesAsync();

        response.Success = true;
        response.Message = "Worksheet başarıyla silindi.";

        return response;
    }
}
