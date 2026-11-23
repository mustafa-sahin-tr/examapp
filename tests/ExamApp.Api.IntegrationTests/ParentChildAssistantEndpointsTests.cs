using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos.ParentLinks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #523: a parent switches the child's AI assistant off; the student reads the result (the assistant service checks it before every
/// turn). Each Active parent decides on their own link; any one switched off → off. Another parent's child / a pending link → 404.
/// </summary>
public class ParentChildAssistantEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<int> SeedStudentAsync(int studentUserId, params int[] parentUserIds)
    {
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        directory.Add(new() { Id = studentUserId, KeycloakId = $"kc-{studentUserId}", FullName = "Ayşe Kaya" });
        foreach (var p in parentUserIds)
            directory.Add(new() { Id = p, KeycloakId = $"kc-{p}", FullName = $"Veli {p}" });

        return await WithDbAsync(async db =>
        {
            var student = new Student { UserId = studentUserId, StudentNumber = $"A{studentUserId}" };
            db.Students.Add(student);
            db.Parents.AddRange(parentUserIds.Select(u => new Parent { UserId = u }));
            await db.SaveChangesAsync();
            return student.Id;
        });
    }

    private Task<HttpClient> ParentAsync(int userId) => ClientAsAsync(userId, "Parent", $"kc-{userId}", "Parent");

    private Task<HttpClient> StudentAsync(int userId) => ClientAsAsync(userId, "Student", $"kc-{userId}", "Student");

    private static Task<HttpResponseMessage> SetAsync(HttpClient client, int studentId, bool allowed) =>
        client.PutAsJsonAsync($"/api/parent/children/{studentId}/assistant", new ParentChildAssistantSettingDto { Allowed = allowed });

    private async Task<bool> StudentAllowedAsync(HttpClient student)
    {
        var res = await student.GetAsync("/api/student/assistant-access");
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<StudentAssistantAccessDto>(Json))!.Allowed;
    }

    private async Task<LinkedChildDto> ChildCardAsync(HttpClient parent, int studentId)
    {
        var list = await parent.GetFromJsonAsync<List<LinkedChildDto>>("/api/parent-links/my-children", Json);
        return list!.Single(c => c.StudentId == studentId);
    }

    [Fact]
    public async Task Parent_switches_the_assistant_off_and_on_and_every_change_is_audited()
    {
        const int studentUser = 52301, parentUser = 52302;
        var studentId = await SeedStudentAsync(studentUser, parentUser);
        await SeedParentLinkAsync(parentUser, studentUser);
        var parent = await ParentAsync(parentUser);
        var student = await StudentAsync(studentUser);

        (await StudentAllowedAsync(student)).ShouldBeTrue();
        (await ChildCardAsync(parent, studentId)).AssistantAllowed.ShouldBeTrue();

        var off = await SetAsync(parent, studentId, allowed: false);
        off.StatusCode.ShouldBe(HttpStatusCode.OK);
        var state = (await off.Content.ReadFromJsonAsync<ParentChildAssistantStateDto>(Json))!;
        state.Allowed.ShouldBeFalse();
        state.EffectiveAllowed.ShouldBeFalse();
        (await StudentAllowedAsync(student)).ShouldBeFalse();
        (await ChildCardAsync(parent, studentId)).AssistantAllowed.ShouldBeFalse();

        (await SetAsync(parent, studentId, allowed: true)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StudentAllowedAsync(student)).ShouldBeTrue();

        var audits = await WithDbAsync(db => db.ParentAccessAudits.AsNoTracking()
            .Where(a => a.Endpoint == ParentAccessEndpoints.ChildAssistantSetting && a.StudentId == studentId)
            .Select(a => a.ResourceId).ToListAsync());
        audits.OrderBy(v => v).ShouldBe([0, 1]);
    }

    [Fact]
    public async Task Any_parent_switching_off_wins_and_the_other_parent_sees_it()
    {
        const int studentUser = 52311, first = 52312, second = 52313;
        var studentId = await SeedStudentAsync(studentUser, first, second);
        await SeedParentLinkAsync(first, studentUser);
        await SeedParentLinkAsync(second, studentUser);
        var firstParent = await ParentAsync(first);
        var secondParent = await ParentAsync(second);

        (await SetAsync(secondParent, studentId, allowed: false)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var stillOn = (await (await SetAsync(firstParent, studentId, allowed: true)).Content
            .ReadFromJsonAsync<ParentChildAssistantStateDto>(Json))!;
        stillOn.Allowed.ShouldBeTrue();
        stillOn.EffectiveAllowed.ShouldBeFalse();
        var card = await ChildCardAsync(firstParent, studentId);
        card.AssistantAllowed.ShouldBeTrue();
        card.AssistantBlockedByOtherParent.ShouldBeTrue();
        (await StudentAllowedAsync(await StudentAsync(studentUser))).ShouldBeFalse();

        // A revoked link's choice no longer counts.
        await WithDbAsync(async db =>
        {
            var parentId = await db.Parents.Where(p => p.UserId == second).Select(p => p.Id).SingleAsync();
            await db.ParentStudentLinks.Where(l => l.ParentId == parentId)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, ParentStudentLinkStatus.Revoked));
            return 0;
        });
        (await StudentAllowedAsync(await StudentAsync(studentUser))).ShouldBeTrue();
    }

    [Fact]
    public async Task Another_parents_child_and_a_pending_link_are_404()
    {
        const int studentUser = 52321, owner = 52322, stranger = 52323, pending = 52324;
        var studentId = await SeedStudentAsync(studentUser, owner, stranger, pending);
        await SeedParentLinkAsync(owner, studentUser);
        await SeedParentLinkAsync(pending, studentUser, active: false);

        foreach (var parentUser in new[] { stranger, pending })
            (await SetAsync(await ParentAsync(parentUser), studentId, allowed: false)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await StudentAllowedAsync(await StudentAsync(studentUser))).ShouldBeTrue();
        (await WithDbAsync(db => db.ParentAccessAudits.CountAsync(a => a.Endpoint == ParentAccessEndpoints.ChildAssistantSetting
            && a.StudentId == studentId))).ShouldBe(0);
    }

    [Fact]
    public async Task Only_a_student_reads_assistant_access()
    {
        var teacher = await ClientAsAsync(52331, "Teacher", "kc-52331", "Teacher");
        (await teacher.GetAsync("/api/student/assistant-access")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
