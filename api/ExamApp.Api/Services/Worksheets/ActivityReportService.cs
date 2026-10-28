using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>issue #584: öğretmen etkinlik analizi (madde/satır doğru oranı, en sık yanlış cevap, öğrenci × madde ısı haritası).</summary>
public interface IActivityReportService
{
    /// <summary>
    /// Etkinlik sayfasının raporu. null = sayfa yok, etkinlik değil ya da istek sahibinin bu sayfada ataması yok (öğretmen
    /// takibiyle aynı gizlilik: başkasının sayfası hakkında bilgi dönmez).
    /// </summary>
    Task<ActivityReportDto?> GetReportAsync(int worksheetId, SchoolScope requester, CancellationToken ct = default);
}

/// <summary>
/// <inheritdoc cref="IActivityReportService"/>
/// <para>
/// Öğrenci ve oturum seçimi "Çalışma takibi"nden (<see cref="IWorksheetAssignmentService.GetWorksheetAssignmentsForTeacherAsync"/>)
/// gelir — kapsam, okul sınırı ve atama penceresi kuralı tek yerde kalır. Aynı öğrenci birden çok atamada ise tamamlanmış oturumu
/// bir kez sayılır. Cevaplar kontrolcünün <see cref="IInteractionGrader.Grade"/> / <see cref="IInteractionGrader.Analyze"/>'ı ile
/// yeniden değerlendirilir (saklanan <c>CorrectCount</c> son kontrol anınındır; kontrol edilmemiş son cevapları da kapsamak için).
/// </para>
/// </summary>
public sealed class ActivityReportService : IActivityReportService
{
    /// <summary>Öğe başına raporlanan en sık yanlış cevap sayısı.</summary>
    public const int MaxWrongAnswers = 3;

    private readonly AppDbContext _context;
    private readonly IWorksheetAssignmentService _assignments;
    private readonly IInteractionGraderRegistry _graders;

    public ActivityReportService(AppDbContext context, IWorksheetAssignmentService assignments, IInteractionGraderRegistry graders)
    {
        _context = context;
        _assignments = assignments;
        _graders = graders;
    }

    public async Task<ActivityReportDto?> GetReportAsync(int worksheetId, SchoolScope requester, CancellationToken ct = default)
    {
        var overview = await _assignments.GetWorksheetAssignmentsForTeacherAsync(worksheetId, requester);
        if (overview.WorksheetKind != WorksheetKind.Activity || overview.Assignments.Count == 0)
            return null;

        // Öğrenci başına tek kayıt: tamamlanmış (yeniden çözmeyen) oturum varsa o; yoksa yeniden çözülen; yoksa diğerleri.
        var byStudent = new Dictionary<int, TeacherAssignmentStudentDto>();
        foreach (var student in overview.Assignments.SelectMany(a => a.Students))
        {
            if (!byStudent.TryGetValue(student.StudentId, out var existing) || Rank(student) > Rank(existing))
                byStudent[student.StudentId] = student;
        }

        var analyzed = byStudent.Values
            .Where(s => Rank(s) == 2)
            .OrderBy(s => s.StudentNumber, StringComparer.Ordinal)
            .ThenBy(s => s.StudentId)
            .ToList();

        var report = new ActivityReportDto
        {
            WorksheetId = overview.WorksheetId,
            WorksheetName = overview.WorksheetName,
            StudentCount = byStudent.Count,
            AnalyzedCount = analyzed.Count,
            RetakingCount = byStudent.Values.Count(s => Rank(s) == 1)
        };

        var questions = await _context.TestQuestions.AsNoTracking()
            .Where(wq => wq.TestId == worksheetId)
            .OrderBy(wq => wq.Order).ThenBy(wq => wq.Id)
            .Select(wq => new
            {
                wq.Id,
                wq.Question.InteractionType,
                wq.Question.InteractionPlan,
                wq.Question.InteractionSolution,
                wq.Question.CorrectAnswerId,
                Answers = wq.Question.Answers
                    .OrderBy(a => a.Order ?? int.MaxValue).ThenBy(a => a.Id)
                    .Select(a => new { a.Id, a.Tag })
                    .ToList()
            })
            .ToListAsync(ct);

        var instanceIds = analyzed.Select(s => s.InstanceId!.Value).ToList();
        var rows = instanceIds.Count == 0
            ? []
            : await _context.TestInstanceQuestions.AsNoTracking()
                .Where(r => instanceIds.Contains(r.WorksheetInstanceId))
                .Select(r => new { r.WorksheetInstanceId, r.WorksheetQuestionId, r.SelectedAnswerId, r.AnswerPayload })
                .ToListAsync(ct);
        var rowByKey = rows
            .GroupBy(r => (r.WorksheetInstanceId, r.WorksheetQuestionId))
            .ToDictionary(g => g.Key, g => g.First());

        var cells = analyzed.ToDictionary(s => s.StudentId, _ => new List<ActivityReportCellDto>());
        var number = 0;
        foreach (var question in questions)
        {
            number++;
            var grader = _graders.Resolve(question.InteractionType);
            var (_, solution) = InteractionPlanSolution.ForOutput(question.InteractionPlan, question.InteractionSolution, includeSolution: true);
            var mcq = grader is McqGrader;
            var optionLabels = mcq
                ? question.Answers
                    .Select((a, i) => (Key: a.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Label: string.IsNullOrWhiteSpace(a.Tag) ? ((char)('A' + Math.Min(i, 25))).ToString() : a.Tag!.Trim()))
                    .ToDictionary(x => x.Key, x => x.Label)
                : null;

            var dto = new ActivityReportQuestionDto
            {
                WorksheetQuestionId = question.Id,
                Number = number,
                InteractionType = question.InteractionType
            };
            var aggregate = new ItemAggregate();

            foreach (var student in analyzed)
            {
                rowByKey.TryGetValue((student.InstanceId!.Value, question.Id), out var row);
                var answered = row != null && (row.SelectedAnswerId != null || row.AnswerPayload != null);
                var input = new InteractionGradeInput(question.InteractionPlan, solution,
                    row?.AnswerPayload, row?.SelectedAnswerId, question.CorrectAnswerId);
                var grade = grader.Grade(input);
                cells[student.StudentId].Add(new ActivityReportCellDto
                {
                    Answered = answered,
                    CorrectCount = grade.CorrectCount,
                    TotalCount = grade.TotalCount
                });
                if (answered)
                    dto.AnsweredCount++;
                if (grade.IsCorrect)
                    dto.FullyCorrectCount++;
                // Doğru şıkkı girilmemiş MCQ de 0/1 döner ama puanlanamaz (Analyze boş).
                if (grade.TotalCount > 0 && !(mcq && question.CorrectAnswerId == null))
                    dto.Gradable = true;

                aggregate.Add(grader.Analyze(input), optionLabels);
            }

            dto.Items = aggregate.Build(analyzed.Count);
            report.Questions.Add(dto);
        }

        report.Students = analyzed.Select(s => new ActivityReportStudentDto
        {
            StudentId = s.StudentId,
            UserId = s.UserId,
            StudentNumber = s.StudentNumber,
            GradeName = s.GradeName,
            Cells = cells[s.StudentId]
        }).ToList();

        return report;
    }

