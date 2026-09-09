using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.LoginEvents;

/// <summary>
/// Login event kayıt servisi (issue #84). Aggregate/trend tarafı issue #6.
/// </summary>
public interface ILoginEventService
{
    Task<LoginEventCreatedDto> RecordAsync(LoginEventCreateDto request, CancellationToken ct = default);

    /// <summary>
    /// Kullanıcının mevcut oturumu hariç bir önceki başarılı girişini döner (issue #125).
    /// En son başarılı kayıt mevcut oturum kabul edilip atlanır; önceki giriş yoksa <c>null</c>.
    /// </summary>
    Task<LoginEvent?> GetPreviousSuccessfulLoginAsync(string keycloakUserId, CancellationToken ct = default);
}
