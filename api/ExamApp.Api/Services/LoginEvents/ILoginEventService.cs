using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.LoginEvents;

/// <summary>
/// Login event kayıt servisi (issue #84). Sadece yazma; okuma/aggregate tarafı issue #6.
/// </summary>
public interface ILoginEventService
{
    Task<LoginEventCreatedDto> RecordAsync(LoginEventCreateDto request, CancellationToken ct = default);
}