    /// <summary>2 = analize girer (tamamlandı, yeniden çözmüyor), 1 = yeniden çözüyor, 0 = tamamlamadı.</summary>
    private static int Rank(TeacherAssignmentStudentDto student)
        => student.Status != AssignmentStudentStatuses.Completed || student.InstanceId == null ? 0
            : student.Retaking ? 1
            : 2;

    /// <summary>Öğe dağılımı: öğe sırası ilk görülme sırasıdır (kontrolcü sırası), dinamik çeldiriciler sonda.</summary>
    private sealed class ItemAggregate
    {
        private readonly List<ItemCounts> _items = new();
        private readonly Dictionary<string, ItemCounts> _byId = new(StringComparer.Ordinal);

        public void Add(IReadOnlyList<InteractionItemResponse> responses, IReadOnlyDictionary<string, string>? optionLabels)
        {
            foreach (var response in responses)
            {
                if (!_byId.TryGetValue(response.ItemId, out var counts))
                {
                    counts = new ItemCounts(response.ItemId, response.ItemLabel, response.Distractor);
                    _byId[response.ItemId] = counts;
                    _items.Add(counts);
                }
                counts.Seen++;
                if (response.Correct)
                    counts.Correct++;
                else if (response.AnswerKey == null)
                    counts.Empty++;
                else
                {
                    var label = optionLabels != null && optionLabels.TryGetValue(response.AnswerKey, out var option)
                        ? option
                        : response.AnswerLabel ?? string.Empty; // boş = yalnız "işaretlendi" (textSelect çeldiricisi)
                    if (!counts.Wrong.TryGetValue(response.AnswerKey, out var wrong))
                        counts.Wrong[response.AnswerKey] = wrong = new WrongCount(label);
                    wrong.Count++;
                }
            }
        }

        public List<ActivityReportItemDto> Build(int studentCount)
            => _items
                .OrderBy(i => i.Distractor ? 1 : 0)
                .Select(i => new ActivityReportItemDto
                {
                    ItemId = i.Id,
                    Label = i.Label,
                    Distractor = i.Distractor,
                    // Yalnız seçildiğinde öğe olan çeldirici (textSelect): görmeyen öğrenciler onu doğru bırakmıştır.
                    CorrectCount = i.Correct + Math.Max(0, studentCount - i.Seen),
                    EmptyCount = i.Empty,
                    StudentCount = Math.Max(studentCount, i.Seen),
                    WrongAnswers = i.Wrong.Values
                        .OrderByDescending(w => w.Count).ThenBy(w => w.Label, StringComparer.Ordinal)
                        .Take(MaxWrongAnswers)
                        .Select(w => new ActivityReportAnswerDto { Label = w.Label, Count = w.Count })
                        .ToList()
                })
                .ToList();
    }

    private sealed class ItemCounts(string id, string label, bool distractor)
    {
        public string Id { get; } = id;
        public string Label { get; } = label;
        public bool Distractor { get; } = distractor;
        public int Seen { get; set; }
        public int Correct { get; set; }
        public int Empty { get; set; }
        public Dictionary<string, WrongCount> Wrong { get; } = new(StringComparer.Ordinal);
    }

    private sealed class WrongCount(string label)
    {
        public string Label { get; } = label;
        public int Count { get; set; }
    }
}
