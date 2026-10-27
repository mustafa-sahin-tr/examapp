using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Etkinlik bloğu kırpmalarının SHA-256'sı (#577 <see cref="QuestionCropHash"/>): bankaya kayıttaki yolla — sayfa bir kez JPEG'e
/// kodlanır (<c>LabelingImage.MaxEdge</c>, küçültme yok), her blok o JPEG'den kırpılır. Aynı blok ileride bankaya kaydedilirse
/// <c>QuestionSource.CropSha256</c> ile aynı değer çıkar. #579 yapı önbelleği ve #580 çözüm önbelleği aynı tanımı kullanır.
/// </summary>
internal static class ActivityCropHash
{
    public static IReadOnlyList<string> Compute(ImageHelper imageHelper, LabelingImage image, IReadOnlyList<Rectangle> rects)
    {
        try
        {
            var pageJpeg = image.Encode(LabelingImage.MaxEdge).Jpeg;
            return QuestionCropHash.Compute(imageHelper, pageJpeg, rects.Select(r => (r.X, r.Y, r.Width, r.Height)).ToList());
        }
        catch (Exception ex) when (ex is ImageDecodeException or ArgumentOutOfRangeException)
        {
            throw new LabelingValidationException("labeling.invalidImage");
        }
    }
}
