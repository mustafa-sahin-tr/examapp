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

    /// <summary>Seeded data plus the (subject, grade) GradeSubject link the seeded topic lives under.</summary>
    private async Task<(int gradeId, int subjectId, int topicId)> SeedLinkedAsync()
    {
        var ids = await SeedAsync();
        await using var ctx = _db.NewContext();
        ctx.GradeSubjects.Add(new GradeSubject { SubjectId = ids.subjectId, GradeId = ids.gradeId });
        await ctx.SaveChangesAsync();
        return ids;
    }

    private async Task<int> AddGradeAsync(string name)
    {
        await using var ctx = _db.NewContext();
        var g = new Grade { Name = name };
        ctx.Grades.Add(g);
        await ctx.SaveChangesAsync();
        return g.Id;
    }

    private async Task LinkAsync(int subjectId, int gradeId)
    {
        await using var ctx = _db.NewContext();
        ctx.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeId });
        await ctx.SaveChangesAsync();
    }

    private async Task SoftDeleteTopicAsync(int topicId)
    {
        await using var ctx = _db.NewContext();
        ctx.Topics.Remove((await ctx.Topics.FindAsync(topicId))!);
        await ctx.SaveChangesAsync();
    }

    private async Task<List<int>> ActiveGradeIdsAsync(int subjectId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.GradeSubjects.Where(gs => gs.SubjectId == subjectId)
            .Select(gs => gs.GradeId).OrderBy(g => g).ToListAsync();
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
        var gradeId = await AddGradeAsync("3. Sınıf");
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSubjectAsync(
            new UpsertSubjectDto { Name = "  Fen  ", GradeIds = new List<int> { gradeId } }, userId: 7);

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

    // Issue #249: a subject with no grade link is unreachable from the grade-filtered admin screen.
    public static TheoryData<List<int>?> MissingGradeIds() => new() { (List<int>?)null, new List<int>() };

    [Theory]
    [MemberData(nameof(MissingGradeIds))]
    public async Task CreateSubject_without_gradeIds_is_rejected_and_creates_nothing(List<int>? gradeIds)
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSubjectAsync(new UpsertSubjectDto { Name = "Fen", GradeIds = gradeIds }, userId: 1);

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(TaxonomyErrorCodes.GradeRequired);
        result.Message.ShouldContain("en az bir sınıf");
        ScheduledAJob().ShouldBeFalse();
        await using var check = _db.NewContext();
        (await check.Subjects.AnyAsync(s => s.Name == "Fen")).ShouldBeFalse();
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
        var (gradeId, subjectId, topicId) = await SeedAsync();
        await SoftDeleteTopicAsync(topicId); // no topics in the old grade → its link may go (#249)
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
    public async Task RemoveSubjectGrade_removes_a_link_without_topics_and_leaves_other_grades_topics_intact()
    {
        var (topicGradeId, subjectId, topicId) = await SeedLinkedAsync();
        var gradeId = await AddGradeAsync("4. Sınıf"); // linked, but no topics in it
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
        (await check.GradeSubjects.AnyAsync(gs => gs.SubjectId == subjectId && gs.GradeId == topicGradeId)).ShouldBeTrue();
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
    public async Task RemoveSubjectGrade_rejects_removing_the_last_remaining_link_even_without_topics()
    {
        var (gradeId, subjectId, topicId) = await SeedLinkedAsync();
        await SoftDeleteTopicAsync(topicId); // no topics → only the last-link rule can block

        await using var ctx2 = _db.NewContext();
        var result = await NewService(ctx2).RemoveSubjectGradeAsync(subjectId, gradeId, userId: 1);

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(TaxonomyErrorCodes.LastGradeLink);
        result.Message.ShouldContain("en az bir sınıfa bağlı");
        ScheduledAJob().ShouldBeFalse();
        (await ActiveGradeIdsAsync(subjectId)).ShouldBe(new[] { gradeId });
    }

    [Fact]
    public async Task RemoveSubjectGrade_checks_the_last_link_rule_before_the_topic_rule()
    {
        var (gradeId, subjectId, _) = await SeedLinkedAsync(); // only link AND it has a topic

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).RemoveSubjectGradeAsync(subjectId, gradeId, userId: 1);

        result.ErrorCode.ShouldBe(TaxonomyErrorCodes.LastGradeLink);
    }

    [Fact]
    public async Task UpdateSubject_with_empty_gradeIds_leaves_links_untouched_so_it_can_never_drop_the_last_link()
    {
        var (gradeId, subjectId, _) = await SeedLinkedAsync();

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateSubjectAsync(
            subjectId, new UpsertSubjectDto { Name = "Matematik", GradeIds = new List<int>() }, userId: 1);

        result.Success.ShouldBeTrue();
        (await ActiveGradeIdsAsync(subjectId)).ShouldBe(new[] { gradeId });
    }

    [Fact]
    public async Task RemoveSubjectGrade_fails_for_an_unknown_subject_or_grade()
    {
        var (gradeId, subjectId, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await NewService(ctx).RemoveSubjectGradeAsync(9999, gradeId, userId: 1)).Success.ShouldBeFalse();
        (await NewService(ctx).RemoveSubjectGradeAsync(subjectId, 9999, userId: 1)).Success.ShouldBeFalse();
    }

    // ---- Issue #249: no "sahipsiz" (unreachable) taxonomy records ----

    [Fact]
    public async Task RemoveSubjectGrade_is_rejected_while_the_subject_has_topics_in_that_grade()
    {
        var (gradeId, subjectId, topicId) = await SeedLinkedAsync();
        var otherGrade = await AddGradeAsync("4. Sınıf");
        await LinkAsync(subjectId, otherGrade); // not the last link → the topic rule is what blocks

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).RemoveSubjectGradeAsync(subjectId, gradeId, userId: 1);

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(TaxonomyErrorCodes.SubjectGradeHasTopics);
        result.Message.ShouldContain("3. Sınıf");
        result.Message.ShouldContain("konuları taşıyın veya silin");
        ScheduledAJob().ShouldBeFalse();
        (await ActiveGradeIdsAsync(subjectId)).ShouldBe(new[] { gradeId, otherGrade }.OrderBy(g => g).ToList());
        await using var check = _db.NewContext();
        (await check.Topics.AnyAsync(t => t.Id == topicId)).ShouldBeTrue();
    }

    [Fact]
    public async Task RemoveSubjectGrade_ignores_soft_deleted_topics()
    {
        var (gradeId, subjectId, topicId) = await SeedLinkedAsync();
        var otherGrade = await AddGradeAsync("4. Sınıf");
        await LinkAsync(subjectId, otherGrade);
        await SoftDeleteTopicAsync(topicId);

        await using var ctx = _db.NewContext();
        (await NewService(ctx).RemoveSubjectGradeAsync(subjectId, gradeId, userId: 1)).Success.ShouldBeTrue();

        (await ActiveGradeIdsAsync(subjectId)).ShouldBe(new[] { otherGrade });
    }

    [Fact]
    public async Task UpdateSubject_sync_is_blocked_by_topics_in_a_soft_deleted_grade()
    {
        // The link to gradeA still exists and gradeA's topic is active, but the Grade row itself is
        // soft-deleted — the global filter must not hide the blocking topic (Düşük-4).
        var (gradeA, subjectId, _) = await SeedLinkedAsync();
        var gradeB = await AddGradeAsync("4. Sınıf");
        await using (var ctx = _db.NewContext())
        {
            ctx.Grades.Remove((await ctx.Grades.FindAsync(gradeA))!);
            await ctx.SaveChangesAsync();
        }

        await using var upd = _db.NewContext();
        var result = await NewService(upd).UpdateSubjectAsync(
            subjectId, new UpsertSubjectDto { Name = "Matematik", GradeIds = new List<int> { gradeB } }, userId: 1);

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(TaxonomyErrorCodes.SubjectGradeHasTopics);
        result.Message.ShouldContain("3. Sınıf"); // name resolved even though the grade is soft-deleted
        await using var check = _db.NewContext();
        (await check.GradeSubjects.AnyAsync(gs => gs.SubjectId == subjectId && gs.GradeId == gradeA)).ShouldBeTrue();
        (await check.GradeSubjects.AnyAsync(gs => gs.SubjectId == subjectId && gs.GradeId == gradeB)).ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateSubject_rejected_sync_does_not_schedule_a_cache_reconcile_but_an_accepted_one_does()
    {
        var (gradeA, subjectId, _) = await SeedLinkedAsync();
        var gradeB = await AddGradeAsync("4. Sınıf");

        await using (var rejected = _db.NewContext())
        {
            (await NewService(rejected).UpdateSubjectAsync(
                subjectId, new UpsertSubjectDto { Name = "Matematik", GradeIds = new List<int> { gradeB } }, userId: 1))
                .ErrorCode.ShouldBe(TaxonomyErrorCodes.SubjectGradeHasTopics);
        }
        ScheduledAJob().ShouldBeFalse();

        // Positive control: the same detector sees the Schedule call of an accepted sync.
        await using var accepted = _db.NewContext();
        (await NewService(accepted).UpdateSubjectAsync(
            subjectId, new UpsertSubjectDto { Name = "Matematik", GradeIds = new List<int> { gradeA, gradeB } }, userId: 1))
            .Success.ShouldBeTrue();
        ScheduledAJob().ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateSubject_sync_is_rejected_atomically_when_a_removed_grade_still_has_topics()
    {
        // Links: gradeA (has the seeded topic), gradeB (empty). Request: keep only a brand-new gradeC.
        var (gradeA, subjectId, _) = await SeedLinkedAsync();
        var gradeB = await AddGradeAsync("4. Sınıf");
        var gradeC = await AddGradeAsync("5. Sınıf");
        await using (var ctx = _db.NewContext())
        {
            ctx.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeB });
            await ctx.SaveChangesAsync();
        }

        await using var upd = _db.NewContext();
        var result = await NewService(upd).UpdateSubjectAsync(
            subjectId, new UpsertSubjectDto { Name = "Yeni Ad", GradeIds = new List<int> { gradeC } }, userId: 1);

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(TaxonomyErrorCodes.SubjectGradeHasTopics);
        result.Message.ShouldContain("3. Sınıf");
        result.Message.ShouldNotContain("4. Sınıf"); // only the blocking grade is named
        ScheduledAJob().ShouldBeFalse();

        // Nothing written: name unchanged, gradeB not removed, gradeC not added.
        await using var check = _db.NewContext();
        (await check.Subjects.FindAsync(subjectId))!.Name.ShouldBe("Matematik");
        (await ActiveGradeIdsAsync(subjectId)).ShouldBe(new[] { gradeA, gradeB }.OrderBy(g => g).ToList());
    }

    [Fact]
    public async Task UpdateSubject_sync_keeping_the_grade_with_topics_is_allowed()
    {
        var (gradeA, subjectId, _) = await SeedLinkedAsync();
        var gradeB = await AddGradeAsync("4. Sınıf");
        var gradeC = await AddGradeAsync("5. Sınıf");
        await using (var ctx = _db.NewContext())
        {
            ctx.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeB });
            await ctx.SaveChangesAsync();
        }

        await using var upd = _db.NewContext();
        var result = await NewService(upd).UpdateSubjectAsync(
            subjectId, new UpsertSubjectDto { Name = "Matematik", GradeIds = new List<int> { gradeA, gradeC } }, userId: 1);

        result.Success.ShouldBeTrue();
        result.ErrorCode.ShouldBeNull();
        (await ActiveGradeIdsAsync(subjectId)).ShouldBe(new[] { gradeA, gradeC }.OrderBy(g => g).ToList());
    }

    [Fact]
    public async Task CreateTopic_is_rejected_when_the_subject_is_not_linked_to_the_grade()
    {
        var (gradeId, subjectId, _) = await SeedAsync(); // no GradeSubject link

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateTopicAsync(
            new UpsertTopicDto { Name = "Çıkarma", SubjectId = subjectId, GradeId = gradeId }, userId: 1);

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(TaxonomyErrorCodes.SubjectGradeNotLinked);
        result.Message.ShouldContain("sınıfa bağlı değil");
        ScheduledAJob().ShouldBeFalse();
        await using var check = _db.NewContext();
        (await check.Topics.AnyAsync(t => t.Name == "Çıkarma")).ShouldBeFalse();
    }

    [Fact]
    public async Task CreateTopic_is_rejected_when_the_link_is_soft_deleted()
    {
        var (gradeId, subjectId, _) = await SeedLinkedAsync();
        await using (var ctx = _db.NewContext())
        {
            var link = await ctx.GradeSubjects.FirstAsync(gs => gs.SubjectId == subjectId && gs.GradeId == gradeId);
            ctx.GradeSubjects.Remove(link); // soft delete via ApplyAuditInfo
            await ctx.SaveChangesAsync();
        }

        await using var create = _db.NewContext();
        var result = await NewService(create).CreateTopicAsync(
            new UpsertTopicDto { Name = "Çıkarma", SubjectId = subjectId, GradeId = gradeId }, userId: 1);

        result.ErrorCode.ShouldBe(TaxonomyErrorCodes.SubjectGradeNotLinked);
    }

    [Fact]
    public async Task CreateTopic_succeeds_when_the_subject_is_linked_to_the_grade()
    {
        var (gradeId, subjectId, _) = await SeedLinkedAsync();

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateTopicAsync(
            new UpsertTopicDto { Name = "Çıkarma", SubjectId = subjectId, GradeId = gradeId }, userId: 1);

        result.Success.ShouldBeTrue();
        result.ErrorCode.ShouldBeNull();
        await using var check = _db.NewContext();
        (await check.Topics.FindAsync(result.ObjectId))!.GradeId.ShouldBe(gradeId);
    }

    [Fact]
    public async Task UpdateTopic_is_rejected_when_moving_to_a_grade_the_subject_is_not_linked_to()
    {
        var (gradeId, subjectId, topicId) = await SeedLinkedAsync();
        var otherGrade = await AddGradeAsync("4. Sınıf"); // subject NOT linked here

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateTopicAsync(
            topicId, new UpsertTopicDto { Name = "Toplama 2", SubjectId = subjectId, GradeId = otherGrade }, userId: 1);

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(TaxonomyErrorCodes.SubjectGradeNotLinked);
        await using var check = _db.NewContext();
        var topic = (await check.Topics.FindAsync(topicId))!;
        topic.Name.ShouldBe("Toplama");
        topic.GradeId.ShouldBe(gradeId);
    }

    [Fact]
    public async Task UpdateTopic_can_rename_an_already_orphaned_topic_in_place()
    {
        var (gradeId, subjectId, topicId) = await SeedAsync(); // pre-#249 data: topic without a GradeSubject link

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateTopicAsync(
            topicId, new UpsertTopicDto { Name = "Toplama (düzeltildi)", SubjectId = subjectId, GradeId = gradeId }, userId: 1);

        result.Success.ShouldBeTrue();
        await using var check = _db.NewContext();
        (await check.Topics.FindAsync(topicId))!.Name.ShouldBe("Toplama (düzeltildi)");
    }

    [Fact]
    public async Task UpdateTopic_moving_an_orphaned_topic_to_another_unlinked_pair_is_still_rejected()
    {
        var (_, subjectId, topicId) = await SeedAsync(); // orphaned
        var otherGrade = await AddGradeAsync("4. Sınıf");

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateTopicAsync(
            topicId, new UpsertTopicDto { Name = "Toplama", SubjectId = subjectId, GradeId = otherGrade }, userId: 1);

        result.ErrorCode.ShouldBe(TaxonomyErrorCodes.SubjectGradeNotLinked);
    }

    [Fact]
    public async Task UpdateTopic_succeeds_within_a_linked_subject_and_grade()
    {
        var (gradeId, subjectId, topicId) = await SeedLinkedAsync();

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateTopicAsync(
            topicId, new UpsertTopicDto { Name = "Toplama 2", SubjectId = subjectId, GradeId = gradeId }, userId: 1);

        result.Success.ShouldBeTrue();
        await using var check = _db.NewContext();
        (await check.Topics.FindAsync(topicId))!.Name.ShouldBe("Toplama 2");
    }

    public void Dispose() => _db.Dispose();
}
