using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Classifier;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #1047: sınıflandırıcının kazanım / beceri seçtiği kapalı listeler ve insanın kurduğu bağların korunması.</summary>
public class ClassifierTagServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private sealed record Seed(int QuestionId, int GradeId, int OtherGradeId, int SubjectId, int TopicId);

    private async Task<Seed> SeedAsync(bool withTopic = true)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var otherGrade = new Grade { Name = "6" };
        var subject = new Subject { Name = "Matematik" };
        var otherSubject = new Subject { Name = "Türkçe" };
        ctx.AddRange(grade, otherGrade, subject, otherSubject);
        await ctx.SaveChangesAsync();

        var topic = new Topic { Name = "Sayılar", SubjectId = subject.Id, GradeId = grade.Id };
        ctx.Topics.Add(topic);
        await ctx.SaveChangesAsync();

        var question = new Question
        {
            Text = "?", Point = 1, DifficultyLevel = 1, SubjectId = subject.Id, TopicId = withTopic ? topic.Id : null,
        };
        ctx.Questions.Add(question);
        ctx.LearningOutcomes.AddRange(
            Outcome("MAT.5.1.1", grade.Id, subject.Id, CurriculumVersion.Maarif),
            Outcome("MAT.5.1.2", grade.Id, subject.Id, CurriculumVersion.Maarif),
            Outcome("M.5.1.1.1", grade.Id, subject.Id, CurriculumVersion.Previous),
            Outcome("MAT.6.1.1", otherGrade.Id, subject.Id, CurriculumVersion.Maarif),
            Outcome("T.5.1.1", grade.Id, otherSubject.Id, CurriculumVersion.Maarif));
        await ctx.SaveChangesAsync();
        return new Seed(question.Id, grade.Id, otherGrade.Id, subject.Id, topic.Id);
    }

    private static LearningOutcome Outcome(string code, int gradeId, int subjectId, CurriculumVersion version) => new()
    {
        Code = code, Description = code + " açıklama", GradeId = gradeId, SubjectId = subjectId, CurriculumVersion = version,
    };

    private async Task<int> OutcomeIdAsync(string code)
    {
        await using var ctx = _db.NewContext();
        return await ctx.LearningOutcomes.Where(lo => lo.Code == code).Select(lo => lo.Id).SingleAsync();
    }

    private async Task<ClassifierTagOptionsDto?> OptionsAsync(int questionId, int? gradeId = null)
    {
        await using var ctx = _db.NewContext();
        return await new ClassifierTagService(ctx).GetOptionsAsync(questionId, gradeId);
    }

    private async Task<bool?> SetAsync(int questionId, IEnumerable<int> outcomeIds, IEnumerable<int> skillIds, int? gradeId = null)
    {
        await using var ctx = _db.NewContext();
        return await new ClassifierTagService(ctx).SetAsync(
            questionId, gradeId, new SetClassifierTagsDto { LearningOutcomeIds = outcomeIds.ToList(), SkillIds = skillIds.ToList() });
    }

    [Fact]
    public async Task Options_are_the_Maarif_outcomes_of_the_topic_grade_and_subject_and_all_skills()
    {
        var seed = await SeedAsync();

        var options = (await OptionsAsync(seed.QuestionId, gradeId: seed.OtherGradeId)).ShouldNotBeNull();

        // Topic grade wins over the event grade; previous curriculum, other grade and other subject are left out.
        options.LearningOutcomes.Select(o => o.Code).ShouldBe(new[] { "MAT.5.1.1", "MAT.5.1.2" });
        options.Skills.Count.ShouldBe(23);
    }

    [Fact]
    public async Task Without_a_topic_the_given_grade_is_used_and_without_a_grade_no_outcomes_are_offered()
    {
        var seed = await SeedAsync(withTopic: false);

        (await OptionsAsync(seed.QuestionId, seed.OtherGradeId))!.LearningOutcomes.Select(o => o.Code).ShouldBe(new[] { "MAT.6.1.1" });
        (await OptionsAsync(seed.QuestionId))!.LearningOutcomes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Previous_curriculum_is_offered_when_the_pair_has_no_Maarif_outcomes()
    {
        var seed = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.LearningOutcomes.RemoveRange(ctx.LearningOutcomes.Where(lo => lo.Code.StartsWith("MAT.5")));
            await ctx.SaveChangesAsync();
        }

        (await OptionsAsync(seed.QuestionId))!.LearningOutcomes.Select(o => o.Code).ShouldBe(new[] { "M.5.1.1.1" });
    }

    [Fact]
    public async Task Set_adds_the_picked_links()
    {
        var seed = await SeedAsync();
        var outcome = await OutcomeIdAsync("MAT.5.1.2");

        (await SetAsync(seed.QuestionId, [outcome], [8, 102])).ShouldBe(true);

        await using var ctx = _db.NewContext();
        ctx.QuestionLearningOutcomes.Where(l => l.QuestionId == seed.QuestionId).Select(l => l.LearningOutcomeId).ShouldBe(new[] { outcome });
        ctx.QuestionSkills.Where(l => l.QuestionId == seed.QuestionId).Select(l => l.SkillId).OrderBy(id => id).ShouldBe(new[] { 8, 102 });
    }

    [Fact]
    public async Task Set_rejects_ids_outside_the_offered_lists_and_writes_nothing()
    {
        var seed = await SeedAsync();
        var otherGrade = await OutcomeIdAsync("MAT.6.1.1");
        var previous = await OutcomeIdAsync("M.5.1.1.1");

        (await SetAsync(seed.QuestionId, [otherGrade], [])).ShouldBe(false);
        (await SetAsync(seed.QuestionId, [previous], [])).ShouldBe(false);
        (await SetAsync(seed.QuestionId, [], [999])).ShouldBe(false);
        (await SetAsync(seed.QuestionId, [], [1, 2, 3])).ShouldBe(false); // at most 2 skills

        await using var ctx = _db.NewContext();
        ctx.QuestionLearningOutcomes.Count().ShouldBe(0);
        ctx.QuestionSkills.Count().ShouldBe(0);
    }

    [Fact]
    public async Task Links_a_human_set_are_kept_and_that_kind_is_no_longer_offered()
    {
        var seed = await SeedAsync();
        var human = await OutcomeIdAsync("MAT.5.1.1");
        var other = await OutcomeIdAsync("MAT.5.1.2");
        await using (var ctx = _db.NewContext())
        {
            ctx.QuestionLearningOutcomes.Add(new QuestionLearningOutcome { QuestionId = seed.QuestionId, LearningOutcomeId = human });
            await ctx.SaveChangesAsync();
        }

        var options = (await OptionsAsync(seed.QuestionId))!;
        options.LearningOutcomes.ShouldBeEmpty();
        options.Skills.ShouldNotBeEmpty();

        (await SetAsync(seed.QuestionId, [other], [])).ShouldBe(false);
        (await SetAsync(seed.QuestionId, [], [3])).ShouldBe(true);

        await using var check = _db.NewContext();
        check.QuestionLearningOutcomes.Where(l => l.QuestionId == seed.QuestionId).Select(l => l.LearningOutcomeId).ShouldBe(new[] { human });
    }

    [Fact]
    public async Task Book_import_drafts_get_options_and_unknown_questions_none()
    {
        var seed = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var question = await ctx.Questions.SingleAsync(q => q.Id == seed.QuestionId);
            question.IsDraft = true;
            await ctx.SaveChangesAsync();
        }

        (await OptionsAsync(seed.QuestionId)).ShouldNotBeNull().LearningOutcomes.Count.ShouldBe(2);
        (await OptionsAsync(999_999)).ShouldBeNull();
        (await SetAsync(999_999, [], [])).ShouldBeNull();
    }

    public void Dispose() => _db.Dispose();
}
