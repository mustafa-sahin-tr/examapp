using System.Collections.Generic;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProgramController : ControllerBase
    {
        private readonly IProgramService _programService;

        public ProgramController(IProgramService programService)
        {
            _programService = programService;
        }

        [HttpGet("steps")]
        public async Task<ActionResult<List<ProgramStepDto>>> GetProgramSteps()
        {
            var programSteps = await _programService.GetProgramStepsAsync();
            return Ok(programSteps);
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
