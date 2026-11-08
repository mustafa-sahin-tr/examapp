using System.Globalization;
using System.Text;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.BookImport;

/// <summary>
/// Issue #1056: maliyet raporunun iş tablosu CSV'si. Türkçe Excel'in açtığı biçim (<c>;</c> ayraç, virgüllü ondalık, BOM; emsal taban
/// puan CSV'si). Kitap adı ve kullanıcı adı serbest metindir: formül olarak çalışmasın diye başına <c>'</c> konur.
/// </summary>
public static class BookImportCostCsv
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static byte[] Write(IEnumerable<BookImportCostJobDto> jobs)
    {
        var csv = new StringBuilder();
        csv.AppendLine("İş;Tür;Kitap;Kullanıcı Id;Kullanıcı;Oluşturma (UTC);Durum;Sayfa;Kaydedilen soru;Maliyet (USD)");
        foreach (var job in jobs)
        {
            csv.AppendJoin(';',
                job.Id.ToString(),
                job.Kind,
                Text(job.BookName),
                job.OwnerId.ToString(CultureInfo.InvariantCulture),
                Text(job.OwnerName ?? string.Empty),
                job.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                job.Status,
                job.Pages.ToString(CultureInfo.InvariantCulture),
                job.QuestionsSaved.ToString(CultureInfo.InvariantCulture),
                job.CostUsd.ToString("0.000000", Turkish));
            csv.AppendLine();
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static string Text(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
