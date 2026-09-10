using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #49: QuestionTransferController is Teacher/Admin-only, ProgramController is
/// Student-only (except the anonymous "steps" catalog).
/// </summary>
public class RoleAuthorizationEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    // ---- QuestionTransferController: Teacher/Admin only ----

    [Fact]
    public async Task StartExport_is_forbidden_for_a_student()
    {
        var student = await ClientAsAsync(1, "Student", "kc-qt-1", "Student");

        var response = await student.PostAsJsonAsync(
            "/api/question-transfer/exports", new StartQuestionExportDto());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListJobs_is_forbidden_for_a_student()
    {
        var student = await ClientAsAsync(2, "Student", "kc-qt-2", "Student");

        (await student.GetAsync("/api/question-transfer/jobs"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListSources_is_forbidden_for_a_student()
    {
        var student = await ClientAsAsync(3, "Student", "kc-qt-3", "Student");

        (await student.GetAsync("/api/question-transfer/exports/sources"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task QuestionTransfer_endpoints_reject_anonymous_callers()
    {
        (await Anonymous().GetAsync("/api/question-transfer/jobs"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task StartExport_passes_the_authorization_gate_for_a_teacher()
    {
        var teacher = await ClientAsAsync(4, "Teacher", "kc-qt-4", "Teacher");

        var response = await teacher.PostAsJsonAsync(
            "/api/question-transfer/exports", new StartQuestionExportDto());

        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StartExport_passes_the_authorization_gate_for_an_admin()
    {
        var admin = await ClientAsAsync(5, "Admin", "kc-qt-5", "Admin");

        var response = await admin.PostAsJsonAsync(
            "/api/question-transfer/exports", new StartQuestionExportDto());

        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListJobs_and_ListSources_pass_the_authorization_gate_for_a_teacher()
    {
        var teacher = await ClientAsAsync(6, "Teacher", "kc-qt-6", "Teacher");

        (await teacher.GetAsync("/api/question-transfer/jobs")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await teacher.GetAsync("/api/question-transfer/exports/sources")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- ProgramController: Student only (except /steps) ----

    [Fact]
    public async Task CreateProgram_is_forbidden_for_a_teacher()
    {
        var teacher = await ClientAsAsync(10, "Teacher", "kc-p-10", "Teacher");

        var response = await teacher.PostAsJsonAsync(
            "/api/program/create", new CreateProgramRequestDto { ProgramName = "P" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateProgram_is_forbidden_for_an_admin()
    {
        var admin = await ClientAsAsync(11, "Admin", "kc-p-11", "Admin");

        var response = await admin.PostAsJsonAsync(
            "/api/program/create", new CreateProgramRequestDto { ProgramName = "P" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetMyPrograms_is_forbidden_for_a_teacher()
    {
        var teacher = await ClientAsAsync(12, "Teacher", "kc-p-12", "Teacher");

        (await teacher.GetAsync("/api/program/my-programs")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetProgramById_is_forbidden_for_a_teacher()
    {
        var teacher = await ClientAsAsync(13, "Teacher", "kc-p-13", "Teacher");

        (await teacher.GetAsync("/api/program/1")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AddStudyItems_is_forbidden_for_a_teacher()
    {
        var teacher = await ClientAsAsync(14, "Teacher", "kc-p-14", "Teacher");

        var response = await teacher.PostAsJsonAsync(
            "/api/program/1/study-pages", new ProgramStudyItemScheduleRequestDto());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Program_endpoints_reject_anonymous_callers()
    {
        (await Anonymous().GetAsync("/api/program/my-programs"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateProgram_passes_the_authorization_gate_for_a_student()
    {
        var student = await ClientAsAsync(15, "Student", "kc-p-15", "Student");

        var response = await student.PostAsJsonAsync(
            "/api/program/create", new CreateProgramRequestDto { ProgramName = "My plan" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMyPrograms_passes_the_authorization_gate_for_a_student()
    {
        var student = await ClientAsAsync(16, "Student", "kc-p-16", "Student");

        (await student.GetAsync("/api/program/my-programs")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetProgramById_passes_the_authorization_gate_for_a_student_even_when_not_found()
    {
        var student = await ClientAsAsync(17, "Student", "kc-p-17", "Student");

        (await student.GetAsync("/api/program/999999")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddStudyItems_passes_the_authorization_gate_for_a_student_even_when_program_not_found()
    {
        var student = await ClientAsAsync(18, "Student", "kc-p-18", "Student");

        var response = await student.PostAsJsonAsync(
            "/api/program/999999/study-pages", new ProgramStudyItemScheduleRequestDto());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- GetProgramSteps stays anonymous ----

    [Fact]
    public async Task GetProgramSteps_is_reachable_without_authentication()
    {
        var response = await Anonymous().GetAsync("/api/program/steps");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
