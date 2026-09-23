namespace ExamApp.Api.Helpers;

/// <summary>
/// <c>POST /api/auth/register</c> ayarları (issue #240). "Registration" bölümünden okunur.
/// </summary>
public class RegistrationSettings
{
    public const string SectionName = "Registration";

    /// <summary>
    /// Kabul yanıtının (yeni kayıt / zaten kayıtlı e-posta) en erken döneceği süre. Kayıtlı e-posta yolu
    /// Keycloak'ta kullanıcı oluşturmadığı için çok daha hızlı biter; bu taban süre iki yolu yanıt süresinden
    /// ayırt edilemez kılar. Başarılı kaydın tipik süresinden (Keycloak kullanıcı + rol ataması + DB) büyük
    /// tutulmalı. 0 = kapalı (yalnızca testler).
    /// </summary>
    public int MinimumResponseMilliseconds { get; set; } = 1500;
}
