using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// Admin kullanıcı listeleri (öğretmen #152, öğrenci #153) için sayfadaki kullanıcıların ad/e-posta ve Keycloak
/// hesap durumunu TEK auth-api çağrısıyla çözer. Fail-soft: auth-api/Keycloak erişilemezse boş sözlük döner
/// (liste yine döner, ad/e-posta boş, hesap durumu null kalır).
/// </summary>
public interface IAdminUserDirectory
{
    Task<IReadOnlyDictionary<int, UserLookupResultDto>> ResolveWithAccountStatusAsync(
        IReadOnlyCollection<int> userIds, CancellationToken ct = default);
}
