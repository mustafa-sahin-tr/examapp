using System;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Foundation.Contracts;

namespace ExamApp.Api.Services.Teachers.Seed;

/// <summary>
/// auth-api'nin dev-only <c>POST /api/auth/dev/seed-users</c> ucu (issue #217). Sözleşme
/// <c>ExamApp.Foundation.Contracts.DevSeedUsers*</c>. Servis token'ı (client_credentials) ile çağrılır;
/// Keycloak kullanıcısı + identity <c>User</c> satırını auth-api açar ve exam <c>Teacher.UserId</c> için
/// gereken identity id'sini döner. Bilinçli istisna: servisler arası doğrudan HTTP yalnızca bu dev-only
/// araçta — üretim akışı değildir; gateway bu yolu engeller, çağrı <c>AuthApiBaseUrl</c> ile doğrudandır.
/// </summary>
public interface IAuthApiSeedClient
{
    Task<DevSeedUsersResponse> SeedUsersAsync(DevSeedUsersRequest request, CancellationToken ct = default);
}

/// <summary>auth-api ucuna ulaşılamadı / reddetti (404 = ortam guard'ı ya da eski sürüm, 401/403 = servis token'ı, zaman aşımı).</summary>
public sealed class TeacherSeedAuthApiException : Exception
{
    public TeacherSeedAuthApiException(string message, Exception? inner = null) : base(message, inner) { }
}
