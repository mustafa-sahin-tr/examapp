using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// Worksheet assignment + assignment-progress reads. Extracted from ExamService.
/// </summary>
public class WorksheetAssignmentService : IWorksheetAssignmentService
{
    private readonly AppDbContext _context;

    public WorksheetAssignmentService(AppDbContext context)
    {
        _context = context;
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
}
