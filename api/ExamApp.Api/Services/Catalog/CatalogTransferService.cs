using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Classifier;
using ExamApp.Api.Services.Questions;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Catalog;

public interface ICatalogTransferService
{
    Task<CatalogBundleDto> ExportAsync(CancellationToken ct = default);

    /// <summary>
    /// Koda göre eşleşir: yoksa oluşturur, varsa adını ve üst kaydını günceller. Dosyada olmayan kayda dokunmaz, hiçbir şey silmez.
    /// Kodu olmayan eski kayıt aynı adla eşleşip kodu alır. Aynı ad başka bir kodla varsa hata verir, tahmin yürütmez.
    /// Tek hatalı satırda hiçbir şey yazılmaz; <paramref name="dryRun"/> aynı sayıları yazmadan döner.
    /// </summary>
    Task<CatalogImportResultDto> ImportAsync(CatalogBundleDto bundle, bool dryRun, int userId, CancellationToken ct = default);
}

public class CatalogTransferService(AppDbContext context, IBackgroundJobClient jobs) : ICatalogTransferService
{
    private const int GradeNameMaxLength = 50;
    private const int SubjectNameMaxLength = 100;
    private const int SourceReferenceMaxLength = 300;
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    public async Task<CatalogBundleDto> ExportAsync(CancellationToken ct = default)
    {
        var grades = await context.Grades.AsNoTracking().OrderBy(g => g.Id).Select(g => new { g.Id, g.Code, g.Name }).ToListAsync(ct);
        var subjects = await context.Subjects.AsNoTracking().OrderBy(s => s.Id).Select(s => new { s.Id, s.Code, s.Name }).ToListAsync(ct);
        var gradeCodes = grades.ToDictionary(g => g.Id, g => g.Code ?? CatalogCode.Slug(g.Name));
        var subjectCodes = subjects.ToDictionary(s => s.Id, s => s.Code ?? CatalogCode.Slug(s.Name));

        var gradeSubjects = await context.GradeSubjects.AsNoTracking().Select(gs => new { gs.GradeId, gs.SubjectId }).ToListAsync(ct);
        var topicRows = await context.Topics.AsNoTracking().Select(t => new { t.Id, t.Code, t.Name, t.GradeId, t.SubjectId }).ToListAsync(ct);
        var subTopicRows = await context.SubTopics.AsNoTracking().Select(s => new { s.Id, s.Code, s.Name, s.TopicId }).ToListAsync(ct);
        var outcomes = await context.LearningOutcomes.AsNoTracking()
            .Select(lo => new { lo.CurriculumVersion, lo.Code, lo.Description, lo.GradeId, lo.SubjectId, lo.SubTopicId, lo.SourceReference })
            .ToListAsync(ct);

        // Silinmiş bir üst kaydın altındaki satır taşınamaz: üst kaydı dosyada yoktur.
        var topics = topicRows
            .Where(t => gradeCodes.ContainsKey(t.GradeId) && subjectCodes.ContainsKey(t.SubjectId))
            .ToDictionary(t => t.Id, t => new
            {
                t.Name,
                t.GradeId,
                t.SubjectId,
                Code = t.Code ?? CatalogCode.ForTopic(gradeCodes[t.GradeId], subjectCodes[t.SubjectId], t.Name),
            });
        var subTopics = subTopicRows
            .Where(s => topics.ContainsKey(s.TopicId))
            .Select(s => new { s.Id, s.Name, s.TopicId, Code = s.Code ?? CatalogCode.ForSubTopic(topics[s.TopicId].Code, s.Name) })
            .ToList();
        var subTopicCodes = subTopics.ToDictionary(s => s.Id, s => s.Code);
        var subTopicsByTopic = subTopics.ToLookup(s => s.TopicId);

        return new CatalogBundleDto
        {
            Grades = grades.Select(g => new CatalogNamedDto { Code = gradeCodes[g.Id], Name = g.Name }).ToList(),
            Subjects = subjects.Select(s => new CatalogNamedDto { Code = subjectCodes[s.Id], Name = s.Name }).ToList(),
            GradeSubjects = gradeSubjects
                .Where(gs => gradeCodes.ContainsKey(gs.GradeId) && subjectCodes.ContainsKey(gs.SubjectId))
                .Select(gs => new CatalogGradeSubjectDto { Grade = gradeCodes[gs.GradeId], Subject = subjectCodes[gs.SubjectId] })
                .OrderBy(gs => gs.Grade, StringComparer.Ordinal).ThenBy(gs => gs.Subject, StringComparer.Ordinal)
                .ToList(),
            Topics = topics
                .Select(pair => new CatalogTopicDto
                {
                    Code = pair.Value.Code,
                    Name = pair.Value.Name,
                    Grade = gradeCodes[pair.Value.GradeId],
                    Subject = subjectCodes[pair.Value.SubjectId],
                    SubTopics = subTopicsByTopic[pair.Key]
                        .Select(s => new CatalogNamedDto { Code = s.Code, Name = s.Name })
                        .OrderBy(s => s.Code, StringComparer.Ordinal)
                        .ToList(),
                })
                .OrderBy(t => t.Code, StringComparer.Ordinal)
                .ToList(),
            LearningOutcomes = outcomes
                .Where(lo => gradeCodes.ContainsKey(lo.GradeId) && lo.SubjectId is int id && subjectCodes.ContainsKey(id))
                .Select(lo => new CatalogLearningOutcomeDto
                {
                    CurriculumVersion = lo.CurriculumVersion.ToString(),
                    Code = lo.Code,
                    Description = lo.Description,
                    Grade = gradeCodes[lo.GradeId],
                    Subject = subjectCodes[lo.SubjectId!.Value],
                    SubTopic = lo.SubTopicId is int sid && subTopicCodes.TryGetValue(sid, out var code) ? code : null,
                    SourceReference = lo.SourceReference,
                })
                .OrderBy(lo => lo.CurriculumVersion, StringComparer.Ordinal).ThenBy(lo => lo.Code, StringComparer.Ordinal)
                .ToList(),
        };
    }

