using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Foundation.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Student")]
    public class ProgramController : ControllerBase
    {
        private readonly IProgramService _programService;

        // Client'a dönen tüm metinler mesaj sözlüğünden gelir (issue #184).
    // DI her zaman gerçek localizer'ı verir; parametre yalnızca DI'siz kurulan (birim test)
    // senaryolarda varsayılan dile düşebilmek için opsiyonel.
        private readonly IStringLocalizer<Messages> _localizer;

        public ProgramController(IProgramService programService, IStringLocalizer<Messages>? localizer = null)
        {
            _programService = programService;
            _localizer = localizer ?? FallbackMessageLocalizer.Instance;
        }

        [HttpGet("steps")]
        [AllowAnonymous]
        public async Task<ActionResult<List<ProgramStepDto>>> GetProgramSteps()
        {
            var programSteps = await _programService.GetProgramStepsAsync();
            return Ok(programSteps);
        }

        [HttpPost("create")]
        public async Task<ActionResult<UserProgramDto>> CreateProgram([FromBody] CreateProgramRequestDto request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(_localizer["program.userIdNotFoundInToken"].Value);
            }

            var userProgram = await _programService.CreateUserProgramAsync(userId, request);
            return Ok(userProgram);
        }

        [HttpGet("my-programs")]
        public async Task<ActionResult<List<UserProgramDto>>> GetMyPrograms()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(_localizer["program.userIdNotFoundInToken"].Value);
            }

            var programs = await _programService.GetUserProgramsAsync(userId);
            return Ok(programs);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<UserProgramDto>> GetProgramById(int id)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(_localizer["program.userIdNotFoundInToken"].Value);
            }

            var program = await _programService.GetUserProgramByIdAsync(userId, id);
            if (program == null)
            {
                return NotFound();
            }

            return Ok(program);
        }

        [HttpPost("{id:int}/study-pages")]
        public async Task<ActionResult<UserProgramDto>> AddStudyItems(int id, [FromBody] ProgramStudyItemScheduleRequestDto request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(_localizer["program.userIdNotFoundInToken"].Value);
            }

            var program = await _programService.AddStudyItemSchedulesAsync(userId, id, request);
            if (program == null)
            {
                return NotFound();
            }

            return Ok(program);
        }

        [HttpPut("{programId:int}/study-pages/{scheduleId:int}/complete")]
        public async Task<IActionResult> CompleteStudyItem(int programId, int scheduleId, CancellationToken ct)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(_localizer["program.userIdNotFoundInToken"].Value);
            }

            var found = await _programService.CompleteStudyItemAsync(userId, programId, scheduleId, ct);
            if (!found)
            {
                return NotFound();
            }

            return NoContent();
        }

        [HttpDelete("{programId:int}/study-pages/{scheduleId:int}/complete")]
        public async Task<IActionResult> UncompleteStudyItem(int programId, int scheduleId, CancellationToken ct)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(_localizer["program.userIdNotFoundInToken"].Value);
            }

            var found = await _programService.UncompleteStudyItemAsync(userId, programId, scheduleId, ct);
            if (!found)
            {
                return NotFound();
            }

            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteProgram(int id, CancellationToken ct)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(_localizer["program.userIdNotFoundInToken"].Value);
            }

            var found = await _programService.DeleteUserProgramAsync(userId, id, ct);
            if (!found)
            {
                return NotFound();
            }

            return NoContent();
        }

        // Add other actions as needed for CRUD operations
        // For example:
        // [HttpGet("steps/{id}")]
        // public async Task<ActionResult<ProgramStep>> GetProgramStep(int id)
        // {
        //     var programStep = await _programService.GetProgramStepByIdAsync(id);
        //     if (programStep == null)
        //     {
        //         return NotFound();
        //     }
        //     return Ok(programStep);
        // }

        // [HttpPost("steps")]
        // public async Task<ActionResult<ProgramStep>> CreateProgramStep(ProgramStep programStep)
        // {
        //     await _programService.CreateProgramStepAsync(programStep);
        //     return CreatedAtAction(nameof(GetProgramStep), new { id = programStep.Id }, programStep);
        // }

        // [HttpPut("steps/{id}")]
        // public async Task<IActionResult> UpdateProgramStep(int id, ProgramStep programStep)
        // {
        //     if (id != programStep.Id)
        //     {
        //         return BadRequest();
        //     }
        //     await _programService.UpdateProgramStepAsync(programStep);
        //     return NoContent();
        // }

        // [HttpDelete("steps/{id}")]
        // public async Task<IActionResult> DeleteProgramStep(int id)
        // {
        //     await _programService.DeleteProgramStepAsync(id);
        //     return NoContent();
        // }
    }
}
