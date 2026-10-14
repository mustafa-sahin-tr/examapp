namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// <c>POST api/admin/{teachers|students}/{id}/reset-password</c> başarılı yanıtı (issue #156).
/// Geçici şifre YALNIZCA bu yanıtta, bir kez döner; sunucu hiçbir yerde saklamaz/loglamaz.
/// Kullanıcı ilk girişte şifresini değiştirmek zorundadır (Keycloak <c>temporary: true</c>).
/// </summary>
public sealed class AdminPasswordResetResponseDto
{
    public AdminPasswordResetResponseDto()
    {
    }

    public AdminPasswordResetResponseDto(string pw, bool permanent)
    {
        TemporaryPassword = pw;
        Permanent = permanent;
    }

    public string TemporaryPassword { get; init; } = string.Empty;

    /// <summary>
    /// Issue #438: true = veli tarafından açılmış çocuk hesabı; şifre KALICI (ilk girişte değiştirme zorunluluğu yok — çocuk
    /// kendi şifresini koyup veli kontrolünü delmesin). Admin şifreyi veliye iletir. Alan adı geriye uyum için
    /// <see cref="TemporaryPassword"/> kalır.
    /// </summary>
    public bool Permanent { get; init; }

    // Şifre yanlışlıkla ToString/log ile yazılmasın.
    public override string ToString() => $"{nameof(AdminPasswordResetResponseDto)} {{ TemporaryPassword = *** }}";
}
