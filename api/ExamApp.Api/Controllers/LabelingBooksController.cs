using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #577: kitap kaydı — sayfa ofsetleri, cevap anahtarı aralıkları, içindekiler. Gateway:
/// <c>/api/exam/labeling/books/*</c> (işaretleme kuralı: Admin + QuestionTrainer). Yetki <see cref="LabelingController"/> ile
/// aynı: QuestionTrainer rolündeki öğretmen onaylı ve askıda olmamalı (Admin muaf).
/// </summary>
[ApiController]
[Route("api/labeling/books")]
[Authorize(Roles = "Admin,QuestionTrainer")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class LabelingBooksController : BaseController
{
    private readonly IBookRegistryService _books;
    private readonly IStringLocalizer<Messages> _localizer;

    public LabelingBooksController(IBookRegistryService books, IStringLocalizer<Messages>? localizer = null)
    {
        _books = books;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>Tüm kitaplar, kümeleriyle (ad sırasıyla).</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await _books.ListAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var book = await _books.GetAsync(id, ct);
        return book is null ? NotFound(Error("labeling.books.notFound")) : Ok(book);
    }

    /// <summary>201 + kitap; 400 geçersiz aralık / taksonomi; 409 ad kullanımda.</summary>
    [HttpPost]
    public Task<IActionResult> Create([FromBody] LabelingBookSaveRequestDto request, CancellationToken ct) =>
        Handle(async () =>
        {
            var book = await _books.CreateAsync(request, ct);
            return CreatedAtAction(nameof(Get), new { id = book.Id }, book);
        });

    /// <summary>Ad / asıl kayıt / not ve kümeleri bütün olarak değiştirir. 404 kitap yok; 400; 403; 409 ad kullanımda.</summary>
    /// Paylaşımlı kitabın (kitap testi / çalışma öğesi / başka kullanıcının aynı adlı sorusu) adı ya da asıl kaydı yalnız
    /// Admin tarafından değiştirilir → aksi halde 403 <c>LabelingBookRenameForbidden</c>; aralık/içindekiler/not serbest.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] LabelingBookSaveRequestDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(Error("labeling.bank.userNotResolved"));

        return await Handle(async () =>
        {
            var book = await _books.UpdateAsync(id, request, new BookEditor(user.Id, IsAdmin), ct);
            return book is null ? NotFound(Error("labeling.books.notFound")) : Ok(book);
        });
    }

    /// <summary>204; 404; 409 kitap kullanımda (soru kaynağı, kitap testi, başka kitabın asıl kaydı ya da aynı adlı soru).</summary>
    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id, CancellationToken ct) =>
        Handle(async () => await _books.DeleteAsync(id, ct) ? NoContent() : NotFound(Error("labeling.books.notFound")));

    /// <summary>Dosya sayfasının bastırılmış numarası, cevap anahtarı mı ve içindekiler satırı (sınıf/ders/konu varsayılanı).</summary>
    [HttpGet("{id:int}/pages/{filePage:int}")]
    public Task<IActionResult> GetPage(int id, int filePage, CancellationToken ct) =>
        Handle(async () =>
        {
            var page = await _books.GetPageAsync(id, filePage, ct);
            return page is null ? NotFound(Error("labeling.books.notFound")) : Ok(page);
        });

    /// <summary>Bastırılmış sayfa → dosya sayfaları (fasiküllü kitapta birden çok; <c>near</c> verilirse en yakın önce).</summary>
    [HttpGet("{id:int}/printed/{printedPage:int}")]
    public Task<IActionResult> FindFilePages(int id, int printedPage, [FromQuery] int? near, CancellationToken ct) =>
        Handle(async () =>
        {
            var result = await _books.FindFilePagesAsync(id, printedPage, near, ct);
            return result is null ? NotFound(Error("labeling.books.notFound")) : Ok(result);
        });

    private async Task<IActionResult> Handle(System.Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (LabelingValidationException ex)
        {
            return BadRequest(Error(ex.Message));
        }
        catch (BookRegistryConflictException ex)
        {
            return Conflict(Error(ex.Message, ex.ErrorCode));
        }
        catch (BookRegistryForbiddenException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, Error(ex.Message, ex.ErrorCode));
        }
    }

    private object Error(string messageKey, string? errorCode = null) =>
        new { message = _localizer[messageKey].Value, errorCode };
}
