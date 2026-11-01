using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Data;

/// <summary>
/// issue #684: <see cref="WorksheetQuestion"/>'ın soft-delete query filter'ı adlıdır (<see cref="SoftDeleteKey"/>), #376'daki
/// <see cref="AvailabilitySlotQueryFilter"/> ile aynı gerekçe.
/// <para>
/// Maddeyi sayfadan kaldırmak bağı soft-delete eder. <see cref="WorksheetInstanceQuestion.WorksheetQuestion"/> zorunlu
/// navigasyon; EF filtreli zorunlu navigasyonu INNER JOIN + filtre ile çevirdiği için kaldırılan madde, onu çözmeye başlamış
/// (ya da bitirmiş) oturumun oynatıcısından, sonucundan, takibinden ve analizinden düşüyordu. Karar (#647): başlamış oturumlar
/// etkilenmez — oturum maddeyi başladığı hâliyle gösterir, bitirir ve puanlar. Kaldırma yalnız bundan sonra başlayan oturumları
/// (start-test'in madde kopyası) ve sayfanın güncel madde listesini değiştirir.
/// </para>
/// <para>
/// <see cref="WithRemovedWorksheetItems{T}"/> YALNIZ bu filtreyi kapatır; sorgu geneline uygulanır. Oturum satırları üzerinden
/// okuyan sorgular (oynatıcı, cevap / kontrol, sonuç, takip, analiz, açık uçlu kuyruğu) kullanır. Sayfanın güncel maddelerini
/// okuyan sorgular (start-test kopyası, madde listesi, numaralama, ekleme / bağlama) KULLANMAZ.
/// </para>
/// </summary>
public static class WorksheetQuestionQueryFilter
{
    /// <summary>Sayfa-madde bağının soft-delete filtresinin anahtarı.</summary>
    public const string SoftDeleteKey = "WorksheetQuestionSoftDelete";

    private static readonly string[] SoftDeleteKeys = [SoftDeleteKey];

    /// <summary>Oturum satırının maddesi sayfadan kaldırılmış olsa da okunur; diğer soft-delete filtreleri geçerli kalır.</summary>
    public static IQueryable<T> WithRemovedWorksheetItems<T>(this IQueryable<T> source) where T : class
        => source.IgnoreQueryFilters(SoftDeleteKeys);
}
