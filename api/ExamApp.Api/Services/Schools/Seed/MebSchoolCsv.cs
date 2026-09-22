using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ExamApp.Api.Services.Schools.Seed;

/// <summary>Kaynak CSV'deki bir satır — yalnızca içe aktarma için gereken sütunlar.</summary>
public sealed record MebSchoolRow(
    string Province,
    string District,
    string Name,
    string InstitutionCode,
    string SchoolType);

/// <summary>
/// <c>dalgali/MEB-okul-listesi</c> CSV'sini okur (issue #216). RFC 4180 kuralları: virgül ayraç,
/// tırnaklı alanlar, tırnak içinde <c>""</c> kaçışı ve satır sonu, CRLF/LF. UTF-8 BOM'u
/// <see cref="StreamReader"/> zaten yutar; ilk başlıkta kalmış bir BOM de ayrıca temizlenir.
/// Sütunlar başlık adına göre bulunur (sıra bağımsız).
/// </summary>
public static class MebSchoolCsv
{
    private const string ColProvince = "il";
    private const string ColDistrict = "ilce";
    private const string ColName = "okul_adi";
    private const string ColCode = "kurum_kodu";
    private const string ColType = "okul_turu";

    public static IReadOnlyList<MebSchoolRow> Parse(Stream stream)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return Parse(reader);
    }

    public static IReadOnlyList<MebSchoolRow> Parse(TextReader reader)
    {
        var header = ReadRecord(reader)
            ?? throw new InvalidDataException("Okul CSV'si boş: başlık satırı yok.");

        if (header.Count > 0)
            header[0] = header[0].TrimStart('﻿');

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Count; i++)
            index[header[i].Trim()] = i;

        var iProvince = Require(index, ColProvince);
        var iDistrict = Require(index, ColDistrict);
        var iName = Require(index, ColName);
        var iCode = Require(index, ColCode);
        var iType = Require(index, ColType);

        var rows = new List<MebSchoolRow>();
        List<string>? record;
        var recordNo = 1;
        while ((record = ReadRecord(reader)) != null)
        {
            recordNo++;
            if (record.Count == 1 && record[0].Length == 0)
                continue; // boş satır

            if (record.Count <= Math.Max(Math.Max(iProvince, iDistrict), Math.Max(Math.Max(iName, iCode), iType)))
                throw new InvalidDataException($"Okul CSV'si kayıt {recordNo}: {record.Count} alan var, beklenen en az {header.Count}.");

            rows.Add(new MebSchoolRow(
                record[iProvince].Trim(),
                record[iDistrict].Trim(),
                record[iName].Trim(),
                record[iCode].Trim(),
                record[iType].Trim()));
        }

        return rows;
    }

    private static int Require(Dictionary<string, int> index, string column)
        => index.TryGetValue(column, out var i)
            ? i
            : throw new InvalidDataException($"Okul CSV'sinde '{column}' sütunu yok. Bulunan: {string.Join(", ", index.Keys)}");

    /// <summary>Bir kaydı (tırnak içinde satır sonu olabilir) alanlarına ayırır; dosya bittiyse null.</summary>
    private static List<string>? ReadRecord(TextReader reader)
    {
        if (reader.Peek() < 0)
            return null;

        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        while (true)
        {
            var c = reader.Read();
            if (c < 0)
            {
                fields.Add(field.ToString());
                return fields;
            }

            var ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    if (reader.Peek() == '\n')
                        reader.Read();
                    fields.Add(field.ToString());
                    return fields;
                case '\n':
                    fields.Add(field.ToString());
                    return fields;
                default:
                    field.Append(ch);
                    break;
            }
        }
    }
}
