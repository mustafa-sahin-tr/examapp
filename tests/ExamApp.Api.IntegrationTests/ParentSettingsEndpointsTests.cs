using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos.ParentLinks;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #1112: veli haftalık rapor e-postasını kendi ayarından kapatır / açar (varsayılan açık); yalnız kendi profili. Veli
/// profili olmayan Parent rolü 404, başka rol 403.
/// </summary>
public class ParentSettingsEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const string Url = "/api/parent/settings";

    private Task<HttpClient> ParentAsync(int userId) => ClientAsAsync(userId, "Parent", $"kc-{userId}", "Parent");

    [Fact]
    public async Task Parent_turns_the_weekly_report_email_off_and_on_for_itself_only()
    {
        const int parentUser = 47301, otherParentUser = 47302;
        await WithDbAsync(async db =>
        {
            db.Parents.AddRange(new Parent { UserId = parentUser }, new Parent { UserId = otherParentUser });
            await db.SaveChangesAsync();
            return 0;
        });
        var parent = await ParentAsync(parentUser);

        var initial = await parent.GetAsync(Url);
        initial.StatusCode.ShouldBe(HttpStatusCode.OK);
        initial.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (await initial.Content.ReadFromJsonAsync<ParentSettingsDto>(Json))!.WeeklyReportEmail.ShouldBeTrue();

        (await parent.PutAsJsonAsync(Url, new ParentSettingsDto { WeeklyReportEmail = false })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await parent.GetAsync(Url)).Content.ReadFromJsonAsync<ParentSettingsDto>(Json))!.WeeklyReportEmail.ShouldBeFalse();
        var optOut = await WithDbAsync(db => db.Parents.AsNoTracking()
            .Where(p => p.UserId == parentUser || p.UserId == otherParentUser)
            .ToDictionaryAsync(p => p.UserId, p => p.WeeklyReportEmailOptOut));
        optOut[parentUser].ShouldBeTrue();
        optOut[otherParentUser].ShouldBeFalse();

        (await parent.PutAsJsonAsync(Url, new ParentSettingsDto { WeeklyReportEmail = true })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await WithDbAsync(db => db.Parents.AsNoTracking().SingleAsync(p => p.UserId == parentUser))).WeeklyReportEmailOptOut.ShouldBeFalse();
    }

    [Fact]
    public async Task Parent_role_without_a_profile_gets_404_and_a_student_403()
    {
        var noProfile = await ParentAsync(47311);
        (await noProfile.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await noProfile.PutAsJsonAsync(Url, new ParentSettingsDto { WeeklyReportEmail = false })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var student = await ClientAsAsync(47312, "Student", "kc-47312", "Student");
        (await student.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
