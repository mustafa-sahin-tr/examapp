using System.Collections.Generic;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services
{
    public interface IProgramService
    {
        Task<List<ProgramStepDto>> GetProgramStepsAsync();
        Task<UserProgramDto> CreateUserProgramAsync(string userId, CreateProgramRequestDto request);
        Task<List<UserProgramDto>> GetUserProgramsAsync(string userId);
        // Add other methods as needed, for example:
        // Task<ProgramStep> GetProgramStepByIdAsync(int id);
        // Task CreateProgramStepAsync(ProgramStep programStep);
        // Task UpdateProgramStepAsync(ProgramStep programStep);
        // Task DeleteProgramStepAsync(int id);
    }
}
