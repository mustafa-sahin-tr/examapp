using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.BookImport;

/// <summary>
/// #1235: kitapta basılı cevap işaretleri (renkli kutudaki doğru şık; temizlikte silinir ve <see cref="BookImportPage.MarksJson"/>'a
/// yazılır, #1234) cevap anahtarı olarak kullanılır. Kurulumda testin sayfalarındaki işaretler okuma sırasıyla (sütun sütun, yukarıdan
/// aşağı) anahtar olur: anahtarsız teste <see cref="BookKeySources.Marks"/> kaynağıyla yazılır, kitaptan okunmuş anahtarla farkı
/// <see cref="BookImportJobTest.KeyConflicts"/>'e "diğer okuma" olarak düşer. Soru aşamasında soru kutusunun içindeki işaret o
/// sorunun cevabıdır (<see cref="AnswerIn"/>).
/// </summary>
public static class BookMarkKeys
{
    public static IReadOnlyList<PageMark> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<PageMark>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Sayfadaki işaretlerin okuma sırası: sayfa iki sütunluysa önce sol sütun yukarıdan aşağı, sonra sağ. Sütun işaretin
    /// merkezinin sayfa ortasının hangi yanında olduğuna göre.
    /// </summary>
    public static IEnumerable<PageMark> ReadingOrder(IEnumerable<PageMark> marks, int pageWidth) =>
        marks.OrderBy(m => m.X + m.W / 2 < pageWidth / 2 ? 0 : 1).ThenBy(m => m.Y).ThenBy(m => m.X);

    /// <summary>Testin sayfalarındaki işaretlerden anahtar (harf dizisi); işaret yoksa null.</summary>
    public static string? KeyOf(IEnumerable<BookImportPage> pages, int from, int to)
    {
        var key = new StringBuilder();
        foreach (var page in pages.Where(p => p.FilePage >= from && p.FilePage <= to).OrderBy(p => p.FilePage))
            foreach (var mark in ReadingOrder(Parse(page.MarksJson), page.Width))
                key.Append(mark.Letter);
        return key.Length == 0 ? null : key.ToString();
    }

    /// <summary>Kutunun içinde merkezi kalan işaretin şık sırası (A = 0); yoksa ya da birden çok farklı harf varsa -1.</summary>
    public static int AnswerIn(IReadOnlyList<PageMark> marks, LayoutBox box)
    {
        var letters = marks
            .Where(m => m.X + m.W / 2 >= box.X && m.X + m.W / 2 <= box.X + box.Width
                        && m.Y + m.H / 2 >= box.Y && m.Y + m.H / 2 <= box.Y + box.Height)
            .Select(m => m.Letter)
            .Distinct()
            .ToList();
        return letters is [{ Length: 1 } letter] && letter[0] is >= 'A' and <= 'J' ? letter[0] - 'A' : -1;
    }

    /// <summary>
    /// Kurulum onayı bekleyen işin testlerine işaretlerden anahtar yazar: anahtarsız ya da anahtarı önceki işaretlerden gelen teste
    /// <see cref="BookKeySources.Marks"/>; kitaptan / QR'dan / AI'dan gelen aynı uzunluktaki anahtarla farklı harfler
    /// <see cref="BookImportJobTest.KeyConflicts"/>'e (boş hücreye). Elle girilen anahtara dokunulmaz. Değişen test sayısı döner.
    /// </summary>
    public static async Task<int> ApplyAsync(AppDbContext db, Guid jobId, CancellationToken ct)
    {
        var pages = await db.BookImportPages.AsNoTracking().Where(p => p.JobId == jobId && p.MarksJson != null).ToListAsync(ct);
        var tests = await db.BookImportJobTests.AsTracking().Where(t => t.JobId == jobId).ToListAsync(ct);
        var changed = 0;
        foreach (var test in tests)
        {
            if (test.FileFrom is not { } from || test.FileTo is not { } to || test.KeySource == BookKeySources.Manual)
                continue;
            var marks = KeyOf(pages, from, to);
            if (test.AnswerKey is null || test.KeySource == BookKeySources.Marks)
            {
                if (marks is null || marks == test.AnswerKey)
                    continue;
                test.AnswerKey = marks;
                test.KeySource = BookKeySources.Marks;
                test.KeyConflicts = new string('.', marks.Length);
                test.KeyAnswers = marks.Length;
                test.KeyFilePage = null;
                changed++;
            }
            else if (marks is not null && marks.Length == test.AnswerKey.Length)
            {
                var conflicts = (test.KeyConflicts is { } c && c.Length == marks.Length ? c : new string('.', marks.Length)).ToCharArray();
                var any = false;
                for (var i = 0; i < marks.Length; i++)
                {
                    if (conflicts[i] != '.' || test.AnswerKey[i] == marks[i] || test.AnswerKey[i] == '?')
                        continue;
                    conflicts[i] = marks[i];
                    any = true;
                }
                if (any)
                {
                    test.KeyConflicts = new string(conflicts);
                    changed++;
                }
            }
        }

        if (changed > 0)
            await db.SaveChangesAsync(ct);
        return changed;
    }
}
