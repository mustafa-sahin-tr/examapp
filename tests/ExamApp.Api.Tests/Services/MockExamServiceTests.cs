using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.MockExams;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class MockExamServiceTests : IDisposable
{
    private const int TeacherId = 10;
    private const int OtherTeacherId = 11;
    private const int StudentUserId = 20;
    private readonly TestDb _db = TestDb.Create();
    private int _gradeId;
    private StudentProfileDto _student = null!;

    public MockExamServiceTests()
    {
        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "8. Sınıf" };
        var school = new School { Name = "Okul" };
        ctx.AddRange(grade, school);
        ctx.SaveChanges();
        var student = new Student { UserId = StudentUserId, StudentNumber = "S1", SchoolName = "s", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow, GradeId = grade.Id };
        ctx.Add(student);
        ctx.SaveChanges();
        _gradeId = grade.Id;
        _student = new StudentProfileDto { Id = student.Id, GradeId = grade.Id, SchoolId = school.Id };
    }

    public void Dispose() => _db.Dispose();

    private static MockExamService Service(AppDbContext ctx) => new(ctx, new QuestionTestLinkService(ctx));

    private async Task<T> UseAsync<T>(Func<MockExamService, Task<T>> work)
    {
        await using var ctx = _db.NewContext();
        return await work(Service(ctx));
    }

    private async Task<List<int>> QuestionsAsync(int count, int owner = TeacherId, Action<Question>? configure = null)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(owner);
        var ids = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var question = new Question { Text = $"Soru {Guid.NewGuid():N}", Point = 5, DifficultyLevel = 1, InteractionType = McqGrader.Type };
            configure?.Invoke(question);
            ctx.Questions.Add(question);
            await ctx.SaveChangesAsync();
            var answers = new[] { new Answer { QuestionId = question.Id, Text = "doğru", Order = 1 }, new Answer { QuestionId = question.Id, Text = "yanlış", Order = 2 } };
            ctx.Answers.AddRange(answers);
            await ctx.SaveChangesAsync();
            question.CorrectAnswerId = answers[0].Id;
            await ctx.SaveChangesAsync();
            ids.Add(question.Id);
        }
        return ids;
    }

    private Task<MockExamDetailDto> CreateAsync(int templateId, int owner = TeacherId) =>
        UseAsync(async s => (await s.CreateAsync(new CreateMockExamDto { ExamTemplateId = templateId, Name = "Deneme 1", GradeId = _gradeId }, owner, default))!);

    /// <summary>Şablondaki bütün dersleri doldurur.</summary>
    private async Task<MockExamDetailDto> FilledAsync(int templateId)
    {
        var exam = await CreateAsync(templateId);
        foreach (var section in exam.Sessions.SelectMany(s => s.Sections))
        {
            var ids = await QuestionsAsync(section.RequiredCount);
            exam = (await UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, section.SectionId, ids, TeacherId, false, default)))!;
        }
        return exam;
    }

    private async Task<int> ExamWorksheetVisibilityAsync(MockExamDetailDto exam)
    {
        await using var ctx = _db.NewContext();
        var worksheetId = exam.Sessions[0].WorksheetId;
        return (int)(await ctx.Worksheets.AsNoTracking().SingleAsync(w => w.Id == worksheetId)).StudentVisibility;
    }

    /// <summary>Gerçek test akışı: başlatır, ders başına verilen sayıda doğru ve yanlış işaretler, bitirir. Kalanlar boş kalır.</summary>
    private async Task SolveAsync(MockExamSessionDetailDto session, Dictionary<string, (int Correct, int Wrong)> answers,
        (StudentProfileDto Profile, int UserId)? who = null, bool finish = true)
    {
        var (student, userId) = who ?? (_student, StudentUserId);
        int instanceId;
        await using (var ctx = _db.NewContext())
            instanceId = (await new TestSessionService(ctx).StartTestAsync(session.WorksheetId, student)).InstanceId;

        foreach (var section in session.Sections)
        {
            if (!answers.TryGetValue(section.Name, out var plan))
                continue;
            for (var i = 0; i < plan.Correct + plan.Wrong; i++)
            {
                await using var ctx = _db.NewContext();
                var questionId = section.QuestionIds[i];
                var row = await ctx.TestInstanceQuestions.AsNoTracking().SingleAsync(r => r.WorksheetInstanceId == instanceId && r.WorksheetQuestion.QuestionId == questionId);
                var choices = await ctx.Answers.AsNoTracking().Where(a => a.QuestionId == questionId).OrderBy(a => a.Order).Select(a => a.Id).ToListAsync();
                var saved = await new TestSessionService(ctx).SaveAnswer(
                    new SaveAnswerDto { TestInstanceId = instanceId, TestQuestionId = row.Id, SelectedAnswerId = i < plan.Correct ? choices[0] : choices[1], TimeTaken = 3 },
                    new UserProfileDto { Id = userId, KeycloakId = "kc", Role = "Student" });
                saved.Success.ShouldBeTrue(saved.Message);
            }
        }

        if (!finish)
            return;
        await using var end = _db.NewContext();
        (await new TestSessionService(end).EndTest(instanceId, userId)).Success.ShouldBeTrue();
    }

    /// <summary>#1094: aynı sınıf ve okulda başka bir öğrenci.</summary>
    private async Task<(StudentProfileDto Profile, int UserId)> OtherStudentAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        var student = new Student
        {
            UserId = userId, StudentNumber = $"S{userId}", SchoolName = "s", SchoolId = _student.SchoolId, SchoolVerifiedAt = DateTime.UtcNow,
            GradeId = _gradeId,
        };
        ctx.Add(student);
        await ctx.SaveChangesAsync();
        return (new StudentProfileDto { Id = student.Id, GradeId = _gradeId, SchoolId = _student.SchoolId }, userId);
    }

    // ---- şablonlar

    [Theory]
    [InlineData(ExamTemplateSeed.LgsId, "lgs", 3, 2, 90, 155)]
    [InlineData(ExamTemplateSeed.TytId, "tyt", 4, 1, 120, 165)]
    [InlineData(ExamTemplateSeed.AytId, "ayt", 4, 1, 160, 180)]
    public async Task Templates_match_the_official_guides(int id, string code, int penalty, int sessions, int questions, int minutes)
    {
        var template = (await UseAsync(s => s.ListTemplatesAsync(default))).Single(t => t.Id == id);

        template.Code.ShouldBe(code);
        template.WrongAnswersPerCorrect.ShouldBe(penalty);
        template.Sessions.Count.ShouldBe(sessions);
        template.Sessions.SelectMany(s => s.Sections).Sum(s => s.QuestionCount).ShouldBe(questions);
        template.Sessions.Sum(s => s.DurationSeconds).ShouldBe(minutes * 60);
        template.SourceReference.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Lgs_sessions_have_their_official_durations_and_subjects()
    {
        var lgs = (await UseAsync(s => s.ListTemplatesAsync(default))).Single(t => t.Id == ExamTemplateSeed.LgsId);

        lgs.Sessions[0].DurationSeconds.ShouldBe(75 * 60);
        lgs.Sessions[0].Sections.Select(s => (s.Name, s.QuestionCount)).ShouldBe([("Türkçe", 20), ("T.C. İnkılap Tarihi ve Atatürkçülük", 10), ("Din Kültürü ve Ahlak Bilgisi", 10), ("Yabancı Dil", 10)]);
        lgs.Sessions[1].DurationSeconds.ShouldBe(80 * 60);
        lgs.Sessions[1].Sections.Select(s => (s.Name, s.QuestionCount)).ShouldBe([("Matematik", 20), ("Fen Bilimleri", 20)]);
    }

    private static ExamTemplateDto NewTemplate(string code = "ozel") => new()
    {
        Code = code,
        Name = "Özel",
        WrongAnswersPerCorrect = 0,
        Sessions = [new ExamTemplateSessionDto { Name = "Tek", DurationSeconds = 600, Sections = [new ExamTemplateSectionDto { Name = "Ders", QuestionCount = 2 }] }],
    };

    [Fact]
    public async Task Admin_can_define_a_template_and_change_it_until_it_is_used()
    {
        var created = await UseAsync(s => s.SaveTemplateAsync(NewTemplate(), null, 1, default));
        created.Id.ShouldBeGreaterThan(0);

        var changed = NewTemplate();
        changed.Sessions[0].Sections[0].QuestionCount = 3;
        var saved = await UseAsync(s => s.SaveTemplateAsync(changed, created.Id, 1, default));
        saved.Sessions.Single().Sections.Single().QuestionCount.ShouldBe(3);

        await CreateAsync(created.Id);
        var error = await Should.ThrowAsync<MockExamRuleException>(() => UseAsync(s => s.SaveTemplateAsync(changed, created.Id, 1, default)));
        error.Message.ShouldBe(MockExamService.TemplateInUse);
    }

    [Fact]
    public async Task Template_list_counts_the_mock_exams_built_from_each_template()
    {
        var created = await UseAsync(s => s.SaveTemplateAsync(NewTemplate("sayim"), null, 1, default));
        (await UseAsync(s => s.ListTemplatesAsync(default))).Single(t => t.Id == created.Id).MockExamCount.ShouldBe(0);

        await CreateAsync(created.Id);
        await CreateAsync(created.Id);

        var listed = (await UseAsync(s => s.ListTemplatesAsync(default))).Single(t => t.Id == created.Id);
        listed.MockExamCount.ShouldBe(2);
        listed.InUse.ShouldBeTrue();
    }

    [Fact]
    public async Task Official_templates_are_locked_but_a_copy_can_be_saved_and_edited_issue917()
    {
        var templates = await UseAsync(s => s.ListTemplatesAsync(default));
        var lgs = templates.Single(t => t.Id == ExamTemplateSeed.LgsId);
        lgs.IsOfficial.ShouldBeTrue();

        var error = await Should.ThrowAsync<MockExamRuleException>(() => UseAsync(s => s.SaveTemplateAsync(lgs, lgs.Id, 1, default)));
        error.Message.ShouldBe(MockExamService.OfficialTemplate);

        lgs.Code = "lgs-kopya";
        lgs.Name = "LGS (kopya)";
        var copy = await UseAsync(s => s.SaveTemplateAsync(lgs, null, 1, default));
        copy.IsOfficial.ShouldBeFalse();
        copy.Sessions.Sum(x => x.Sections.Sum(y => y.QuestionCount)).ShouldBe(90);

        lgs.Sessions[0].Sections[0].QuestionCount = 15;
        (await UseAsync(s => s.SaveTemplateAsync(lgs, copy.Id, 1, default))).Sessions[0].Sections[0].QuestionCount.ShouldBe(15);
    }

    [Fact]
    public async Task Template_code_is_unique()
    {
        var error = await Should.ThrowAsync<MockExamRuleException>(() => UseAsync(s => s.SaveTemplateAsync(NewTemplate("LGS"), null, 1, default)));

        error.Message.ShouldBe(MockExamService.CodeTaken);
    }

    // ---- kurulum

    [Fact]
    public async Task Each_session_is_a_closed_test_with_its_own_server_time_limit()
    {
        var exam = await CreateAsync(ExamTemplateSeed.LgsId);

        await using var ctx = _db.NewContext();
        var worksheets = await ctx.Worksheets.AsNoTracking().OrderBy(w => w.Id).ToListAsync();
        worksheets.Select(w => w.MaxDurationSeconds).ShouldBe([75 * 60, 80 * 60]);
        worksheets.ShouldAllBe(w => w.StudentVisibility == WorksheetStudentVisibility.Restricted && w.TeacherSharing == WorksheetTeacherSharing.Private && w.Kind == WorksheetKind.Test && w.CreateUserId == TeacherId);
        exam.PublishedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Unknown_template_or_grade_is_refused()
    {
        (await Should.ThrowAsync<MockExamRuleException>(() => CreateAsync(9999))).Message.ShouldBe(MockExamService.WrongTemplate);
        (await Should.ThrowAsync<MockExamRuleException>(() => UseAsync(s => s.CreateAsync(new CreateMockExamDto { ExamTemplateId = 1, Name = "x", GradeId = 9999 }, TeacherId, default)))).Message.ShouldBe(MockExamService.WrongGrade);
    }

    [Fact]
    public async Task Cannot_be_published_with_wrong_question_counts()
    {
        var exam = await CreateAsync(ExamTemplateSeed.LgsId);
        var section = exam.Sessions[0].Sections[0];
        var ids = await QuestionsAsync(5);
        await UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, section.SectionId, ids, TeacherId, false, default));

        var result = (await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default)))!;

        result.PublishedAt.ShouldBeNull();
        result.Problems.ShouldContain(p => p.Code == "WrongCount" && p.Section == "Türkçe" && p.Required == 20 && p.Actual == 5);
        result.Problems.Count(p => p.Code == "WrongCount").ShouldBe(6);
        (await ExamWorksheetVisibilityAsync(exam)).ShouldBe((int)WorksheetStudentVisibility.Restricted);
    }

    [Fact]
    public async Task Published_sessions_stay_out_of_test_lists_and_the_practice_pool()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        exam.Problems.ShouldBeEmpty();

        var published = (await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default)))!;

        published.PublishedAt.ShouldNotBeNull();
        (await ExamWorksheetVisibilityAsync(exam)).ShouldBe((int)WorksheetStudentVisibility.Restricted);
    }

    [Fact]
    public async Task Exam_open_to_the_grade_can_be_started_without_an_assignment()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default));

        await using var ctx = _db.NewContext();
        (await new TestSessionService(ctx).StartTestAsync(exam.Sessions[0].WorksheetId, _student)).InstanceId.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Draft_or_closed_exam_session_cannot_be_started()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        await using (var draft = _db.NewContext())
            await Should.ThrowAsync<UnauthorizedAccessException>(() => new TestSessionService(draft).StartTestAsync(exam.Sessions[0].WorksheetId, _student));

        await UseAsync(s => s.PublishAsync(exam.Id, false, TeacherId, false, default));

        await using var closed = _db.NewContext();
        await Should.ThrowAsync<UnauthorizedAccessException>(() => new TestSessionService(closed).StartTestAsync(exam.Sessions[0].WorksheetId, _student));
    }

    [Fact]
    public async Task Exam_of_another_grade_cannot_be_started()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default));

        await using var ctx = _db.NewContext();
        var otherGrade = new StudentProfileDto { Id = _student.Id, GradeId = _gradeId + 100, SchoolId = _student.SchoolId };
        await Should.ThrowAsync<UnauthorizedAccessException>(() => new TestSessionService(ctx).StartTestAsync(exam.Sessions[0].WorksheetId, otherGrade));
    }

    [Fact]
    public async Task Sessions_cannot_be_changed_through_the_test_screens()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        var worksheetId = exam.Sessions[0].WorksheetId;

        await using var ctx = _db.NewContext();
        (await new QuestionOwnershipGuard(ctx).CanModifyWorksheetAsync(worksheetId, TeacherId, false, default)).ShouldBe(QuestionAccessResult.Forbidden);
        (await new QuestionOwnershipGuard(ctx).CanModifyWorksheetAsync(worksheetId, 1, true, default)).ShouldBe(QuestionAccessResult.Forbidden);
    }

    [Fact]
    public async Task Ids_that_are_not_positive_change_nothing()
    {
        var exam = await CreateAsync(ExamTemplateSeed.LgsId);
        var ids = await QuestionsAsync(1);

        (await Should.ThrowAsync<MockExamRuleException>(() => UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, exam.Sessions[0].Sections[0].SectionId, [ids[0], -1], TeacherId, false, default)))).Message.ShouldBe(MockExamService.QuestionNotUsable);

        await using var ctx = _db.NewContext();
        (await ctx.TestQuestions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Item_without_a_section_blocks_publishing_and_is_cleaned_on_the_next_change()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        var stray = await QuestionsAsync(1);
        await using (var ctx = _db.NewContext())
        {
            ctx.TestQuestions.Add(new WorksheetQuestion { TestId = exam.Sessions[0].WorksheetId, QuestionId = stray[0], Order = 99 });
            await ctx.SaveChangesAsync();
        }

        var blocked = (await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default)))!;
        blocked.PublishedAt.ShouldBeNull();
        blocked.Problems.ShouldContain(p => p.Code == "Unmapped" && p.QuestionId == stray[0]);

        var section = exam.Sessions[0].Sections[0];
        var cleaned = (await UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, section.SectionId, section.QuestionIds, TeacherId, false, default)))!;
        cleaned.Problems.ShouldBeEmpty();
    }

    [Fact]
    public async Task Example_question_blocks_publishing_because_it_shows_its_answer()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        var id = exam.Sessions[0].Sections[0].QuestionIds[0];
        await using (var ctx = _db.NewContext())
        {
            (await ctx.Questions.SingleAsync(q => q.Id == id)).IsExample = true;
            await ctx.SaveChangesAsync();
        }

        (await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default)))!.Problems.ShouldContain(p => p.Code == "ExampleQuestion" && p.QuestionId == id);
    }

    [Fact]
    public async Task Question_deleted_after_answering_counts_as_wrong_so_net_does_not_grow()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default));
        await SolveAsync(exam.Sessions[0], new() { ["Türkçe"] = (0, 3) });
        var id = exam.Sessions[0].Sections[0].QuestionIds[0];
        await using (var ctx = _db.NewContext())
        {
            (await ctx.Questions.SingleAsync(q => q.Id == id)).IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        var turkish = (await UseAsync(s => s.GetResultAsync(exam.Id, _student, default)))!.Sections.Single(s => s.Name == "Türkçe");

        (turkish.Correct, turkish.Wrong, turkish.Blank).ShouldBe((0, 3, 17));
        turkish.Net.ShouldBe(-1m);
    }

    // ---- #1094: sıralama

    [Fact]
    public async Task Ranking_counts_students_who_finished_every_session_and_ties_share_a_rank()
    {
        var exam = await FilledAsync(ExamTemplateSeed.TytId);
        await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default));
        var session = exam.Sessions.ShouldHaveSingleItem();
        var section = session.Sections[0].Name;

        await SolveAsync(session, new() { [section] = (3, 0) }); // net 3
        var others = new (int Correct, int Wrong)[] { (5, 0), (3, 0), (1, 0), (0, 0), (2, 4) }; // 5, 3, 1, 0, 1
        for (var i = 0; i < others.Length; i++)
            await SolveAsync(session, new() { [section] = others[i] }, await OtherStudentAsync(9500 + i));
        // Süren oturum sayılmaz.
        await SolveAsync(session, new() { [section] = (10, 0) }, await OtherStudentAsync(9600), finish: false);

        var ranking = (await UseAsync(s => s.GetResultAsync(exam.Id, _student, default)))!.Ranking!;

        ranking.Takers.ShouldBe(6);
        ranking.Rank.ShouldBe(2); // 5 > 3; eşit 3 aynı sırada
        ranking.TopPercent.ShouldBe(34); // ⌈200 / 6⌉
        (await UseAsync(s => s.GetLatestCompleteResultsAsync(_student.Id, ["tyt"], default))).ShouldHaveSingleItem().Ranking!.Rank.ShouldBe(2);
    }

    [Fact]
    public async Task Ranking_hides_the_rank_below_five_takers_and_is_absent_until_the_result_is_complete()
    {
        var lgs = await FilledAsync(ExamTemplateSeed.LgsId);
        await UseAsync(s => s.PublishAsync(lgs.Id, true, TeacherId, false, default));
        await SolveAsync(lgs.Sessions[0], new() { ["Türkçe"] = (2, 0) });
        (await UseAsync(s => s.GetResultAsync(lgs.Id, _student, default)))!.Ranking.ShouldBeNull();

        await SolveAsync(lgs.Sessions[1], []);
        var ranking = (await UseAsync(s => s.GetResultAsync(lgs.Id, _student, default)))!.Ranking!;
        ranking.Takers.ShouldBe(1);
        ranking.Rank.ShouldBeNull();
        ranking.TopPercent.ShouldBeNull();
    }

    [Fact]
    public async Task Finished_exam_stays_with_the_student_after_changing_grade()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default));
        await SolveAsync(exam.Sessions[0], new() { ["Türkçe"] = (2, 0) });
        var moved = new StudentProfileDto { Id = _student.Id, GradeId = _gradeId + 100, SchoolId = null };

        (await UseAsync(s => s.GetResultAsync(exam.Id, moved, default)))!.Correct.ShouldBe(2);

        var item = (await UseAsync(s => s.ListForStudentAsync(moved, default))).ShouldHaveSingleItem();
        item.Sessions[0].CanStart.ShouldBeTrue();
        item.Sessions[1].CanStart.ShouldBeFalse();
    }

    [Fact]
    public async Task Published_exam_cannot_be_changed_or_deleted()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default));
        var section = exam.Sessions[0].Sections[0];

        (await Should.ThrowAsync<MockExamRuleException>(() => UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, section.SectionId, [], TeacherId, false, default)))).Message.ShouldBe(MockExamService.AlreadyPublished);
        (await Should.ThrowAsync<MockExamRuleException>(() => UseAsync(s => s.DeleteDraftAsync(exam.Id, TeacherId, false, default)))).Message.ShouldBe(MockExamService.AlreadyPublished);
        (await Should.ThrowAsync<MockExamRuleException>(() => UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default)))).Message.ShouldBe(MockExamService.AlreadyPublished);
    }

    [Fact]
    public async Task Another_teacher_cannot_see_or_change_the_exam()
    {
        var exam = await CreateAsync(ExamTemplateSeed.LgsId);
        var section = exam.Sessions[0].Sections[0];

        (await UseAsync(s => s.GetAsync(exam.Id, OtherTeacherId, false, default))).ShouldBeNull();
        (await UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, section.SectionId, [], OtherTeacherId, false, default))).ShouldBeNull();
        (await UseAsync(s => s.PublishAsync(exam.Id, true, OtherTeacherId, false, default))).ShouldBeNull();
        (await UseAsync(s => s.DeleteDraftAsync(exam.Id, OtherTeacherId, false, default))).ShouldBeFalse();
        (await UseAsync(s => s.ListMineAsync(OtherTeacherId, false, default))).ShouldBeEmpty();
        (await UseAsync(s => s.GetAsync(exam.Id, 1, true, default))).ShouldNotBeNull();
    }

    [Fact]
    public async Task Section_of_another_template_is_not_accepted()
    {
        var exam = await CreateAsync(ExamTemplateSeed.LgsId);
        var other = await CreateAsync(ExamTemplateSeed.TytId);

        (await UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, other.Sessions[0].Sections[0].SectionId, [], TeacherId, false, default))).ShouldBeNull();
    }

    [Fact]
    public async Task A_section_takes_no_more_questions_than_it_needs_and_no_duplicates()
    {
        var exam = await CreateAsync(ExamTemplateSeed.LgsId);
        var din = exam.Sessions[0].Sections[2];
        var ids = await QuestionsAsync(11);

        (await Should.ThrowAsync<MockExamRuleException>(() => UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, din.SectionId, ids, TeacherId, false, default)))).Message.ShouldBe(MockExamService.TooManyQuestions);
        (await Should.ThrowAsync<MockExamRuleException>(() => UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, din.SectionId, [ids[0], ids[0]], TeacherId, false, default)))).Message.ShouldBe(MockExamService.QuestionTwice);
    }

    [Fact]
    public async Task The_same_question_cannot_count_in_two_sections_even_across_sessions()
    {
        var exam = await CreateAsync(ExamTemplateSeed.LgsId);
        var ids = await QuestionsAsync(1);
        await UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, exam.Sessions[0].Sections[0].SectionId, ids, TeacherId, false, default));

        (await Should.ThrowAsync<MockExamRuleException>(() => UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, exam.Sessions[0].Sections[1].SectionId, ids, TeacherId, false, default)))).Message.ShouldBe(MockExamService.QuestionTwice);
        (await Should.ThrowAsync<MockExamRuleException>(() => UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, exam.Sessions[1].Sections[0].SectionId, ids, TeacherId, false, default)))).Message.ShouldBe(MockExamService.QuestionTwice);
    }

    [Fact]
    public async Task Rules_for_test_questions_apply_open_text_and_someone_elses_private_question()
    {
        var exam = await CreateAsync(ExamTemplateSeed.LgsId);
        var section = exam.Sessions[0].Sections[0];
        var openText = await QuestionsAsync(1, configure: q => { q.InteractionType = OpenTextGrader.Type; q.InteractionPlan = """{"version":1,"type":"openText","maxWords":50}"""; });
        var others = await QuestionsAsync(1, OtherTeacherId);

        (await Should.ThrowAsync<MockExamRuleException>(() => UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, section.SectionId, openText, TeacherId, false, default)))).Message.ShouldBe(MockExamService.QuestionNotUsable);
        (await Should.ThrowAsync<MockExamRuleException>(() => UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, section.SectionId, others, TeacherId, false, default)))).Message.ShouldBe(MockExamService.QuestionNotUsable);
    }

    [Fact]
    public async Task Replacing_questions_removes_the_old_ones_and_keeps_section_order_in_the_test()
    {
        var exam = await CreateAsync(ExamTemplateSeed.LgsId);
        var turkish = exam.Sessions[0].Sections[0];
        var history = exam.Sessions[0].Sections[1];
        var a = await QuestionsAsync(2);
        var b = await QuestionsAsync(1);
        var c = await QuestionsAsync(1);
        // Önce ikinci ders doldurulur: sıra ekleme zamanından değil şablondan gelmeli.
        await UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, history.SectionId, b, TeacherId, false, default));
        await UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, turkish.SectionId, a, TeacherId, false, default));
        var detail = (await UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, turkish.SectionId, [c[0], a[1]], TeacherId, false, default)))!;

        detail.Sessions[0].Sections[0].QuestionIds.ShouldBe([c[0], a[1]]);
        await using var ctx = _db.NewContext();
        var worksheetId = exam.Sessions[0].WorksheetId;
        var order = await ctx.TestQuestions.AsNoTracking().Where(i => i.TestId == worksheetId).OrderBy(i => i.Order).Select(i => i.QuestionId).ToListAsync();
        order.ShouldBe([c[0], a[1], b[0]]);
    }

    [Fact]
    public async Task A_removed_question_can_be_added_again()
    {
        var exam = await CreateAsync(ExamTemplateSeed.LgsId);
        var turkish = exam.Sessions[0].Sections[0];
        var ids = await QuestionsAsync(2);
        await UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, turkish.SectionId, ids, TeacherId, false, default));
        await UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, turkish.SectionId, [ids[1]], TeacherId, false, default));

        var detail = (await UseAsync(s => s.SetSectionQuestionsAsync(exam.Id, turkish.SectionId, [ids[0], ids[1]], TeacherId, false, default)))!;

        detail.Sessions[0].Sections[0].QuestionIds.ShouldBe([ids[0], ids[1]]);
        await using var ctx = _db.NewContext();
        var worksheetId = exam.Sessions[0].WorksheetId;
        (await ctx.TestQuestions.AsNoTracking().Where(i => i.TestId == worksheetId).OrderBy(i => i.Order).Select(i => i.QuestionId).ToListAsync()).ShouldBe([ids[0], ids[1]]);
    }

    [Fact]
    public async Task A_question_that_stops_being_usable_blocks_publishing()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        var first = exam.Sessions[0].Sections[0].QuestionIds[0];
        var second = exam.Sessions[0].Sections[0].QuestionIds[1];
        var third = exam.Sessions[0].Sections[0].QuestionIds[2];
        await using (var ctx = _db.NewContext())
        {
            (await ctx.Questions.SingleAsync(q => q.Id == first)).IsUnpublished = true;
            (await ctx.Questions.SingleAsync(q => q.Id == second)).LicenseEndsAt = DateTime.UtcNow.AddDays(-1);
            (await ctx.Questions.SingleAsync(q => q.Id == third)).CorrectAnswerId = null;
            await ctx.SaveChangesAsync();
        }

        var result = (await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default)))!;

        result.PublishedAt.ShouldBeNull();
        result.Problems.ShouldContain(p => p.Code == "Unpublished" && p.QuestionId == first);
        result.Problems.ShouldContain(p => p.Code == "LicenseEnded" && p.QuestionId == second);
        result.Problems.ShouldContain(p => p.Code == "NoCorrectAnswer" && p.QuestionId == third);
    }

    [Fact]
    public async Task Draft_can_be_deleted_with_its_sessions()
    {
        var exam = await CreateAsync(ExamTemplateSeed.LgsId);

        (await UseAsync(s => s.DeleteDraftAsync(exam.Id, TeacherId, false, default))).ShouldBeTrue();

        (await UseAsync(s => s.ListMineAsync(TeacherId, false, default))).ShouldBeEmpty();
        await using var ctx = _db.NewContext();
        (await ctx.Worksheets.CountAsync()).ShouldBe(0);
    }

    // ---- öğrenci

    [Fact]
    public async Task Student_does_not_see_a_draft_or_another_grade()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        (await UseAsync(s => s.ListForStudentAsync(_student, default))).ShouldBeEmpty();
        (await UseAsync(s => s.GetResultAsync(exam.Id, _student, default))).ShouldBeNull();

        await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default));
        (await UseAsync(s => s.ListForStudentAsync(_student, default))).ShouldHaveSingleItem();

        var otherGrade = new StudentProfileDto { Id = _student.Id, GradeId = _gradeId + 100, SchoolId = _student.SchoolId };
        (await UseAsync(s => s.ListForStudentAsync(otherGrade, default))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Exam_not_open_to_the_grade_needs_an_assignment()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);

        await UseAsync(s => s.PublishAsync(exam.Id, false, TeacherId, false, default));

        (await ExamWorksheetVisibilityAsync(exam)).ShouldBe((int)WorksheetStudentVisibility.Restricted);
        (await UseAsync(s => s.ListForStudentAsync(_student, default))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Result_shows_correct_wrong_blank_and_net_per_subject_and_in_total()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default));

        await SolveAsync(exam.Sessions[0], new() { ["Türkçe"] = (15, 3), ["Yabancı Dil"] = (2, 1) });
        await SolveAsync(exam.Sessions[1], new() { ["Matematik"] = (10, 6) });

        var result = (await UseAsync(s => s.GetResultAsync(exam.Id, _student, default)))!;

        result.Complete.ShouldBeTrue();
        result.WrongAnswersPerCorrect.ShouldBe(3);
        var turkish = result.Sections.Single(s => s.Name == "Türkçe");
        (turkish.Correct, turkish.Wrong, turkish.Blank, turkish.Net).ShouldBe((15, 3, 2, 14m));
        var language = result.Sections.Single(s => s.Name == "Yabancı Dil");
        (language.Correct, language.Wrong, language.Blank, language.Net).ShouldBe((2, 1, 7, 1.67m));
        var math = result.Sections.Single(s => s.Name == "Matematik");
        (math.Correct, math.Wrong, math.Blank, math.Net).ShouldBe((10, 6, 4, 8m));
        var history = result.Sections.Single(s => s.Name == "T.C. İnkılap Tarihi ve Atatürkçülük");
        (history.Correct, history.Wrong, history.Blank, history.Net).ShouldBe((0, 0, 10, 0m));
        (result.Correct, result.Wrong, result.Blank).ShouldBe((27, 10, 53));
        result.Net.ShouldBe(23.67m);
    }

    [Fact]
    public async Task Unfinished_session_shows_no_numbers_so_answers_do_not_leak_during_the_exam()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default));
        await SolveAsync(exam.Sessions[0], new() { ["Türkçe"] = (5, 0) });
        await using (var start = _db.NewContext())
            await new TestSessionService(start).StartTestAsync(exam.Sessions[1].WorksheetId, _student);

        var result = (await UseAsync(s => s.GetResultAsync(exam.Id, _student, default)))!;

        result.Complete.ShouldBeFalse();
        result.Sections.Single(s => s.Name == "Türkçe").Pending.ShouldBeFalse();
        var math = result.Sections.Single(s => s.Name == "Matematik");
        math.Pending.ShouldBeTrue();
        (math.Correct, math.Wrong, math.Blank).ShouldBe((0, 0, 0));
        result.Correct.ShouldBe(5);
    }

    [Fact]
    public async Task List_shows_each_sessions_state()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default));
        await SolveAsync(exam.Sessions[0], new());

        var item = (await UseAsync(s => s.ListForStudentAsync(_student, default))).ShouldHaveSingleItem();

        item.Sessions.Select(s => s.Status).ShouldBe(["Finished", "NotStarted"]);
        item.Sessions[0].QuestionCount.ShouldBe(50);
        item.Sessions[0].InstanceId.ShouldNotBeNull();
        item.Sessions[1].DurationSeconds.ShouldBe(80 * 60);
    }

    [Fact]
    public async Task Expired_session_counts_as_finished()
    {
        var exam = await FilledAsync(ExamTemplateSeed.LgsId);
        await UseAsync(s => s.PublishAsync(exam.Id, true, TeacherId, false, default));
        await using (var start = _db.NewContext())
        {
            var started = await new TestSessionService(start).StartTestAsync(exam.Sessions[0].WorksheetId, _student);
            (await start.TestInstances.SingleAsync(i => i.Id == started.InstanceId)).StartTime = DateTime.UtcNow.AddHours(-5);
            await start.SaveChangesAsync();
        }

        var item = (await UseAsync(s => s.ListForStudentAsync(_student, default))).ShouldHaveSingleItem();
        item.Sessions[0].Status.ShouldBe("Finished");

        var result = (await UseAsync(s => s.GetResultAsync(exam.Id, _student, default)))!;
        result.Sections.Single(s => s.Name == "Türkçe").Blank.ShouldBe(20);
    }

    [Theory]
    [InlineData(10, 4, 4, 9)]
    [InlineData(10, 3, 3, 9)]
    [InlineData(10, 2, 3, 9.33)]
    [InlineData(0, 8, 4, -2)]
    [InlineData(10, 5, 0, 10)]
    public void Net_follows_the_exam_rule(int correct, int wrong, int penalty, double expected) =>
        MockExamService.Net(correct, wrong, penalty).ShouldBe((decimal)expected);
}
