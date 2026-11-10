using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #1092: okul lisansı — özel anlaşmayla okulun bütün öğretmen ve öğrencilerine plan (yalnız Admin / SuperAdmin). Her değişiklik
/// <c>SchoolLicenseAudits</c>'e yazılır.
/// </summary>
[Route("api/admin/subscription-plans/schools/{schoolId:int}/license")]
[Authorize(Roles = "Admin,SuperAdmin")]
public class AdminSchoolLicensesController : BaseController
{
    private readonly ISchoolLicenseService _licenses;

    public AdminSchoolLicensesController(ISchoolLicenseService licenses)
    {
        _licenses = licenses;
    }

    [HttpGet]
    public async Task<ActionResult<SchoolLicenseDto>> Get(int schoolId, CancellationToken ct) =>
        await _licenses.GetAsync(schoolId, ct) is { } license ? license : NotFound(new { code = "subscription.schoolNotFound" });

    // PUT — oluşturur ya da günceller (uzatma, plan değişikliği); okulun sonradan katılan üyeleri de lisansı alır.
    [HttpPut]
    public async Task<ActionResult<SchoolLicenseDto>> Save(int schoolId, [FromBody] SaveSchoolLicenseDto request, CancellationToken ct)
    {
        var admin = await GetAuthenticatedUserAsync(ct);
        return await ResultAsync(schoolId, await _licenses.SaveAsync(schoolId, request, admin.Id, ct), ct);
    }

    // POST end — lisansı şimdi bitirir; üyeler kendi planına ya da (kilit açıksa) plansız duruma döner.
    [HttpPost("end")]
    public async Task<ActionResult<SchoolLicenseDto>> End(int schoolId, CancellationToken ct)
    {
        var admin = await GetAuthenticatedUserAsync(ct);
        return await ResultAsync(schoolId, await _licenses.EndAsync(schoolId, admin.Id, ct), ct);
    }

    private async Task<ActionResult<SchoolLicenseDto>> ResultAsync(int schoolId, SchoolLicenseError error, CancellationToken ct) => error switch
    {
        SchoolLicenseError.None => (await _licenses.GetAsync(schoolId, ct))!,
        SchoolLicenseError.SchoolNotFound => NotFound(new { code = "subscription.schoolNotFound" }),
        SchoolLicenseError.NoLicense => NotFound(new { code = "subscription.noLicense" }),
        SchoolLicenseError.UnknownPlan => BadRequest(new { code = "subscription.unknownPlan" }),
        SchoolLicenseError.AudienceMismatch => BadRequest(new { code = "subscription.audienceMismatch" }),
        SchoolLicenseError.NoteTooLong => BadRequest(new { code = "subscription.noteTooLong" }),
        _ => BadRequest(new { code = "subscription.invalidRange" }),
    };
}
