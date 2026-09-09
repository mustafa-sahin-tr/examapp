using System.Collections.Generic;
using System.Threading;
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
        Task<UserProgramDto?> GetUserProgramByIdAsync(string userId, int programId);
        Task<UserProgramDto?> AddStudyPageSchedulesAsync(string userId, int programId, ProgramStudyPageScheduleRequestDto request);
        /// <summary>Sayfayı tamamlandı olarak işaretler (idempotent). Program/schedule sahibine ait değilse veya yoksa false döner.</summary>
        Task<bool> CompleteStudyPageAsync(string userId, int programId, int scheduleId, CancellationToken ct = default);
        /// <summary>Tamamlanma işaretini geri alır (idempotent). Program/schedule sahibine ait değilse veya yoksa false döner.</summary>
        Task<bool> UncompleteStudyPageAsync(string userId, int programId, int scheduleId, CancellationToken ct = default);
        /// <summary>Programı soft-delete eder (IsActive=false). Sahibine ait değilse veya yoksa false döner.</summary>
        Task<bool> DeleteUserProgramAsync(string userId, int programId, CancellationToken ct = default);
        // Add other methods as needed, for example:
        // Task<ProgramStep> GetProgramStepByIdAsync(int id);
        // Task CreateProgramStepAsync(ProgramStep programStep);
        // Task UpdateProgramStepAsync(ProgramStep programStep);
        // Task DeleteProgramStepAsync(int id);
    }
}
