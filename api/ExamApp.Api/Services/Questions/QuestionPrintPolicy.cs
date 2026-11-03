using ExamApp.Api.Data;

namespace ExamApp.Api.Services.Questions;

/// <summary>
/// Issue #701: sorunun kağıda basılıp basılamayacağı. Yayıncı sözleşmeleri baskı iznini henüz kayıt altına almadığı için
/// yayıncı sorusu basılmaz; sözleşme alanı eklenince bu kural değişir. Kaynağı bilinmeyen (Unknown) soru engellenmez.
/// </summary>
public static class QuestionPrintPolicy
{
    public const string Unpublished = "unpublished";
    public const string LicenseExpired = "licenseExpired";
    public const string PublisherLicense = "publisherLicense";

    /// <summary>Basılamıyorsa nedeni, basılabiliyorsa <c>null</c>.</summary>
    public static string? BlockReason(QuestionSourceType sourceType, bool isUnpublished, DateTime? licenseEndsAt, DateTime utcNow)
    {
        if (isUnpublished)
            return Unpublished;
        if (licenseEndsAt <= utcNow)
            return LicenseExpired;
        return sourceType == QuestionSourceType.Publisher ? PublisherLicense : null;
    }
}
