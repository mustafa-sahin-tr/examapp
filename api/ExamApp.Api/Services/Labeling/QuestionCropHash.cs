using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Issue #577: soru kırpmasının SHA-256'sı (<c>QuestionSource.CropSha256</c>). Tanım: MinIO'ya yüklenen
/// <c>question.jpg</c> baytlarının hash'i. Toplu kayıt (<c>QuestionService.SaveBulkQuestion</c>) sayfayı
/// <see cref="ImageHelper.LoadImage"/> ile bir kez çözer, her soruyu <see cref="ImageHelper.CropBitmap"/> ile kırpar ve
/// <see cref="ImageHelper.EncodeImage"/> ile sayfa formatında yazar; <see cref="Compute"/> aynı yolu izlediği için kayıt
/// öncesi tekrar kontrolü ile kayıtta saklanan hash birebir aynıdır (Skia kodlaması deterministik).
/// </summary>
public static class QuestionCropHash
{
    public static string Of(byte[] cropBytes) => Convert.ToHexStringLower(SHA256.HashData(cropBytes));

    /// <summary>
    /// Sayfa baytları + kutular (sayfa pikseli, sayfanın içinde) → kutu başına kırpma hash'i (aynı sırada). Issue #581:
    /// <paramref name="masks"/> verilirse (kutu sırasıyla, kutuya göre) toplu kayıttaki gibi önce tüm maskeler sayfaya boyanır,
    /// sonra kırpılır — maskeli sorunun (ve maskeyle örtüşen komşu kutunun) hash'i maskeli görselindir.
    /// </summary>
    public static IReadOnlyList<string> Compute(
        ImageHelper imageHelper, byte[] pageBytes, IReadOnlyList<(int X, int Y, int Width, int Height)> boxes,
        IReadOnlyList<IReadOnlyList<RegionBoxDto>?>? masks = null)
    {
        using var page = imageHelper.LoadImage(pageBytes, out var format);
        // Toplu kayıtla aynı sıra: önce tüm maskeler sayfaya (bölgeye göre → sayfa koordinatı), sonra kırpmalar.
        if (masks is not null)
        {
            for (var i = 0; i < boxes.Count && i < masks.Count; i++)
            {
                var (bx, by, bw, bh) = boxes[i];
                QuestionImageMasks.ApplyToPage(page, bx, by, bw, bh, masks[i]);
            }
        }
        var hashes = new List<string>(boxes.Count);
        foreach (var (x, y, width, height) in boxes)
        {
            using var crop = imageHelper.CropBitmap(page, x, y, width, height);
            hashes.Add(Of(imageHelper.EncodeImage(crop, format)));
        }
        return hashes;
    }
}
