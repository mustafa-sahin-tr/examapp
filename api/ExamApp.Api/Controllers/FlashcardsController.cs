using System;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Flashcards;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #699: öğrencinin bilgi kartları. Kendi destesi özeldir; öğretmen destesi salt okunur gelir.
/// Gateway'de <c>/api/exam/{everything}</c> joker route'u kapsar.
/// </summary>
[ApiController]
[Route("api/flashcards")]
[Authorize(Roles = "Student")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class FlashcardsController(
    IFlashcardDeckService decks,
    IFlashcardStudyService study,
    IStudentService studentService,
    IStringLocalizer<Messages> localizer) : BaseController
{
    private const int MaxDueTake = 50;

    [HttpGet("decks")]
    public Task<IActionResult> ListDecks(CancellationToken ct) =>
        AsLearnerAsync(async learner => Ok(await study.ListDecksAsync(learner, ct)), ct);

    [HttpGet("decks/{id:int}")]
    public Task<IActionResult> GetDeck(int id, CancellationToken ct) =>
        AsLearnerAsync(async learner => await study.GetDeckAsync(learner, id, ct) is { } deck ? Ok(deck) : NotFound(), ct);

    [HttpPost("decks")]
    public Task<IActionResult> CreateDeck([FromBody] SaveFlashcardDeckDto request, CancellationToken ct) =>
        AsLearnerAsync(async learner => Ok(await decks.CreateAsync(Owner(learner), request, ct)), ct);

    [HttpPut("decks/{id:int}")]
    public Task<IActionResult> UpdateDeck(int id, [FromBody] SaveFlashcardDeckDto request, CancellationToken ct) =>
        AsLearnerAsync(async learner => await decks.UpdateAsync(Owner(learner), id, request, ct) is { } deck ? Ok(deck) : NotFound(), ct);

    [HttpDelete("decks/{id:int}")]
    public Task<IActionResult> DeleteDeck(int id, CancellationToken ct) =>
        AsLearnerAsync(async learner => await decks.DeleteAsync(Owner(learner), id, ct) ? NoContent() : NotFound(), ct);

    [HttpPost("decks/{id:int}/cards")]
    public Task<IActionResult> AddCard(int id, [FromBody] SaveFlashcardDto request, CancellationToken ct) =>
        AsLearnerAsync(async learner => await decks.AddCardAsync(Owner(learner), id, request, ct) is { } card ? Ok(card) : NotFound(), ct);

    [HttpPut("cards/{cardId:int}")]
    public Task<IActionResult> UpdateCard(int cardId, [FromBody] SaveFlashcardDto request, CancellationToken ct) =>
        AsLearnerAsync(async learner => await decks.UpdateCardAsync(Owner(learner), cardId, request, ct) is { } card ? Ok(card) : NotFound(), ct);

    [HttpDelete("cards/{cardId:int}")]
    public Task<IActionResult> DeleteCard(int cardId, CancellationToken ct) =>
        AsLearnerAsync(async learner => await decks.DeleteCardAsync(Owner(learner), cardId, ct) ? NoContent() : NotFound(), ct);

    [HttpGet("due")]
    public Task<IActionResult> GetDue([FromQuery] int? deckId = null, [FromQuery] int take = 20, CancellationToken ct = default) =>
        AsLearnerAsync(async learner => Ok(await study.GetDueCardsAsync(learner, deckId, Math.Clamp(take, 1, MaxDueTake), ct)), ct);

    [HttpGet("due-count")]
    public Task<IActionResult> GetDueCount(CancellationToken ct) =>
        AsLearnerAsync(async learner =>
        {
            var count = await study.GetDueCountAsync(learner, ct);
            var (nextDay, nextCount) = await study.GetNextDueAsync(learner, ct);
            return Ok(new FlashcardDueCountDto { Count = count, NextDueDay = nextDay, NextDueCount = nextCount });
        }, ct);

    [HttpPost("cards/{cardId:int}/review")]
    public Task<IActionResult> Review(int cardId, [FromBody] FlashcardReviewRequestDto request, CancellationToken ct)
    {
        if (!Enum.TryParse<FlashcardRecall>(request.Recall, ignoreCase: false, out var recall) || !Enum.IsDefined(recall))
            return Task.FromResult<IActionResult>(BadRequest());

        return AsLearnerAsync(async learner =>
        {
            var result = await study.ReviewAsync(learner, cardId, recall, ct);
            return result.Outcome switch
            {
                FlashcardReviewOutcome.Recorded => Ok(result.Review),
                FlashcardReviewOutcome.NotDue => Conflict(new { errorCode = "NotDue" }),
                _ => NotFound(),
            };
        }, ct);
    }

    private static FlashcardOwner Owner(FlashcardLearner learner) => new(learner.UserId, FlashcardDeckOwnerKind.Student);

    private async Task<IActionResult> AsLearnerAsync(Func<FlashcardLearner, Task<IActionResult>> action, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(localizer["auth.authenticationFailed"].Value);

        var student = await studentService.GetStudentProfile(user.Id);
        if (student == null)
            return NotFound(localizer["student.profileNotFound"].Value);

        try
        {
            return await action(new FlashcardLearner(student.Id, user.Id, student.GradeId, student.SchoolId));
        }
        catch (FlashcardRuleException ex)
        {
            return BadRequest(new { errorCode = ex.Message });
        }
    }
}

/// <summary>Issue #699: öğretmen destesi; onaylı öğretmen, yalnız kendi desteleri. Admin ayrıcalığı yok.</summary>
[ApiController]
[Route("api/teacher/flashcards")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
[Authorize(Roles = "Teacher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TeacherFlashcardsController(IFlashcardDeckService decks) : BaseController
{
    [HttpGet("decks")]
    public Task<IActionResult> ListDecks(CancellationToken ct) =>
        AsOwnerAsync(async owner => Ok(await decks.ListOwnedAsync(owner, ct)), ct);

    [HttpGet("decks/{id:int}")]
    public Task<IActionResult> GetDeck(int id, CancellationToken ct) =>
        AsOwnerAsync(async owner => await decks.GetOwnedAsync(owner, id, ct) is { } deck ? Ok(deck) : NotFound(), ct);

    [HttpPost("decks")]
    public Task<IActionResult> CreateDeck([FromBody] SaveFlashcardDeckDto request, CancellationToken ct) =>
        AsOwnerAsync(async owner => Ok(await decks.CreateAsync(owner, request, ct)), ct);

    [HttpPut("decks/{id:int}")]
    public Task<IActionResult> UpdateDeck(int id, [FromBody] SaveFlashcardDeckDto request, CancellationToken ct) =>
        AsOwnerAsync(async owner => await decks.UpdateAsync(owner, id, request, ct) is { } deck ? Ok(deck) : NotFound(), ct);

    [HttpDelete("decks/{id:int}")]
    public Task<IActionResult> DeleteDeck(int id, CancellationToken ct) =>
        AsOwnerAsync(async owner => await decks.DeleteAsync(owner, id, ct) ? NoContent() : NotFound(), ct);

    [HttpPost("decks/{id:int}/cards")]
    public Task<IActionResult> AddCard(int id, [FromBody] SaveFlashcardDto request, CancellationToken ct) =>
        AsOwnerAsync(async owner => await decks.AddCardAsync(owner, id, request, ct) is { } card ? Ok(card) : NotFound(), ct);

    [HttpPut("cards/{cardId:int}")]
    public Task<IActionResult> UpdateCard(int cardId, [FromBody] SaveFlashcardDto request, CancellationToken ct) =>
        AsOwnerAsync(async owner => await decks.UpdateCardAsync(owner, cardId, request, ct) is { } card ? Ok(card) : NotFound(), ct);

    [HttpDelete("cards/{cardId:int}")]
    public Task<IActionResult> DeleteCard(int cardId, CancellationToken ct) =>
        AsOwnerAsync(async owner => await decks.DeleteCardAsync(owner, cardId, ct) ? NoContent() : NotFound(), ct);

    [HttpGet("decks/{id:int}/assignments")]
    public async Task<IActionResult> ListAssignments(int id, CancellationToken ct)
    {
        var scope = await GetSchoolScopeAsync(ct);
        return await decks.ListAssignmentsAsync(scope, id, ct) is { } rows ? Ok(rows) : NotFound();
    }

    [HttpPost("decks/{id:int}/assignments")]
    public async Task<IActionResult> Assign(int id, [FromBody] AssignFlashcardDeckDto request, CancellationToken ct)
    {
        var scope = await GetSchoolScopeAsync(ct);
        try
        {
            return await decks.AssignAsync(scope, id, request, ct) is { } assignment ? Ok(assignment) : NotFound();
        }
        catch (FlashcardRuleException ex)
        {
            return BadRequest(new { errorCode = ex.Message });
        }
    }

    [HttpDelete("decks/{id:int}/assignments/{assignmentId:int}")]
    public async Task<IActionResult> Unassign(int id, int assignmentId, CancellationToken ct)
    {
        var scope = await GetSchoolScopeAsync(ct);
        return await decks.UnassignAsync(scope, id, assignmentId, ct) ? NoContent() : NotFound();
    }

    private async Task<IActionResult> AsOwnerAsync(Func<FlashcardOwner, Task<IActionResult>> action, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();

        try
        {
            return await action(new FlashcardOwner(user.Id, FlashcardDeckOwnerKind.Teacher));
        }
        catch (FlashcardRuleException ex)
        {
            return BadRequest(new { errorCode = ex.Message });
        }
    }
}
