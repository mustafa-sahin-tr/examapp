using BadgeService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BadgeService.Controllers;

/// <summary>
/// Issue #897: the admin "Classifier" card — which provider is active, with which model and book mode, whether its key is
/// set, and what it did in the last 24 hours (<see cref="ClassifierActivity"/>, since the service started). No secret
/// leaves: only whether the key is present.
/// </summary>
[ApiController]
[Route("api/admin/classifier-status")]
[Authorize(Roles = "Admin")]
public class ClassifierStatusController(IConfiguration configuration, ClassifierActivity activity) : ControllerBase
{
    [HttpGet]
    public ActionResult<ClassifierStatusDto> Get()
    {
        var provider = configuration[QuestionClassifierRegistration.ProviderKey];
        var gemini = string.Equals(provider, QuestionClassifierRegistration.Gemini, StringComparison.OrdinalIgnoreCase);
        var section = configuration.GetSection(gemini ? GeminiOptions.SectionName : ClaudeClassifierOptions.SectionName);
        var snapshot = activity.Snapshot();

        return Ok(new ClassifierStatusDto
        {
            Provider = gemini ? QuestionClassifierRegistration.Gemini : QuestionClassifierRegistration.Claude,
            Model = section["Model"] ?? (gemini ? new GeminiOptions().Model : new ClaudeClassifierOptions().Model),
            Active = configuration.GetValue<bool?>("QuestionAnalyzer:AIActive") ?? true,
            ApiKeyConfigured = !string.IsNullOrWhiteSpace(section["ApiKey"]),
            BookTocMode = BookTocModeConfig.Read(configuration).ToString(),
            Since = snapshot.StartedAt,
            Classified = snapshot.Classified,
            Skipped = snapshot.Skipped,
            Failed = snapshot.Failed,
            LastErrorAt = snapshot.LastErrorAt,
            LastErrorKind = snapshot.LastErrorKind,
        });
    }
}

public sealed class ClassifierStatusDto
{
    /// <summary><c>Claude</c> or <c>Gemini</c>.</summary>
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    /// <summary><c>QuestionAnalyzer:AIActive</c>; false: every question is skipped.</summary>
    public bool Active { get; init; }
    public bool ApiKeyConfigured { get; init; }
    public string BookTocMode { get; init; } = string.Empty;
    /// <summary>Counts below are from this moment (service start) or the last 24 hours, whichever is later.</summary>
    public DateTime Since { get; init; }
    public int Classified { get; init; }
    public int Skipped { get; init; }
    /// <summary>Failed attempts; a message retried by MassTransit counts once per attempt.</summary>
    public int Failed { get; init; }
    public DateTime? LastErrorAt { get; init; }
    /// <summary>Exception type only, e.g. <c>HttpRequestException</c>.</summary>
    public string? LastErrorKind { get; init; }
}
