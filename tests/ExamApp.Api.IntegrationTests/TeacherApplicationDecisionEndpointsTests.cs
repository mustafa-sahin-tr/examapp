using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #157: POST api/admin/teacher-applications/{id}/approve|reject — gerçek pipeline + Postgres.
/// Admin olmayan rol → 403; başarılı kararda karar bildirimi outbox'ı
/// (<see cref="TeacherApplicationDecidedEvent"/>) iş işlemiyle atomik yazılır ve gerekçe/admin
/// kimliği taşımaz (security review).
/// </summary>
public class TeacherApplicationDecisionEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private static string NewSub(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private async Task<int> SeedPendingIndependentTeacherAsync(int userId)
        => await WithDbAsync(async db =>
        {
            var teacher = new Teacher
            {
                UserId = userId,
                IsIndependentTutor = true,
                ApprovalStatus = TeacherApprovalStatus.Pending
            };
            db.Teachers.Add(teacher);
            await db.SaveChangesAsync();
            return teacher.Id;
        });

    [Fact]
    public async Task Approve_requires_the_Admin_realm_role()
    {
        var teacherId = await SeedPendingIndependentTeacherAsync(userId: 5001);

        (await Anonymous().PostAsync($"/api/admin/teacher-applications/{teacherId}/approve", null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var teacher = await ClientAsAsync(1, "Teacher", NewSub("kc-teacher"), "Teacher");
        (await teacher.PostAsync($"/api/admin/teacher-applications/{teacherId}/approve", null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var student = await ClientAsAsync(2, "Student", NewSub("kc-student"), "Student");
        (await student.PostAsync($"/api/admin/teacher-applications/{teacherId}/approve", null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reject_requires_the_Admin_realm_role()
    {
        var teacherId = await SeedPendingIndependentTeacherAsync(userId: 5002);
        JsonContent Body() => JsonContent.Create(new { reason = "gerekçe" });

        (await Anonymous().PostAsync($"/api/admin/teacher-applications/{teacherId}/reject", Body()))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var teacher = await ClientAsAsync(3, "Teacher", NewSub("kc-teacher"), "Teacher");
        (await teacher.PostAsync($"/api/admin/teacher-applications/{teacherId}/reject", Body()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var student = await ClientAsAsync(4, "Student", NewSub("kc-student"), "Student");
        (await student.PostAsync($"/api/admin/teacher-applications/{teacherId}/reject", Body()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Approve_by_admin_with_resolvable_sub_writes_decision_outbox_without_reason_or_admin_identity()
    {
        var sub = NewSub("kc-target");
        const int userId = 5004;
        Factory.Services.GetRequiredService<FakeUserDirectory>()
            .Add(new() { Id = userId, KeycloakId = sub, FullName = "Hedef Öğretmen" });

        var teacherId = await SeedPendingIndependentTeacherAsync(userId);
        var adminSub = NewSub("kc-admin");
        var admin = await ClientAsAsync(7, "Admin", adminSub, "Admin");

        var response = await admin.PostAsync($"/api/admin/teacher-applications/{teacherId}/approve", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await WithDbAsync(async db =>
        {
            var teacherState = await db.Teachers.SingleAsync(t => t.Id == teacherId);
            teacherState.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);

            var outbox = await db.OutboxMessages
                .SingleAsync(m => m.Type == OutboxEventRegistry.NameFor<TeacherApplicationDecidedEvent>());
            outbox.Content.ShouldContain(sub);
            outbox.Content.ShouldNotContain(adminSub);
            outbox.Content.ToLowerInvariant().ShouldNotContain("reason");
        });
    }

    [Fact]
    public async Task Reject_by_admin_with_resolvable_sub_writes_decision_outbox_without_reason_or_admin_identity()
    {
        var sub = NewSub("kc-target");
        const int userId = 5005;
        Factory.Services.GetRequiredService<FakeUserDirectory>()
            .Add(new() { Id = userId, KeycloakId = sub, FullName = "Hedef Öğretmen" });

        var teacherId = await SeedPendingIndependentTeacherAsync(userId);
        var adminSub = NewSub("kc-admin");
        var admin = await ClientAsAsync(8, "Admin", adminSub, "Admin");

        var response = await admin.PostAsync($"/api/admin/teacher-applications/{teacherId}/reject",
            JsonContent.Create(new { reason = "Belge eksik" }));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await WithDbAsync(async db =>
        {
            var teacherState = await db.Teachers.SingleAsync(t => t.Id == teacherId);
            teacherState.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Rejected);
            teacherState.RejectionReason.ShouldBe("Belge eksik");

            var outbox = await db.OutboxMessages
                .SingleAsync(m => m.Type == OutboxEventRegistry.NameFor<TeacherApplicationDecidedEvent>());
            outbox.Content.ShouldContain(sub);
            outbox.Content.ShouldNotContain(adminSub);
            outbox.Content.ShouldNotContain("Belge eksik");
        });
    }

    [Fact]
    public async Task Approve_by_admin_without_resolvable_sub_commits_decision_but_skips_outbox()
    {
        // FakeUserDirectory'de kayıtlı DEĞİL → gerçek auth-api'ye düşer, test ortamında erişilemez
        // → lookup başarısız → outbox YAZILMAZ, karar yine commit edilir (mimari karar #157).
        var teacherId = await SeedPendingIndependentTeacherAsync(userId: 5006);
        var admin = await ClientAsAsync(9, "Admin", NewSub("kc-admin"), "Admin");

        var response = await admin.PostAsync($"/api/admin/teacher-applications/{teacherId}/approve", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await WithDbAsync(async db =>
        {
            var teacherState = await db.Teachers.SingleAsync(t => t.Id == teacherId);
            teacherState.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);

            var outboxCount = await db.OutboxMessages
                .CountAsync(m => m.Type == OutboxEventRegistry.NameFor<TeacherApplicationDecidedEvent>());
            outboxCount.ShouldBe(0);
        });
    }
}
