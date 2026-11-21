using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.Video;

/// <summary>
/// issue #490: randevunun görüşme odasını sunucu tarafında kapatır (içindekiler atılır). Randevu iptalinde ve öğretmen
/// askısında çağrılır; yoklamayı çalıştırmayan bir istemci (ör. joinUrl'i doğrudan açan askıdaki öğretmen) odada kalamaz.
/// <para>
/// Hata fırlatmaz: kapatma başarısızsa loglanır. Yedekler istemci yoklaması (#320, <c>live-status</c>) ve token ömrüdür;
/// odaya yeniden girmek için alınan yeni token zaten sunucuda doğrulanır (Approved + öğretmen müsait + pencere).
/// </para>
/// </summary>
public interface IVideoRoomCloser
{
    /// <summary>Randevunun odası açıktıysa kapatır. Kapatıldıysa true; oda yoksa, kapatma kapalıysa ya da hata olursa false.</summary>
    Task<bool> CloseRoomAsync(int bookingId, CancellationToken ct = default);
}

/// <summary>Kapatma yapılandırılmamış ortamlar ve DI'siz kurulan örnekler için.</summary>
public sealed class NoOpVideoRoomCloser : IVideoRoomCloser
{
    public static readonly NoOpVideoRoomCloser Instance = new();

    public Task<bool> CloseRoomAsync(int bookingId, CancellationToken ct = default) => Task.FromResult(false);
}
