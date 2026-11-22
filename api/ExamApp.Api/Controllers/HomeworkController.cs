using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// issue #1282 (#1071 S7): öğretmenin düz ödevleri. Oluşturan okulun onaylı, askısız, bağımsız olmayan öğretmenidir (okul DB'den,
/// 403); başka okulun şubesi / öğrencisi ve başkasının ödevi 404; günlük sınır 429. Liste öğretmenin kendi ödevleri (okul değişse
/// de kalır), durumlar yalnız güncel okulunun öğrencileri. Gateway <c>/api/exam/{everything}</c>.
/// </summary>
[ApiController]
[Route("api/homework")]
[Authorize(Roles = "Teacher")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
public class HomeworkController : BaseController
{
    private readonly ISchoolHomeworkService _homework;
    private readonly IStringLocalizer<Messages> _localizer;

    public HomeworkController(ISchoolHomeworkService homework, IStringLocalizer<Messages>? localizer = null)
    {
        _homework = homework;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>POST api/homework → ödevi oluşturur; 201 + özet.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SchoolHomeworkCreateRequestDto? request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return HomeworkError(_localizer, SchoolHomeworkStatus.Forbidden);
        var result = await _homework.CreateAsync(user.Id, request ?? new SchoolHomeworkCreateRequestDto(), ct);
        return result.Status == SchoolHomeworkStatus.Success
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : HomeworkError(_localizer, result.Status);
    }

    /// <summary>GET api/homework?page&amp;pageSize → öğretmenin ödevleri, yeniden eskiye, durum sayılarıyla.</summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<Paged<SchoolHomeworkDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AdminListPaging.DefaultPageSize,
        CancellationToken ct = default)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return HomeworkError(_localizer, SchoolHomeworkStatus.Forbidden);
        return Ok(await _homework.ListOwnAsync(user.Id, page, pageSize, ct));
    }

    /// <summary>GET api/homework/{id} → ödev + kapsamdaki öğrencilerin durumu.</summary>
    [HttpGet("{id:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return HomeworkError(_localizer, SchoolHomeworkStatus.Forbidden);
        var result = await _homework.GetAsync(user.Id, id, ct);
        return result.Status == SchoolHomeworkStatus.Success ? Ok(result.Value) : HomeworkError(_localizer, result.Status);
    }

    /// <summary>PUT api/homework/{id}/students/{studentId} → Yapıldı / Kısmen / Yapılmadı + not (status null → kontrol kaldırılır).</summary>
    [HttpPut("{id:int}/students/{studentId:int}")]
    public async Task<IActionResult> Check(int id, int studentId, [FromBody] SchoolHomeworkCheckRequestDto? request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return HomeworkError(_localizer, SchoolHomeworkStatus.Forbidden);
        var result = await _homework.CheckAsync(user.Id, id, studentId, request ?? new SchoolHomeworkCheckRequestDto(), ct);
        return result.Status switch
        {
            SchoolHomeworkStatus.Success => Ok(result.Value),
            SchoolHomeworkStatus.Invalid => StatusCode(StatusCodes.Status400BadRequest,
                new { message = _localizer["admin.homework.checkInvalid"].Value }),
            _ => HomeworkError(_localizer, result.Status)
        };
    }

    /// <summary>DELETE api/homework/{id} → ödevi kaldırır (öğrenciler artık görmez); 204.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return HomeworkError(_localizer, SchoolHomeworkStatus.Forbidden);
        var status = await _homework.DeleteAsync(user.Id, id, ct);
        return status == SchoolHomeworkStatus.Success ? NoContent() : HomeworkError(_localizer, status);
    }

    internal static ObjectResult HomeworkError(IStringLocalizer<Messages> localizer, SchoolHomeworkStatus status)
    {
        var (code, key) = status switch
        {
            SchoolHomeworkStatus.NotFound => (StatusCodes.Status404NotFound, "admin.homework.notFound"),
            SchoolHomeworkStatus.Invalid => (StatusCodes.Status400BadRequest, "admin.homework.invalid"),
            SchoolHomeworkStatus.Locked => (StatusCodes.Status409Conflict, "admin.homework.locked"),
            SchoolHomeworkStatus.RateLimited => (StatusCodes.Status429TooManyRequests, "admin.homework.rateLimited"),
            _ => (StatusCodes.Status403Forbidden, "admin.homework.forbidden")
        };
        return new ObjectResult(new { message = localizer[key].Value }) { StatusCode = code };
    }
}

/// <summary>
/// issue #1282 (#1071 S7): öğrencinin ödevleri — açık / gecikmiş / kontrol edilmiş (son 30 gün) ve "yaptım" işareti. Öğretmen kontrol
/// ettikten sonra işaret değişmez (409). Gateway <c>/api/exam/{everything}</c>.
/// </summary>
[ApiController]
[Route("api/student/homework")]
[Authorize(Roles = "Student")]
public class StudentHomeworkController : BaseController
{
    private readonly ISchoolHomeworkService _homework;
    private readonly IStringLocalizer<Messages> _localizer;

    public StudentHomeworkController(ISchoolHomeworkService homework, IStringLocalizer<Messages>? localizer = null)
    {
        _homework = homework;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>GET api/student/homework → teslim gününe göre sıralı ödevler.</summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        var items = IsResolvedUser(user) ? await _homework.ListForStudentAsync(user.Id, ct) : null;
        return items is null
            ? StatusCode(StatusCodes.Status403Forbidden, new { message = _localizer["admin.homework.notStudent"].Value })
            : Ok(items);
    }

    /// <summary>PUT api/student/homework/{id}/done → "yaptım" işareti (<c>done=false</c> geri alır).</summary>
    [HttpPut("{id:int}/done")]
    public async Task<IActionResult> MarkDone(int id, [FromBody] StudentHomeworkDoneRequestDto? request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return HomeworkController.HomeworkError(_localizer, SchoolHomeworkStatus.NotFound);
        var result = await _homework.MarkDoneAsync(user.Id, id, request?.Done ?? true, ct);
        return result.Status == SchoolHomeworkStatus.Success
            ? Ok(result.Value)
            : HomeworkController.HomeworkError(_localizer, result.Status);
    }
}
