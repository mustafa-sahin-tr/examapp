namespace ExamApp.Api.Helpers;

/// <summary>
/// Dev-only toplu kullanıcı oluşturma (issue #217) izin verilmeyen ortamda çağrıldı. Controller bunu 404'e
/// eşler (yüzey yok gibi davranır); diğer istisnalar (EF, Keycloak) 500 olarak kalır.
/// </summary>
public sealed class DevSeedEnvironmentException : Exception
{
    public DevSeedEnvironmentException(string message) : base(message) { }
}
