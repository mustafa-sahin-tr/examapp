namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// <c>PATCH api/admin/{teachers|students}/{id}/account-status</c> gövdesi (issue #155): <c>{ "enabled": false }</c>.
/// Alan zorunludur — eksik/null ise controller yerelleştirilmiş <c>{ message }</c> ile 400 döner (yanlışlıkla varsayılan
/// <c>false</c> ile hesap kapatılmasın). <c>[Required]</c> bilinçli olarak yok: [ApiController]'ın otomatik ProblemDetails
/// yanıtı yerelleştirilmiş mesajı gölgelerdi.
/// </summary>
public sealed class AdminAccountStatusRequestDto
{
    public bool? Enabled { get; init; }
}

/// <summary>Başarılı yanıt: hesabın yeni durumu (<c>{ "enabled": bool }</c>).</summary>
public sealed class AdminAccountStatusResponseDto
{
    public bool Enabled { get; init; }
}
