using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ExamApp.Api.Models.Dtos.Whiteboard;

/// <summary>
/// <c>JoinBoard(bookingId)</c> yanıtı (issue #98).
/// <list type="bullet">
/// <item><see cref="Elements"/>: Excalidraw elemanlarının anlık sahnesi (ilk katılımda boş); her eleman sunucu için opak JSON.</item>
/// <item><see cref="ServerVersion"/>: bkz. aşağıdaki uyarı.</item>
/// <item><see cref="Role"/>: çağıranın tahtadaki rolü, "teacher" | "student".</item>
/// <item><see cref="WindowClosesAtUtc"/>: tahtanın en geç kapanacağı an (UTC; bitiş + JoinWindowAfterMinutes).</item>
/// <item><see cref="PeerOnline"/>: karşı tarafın şu an bağlı en az bir bağlantısı var mı.</item>
/// </list>
/// <para>
/// <b><see cref="ServerVersion"/> bir SIRA NUMARASI DEĞİLDİR.</b> Sahneye kabul edilen her <c>SendElements</c> partisinde
/// artan bir sayaçtır; yalnızca teşhis/"sahne değişti mi" ipucu olarak kullanılmalı. <c>ElementsUpdated</c> yayınları bu
/// değeri taşımaz, farklı gönderenlerin yayınları farklı sırada gelebilir ve API yeniden başlarsa sayaç 0'dan başlar.
/// Uzlaşma her zaman eleman bazında <c>version</c>/<c>versionNonce</c> ile yapılır.
/// </para>
/// </summary>
public sealed record WhiteboardJoinResultDto(
    string BoardId,
    IReadOnlyList<JsonElement> Elements,
    long ServerVersion,
    string Role,
    DateTime WindowClosesAtUtc,
    bool PeerOnline);

/// <summary>
/// <c>SendElements</c> yanıtı. <see cref="Accepted"/>: sahneye giren eleman sayısı. <see cref="Corrections"/>: gönderenin
/// KAYBETTİĞİ id'ler için sunucudaki kazanan elemanlar (daha yüksek version ya da eşit version + daha küçük
/// versionNonce) — istemci bunları kendi sahnesine uygulamalı. <see cref="ServerVersion"/> sıra numarası değildir
/// (bkz. <see cref="WhiteboardJoinResultDto"/>).
/// </summary>
public sealed record WhiteboardSendResultDto(long ServerVersion, int Accepted, IReadOnlyList<JsonElement> Corrections);

/// <summary>
/// İmleç konumu (<c>SendPointer</c> girdisi, <c>PointerUpdated</c> çıktısı). <see cref="Tool"/> opsiyonel
/// (Excalidraw: "pointer" | "laser"); en fazla 16 harf/rakam/tire.
/// </summary>
public sealed class WhiteboardPointerDto
{
    public double X { get; set; }
    public double Y { get; set; }
    public string? Tool { get; set; }
}
