using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Taxonomy;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class TaxonomyServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();

    private TaxonomyService NewService(AppDbContext ctx) => new(ctx, _jobs);

    private async Task<(int gradeId, int subjectId, int topicId)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "3. Sınıf" };
        var subject = new Subject { Name = "Matematik" };
        ctx.Grades.Add(grade);
        ctx.Subjects.Add(subject);
        await ctx.SaveChangesAsync();
        var topic = new Topic { Name = "Toplama", SubjectId = subject.Id, GradeId = grade.Id };
        ctx.Topics.Add(topic);
        await ctx.SaveChangesAsync();
        return (grade.Id, subject.Id, topic.Id);
    }

    private bool ScheduledAJob() =>
        _jobs.ReceivedCalls().Any(c =>
            c.GetMethodInfo().Name == nameof(IBackgroundJobClient.Create) &&
            c.GetArguments().OfType<IState>().Any(s => s is ScheduledState));

    // ---- GetTreeAsync ----

    [Fact]
    public async Task GetTree_nests_subjects_topics_and_subtopics_with_grade_names()
    {
        var (gradeId, subjectId, topicId) = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.SubTopics.Add(new SubTopic { Name = "İki basamaklı", TopicId = topicId });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var tree = await NewService(read).GetTreeAsync();

        var subject = tree.Subjects.ShouldHaveSingleItem();
        subject.Id.ShouldBe(subjectId);
        var topic = subject.Topics.ShouldHaveSingleItem();
        topic.GradeId.ShouldBe(gradeId);
        topic.GradeName.ShouldBe("3. Sınıf");
        topic.SubTopics.ShouldHaveSingleItem().Name.ShouldBe("İki basamaklı");
        tree.Grades.ShouldContain(g => g.Name == "3. Sınıf");
    }

    [Fact]
    public async Task GetTree_reports_question_counts_per_subtopic()
    {
        var (_, subjectId, topicId) = await SeedAsync();
        int subTopicId;
        await using (var ctx = _db.NewContext())
        {
            var st = new SubTopic { Name = "x", TopicId = topicId };
            ctx.SubTopics.Add(st);
            await ctx.SaveChangesAsync();
            subTopicId = st.Id;

            var q = new Question { SubjectId = subjectId, TopicId = topicId };
            ctx.Questions.Add(q);
            await ctx.SaveChangesAsync();
            ctx.QuestionSubTopics.Add(new QuestionSubTopic { QuestionId = q.Id, SubTopicId = subTopicId });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var tree = await NewService(read).GetTreeAsync();

        tree.Subjects[0].Topics[0].SubTopics.Single(s => s.Id == subTopicId).QuestionCount.ShouldBe(1);
    }

    // ---- Create ----

    [Fact]
    public async Task CreateSubject_persists_and_schedules_a_cache_reconcile()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSubjectAsync(new UpsertSubjectDto { Name = "  Fen  " }, userId: 7);

        result.Success.ShouldBeTrue();
        (await _db.NewContext().Subjects.FindAsync(result.ObjectId))!.Name.ShouldBe("Fen");
        ScheduledAJob().ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateSubject_rejects_a_blank_name_and_schedules_nothing(string name)
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSubjectAsync(new UpsertSubjectDto { Name = name }, userId: 1);

        result.Success.ShouldBeFalse();
        ScheduledAJob().ShouldBeFalse();
    }

    [Fact]
    public async Task CreateSubject_rejects_a_duplicate_name_case_insensitively()
    {
        await SeedAsync(); // creates "Matematik"
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSubjectAsync(new UpsertSubjectDto { Name = "matematik" }, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("zaten var");
    }

    [Fact]
    public async Task CreateTopic_rejects_an_unknown_subject()
    {
        var (gradeId, _, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateTopicAsync(
            new UpsertTopicDto { Name = "x", SubjectId = 9999, GradeId = gradeId }, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("Geçersiz ders");
    }

    [Fact]
    public async Task CreateTopic_rejects_an_unknown_grade()
    {
        var (_, subjectId, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateTopicAsync(
            new UpsertTopicDto { Name = "x", SubjectId = subjectId, GradeId = 9999 }, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("Geçersiz sınıf");
    }

    // ---- Delete guards ----

    [Fact]
    public async Task DeleteSubject_is_blocked_while_topics_reference_it()
    {
        var (_, subjectId, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).DeleteSubjectAsync(subjectId, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("konular var");
    }

    [Fact]
    public async Task DeleteSubject_is_blocked_while_questions_reference_it()
    {
        var (_, subjectId, topicId) = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            // remove the topic so only the question blocks the delete
            var t = await ctx.Topics.FindAsync(topicId);
            ctx.Topics.Remove(t!);
            ctx.Questions.Add(new Question { SubjectId = subjectId });
            await ctx.SaveChangesAsync();
        }

        await using var del = _db.NewContext();
        var result = await NewService(del).DeleteSubjectAsync(subjectId, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("sorular var");
    }

    [Fact]
    public async Task DeleteSubTopic_is_blocked_while_questions_are_assigned_to_it()
    {
        var (_, subjectId, topicId) = await SeedAsync();
        int subTopicId;
        await using (var ctx = _db.NewContext())
        {
            var st = new SubTopic { Name = "x", TopicId = topicId };
            ctx.SubTopics.Add(st);
            await ctx.SaveChangesAsync();
            subTopicId = st.Id;
            var q = new Question { SubjectId = subjectId, TopicId = topicId };
            ctx.Questions.Add(q);
            await ctx.SaveChangesAsync();
            ctx.QuestionSubTopics.Add(new QuestionSubTopic { QuestionId = q.Id, SubTopicId = subTopicId });
            await ctx.SaveChangesAsync();
        }

        await using var del = _db.NewContext();
        var result = await NewService(del).DeleteSubTopicAsync(subTopicId, userId: 1);

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteSubTopic_soft_deletes_when_unreferenced()
    {
        var (_, _, topicId) = await SeedAsync();
        int subTopicId;
        await using (var ctx = _db.NewContext())
        {
            var st = new SubTopic { Name = "x", TopicId = topicId };
            ctx.SubTopics.Add(st);
            await ctx.SaveChangesAsync();
            subTopicId = st.Id;
        }

        await using var del = _db.NewContext();
        (await NewService(del).DeleteSubTopicAsync(subTopicId, userId: 1)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.SubTopics.FindAsync(subTopicId)).ShouldBeNull(); // filtered out by the IsDeleted query filter
        (await check.SubTopics.IgnoreQueryFilters().FirstAsync(s => s.Id == subTopicId)).IsDeleted.ShouldBeTrue();
    }

    // ---- GetTreeAsync filtering (GradeSubject) ----

    [Fact]
    public async Task GetTree_with_no_filter_returns_every_subject_regardless_of_grade_links()
    {
        var (gradeId, subjectId, _) = await SeedAsync();
        int unlinkedSubjectId;
        await using (var ctx = _db.NewContext())
        {
            var other = new Subject { Name = "Fen" }; // no GradeSubject link
            ctx.Subjects.Add(other);
            await ctx.SaveChangesAsync();
            unlinkedSubjectId = other.Id;
            ctx.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeId });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var tree = await NewService(read).GetTreeAsync();

        tree.Subjects.Select(s => s.Id).ShouldContain(subjectId);
        tree.Subjects.Select(s => s.Id).ShouldContain(unlinkedSubjectId);
    }

    [Fact]
    public async Task GetTree_filtered_by_grade_returns_only_subjects_linked_to_that_grade()
    {
        var (gradeId, linkedSubjectId, _) = await SeedAsync();
        int otherGradeId, unlinkedSubjectId;
        await using (var ctx = _db.NewContext())
        {
            var otherGrade = new Grade { Name = "4. Sınıf" };
            var unlinkedSubject = new Subject { Name = "Fen" };
            ctx.Grades.Add(otherGrade);
            ctx.Subjects.Add(unlinkedSubject);
            await ctx.SaveChangesAsync();
            otherGradeId = otherGrade.Id;
            unlinkedSubjectId = unlinkedSubject.Id;

            ctx.GradeSubjects.Add(new GradeSubject { SubjectId = linkedSubjectId, GradeId = gradeId });
            ctx.GradeSubjects.Add(new GradeSubject { SubjectId = unlinkedSubjectId, GradeId = otherGradeId });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var tree = await NewService(read).GetTreeAsync(gradeId: gradeId);

        var subject = tree.Subjects.ShouldHaveSingleItem();
        subject.Id.ShouldBe(linkedSubjectId);
    }

    [Fact]
    public async Task GetTree_unassignedOnly_returns_only_subjects_with_no_grade_link()
    {
        var (gradeId, linkedSubjectId, _) = await SeedAsync();
        int unlinkedSubjectId;
        await using (var ctx = _db.NewContext())
        {
            var unlinkedSubject = new Subject { Name = "Fen" };
            ctx.Subjects.Add(unlinkedSubject);
            await ctx.SaveChangesAsync();
            unlinkedSubjectId = unlinkedSubject.Id;

            ctx.GradeSubjects.Add(new GradeSubject { SubjectId = linkedSubjectId, GradeId = gradeId });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var tree = await NewService(read).GetTreeAsync(unassignedOnly: true);

        var subject = tree.Subjects.ShouldHaveSingleItem();
        subject.Id.ShouldBe(unlinkedSubjectId);
        subject.GradeIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetTree_reports_gradeIds_sorted_ascending_for_each_subject()
    {
        var (gradeId, subjectId, _) = await SeedAsync();
        int gradeIdHigh, gradeIdLow;
        await using (var ctx = _db.NewContext())
        {
            var high = new Grade { Name = "8. Sınıf" };
            var low = new Grade { Name = "1. Sınıf" };
            ctx.Grades.AddRange(high, low);
            await ctx.SaveChangesAsync();
            gradeIdHigh = high.Id;
            gradeIdLow = low.Id;

            // insert out of numeric order to prove the result is sorted, not insertion order
            ctx.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeIdHigh });
            ctx.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeId });
            ctx.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeIdLow });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var tree = await NewService(read).GetTreeAsync();

        var subject = tree.Subjects.Single(s => s.Id == subjectId);
        subject.GradeIds.ShouldBe(subject.GradeIds.OrderBy(x => x).ToList());
        subject.GradeIds.ShouldBe(new[] { gradeIdLow, gradeId, gradeIdHigh }.OrderBy(x => x).ToList());
    }

    // ---- CreateSubjectAsync / UpdateSubjectAsync with GradeIds ----

    [Fact]
    public async Task CreateSubject_with_null_gradeIds_creates_the_subject_with_no_grade_links()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSubjectAsync(new UpsertSubjectDto { Name = "Fen", GradeIds = null }, userId: 1);

        result.Success.ShouldBeTrue();
        await using var check = _db.NewContext();
        (await check.GradeSubjects.AnyAsync(gs => gs.SubjectId == result.ObjectId)).ShouldBeFalse();
    }

    [Fact]
    public async Task CreateSubject_with_gradeIds_links_the_subject_to_those_grades()
    {
        var (gradeId, _, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSubjectAsync(
            new UpsertSubjectDto { Name = "Fen", GradeIds = new List<int> { gradeId } }, userId: 1);

        result.Success.ShouldBeTrue();
        await using var check = _db.NewContext();
        var links = await check.GradeSubjects.Where(gs => gs.SubjectId == result.ObjectId).ToListAsync();
        links.ShouldHaveSingleItem().GradeId.ShouldBe(gradeId);
    }

    [Fact]
    public async Task CreateSubject_with_an_invalid_gradeId_fails_and_does_not_create_the_subject()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSubjectAsync(
            new UpsertSubjectDto { Name = "Fen", GradeIds = new List<int> { 9999 } }, userId: 1);

        result.Success.ShouldBeFalse();
        await using var check = _db.NewContext();
        (await check.Subjects.AnyAsync(s => s.Name == "Fen")).ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateSubject_with_null_gradeIds_leaves_existing_grade_links_untouched()
    {
        var (gradeId, subjectId, _) = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeId });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        var result = await NewService(ctx2).UpdateSubjectAsync(
            subjectId, new UpsertSubjectDto { Name = "Matematik Güncel", GradeIds = null }, userId: 1);

        result.Success.ShouldBeTrue();
        await using var check = _db.NewContext();
        var links = await check.GradeSubjects.Where(gs => gs.SubjectId == subjectId).ToListAsync();
        links.ShouldHaveSingleItem().GradeId.ShouldBe(gradeId);
    }

    [Fact]
    public async Task UpdateSubject_with_gradeIds_fully_syncs_removing_old_and_adding_new_links()
    {
        var (gradeId, subjectId, _) = await SeedAsync();
        int newGradeId;
        await using (var ctx = _db.NewContext())
        {
            ctx.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeId });
            var newGrade = new Grade { Name = "5. Sınıf" };
            ctx.Grades.Add(newGrade);
            await ctx.SaveChangesAsync();
            newGradeId = newGrade.Id;
        }

        await using var ctx2 = _db.NewContext();
        var result = await NewService(ctx2).UpdateSubjectAsync(
            subjectId, new UpsertSubjectDto { Name = "Matematik", GradeIds = new List<int> { newGradeId } }, userId: 1);

        result.Success.ShouldBeTrue();
        await using var check = _db.NewContext();
        var links = await check.GradeSubjects.Where(gs => gs.SubjectId == subjectId).ToListAsync();
        links.ShouldHaveSingleItem().GradeId.ShouldBe(newGradeId);
    }

    [Fact]
    public async Task UpdateSubject_with_an_invalid_gradeId_fails_and_does_not_change_the_name()
    {
        var (_, subjectId, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateSubjectAsync(
            subjectId, new UpsertSubjectDto { Name = "Yeni Ad", GradeIds = new List<int> { 9999 } }, userId: 1);

        result.Success.ShouldBeFalse();
        await using var check = _db.NewContext();
        (await check.Subjects.FindAsync(subjectId))!.Name.ShouldBe("Matematik");
    }

    // ---- AddSubjectGradeAsync ----

    [Fact]
    public async Task AddSubjectGrade_creates_a_new_link()
    {
        var (gradeId, subjectId, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).AddSubjectGradeAsync(subjectId, gradeId, userId: 1);

        result.Success.ShouldBeTrue();
        await using var check = _db.NewContext();
        (await check.GradeSubjects.AnyAsync(gs => gs.SubjectId == subjectId && gs.GradeId == gradeId)).ShouldBeTrue();
    }

    [Fact]
    public async Task AddSubjectGrade_is_idempotent_when_the_link_already_exists()
    {
        var (gradeId, subjectId, _) = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeId });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        var result = await NewService(ctx2).AddSubjectGradeAsync(subjectId, gradeId, userId: 1);

        result.Success.ShouldBeTrue();
        await using var check = _db.NewContext();
        (await check.GradeSubjects.CountAsync(gs => gs.SubjectId == subjectId && gs.GradeId == gradeId)).ShouldBe(1);
    }

    [Fact]
    public async Task AddSubjectGrade_fails_for_an_unknown_subject()
    {
        var (gradeId, _, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).AddSubjectGradeAsync(9999, gradeId, userId: 1);

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task AddSubjectGrade_fails_for_an_unknown_grade()
    {
        var (_, subjectId, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).AddSubjectGradeAsync(subjectId, 9999, userId: 1);

        result.Success.ShouldBeFalse();
    }

    // ---- RemoveSubjectGradeAsync ----

    [Fact]
    public async Task RemoveSubjectGrade_removes_the_link_but_leaves_topics_subtopics_and_questions_intact()
    {
        var (gradeId, subjectId, topicId) = await SeedAsync();
        int subTopicId, questionId;
        await using (var ctx = _db.NewContext())
        {
            ctx.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeId });
            var st = new SubTopic { Name = "x", TopicId = topicId };
            ctx.SubTopics.Add(st);
            var q = new Question { SubjectId = subjectId, TopicId = topicId };
            ctx.Questions.Add(q);
            await ctx.SaveChangesAsync();
            subTopicId = st.Id;
            questionId = q.Id;
        }

        await using var ctx2 = _db.NewContext();
        var result = await NewService(ctx2).RemoveSubjectGradeAsync(subjectId, gradeId, userId: 1);

        result.Success.ShouldBeTrue();
        await using var check = _db.NewContext();
        (await check.GradeSubjects.AnyAsync(gs => gs.SubjectId == subjectId && gs.GradeId == gradeId)).ShouldBeFalse();
        (await check.Topics.AnyAsync(t => t.Id == topicId)).ShouldBeTrue();
        (await check.SubTopics.AnyAsync(st => st.Id == subTopicId)).ShouldBeTrue();
        (await check.Questions.AnyAsync(q => q.Id == questionId)).ShouldBeTrue();
        (await check.Subjects.AnyAsync(s => s.Id == subjectId)).ShouldBeTrue();
    }

    [Fact]
    public async Task RemoveSubjectGrade_is_idempotent_when_no_link_exists()
    {
        var (gradeId, subjectId, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).RemoveSubjectGradeAsync(subjectId, gradeId, userId: 1);

        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task RemoveSubjectGrade_can_remove_the_last_remaining_link_leaving_the_subject_unassigned()
    {
        var (gradeId, subjectId, _) = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeId });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        (await NewService(ctx2).RemoveSubjectGradeAsync(subjectId, gradeId, userId: 1)).Success.ShouldBeTrue();

        await using var read = _db.NewContext();
        var tree = await NewService(read).GetTreeAsync(unassignedOnly: true);
        tree.Subjects.ShouldContain(s => s.Id == subjectId);
    }

    [Fact]
    public async Task RemoveSubjectGrade_fails_for_an_unknown_subject_or_grade()
    {
        var (gradeId, subjectId, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await NewService(ctx).RemoveSubjectGradeAsync(9999, gradeId, userId: 1)).Success.ShouldBeFalse();
        (await NewService(ctx).RemoveSubjectGradeAsync(subjectId, 9999, userId: 1)).Success.ShouldBeFalse();
    }

    public void Dispose() => _db.Dispose();
}
