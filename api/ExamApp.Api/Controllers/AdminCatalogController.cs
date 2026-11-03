using System.Text.Encodings.Web;
using System.Text.Json;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>Sınıf, ders, konu, alt konu ve kazanım kataloğunu bir ortamdan diğerine taşır.</summary>
[ApiController]
[Route("api/admin/catalog")]
[Authorize(Roles = "Admin")]
public class AdminCatalogController : BaseController
{
    // Dosya insanlar tarafından okunup karşılaştırılıyor: Türkçe karakterler kaçışsız yazılır.
    private static readonly JsonSerializerOptions FileJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ICatalogTransferService _catalog;

    public AdminCatalogController(ICatalogTransferService catalog) => _catalog = catalog;

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct = default)
    {
        var bundle = await _catalog.ExportAsync(ct);
        return File(JsonSerializer.SerializeToUtf8Bytes(bundle, FileJsonOptions), "application/json", "catalog.json");
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] CatalogBundleDto bundle, [FromQuery] bool dryRun = false, CancellationToken ct = default)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        var result = await _catalog.ImportAsync(bundle, dryRun, user.Id, ct);
        return result.Errors.Count > 0 ? BadRequest(result) : Ok(result);
    }
}
