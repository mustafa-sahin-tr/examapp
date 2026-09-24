using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.StudyLinks;
using ExamApp.Api.Services.StudyLinks;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #61 — konu/alt konu çalışma linkleri: 7 aktif link limiti (ekleme + aktifleştirme, pasifler sayılmaz),
/// URL güvenlik doğrulaması, kaynak tipi çıkarımı, kapsam kuralları, sıralama ve öğrenci sonuç ekranı önerileri
/// (yalnızca yanlış sorular, alt konuya göre gruplama, boş grupların atlanması, sahiplik).
/// </summary>
public class TopicStudyLinkServiceTests : IDisposable
{
    private const int TeacherUserId = 500;
    private const int StudentUserId = 7001;
    private const int OtherStudentUserId = 7002;

    private readonly TestDb _db = TestDb.Create();

    private static TopicStudyLinkService NewService(AppDbContext ctx) => new(ctx);

    private static UserProfileDto Teacher() => new() { Id = TeacherUserId, FullName = "Öğretmen Bir", Role = "Teacher" };

    public void Dispose() => _db.Dispose();

    private sealed record Taxonomy(int TopicId, int SubTopicA, int SubTopicB, int SubTopicC, int OtherTopicId, int OtherTopicSubTopic);

    private async Task<Taxonomy> SeedTaxonomyAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(grade, subject);
        await ctx.SaveChangesAsync();

        var topic = new Topic { Name = "Sayılar", SubjectId = subject.Id, GradeId = grade.Id };
        var otherTopic = new Topic { Name = "Geometri", SubjectId = subject.Id, GradeId = grade.Id };
        ctx.Topics.AddRange(topic, otherTopic);
        await ctx.SaveChangesAsync();

        var a = new SubTopic { Name = "Kesirler", TopicId = topic.Id };
        var b = new SubTopic { Name = "Ondalık Sayılar", TopicId = topic.Id };
        var c = new SubTopic { Name = "Yüzdeler", TopicId = topic.Id };
        var o = new SubTopic { Name = "Açılar", TopicId = otherTopic.Id };
        ctx.SubTopics.AddRange(a, b, c, o);
        await ctx.SaveChangesAsync();

