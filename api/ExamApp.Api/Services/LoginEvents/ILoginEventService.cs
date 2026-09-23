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
    /// <summary>
    /// Login event'ini kaydeder. İş kuralı ihlalinde (ör. başarılı girişte KeycloakUserId yok — issue #100)
    /// satır yazılmaz ve <see cref="LoginEventRecordResult.ErrorKey"/> dolu döner (controller 400'e eşler).
    /// </summary>
    Task<LoginEventRecordResult> RecordAsync(LoginEventCreateDto request, CancellationToken ct = default);

    /// <summary>
    /// Kullanıcının mevcut oturumu hariç bir önceki başarılı girişini döner (issue #125).
    /// En son başarılı kayıt mevcut oturum kabul edilip atlanır; önceki giriş yoksa <c>null</c>.
    /// </summary>
    Task<LoginEvent?> GetPreviousSuccessfulLoginAsync(string keycloakUserId, CancellationToken ct = default);
}

/// <summary>
/// <see cref="ILoginEventService.RecordAsync"/> sonucu: ya <see cref="Created"/> dolu, ya da
/// <see cref="ErrorKey"/> (mesaj sözlüğü anahtarı, issue #184) dolu.
/// </summary>
public sealed class LoginEventRecordResult
{
    public LoginEventCreatedDto? Created { get; init; }
    public string? ErrorKey { get; init; }

    public static LoginEventRecordResult Ok(LoginEventCreatedDto created) => new() { Created = created };
    public static LoginEventRecordResult Fail(string errorKey) => new() { ErrorKey = errorKey };
}
