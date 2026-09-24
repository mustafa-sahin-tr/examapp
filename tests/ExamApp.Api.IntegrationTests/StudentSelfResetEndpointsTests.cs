using System.Net;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Services.StudentReset;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #243: öğrenci self-reset — gerçek pipeline'da sub başına rate limit (varsayılan saatte 1; factory bu
/// ayarı ezmez) ve gerçek Hangfire PostgreSQL storage'ında bekleyen iş tekilleştirmesi.
/// </summary>
public class StudentSelfResetEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const string ResetUrl = "/api/student/me/reset";

    private static string NewSub() => $"kc-self-reset-{Guid.NewGuid():N}";

    // hangfire şeması Respawn ile sıfırlanmaz → her test benzersiz, büyük id.
    private static int NewUserId() => 1_000_000 + Random.Shared.Next(0, 900_000_000);

    [Fact]
    public async Task Second_reset_request_within_the_window_gets_429_and_other_students_are_unaffected()
    {
        // Öğrenci kaydı yok → ilk istek 404 döner ama kovayı tüketir (limiter action'dan önce çalışır).
        var student = await ClientAsAsync(NewUserId(), "Student", NewSub(), realmRoles: "Student");

        (await student.PostAsync(ResetUrl, null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var rejected = await student.PostAsync(ResetUrl, null);
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        (await rejected.Content.ReadAsStringAsync())
            .ShouldBe("Verilerinizi kısa süre önce sıfırladınız. Lütfen daha sonra tekrar deneyin.");

        var other = await ClientAsAsync(NewUserId(), "Student", NewSub(), realmRoles: "Student");
        (await other.PostAsync(ResetUrl, null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Rejected_roles_do_not_consume_the_bucket()
    {
        var sub = NewSub();
        var teacher = await ClientAsAsync(NewUserId(), "Teacher", sub, realmRoles: "Teacher");
        (await teacher.PostAsync(ResetUrl, null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await teacher.PostAsync(ResetUrl, null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void Pending_reset_job_is_deduplicated_on_real_hangfire_storage()
    {
        var userId = NewUserId();
        // Var olmayan öğrenci: iş Hangfire sunucusunda koşsa bile veriye dokunmaz; test sonunda silinir.
        const int missingStudentId = 987_654_321;

        using var scope = Factory.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IStudentResetScheduler>();
        var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();

        var first = scheduler.Enqueue(userId, missingStudentId, "kc-dedup");
        string? third = null;
        try
        {
            first.AlreadyPending.ShouldBeFalse();

            // Enqueued / Processing / (başarısızlık sonrası retry) Scheduled — hepsi "bekliyor".
            var second = scheduler.Enqueue(userId, missingStudentId, "kc-dedup");
            second.AlreadyPending.ShouldBeTrue();
            second.JobId.ShouldBe(first.JobId);

            // İş bittiğinde/silindiğinde yeni istek yeni iş açar.
            jobs.Delete(first.JobId).ShouldBeTrue();
            var next = scheduler.Enqueue(userId, missingStudentId, "kc-dedup");
            third = next.JobId;
            next.AlreadyPending.ShouldBeFalse();
            next.JobId.ShouldNotBe(first.JobId);
        }
        finally
        {
            jobs.Delete(first.JobId);
            if (third != null) jobs.Delete(third);
        }
    }
}