    public async Task<CatalogImportResultDto> ImportAsync(CatalogBundleDto bundle, bool dryRun, int userId, CancellationToken ct = default)
    {
        var errors = Validate(bundle);
        if (errors.Count > 0)
            return new CatalogImportResultDto { DryRun = dryRun, Errors = errors };

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            context.SetCurrentUser(userId);

            var result = await ApplyAsync(bundle, ct);
            result.DryRun = dryRun;
            if (result.Errors.Count > 0 || dryRun)
            {
                await transaction.RollbackAsync(ct);
                context.ChangeTracker.Clear();
                return result;
            }

            await transaction.CommitAsync(ct);
            if (TaxonomyChanged(result))
                jobs.Enqueue<IClassifierCacheService>(s => s.RefreshIfStaleAsync(userId));
            return result;
        });
    }

    // Sınıflandırıcı önbelleği taksonomiden üretilir; kazanım değişikliği onu etkilemez.
    private static bool TaxonomyChanged(CatalogImportResultDto result) =>
        new[] { result.Grades, result.Subjects, result.GradeSubjects, result.Topics, result.SubTopics }
            .Any(c => c.Created + c.Updated > 0);

    private static List<string> Validate(CatalogBundleDto bundle)
    {
        var errors = new List<string>();
        if (bundle.FormatVersion != CatalogBundleDto.CurrentFormatVersion)
            errors.Add($"formatVersion: expected {CatalogBundleDto.CurrentFormatVersion}, got {bundle.FormatVersion}");

        CheckNamed(errors, "grades", bundle.Grades, GradeNameMaxLength);
        CheckNamed(errors, "subjects", bundle.Subjects, SubjectNameMaxLength);
        CheckNamed(errors, "topics", bundle.Topics.Select(t => new CatalogNamedDto { Code = t.Code, Name = t.Name }).ToList(), int.MaxValue);
        CheckNamed(errors, "subTopics", bundle.Topics.SelectMany(t => t.SubTopics).ToList(), int.MaxValue);

        var seenOutcomes = new HashSet<(string Version, string Code)>();
        for (var i = 0; i < bundle.LearningOutcomes.Count; i++)
        {
            var outcome = bundle.LearningOutcomes[i];
            var path = $"learningOutcomes[{i}]";
            if (!CurriculumVersionParser.TryParse(outcome.CurriculumVersion, out _))
                errors.Add($"{path}: unknown curriculum version {outcome.CurriculumVersion}");
            var code = outcome.Code?.Trim() ?? string.Empty;
            if (code.Length == 0 || string.IsNullOrWhiteSpace(outcome.Description))
                errors.Add($"{path}: code and description are required");
            else if (!seenOutcomes.Add((outcome.CurriculumVersion, code)))
                errors.Add($"{path}: duplicate code {code}");
            if (outcome.SourceReference?.Length > SourceReferenceMaxLength)
                errors.Add($"{path}: sourceReference is longer than {SourceReferenceMaxLength}");
        }

        return errors;
    }

    private static void CheckNamed(List<string> errors, string section, List<CatalogNamedDto> rows, int nameMaxLength)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            var path = $"{section}[{i}]";
            var code = rows[i].Code?.Trim() ?? string.Empty;
            var name = rows[i].Name?.Trim() ?? string.Empty;
            if (code.Length == 0 || name.Length == 0)
                errors.Add($"{path}: code and name are required");
            else if (!seen.Add(code))
                errors.Add($"{path}: duplicate code {code}");
            if (code.Length > CatalogCode.MaxLength)
                errors.Add($"{path}: code is longer than {CatalogCode.MaxLength}");
            if (name.Length > nameMaxLength)
                errors.Add($"{path}: name is longer than {nameMaxLength}");
        }
    }

    private async Task<CatalogImportResultDto> ApplyAsync(CatalogBundleDto bundle, CancellationToken ct)
    {
        var result = new CatalogImportResultDto();

        var grades = new CodedIndex<Grade>(await context.Grades.ToListAsync(ct), g => g.Code, g => g.Name, _ => string.Empty);
        foreach (var (row, i) in bundle.Grades.Select((r, i) => (r, i)))
            Upsert(grades, row, string.Empty, () => new Grade { Name = row.Name.Trim() }, e => context.Grades.Add(e),
                (e, code) => e.Code = code, (e, name) => e.Name = name, null, $"grades[{i}]", result.Grades, result.Errors);

        var subjects = new CodedIndex<Subject>(await context.Subjects.ToListAsync(ct), s => s.Code, s => s.Name, _ => string.Empty);
        foreach (var (row, i) in bundle.Subjects.Select((r, i) => (r, i)))
            Upsert(subjects, row, string.Empty, () => new Subject { Name = row.Name.Trim() }, e => context.Subjects.Add(e),
                (e, code) => e.Code = code, (e, name) => e.Name = name, null, $"subjects[{i}]", result.Subjects, result.Errors);

        if (result.Errors.Count > 0)
            return result;
        await context.SaveChangesAsync(ct);
        // Yeni kayıtların kimliği ancak şimdi belli: alt kayıtların kapsamı kimlikten kurulur.

        await ApplyGradeSubjectsAsync(bundle, grades, subjects, result, ct);

        var topics = new CodedIndex<Topic>(await context.Topics.ToListAsync(ct), t => t.Code, t => t.Name, t => TopicScope(t.GradeId, t.SubjectId));
        foreach (var (row, i) in bundle.Topics.Select((r, i) => (r, i)))
        {
            var path = $"topics[{i}]";
            var grade = Resolve(grades, row.Grade, "grade", path, result.Errors);
            var subject = Resolve(subjects, row.Subject, "subject", path, result.Errors);
            if (grade is null || subject is null)
                continue;
            Upsert(topics, new CatalogNamedDto { Code = row.Code, Name = row.Name }, TopicScope(grade.Id, subject.Id),
                () => new Topic { Name = row.Name.Trim(), GradeId = grade.Id, SubjectId = subject.Id }, e => context.Topics.Add(e),
                (e, code) => e.Code = code, (e, name) => e.Name = name,
                e =>
                {
                    var moved = e.GradeId != grade.Id || e.SubjectId != subject.Id;
                    e.GradeId = grade.Id;
                    e.SubjectId = subject.Id;
                    return moved;
                },
                path, result.Topics, result.Errors);
        }

        if (result.Errors.Count > 0)
            return result;
        await context.SaveChangesAsync(ct);

        var subTopics = new CodedIndex<SubTopic>(await context.SubTopics.ToListAsync(ct), s => s.Code, s => s.Name, s => s.TopicId.ToString());
        foreach (var (row, i) in bundle.Topics.Select((r, i) => (r, i)))
        {
            var topic = topics.ByCode[row.Code.Trim()];
            foreach (var (subRow, j) in row.SubTopics.Select((r, j) => (r, j)))
                Upsert(subTopics, subRow, topic.Id.ToString(), () => new SubTopic { Name = subRow.Name.Trim(), TopicId = topic.Id }, e => context.SubTopics.Add(e),
                    (e, code) => e.Code = code, (e, name) => e.Name = name,
                    e =>
                    {
                        var moved = e.TopicId != topic.Id;
                        e.TopicId = topic.Id;
                        return moved;
                    },
                    $"topics[{i}].subTopics[{j}]", result.SubTopics, result.Errors);
        }

        if (result.Errors.Count > 0)
            return result;
        await context.SaveChangesAsync(ct);

        await ApplyOutcomesAsync(bundle, grades, subjects, subTopics, result, ct);
        if (result.Errors.Count == 0)
            await context.SaveChangesAsync(ct);
        return result;
    }

    private static string TopicScope(int gradeId, int subjectId) => $"{gradeId}|{subjectId}";

    private static void Upsert<T>(CodedIndex<T> index, CatalogNamedDto row, string scope, Func<T> create, Action<T> add,
        Action<T, string> setCode, Action<T, string> setName, Func<T, bool>? moveToParent, string path, CatalogImportCountsDto counts, List<string> errors)
        where T : class
    {
        var code = row.Code.Trim();
        var name = row.Name.Trim();
        var named = index.FindByName(scope, name);

        if (index.ByCode.TryGetValue(code, out var existing))
        {
            if (named.Any(other => !ReferenceEquals(other, existing)))
            {
                errors.Add($"{path}: name {name} already belongs to another record in the target");
                return;
            }

            var renamed = !string.Equals(index.Name(existing), name, StringComparison.Ordinal);
            if (renamed)
                setName(existing, name);
            var moved = moveToParent?.Invoke(existing) ?? false;
            if (renamed || moved)
            {
                index.AddName(scope, name, existing);
                counts.Updated++;
            }
            else
            {
                counts.Unchanged++;
            }
            return;
        }

        if (named.Count > 1)
        {
            errors.Add($"{path}: name {name} is ambiguous in the target");
            return;
        }

        if (named.Count == 1)
        {
            var match = named[0];
            if (index.Code(match) is { } otherCode)
            {
                errors.Add($"{path}: name {name} already exists with code {otherCode}, not {code}");
                return;
            }

            // Kodu olmayan eski kayıt: koda burada kavuşur, yeni kopya oluşmaz.
            setCode(match, code);
            index.ByCode[code] = match;
            counts.Updated++;
            return;
        }

        var created = create();
        setCode(created, code);
        add(created);
        index.ByCode[code] = created;
        index.AddName(scope, name, created);
        counts.Created++;
    }

    private static T? Resolve<T>(CodedIndex<T> index, string code, string label, string path, List<string> errors) where T : class
    {
        if (index.ByCode.TryGetValue(code?.Trim() ?? string.Empty, out var row))
            return row;
        errors.Add($"{path}: unknown {label} {code}");
        return null;
    }

    private async Task ApplyGradeSubjectsAsync(CatalogBundleDto bundle, CodedIndex<Grade> grades, CodedIndex<Subject> subjects, CatalogImportResultDto result, CancellationToken ct)
    {
        var existing = (await context.GradeSubjects.AsNoTracking().Select(gs => new { gs.GradeId, gs.SubjectId }).ToListAsync(ct))
            .Select(gs => (gs.GradeId, gs.SubjectId)).ToHashSet();

        for (var i = 0; i < bundle.GradeSubjects.Count; i++)
        {
            var row = bundle.GradeSubjects[i];
            var path = $"gradeSubjects[{i}]";
            var grade = Resolve(grades, row.Grade, "grade", path, result.Errors);
            var subject = Resolve(subjects, row.Subject, "subject", path, result.Errors);
            if (grade is null || subject is null)
                continue;
            if (existing.Add((grade.Id, subject.Id)))
            {
                context.GradeSubjects.Add(new GradeSubject { GradeId = grade.Id, SubjectId = subject.Id });
                result.GradeSubjects.Created++;
            }
            else
            {
                result.GradeSubjects.Unchanged++;
            }
        }
    }

    private async Task ApplyOutcomesAsync(CatalogBundleDto bundle, CodedIndex<Grade> grades, CodedIndex<Subject> subjects,
        CodedIndex<SubTopic> subTopics, CatalogImportResultDto result, CancellationToken ct)
    {
        var existing = (await context.LearningOutcomes.ToListAsync(ct)).ToDictionary(lo => (lo.CurriculumVersion, lo.Code));

        for (var i = 0; i < bundle.LearningOutcomes.Count; i++)
        {
            var row = bundle.LearningOutcomes[i];
            var path = $"learningOutcomes[{i}]";
            var grade = Resolve(grades, row.Grade, "grade", path, result.Errors);
            var subject = Resolve(subjects, row.Subject, "subject", path, result.Errors);
            SubTopic? subTopic = null;
            if (!string.IsNullOrWhiteSpace(row.SubTopic))
            {
                subTopic = Resolve(subTopics, row.SubTopic, "subTopic", path, result.Errors);
                if (subTopic is null)
                    continue;
            }
            if (grade is null || subject is null)
                continue;

            CurriculumVersionParser.TryParse(row.CurriculumVersion, out var version);
            var code = row.Code.Trim();
            var description = row.Description.Trim();
            if (!existing.TryGetValue((version, code), out var outcome))
            {
                outcome = new LearningOutcome
                {
                    Code = code,
                    Description = description,
                    GradeId = grade.Id,
                    SubjectId = subject.Id,
                    SubTopicId = subTopic?.Id,
                    CurriculumVersion = version,
                    SourceReference = row.SourceReference,
                };
                context.LearningOutcomes.Add(outcome);
                existing[(version, code)] = outcome;
                result.LearningOutcomes.Created++;
                continue;
            }

            // Dosyada alt konu yoksa mevcut bağ korunur: içe aktarım bağ silmez.
            var subTopicId = subTopic?.Id ?? outcome.SubTopicId;
            if (outcome.Description == description && outcome.GradeId == grade.Id && outcome.SubjectId == subject.Id
                && outcome.SubTopicId == subTopicId && outcome.SourceReference == row.SourceReference)
            {
                result.LearningOutcomes.Unchanged++;
                continue;
            }

            outcome.Description = description;
            outcome.GradeId = grade.Id;
            outcome.SubjectId = subject.Id;
            outcome.SubTopicId = subTopicId;
            outcome.SourceReference = row.SourceReference;
            result.LearningOutcomes.Updated++;
        }
    }

    private sealed class CodedIndex<T> where T : class
    {
        private readonly Dictionary<string, List<T>> _byName = new(NameComparer);

        public CodedIndex(IEnumerable<T> rows, Func<T, string?> code, Func<T, string> name, Func<T, string> scope)
        {
            Code = code;
            Name = name;
            foreach (var row in rows)
            {
                if (code(row) is { } value)
                    ByCode[value] = row;
                AddName(scope(row), name(row).Trim(), row);
            }
        }

        public Func<T, string?> Code { get; }
        public Func<T, string> Name { get; }
        public Dictionary<string, T> ByCode { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<T> FindByName(string scope, string name) =>
            _byName.TryGetValue(Key(scope, name), out var rows) ? rows : [];

        public void AddName(string scope, string name, T row)
        {
            if (!_byName.TryGetValue(Key(scope, name), out var rows))
                _byName[Key(scope, name)] = rows = new List<T>();
            if (!rows.Contains(row))
                rows.Add(row);
        }

        private static string Key(string scope, string name) => $"{scope}\u0001{name}";
    }
}
