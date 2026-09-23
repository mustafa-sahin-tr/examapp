namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// <c>POST api/admin/{teachers|students}/{id}/reset-password</c> başarılı yanıtı (issue #156).
/// Geçici şifre YALNIZCA bu yanıtta, bir kez döner; sunucu hiçbir yerde saklamaz/loglamaz.
/// Kullanıcı ilk girişte şifresini değiştirmek zorundadır (Keycloak <c>temporary: true</c>).
/// </summary>
public sealed class AdminPasswordResetResponseDto
{
    public string TemporaryPassword { get; init; } = string.Empty;

    // Şifre yanlışlıkla ToString/log ile yazılmasın.
    public override string ToString() => $"{nameof(AdminPasswordResetResponseDto)} {{ TemporaryPassword = *** }}";
}
