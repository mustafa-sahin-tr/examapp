using System;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Keycloak çağrısının neden başarısız olduğunu istemciye sızdırmadan HTTP durumuna eşlemek için
/// sınıflandırma (issue #231). Mesaj metni (Keycloak error/description, ham gövde) yalnızca log içindir.
/// </summary>
public enum KeycloakFailureKind
{
    /// <summary>Sınıflandırılmamış hata (yapılandırma, beklenmeyen yanıt vb.) — istemciye 500.</summary>
    Unexpected = 0,

    /// <summary>
    /// Token uç noktası isteği reddetti (<c>invalid_grant</c>): yanlış kullanıcı adı/parola, süresi dolmuş/
    /// kullanılmış authorization code, devre dışı hesap — istemciye 401.
    /// </summary>
    InvalidGrant = 1,

    /// <summary>Keycloak'a ulaşılamadı: ağ hatası, zaman aşımı, devre kesici veya 5xx — istemciye 503.</summary>
    ProviderUnavailable = 2,

    /// <summary>
    /// Keycloak kaynağı oluşturmayı çakışma ile reddetti (409 — örn. kullanıcı adı/e-posta zaten kayıtlı).
    /// Register bunu istemciye AYIRT EDİLEMEZ şekilde (genel kabul yanıtı) yansıtır — issue #240.
    /// </summary>
    Conflict = 3,

    /// <summary>
    /// Keycloak girdiyi doğrulama hatasıyla reddetti (400 — parola politikası, geçersiz ad/e-posta). Register'da
    /// yerel kurallar realm politikasıyla hizalıdır; buraya düşmek bir kaymadır (#240).
    /// </summary>
    Validation = 4,
}

public class KeycloakException : Exception
{
    public int StatusCode { get; }

    public KeycloakFailureKind Kind { get; }

    public KeycloakException(string message, int statusCode = 500, KeycloakFailureKind kind = KeycloakFailureKind.Unexpected)
        : base(message)
    {
        StatusCode = statusCode;
        Kind = kind;
    }

    public KeycloakException(string message, Exception inner, int statusCode = 500, KeycloakFailureKind kind = KeycloakFailureKind.Unexpected)
        : base(message, inner)
    {
        StatusCode = statusCode;
        Kind = kind;
    }
}
