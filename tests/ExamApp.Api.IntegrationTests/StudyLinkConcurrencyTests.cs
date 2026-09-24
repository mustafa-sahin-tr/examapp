using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos.StudyLinks;
using ExamApp.Api.Services.StudyLinks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #61 — 7 aktif link limitinin gerçek PostgreSQL üzerinde eşzamanlı yazmaya dayanıklılığı. Birim testler
/// SQLite'ta tek bağlantıyla koştuğu için write skew'u üretemez; burada iki ayrı DI scope'u (ayrı DbContext / bağlantı)
/// aynı anda 7. aktif linki eklemeye çalışır. Serializable transaction + Npgsql retry strategy sayesinde tam olarak biri
/// başarılı olmalı, diğeri retry'de 7'yi görüp ActiveLimitReached (409) almalı.
/// </summary>
public class StudyLinkConcurrencyTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int AdminUserId = 9100;

    private async Task<int> SeedSubTopicWithActiveLinksAsync(int activeLinks)
    {
        return await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "6" };
            var subject = new Subject { Name = "Matematik-SL" };
            db.AddRange(grade, subject);
            await db.SaveChangesAsync();

            var topic = new Topic { Name = "Sayılar", SubjectId = subject.Id, GradeId = grade.Id };
            db.Topics.Add(topic);
            await db.SaveChangesAsync();

            var st = new SubTopic { Name = "Kesirler", TopicId = topic.Id };
            db.SubTopics.Add(st);
            await db.SaveChangesAsync();

            for (var i = 0; i < activeLinks; i++)
            {
                db.TopicStudyLinks.Add(new TopicStudyLink
                {
                    TopicId = topic.Id, SubTopicId = st.Id, Title = $"L{i}", Url = $"https://example.com/{i}",
                    SortOrder = i, IsActive = true, CreatedByUserId = AdminUserId, CreatedByName = "Admin", CreatedByRole = "Admin",
                });
            }
            await db.SaveChangesAsync();
            return st.Id;
        });
    }

    [Fact]
    public async Task Two_parallel_creates_for_the_seventh_active_slot_exactly_one_wins()
    {
        var subTopicId = await SeedSubTopicWithActiveLinksAsync(6);
        var actor = new StudyLinkActor(AdminUserId, "Admin", "Admin", IsAdmin: true);

        // İki ayrı scope → iki ayrı DbContext/bağlantı. Barrier ile ikisini aynı anda başlat.
        using var start = new SemaphoreSlim(0, 2);
        async Task<TopicStudyLinkResultDto> CreateInOwnScope(string title)
        {
            using var scope = Factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ITopicStudyLinkService>();
            await start.WaitAsync();
            return await service.CreateAsync(
                new CreateTopicStudyLinkDto { SubTopicId = subTopicId, Title = title, Url = $"https://example.com/{title}" }, actor);
        }

        var first = Task.Run(() => CreateInOwnScope("a"));
        var second = Task.Run(() => CreateInOwnScope("b"));
        start.Release(2);
        var results = await Task.WhenAll(first, second);

        results.Count(r => r.Success).ShouldBe(1);
        var loser = results.Single(r => !r.Success);
        loser.Conflict.ShouldBeTrue();
        loser.ErrorCode.ShouldBe(TopicStudyLinkErrorCodes.ActiveLimitReached);

        await WithDbAsync(async db =>
            (await db.TopicStudyLinks.CountAsync(l => l.SubTopicId == subTopicId && l.IsActive)).ShouldBe(7));
    }

    [Fact]
    public async Task Eighth_active_link_over_http_is_a_409_with_ActiveLimitReached()
    {
        var subTopicId = await SeedSubTopicWithActiveLinksAsync(7);
        var admin = await ClientAsAsync(AdminUserId, "Admin", "kc-sl-admin", "Admin");

        var res = await admin.PostAsJsonAsync("/api/study-links",
            new { subTopicId, title = "8.", url = "https://example.com/8" });

        res.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = await res.Content.ReadFromJsonAsync<TopicStudyLinkResultDto>(Json);
        body!.ErrorCode.ShouldBe(TopicStudyLinkErrorCodes.ActiveLimitReached);
    }
}
