using System.Text;
using ExamApp.Api.Services.TaxonomySearch;

namespace ExamApp.Api.Services.TargetGoals;

/// <summary>
/// Issue #695: admin dosyası. Excel'in Türkçe çıktısı noktalı virgül ve Windows-1254 yazar, bu yüzden ayraç başlıktan,
/// kodlama içerikten anlaşılır. Sütun sırası serbesttir; başlıklar büyük/küçük harf ve Türkçe harf farkı gözetmez.
/// </summary>
internal static class TargetProgramCsv
{
    public const string Name = "name";
    public const string Institution = "institution";
    public const string City = "city";
    public const string District = "district";
    public const string ScoreType = "scoreType";
    public const string MinimumScore = "minimumScore";
    public const string Percentile = "percentile";

    private static readonly Dictionary<string, string> Headers = new()
    {
        ["ad"] = Name, ["okul"] = Name, ["okul adi"] = Name, ["program"] = Name, ["program adi"] = Name,
        ["kurum"] = Institution, ["universite"] = Institution, ["universite adi"] = Institution,
        ["il"] = City, ["sehir"] = City,
        ["ilce"] = District,
        ["puan turu"] = ScoreType,
        ["taban puan"] = MinimumScore, ["taban puani"] = MinimumScore,
        ["yuzdelik"] = Percentile, ["yuzdelik dilim"] = Percentile,
    };

    private static readonly char[] Delimiters = [';', ',', '\t'];

    static TargetProgramCsv() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Geçerli UTF-8 değilse dosya Windows-1254'tür.</summary>
    public static string Decode(byte[] bytes)
    {
        var offset = bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1254).GetString(bytes);
        }
    }

    /// <summary>Başlıktan sonraki satırlar: satır numarası ve sütun adına göre değerler. Tanınmayan sütun yok sayılır, boş satır atlanır.</summary>
    public static List<(int Line, Dictionary<string, string> Values)> Parse(string text)
    {
        var delimiter = DetectDelimiter(text);
        var rows = ReadRows(text, delimiter);
        if (rows.Count == 0)
            throw new TargetGoalRuleException("CsvHeaderMissing");

        var columns = rows[0].Cells.Select(cell => Headers.GetValueOrDefault(TaxonomySearchService.Normalize(cell.Trim()))).ToList();
        if (!columns.Contains(Name) || !columns.Contains(City) || !columns.Contains(MinimumScore))
            throw new TargetGoalRuleException("CsvHeaderMissing");

        var result = new List<(int, Dictionary<string, string>)>(rows.Count - 1);
        foreach (var (line, cells) in rows.Skip(1))
        {
            var values = new Dictionary<string, string>();
            for (var i = 0; i < cells.Count && i < columns.Count; i++)
                if (columns[i] is { } column)
                    values[column] = cells[i];
            result.Add((line, values));
        }
        return result;
    }

    private static char DetectDelimiter(string text)
    {
        var end = text.IndexOf('\n');
        var header = end < 0 ? text : text[..end];
        return Delimiters.MaxBy(d => header.Count(c => c == d));
    }

    private static List<(int Line, List<string> Cells)> ReadRows(string text, char delimiter)
    {
        var rows = new List<(int, List<string>)>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var line = 1;
        var rowLine = 1;
        var quoted = false;

        void EndRow()
        {
            cells.Add(cell.ToString());
            cell.Clear();
            if (cells.Any(c => !string.IsNullOrWhiteSpace(c)))
                rows.Add((rowLine, cells));
            cells = [];
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                    quoted = false;
                else
                {
                    if (c == '\n')
                        line++;
                    cell.Append(c);
                }
            }
            else if (c == '"' && cell.Length == 0)
                quoted = true;
            else if (c == delimiter)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else if (c == '\n')
            {
                EndRow();
                rowLine = ++line;
            }
            else if (c != '\r')
                cell.Append(c);
        }
        EndRow();
        return rows;
    }
}