        return new Taxonomy(topic.Id, a.Id, b.Id, c.Id, otherTopic.Id, o.Id);
    }

    private static CreateTopicStudyLinkDto NewLink(int? topicId = null, int? subTopicId = null, string url = "https://example.com/video",
        bool isActive = true, string title = "Kaynak", TopicStudyLinkSourceType? sourceType = null) => new()
    {
        TopicId = topicId,
        SubTopicId = subTopicId,
        Title = title,
        Url = url,
        IsActive = isActive,
        SourceType = sourceType,
    };

    private async Task<TopicStudyLinkResultDto> CreateAsync(CreateTopicStudyLinkDto dto)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).CreateAsync(dto, Teacher());
    }

    // ---------------- 7 aktif link limiti ----------------

    [Fact]
    public async Task Create_EighthActiveLinkOnSubTopic_IsRejectedWithConflictAndErrorCode()
    {
        var t = await SeedTaxonomyAsync();
        for (var i = 0; i < TopicStudyLinkLimits.MaxActiveLinksPerScope; i++)
            (await CreateAsync(NewLink(subTopicId: t.SubTopicA, title: $"L{i}"))).Success.ShouldBeTrue();

        var eighth = await CreateAsync(NewLink(subTopicId: t.SubTopicA, title: "L8"));

        eighth.Success.ShouldBeFalse();
        eighth.Conflict.ShouldBeTrue();
        eighth.ErrorCode.ShouldBe(TopicStudyLinkErrorCodes.ActiveLimitReached);
        eighth.Message.ShouldContain("7");

        await using var ctx = _db.NewContext();
        (await ctx.TopicStudyLinks.CountAsync(l => l.SubTopicId == t.SubTopicA)).ShouldBe(7);
    }

    [Fact]
    public async Task Create_EighthActiveLinkOnTopicLevel_IsRejected()
    {
        var t = await SeedTaxonomyAsync();
        for (var i = 0; i < 7; i++)
            (await CreateAsync(NewLink(topicId: t.TopicId))).Success.ShouldBeTrue();

        (await CreateAsync(NewLink(topicId: t.TopicId))).ErrorCode.ShouldBe(TopicStudyLinkErrorCodes.ActiveLimitReached);
    }

    [Fact]
    public async Task Create_InactiveLinksAreNotCounted_AndInactiveCanBeAddedBeyondLimit()
    {
        var t = await SeedTaxonomyAsync();
        for (var i = 0; i < 3; i++)
            (await CreateAsync(NewLink(subTopicId: t.SubTopicA, isActive: false))).Success.ShouldBeTrue();
        for (var i = 0; i < 7; i++)
            (await CreateAsync(NewLink(subTopicId: t.SubTopicA))).Success.ShouldBeTrue();

        // 7 aktif + 3 pasif: pasif eklemek serbest, aktif eklemek yasak.
        (await CreateAsync(NewLink(subTopicId: t.SubTopicA, isActive: false))).Success.ShouldBeTrue();
        (await CreateAsync(NewLink(subTopicId: t.SubTopicA))).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Create_LimitIsPerScope_SubTopicAndTopicLevelAndOtherSubTopicAreIndependent()
    {
        var t = await SeedTaxonomyAsync();
        for (var i = 0; i < 7; i++)
            (await CreateAsync(NewLink(subTopicId: t.SubTopicA))).Success.ShouldBeTrue();

        (await CreateAsync(NewLink(subTopicId: t.SubTopicB))).Success.ShouldBeTrue();
        (await CreateAsync(NewLink(topicId: t.TopicId))).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Create_DeletedLinksAreNotCounted()
    {
        var t = await SeedTaxonomyAsync();
        var ids = new List<int>();
        for (var i = 0; i < 7; i++)
            ids.Add((await CreateAsync(NewLink(subTopicId: t.SubTopicA))).ObjectId);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).DeleteAsync(ids[0], TeacherUserId)).Success.ShouldBeTrue();

        (await CreateAsync(NewLink(subTopicId: t.SubTopicA))).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_ActivatingWhenSevenActive_IsRejected_ButEditingAnActiveLinkIsAllowed()
    {
        var t = await SeedTaxonomyAsync();
        var inactiveId = (await CreateAsync(NewLink(subTopicId: t.SubTopicA, isActive: false))).ObjectId;
        var activeIds = new List<int>();
        for (var i = 0; i < 7; i++)
            activeIds.Add((await CreateAsync(NewLink(subTopicId: t.SubTopicA))).ObjectId);

        await using (var ctx = _db.NewContext())
        {
            var activate = await NewService(ctx).UpdateAsync(inactiveId,
                new UpdateTopicStudyLinkDto { Title = "x", Url = "https://example.com", IsActive = true }, TeacherUserId);
            activate.Success.ShouldBeFalse();
            activate.Conflict.ShouldBeTrue();
            activate.ErrorCode.ShouldBe(TopicStudyLinkErrorCodes.ActiveLimitReached);
        }

        // Zaten aktif olan linki (IsActive=true tekrar gönderilerek) düzenlemek limite takılmaz.
        await using (var ctx = _db.NewContext())
        {
            var edit = await NewService(ctx).UpdateAsync(activeIds[0],
                new UpdateTopicStudyLinkDto { Title = "Yeni başlık", Url = "https://example.com/new", IsActive = true }, TeacherUserId);
            edit.Success.ShouldBeTrue();
            edit.Link!.Title.ShouldBe("Yeni başlık");
        }

        await using (var ctx = _db.NewContext())
            (await ctx.TopicStudyLinks.SingleAsync(l => l.Id == inactiveId)).IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Update_DeactivateOneThenActivateAnother_Succeeds()
    {
        var t = await SeedTaxonomyAsync();
        var inactiveId = (await CreateAsync(NewLink(subTopicId: t.SubTopicA, isActive: false))).ObjectId;
        var firstActive = 0;
        for (var i = 0; i < 7; i++)
        {
            var id = (await CreateAsync(NewLink(subTopicId: t.SubTopicA))).ObjectId;
            if (i == 0) firstActive = id;
        }

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).UpdateAsync(firstActive,
                new UpdateTopicStudyLinkDto { Title = "x", Url = "https://example.com", IsActive = false }, TeacherUserId)).Success.ShouldBeTrue();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).UpdateAsync(inactiveId,
                new UpdateTopicStudyLinkDto { Title = "x", Url = "https://example.com", IsActive = true }, TeacherUserId)).Success.ShouldBeTrue();
    }

    // ---------------- URL / alan doğrulama ----------------

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(document.cookie)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/x")]
    [InlineData("/relative/path")]
    [InlineData("www.youtube.com/watch?v=abc")]
    [InlineData("https://youtube.com@evil.example/watch")]
    [InlineData("https://exa mple.com")]
    [InlineData("https://example.com/\tx")]
    public async Task Create_RejectsNonHttpOrUnsafeUrls(string url)
    {
        var t = await SeedTaxonomyAsync();

        var result = await CreateAsync(NewLink(subTopicId: t.SubTopicA, url: url));

        result.Success.ShouldBeFalse();
        result.Message.ShouldBe("Geçerli bir http/https URL giriniz.");
        await using var ctx = _db.NewContext();
        (await ctx.TopicStudyLinks.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Update_RejectsJavascriptUrl()
    {
        var t = await SeedTaxonomyAsync();
        var id = (await CreateAsync(NewLink(subTopicId: t.SubTopicA))).ObjectId;

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateAsync(id,
            new UpdateTopicStudyLinkDto { Title = "x", Url = "javascript:alert(1)" }, TeacherUserId);

        result.Success.ShouldBeFalse();
        (await ctx.TopicStudyLinks.AsNoTracking().SingleAsync(l => l.Id == id)).Url.ShouldBe("https://example.com/video");
    }

    [Fact]
    public async Task Create_RejectsTooLongUrlAndTitle_AndBlankTitle()
    {
        var t = await SeedTaxonomyAsync();

        (await CreateAsync(NewLink(subTopicId: t.SubTopicA, url: "https://example.com/" + new string('a', 2048)))).Success.ShouldBeFalse();
        (await CreateAsync(NewLink(subTopicId: t.SubTopicA, title: new string('t', 201)))).Success.ShouldBeFalse();
        (await CreateAsync(NewLink(subTopicId: t.SubTopicA, title: "   "))).Success.ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc", TopicStudyLinkSourceType.YouTube)]
    [InlineData("https://youtu.be/abc", TopicStudyLinkSourceType.YouTube)]
    [InlineData("https://m.youtube.com/watch?v=abc", TopicStudyLinkSourceType.YouTube)]
    [InlineData("https://example.com/youtube.com", TopicStudyLinkSourceType.Other)]
    [InlineData("https://notyoutube.com/watch", TopicStudyLinkSourceType.Other)]
    public async Task Create_InfersSourceTypeFromHost_WhenNotProvided(string url, TopicStudyLinkSourceType expected)
    {
        var t = await SeedTaxonomyAsync();

        var result = await CreateAsync(NewLink(subTopicId: t.SubTopicA, url: url));

        result.Success.ShouldBeTrue();
        result.Link!.SourceType.ShouldBe(expected);
    }

    [Fact]
    public async Task Create_YouTubeSourceTypeForNonYouTubeHost_IsRejected_OtherForYouTubeIsAccepted()
    {
        var t = await SeedTaxonomyAsync();

        (await CreateAsync(NewLink(subTopicId: t.SubTopicA, url: "https://example.com", sourceType: TopicStudyLinkSourceType.YouTube)))
            .Success.ShouldBeFalse();
        (await CreateAsync(NewLink(subTopicId: t.SubTopicA, url: "https://youtu.be/x", sourceType: TopicStudyLinkSourceType.Other)))
            .Link!.SourceType.ShouldBe(TopicStudyLinkSourceType.Other);
    }

    // ---------------- Kapsam ----------------

    [Fact]
    public async Task Create_WithoutTopicOrSubTopic_IsRejected()
    {
        await SeedTaxonomyAsync();
        (await CreateAsync(NewLink())).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Create_SubTopicOfAnotherTopic_IsRejected_AndSubTopicOnlyFillsParentTopic()
    {
        var t = await SeedTaxonomyAsync();

        (await CreateAsync(NewLink(topicId: t.TopicId, subTopicId: t.OtherTopicSubTopic))).Success.ShouldBeFalse();

        var ok = await CreateAsync(NewLink(subTopicId: t.SubTopicA));
        ok.Link!.TopicId.ShouldBe(t.TopicId);
        ok.Link.SubTopicId.ShouldBe(t.SubTopicA);
    }

    [Fact]
    public async Task Create_RecordsCreatorAndAppendsSortOrder()
    {
        var t = await SeedTaxonomyAsync();

        var first = await CreateAsync(NewLink(subTopicId: t.SubTopicA));
        var second = await CreateAsync(NewLink(subTopicId: t.SubTopicA));

        first.Link!.SortOrder.ShouldBe(0);
        second.Link!.SortOrder.ShouldBe(1);
        second.Link.CreatedByUserId.ShouldBe(TeacherUserId);
        second.Link.CreatedByName.ShouldBe("Öğretmen Bir");
        second.Link.CreatedByRole.ShouldBe("Teacher");
    }

    [Fact]
    public async Task List_TopicLevelExcludesSubTopicLinks_IncludesInactive_AndReportsActiveCount()
    {
        var t = await SeedTaxonomyAsync();
        await CreateAsync(NewLink(topicId: t.TopicId));
        await CreateAsync(NewLink(topicId: t.TopicId, isActive: false));
        await CreateAsync(NewLink(subTopicId: t.SubTopicA));

        await using var ctx = _db.NewContext();
        var list = await NewService(ctx).ListAsync(new TopicStudyLinkQueryDto { TopicId = t.TopicId });

        list.Success.ShouldBeTrue();
        list.Items.Count.ShouldBe(2);
        list.TotalCount.ShouldBe(2);
        list.ActiveCount.ShouldBe(1);
        list.MaxActiveLinks.ShouldBe(7);

        var activeOnly = await NewService(ctx).ListAsync(new TopicStudyLinkQueryDto { TopicId = t.TopicId, IncludeInactive = false });
        activeOnly.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task List_RequiresExactlyOneScope_AndUnknownScopeIsNotFound()
    {
        var t = await SeedTaxonomyAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.ListAsync(new TopicStudyLinkQueryDto())).Success.ShouldBeFalse();
        (await service.ListAsync(new TopicStudyLinkQueryDto { TopicId = t.TopicId, SubTopicId = t.SubTopicA })).Success.ShouldBeFalse();
        (await service.ListAsync(new TopicStudyLinkQueryDto { SubTopicId = 99999 })).NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task Reorder_UpdatesSortOrder_AndRejectsLinksOutsideScope()
    {
        var t = await SeedTaxonomyAsync();
        var a = (await CreateAsync(NewLink(subTopicId: t.SubTopicA, title: "a"))).ObjectId;
        var b = (await CreateAsync(NewLink(subTopicId: t.SubTopicA, title: "b"))).ObjectId;
        var foreign = (await CreateAsync(NewLink(subTopicId: t.SubTopicB, title: "f"))).ObjectId;

        await using (var ctx = _db.NewContext())
        {
            var bad = await NewService(ctx).ReorderAsync(new ReorderTopicStudyLinksDto
            {
                SubTopicId = t.SubTopicA,
                Items = { new() { Id = a, SortOrder = 1 }, new() { Id = foreign, SortOrder = 0 } }
            }, TeacherUserId);
            bad.Success.ShouldBeFalse();
        }

        await using (var ctx = _db.NewContext())
        {
            var ok = await NewService(ctx).ReorderAsync(new ReorderTopicStudyLinksDto
            {
                SubTopicId = t.SubTopicA,
                Items = { new() { Id = a, SortOrder = 1 }, new() { Id = b, SortOrder = 0 } }
            }, TeacherUserId);
            ok.Success.ShouldBeTrue();
            ok.Items.Select(i => i.Id).ShouldBe(new[] { b, a });
        }
    }

    [Fact]
    public async Task Delete_UnknownLink_IsNotFound()
    {
        await SeedTaxonomyAsync();
        await using var ctx = _db.NewContext();
        (await NewService(ctx).DeleteAsync(12345, TeacherUserId)).NotFound.ShouldBeTrue();
    }

    // ---------------- Öğrenci sonuç ekranı ----------------

    private sealed record ResultWorld(int InstanceId, int OtherStudentInstanceId, int InProgressInstanceId,
        int QWrongMulti, int QCorrect, int QWrongNoLinks, int QBlank, int QWrongSingle, Taxonomy Tax,
        int LinkA1, int LinkA2, int LinkB1, int LinkTopic1, int LinkOtherTopic1);

    /// <summary>
    /// Q1 (yanlış) → alt konu A + C + B (C'nin yalnızca pasif linki var); Q2 (doğru) → A; Q3 (yanlış) → yalnızca C;
    /// Q4 (boş bırakıldı) → A; Q5 (yanlış) → B + doğrudan Question.TopicId = Geometri.
    /// Ayrıca aynı sınavın başka öğrenciye ait ve devam eden kopyaları.
    /// <paramref name="withTopicLinks"/>: Sayılar konusuna 1 aktif + 1 pasif, Geometri'ye 1 aktif konu seviyesi link.
    /// </summary>
    private async Task<ResultWorld> SeedResultWorldAsync(bool withTopicLinks = false)
    {
        var tax = await SeedTaxonomyAsync();

        // Linkler: A → 2 aktif + 1 pasif; B → 1 aktif; C → yalnızca pasif (aşağıda).
        var linkA2 = (await CreateAsync(NewLink(subTopicId: tax.SubTopicA, title: "A2", url: "https://youtu.be/a2"))).ObjectId;
        var linkA1 = (await CreateAsync(NewLink(subTopicId: tax.SubTopicA, title: "A1", url: "https://example.com/a1"))).ObjectId;
        await CreateAsync(NewLink(subTopicId: tax.SubTopicA, title: "A-pasif", isActive: false));
        var linkB1 = (await CreateAsync(NewLink(subTopicId: tax.SubTopicB, title: "B1"))).ObjectId;
        int linkTopic1 = 0, linkOtherTopic1 = 0;
        if (withTopicLinks)
        {
            await CreateAsync(NewLink(topicId: tax.TopicId, title: "Sayılar-pasif", isActive: false));
            linkTopic1 = (await CreateAsync(NewLink(topicId: tax.TopicId, title: "Sayılar genel"))).ObjectId;
            linkOtherTopic1 = (await CreateAsync(NewLink(topicId: tax.OtherTopicId, title: "Geometri genel"))).ObjectId;
        }

        // A1'i A2'nin önüne al (SortOrder'a göre sıralama doğrulaması).
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).ReorderAsync(new ReorderTopicStudyLinksDto
            {
                SubTopicId = tax.SubTopicA,
                Items = { new() { Id = linkA1, SortOrder = 0 }, new() { Id = linkA2, SortOrder = 1 } }
            }, TeacherUserId);
        }

        await using var db = _db.NewContext();
        var grade = await db.Grades.FirstAsync();
        var student = new Student { UserId = StudentUserId, StudentNumber = "s1", SchoolName = "Sch", GradeId = grade.Id };
        var other = new Student { UserId = OtherStudentUserId, StudentNumber = "s2", SchoolName = "Sch", GradeId = grade.Id };
        var ws = new Worksheet { Name = "Deneme", Description = "d", GradeId = grade.Id, MaxDurationSeconds = 600 };
        var qs = Enumerable.Range(1, 5).Select(i => new Question { Text = $"Q{i}", DifficultyLevel = 2 }).ToList();
        qs[4].TopicId = tax.OtherTopicId; // doğrudan konu bağı (alt konusu B → Sayılar'dan farklı)
        db.AddRange(student, other, ws);
        db.Questions.AddRange(qs);
        await db.SaveChangesAsync();

        var answers = new List<(int correct, int wrong)>();
        foreach (var q in qs)
        {
            var c = new Answer { QuestionId = q.Id, Text = "c", Tag = "A", Order = 0 };
            var w = new Answer { QuestionId = q.Id, Text = "w", Tag = "B", Order = 1 };
            db.Answers.AddRange(c, w);
            await db.SaveChangesAsync();
            q.CorrectAnswerId = c.Id;
            answers.Add((c.Id, w.Id));
        }
        await db.SaveChangesAsync();

        // Alt konu bağları (QuestionSubTopic many-to-many).
        db.QuestionSubTopics.AddRange(
            new QuestionSubTopic { QuestionId = qs[0].Id, SubTopicId = tax.SubTopicA },
            new QuestionSubTopic { QuestionId = qs[0].Id, SubTopicId = tax.SubTopicC }, // linksiz grup → atlanmalı
            new QuestionSubTopic { QuestionId = qs[0].Id, SubTopicId = tax.SubTopicB },
            new QuestionSubTopic { QuestionId = qs[1].Id, SubTopicId = tax.SubTopicA }, // doğru cevap
            new QuestionSubTopic { QuestionId = qs[2].Id, SubTopicId = tax.SubTopicC }, // yanlış ama linksiz → soru atlanmalı
            new QuestionSubTopic { QuestionId = qs[3].Id, SubTopicId = tax.SubTopicA }, // boş bırakıldı
            new QuestionSubTopic { QuestionId = qs[4].Id, SubTopicId = tax.SubTopicB });
        await db.SaveChangesAsync();

        var wqs = qs.Select((q, i) => new WorksheetQuestion { TestId = ws.Id, QuestionId = q.Id, Order = i + 1 }).ToList();
        db.TestQuestions.AddRange(wqs);
        await db.SaveChangesAsync();

        WorksheetInstance NewInstance(int studentId, WorksheetInstanceStatus status) => new()
        {
            WorksheetId = ws.Id,
            StudentId = studentId,
            Status = status,
            StartTime = DateTime.UtcNow.AddMinutes(-30),
            WorksheetInstanceQuestions = new List<WorksheetInstanceQuestion>
            {
                new() { WorksheetQuestionId = wqs[0].Id, SelectedAnswerId = answers[0].wrong },
                new() { WorksheetQuestionId = wqs[1].Id, SelectedAnswerId = answers[1].correct },
                new() { WorksheetQuestionId = wqs[2].Id, SelectedAnswerId = answers[2].wrong },
                new() { WorksheetQuestionId = wqs[3].Id, SelectedAnswerId = null },
                new() { WorksheetQuestionId = wqs[4].Id, SelectedAnswerId = answers[4].wrong },
            }
        };

        var mine = NewInstance(student.Id, WorksheetInstanceStatus.Completed);
        var others = NewInstance(other.Id, WorksheetInstanceStatus.Completed);
        var inProgress = NewInstance(student.Id, WorksheetInstanceStatus.Started);
        db.TestInstances.AddRange(mine, others, inProgress);
        await db.SaveChangesAsync();

        // C alt konusuna yalnızca PASİF link — "aktif link yoksa bölüm gösterilmez" kuralını da kapsar.
        await CreateAsync(NewLink(subTopicId: tax.SubTopicC, title: "C-pasif", isActive: false));

        return new ResultWorld(mine.Id, others.Id, inProgress.Id,
            qs[0].Id, qs[1].Id, qs[2].Id, qs[3].Id, qs[4].Id, tax,
            linkA1, linkA2, linkB1, linkTopic1, linkOtherTopic1);
    }

    [Fact]
    public async Task ForResult_ReturnsOnlyWrongAnsweredQuestions_GroupedBySubTopic_ActiveLinksOnly()
    {
        var w = await SeedResultWorldAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetSuggestionsForResultAsync(w.InstanceId, StudentUserId);

        result.Success.ShouldBeTrue();
        // Q1 (yanlış, A+B) ve Q5 (yanlış, B). Q2 doğru, Q3 linksiz, Q4 boş → yok.
        result.Items.Select(i => i.QuestionId).ShouldBe(new[] { w.QWrongMulti, w.QWrongSingle });
        result.Items.ShouldNotContain(i => i.QuestionId == w.QCorrect);
        result.Items.ShouldNotContain(i => i.QuestionId == w.QBlank);
        result.Items.ShouldNotContain(i => i.QuestionId == w.QWrongNoLinks);

        var q1 = result.Items[0];
        q1.Groups.Select(g => g.SubTopicId).ShouldBe(new int?[] { w.Tax.SubTopicA, w.Tax.SubTopicB }); // C atlandı
        q1.Groups.ShouldAllBe(g => g.Kind == StudyLinkGroupKind.SubTopic && g.TopicId == w.Tax.TopicId);
        q1.Groups[0].Name.ShouldBe("Kesirler");
        q1.Groups[0].Links.Select(l => l.Id).ShouldBe(new[] { w.LinkA1, w.LinkA2 }); // SortOrder'a göre, pasif yok
        q1.Groups[0].Links[1].SourceType.ShouldBe(TopicStudyLinkSourceType.YouTube);
        q1.Groups[1].Name.ShouldBe("Ondalık Sayılar");
        q1.Groups[1].Links.Single().Id.ShouldBe(w.LinkB1);

        result.Items.SelectMany(i => i.Groups).ShouldAllBe(g => g.Links.Count > 0);
    }

    [Fact]
    public async Task ForResult_OtherStudentsInstance_IsNotFound()
    {
        var w = await SeedResultWorldAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetSuggestionsForResultAsync(w.OtherStudentInstanceId, StudentUserId);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
        result.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ForResult_UnknownInstance_IsNotFound()
    {
        await SeedResultWorldAsync();
        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetSuggestionsForResultAsync(987654, StudentUserId)).NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task ForResult_InProgressInstance_ReturnsEmpty_DoesNotLeakCorrectness()
    {
        var w = await SeedResultWorldAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetSuggestionsForResultAsync(w.InProgressInstanceId, StudentUserId);

        result.Success.ShouldBeTrue();
        result.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ForResult_DeactivatingAllLinksOfASubTopic_RemovesThatGroup()
    {
        var w = await SeedResultWorldAsync();
        await using (var ctx = _db.NewContext())
            await NewService(ctx).UpdateAsync(w.LinkB1,
                new UpdateTopicStudyLinkDto { Title = "B1", Url = "https://example.com/video", IsActive = false }, TeacherUserId);

        await using var read = _db.NewContext();
        var result = await NewService(read).GetSuggestionsForResultAsync(w.InstanceId, StudentUserId);

        // Q5 yalnızca B'ye bağlıydı → tamamen düşer; Q1 yalnızca A grubunu taşır.
        result.Items.Select(i => i.QuestionId).ShouldBe(new[] { w.QWrongMulti });
        result.Items[0].Groups.Select(g => g.SubTopicId).ShouldBe(new int?[] { w.Tax.SubTopicA });
    }

    [Fact]
    public async Task ForResult_AddsOneTopicFallbackGroupPerDistinctTopic_AfterSubTopicGroups()
    {
        var w = await SeedResultWorldAsync(withTopicLinks: true);
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetSuggestionsForResultAsync(w.InstanceId, StudentUserId);

        // Q3 artık dahil: alt konusu C'nin aktif linki yok ama konusu (Sayılar) konu seviyesi linke sahip.
        // Q2 (doğru) ve Q4 (boş) hâlâ yok.
        result.Items.Select(i => i.QuestionId).ShouldBe(new[] { w.QWrongMulti, w.QWrongNoLinks, w.QWrongSingle });

        // Q1: A, C, B aynı konuda → alt konu grupları + TEK Sayılar grubu (pasif konu linki hariç).
        var q1 = result.Items[0];
        q1.Groups.Select(g => g.Kind).ShouldBe(new[] { StudyLinkGroupKind.SubTopic, StudyLinkGroupKind.SubTopic, StudyLinkGroupKind.Topic });
        var topicGroup = q1.Groups[2];
        topicGroup.SubTopicId.ShouldBeNull();
        topicGroup.TopicId.ShouldBe(w.Tax.TopicId);
        topicGroup.Name.ShouldBe("Sayılar");
        topicGroup.Links.Select(l => l.Id).ShouldBe(new[] { w.LinkTopic1 });

        // Q3: yalnızca konu grubu.
        result.Items[1].Groups.Select(g => (g.Kind, g.TopicId)).ShouldBe(new[] { (StudyLinkGroupKind.Topic, (int?)w.Tax.TopicId) });

        // Q5: alt konu B, sonra doğrudan konu (Geometri) ve alt konunun konusu (Sayılar).
        var q5 = result.Items[2];
        q5.Groups.Select(g => (g.Kind, g.Name)).ShouldBe(new[]
        {
            (StudyLinkGroupKind.SubTopic, "Ondalık Sayılar"),
            (StudyLinkGroupKind.Topic, "Geometri"),
            (StudyLinkGroupKind.Topic, "Sayılar"),
        });
        q5.Groups[1].Links.Single().Id.ShouldBe(w.LinkOtherTopic1);

        result.Items.SelectMany(i => i.Groups).ShouldAllBe(g => g.Links.Count > 0);
    }

    [Fact]
    public async Task ForResult_TopicGroupWithOnlyInactiveTopicLinks_IsOmitted()
    {
        var w = await SeedResultWorldAsync(withTopicLinks: true);
        await using (var ctx = _db.NewContext())
        {
            var service = NewService(ctx);
            await service.UpdateAsync(w.LinkTopic1, new UpdateTopicStudyLinkDto { Title = "x", Url = "https://example.com/video", IsActive = false }, TeacherUserId);
        }

        await using var read = _db.NewContext();
        var result = await NewService(read).GetSuggestionsForResultAsync(w.InstanceId, StudentUserId);

        result.Items.SelectMany(i => i.Groups).ShouldNotContain(g => g.Kind == StudyLinkGroupKind.Topic && g.TopicId == w.Tax.TopicId);
        result.Items.ShouldNotContain(i => i.QuestionId == w.QWrongNoLinks);
    }
}
