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
        /// <summary>
        /// Programı oluşturur ve takvimi üretir (issue #514): dersler <paramref name="studentUserId"/> öğrencisinin
        /// sınıfının dersleridir, zor dersler daha sık planlanır. Sınıf bilinmiyorsa bütün dersler kullanılır.
        /// </summary>
        Task<UserProgramDto> CreateUserProgramAsync(string userId, CreateProgramRequestDto request, int? studentUserId = null);
        Task<List<UserProgramDto>> GetUserProgramsAsync(string userId);
        Task<UserProgramDto?> GetUserProgramByIdAsync(string userId, int programId);
        Task<UserProgramDto?> AddStudyItemSchedulesAsync(string userId, int programId, ProgramStudyItemScheduleRequestDto request);
        /// <summary>Sayfayı tamamlandı olarak işaretler (idempotent). Program/schedule sahibine ait değilse veya yoksa false döner.</summary>
        Task<bool> CompleteStudyItemAsync(string userId, int programId, int scheduleId, CancellationToken ct = default);
        /// <summary>Tamamlanma işaretini geri alır (idempotent). Program/schedule sahibine ait değilse veya yoksa false döner.</summary>
        Task<bool> UncompleteStudyItemAsync(string userId, int programId, int scheduleId, CancellationToken ct = default);
        /// <summary>Programı soft-delete eder (IsActive=false). Sahibine ait değilse veya yoksa false döner.</summary>
        Task<bool> DeleteUserProgramAsync(string userId, int programId, CancellationToken ct = default);
        // Add other methods as needed, for example:
        // Task<ProgramStep> GetProgramStepByIdAsync(int id);
        // Task CreateProgramStepAsync(ProgramStep programStep);
        // Task UpdateProgramStepAsync(ProgramStep programStep);
        // Task DeleteProgramStepAsync(int id);
    }
}
