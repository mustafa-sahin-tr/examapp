using System;
using System.Collections.Generic;
using System.Linq;
using ExamApp.Api.Data;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Issue #577: bir kitabın sayfa eşlemesi (saf mantık, DB'siz). Bastırılmış sayfa = dosya sayfası + ofset; fasiküllü
/// kitapta her fasikül ayrı aralık/ofset taşır ve aynı bastırılmış numara birden çok fasikülde görülebilir, bu yüzden
/// bastırılmış → dosya dönüşümü liste döner. Aralıklar küme içinde çakışmaz (<see cref="Validate"/>).
/// </summary>
public sealed class BookPageMap
{
    /// <summary>Dosya sayfası üst sınırı (hatalı girdiye karşı makul tavan).</summary>
    public const int MaxFilePage = 100_000;

    private readonly IReadOnlyList<BookPageOffset> _offsets;
    private readonly IReadOnlyList<BookAnswerKeyRange> _answerKeys;
    private readonly IReadOnlyList<BookTocEntry> _toc;

    public BookPageMap(
        IEnumerable<BookPageOffset> offsets,
        IEnumerable<BookAnswerKeyRange>? answerKeys = null,
        IEnumerable<BookTocEntry>? toc = null)
    {
        _offsets = offsets.OrderBy(o => o.FileFrom).ToList();
        _answerKeys = (answerKeys ?? []).OrderBy(r => r.FileFrom).ToList();
        _toc = (toc ?? []).OrderBy(t => t.FileFrom).ToList();
    }

    /// <summary>Dosya sayfasının bastırılmış numarası; hiçbir ofset aralığında değilse null.</summary>
    public int? ToPrinted(int filePage) =>
        _offsets.FirstOrDefault(o => filePage >= o.FileFrom && filePage <= o.FileTo) is { } o ? filePage + o.Offset : null;

    /// <summary>
    /// Bastırılmış sayfaya karşılık gelen dosya sayfaları. <paramref name="near"/> verilirse ona uzaklığa göre (eşitlikte
    /// küçük önce), yoksa artan sırada.
    /// </summary>
    public IReadOnlyList<int> ToFilePages(int printedPage, int? near = null)
    {
        var pages = _offsets
            .Select(o => (Range: o, File: (long)printedPage - o.Offset))
            .Where(x => x.File >= x.Range.FileFrom && x.File <= x.Range.FileTo)
            .Select(x => (int)x.File)
            .Distinct();
        return (near is { } n
                ? pages.OrderBy(p => Math.Abs((long)p - n)).ThenBy(p => p)
                : pages.OrderBy(p => p))
            .ToList();
    }

    public bool IsAnswerKey(int filePage) => _answerKeys.Any(r => filePage >= r.FileFrom && filePage <= r.FileTo);

    public BookTocEntry? TocFor(int filePage) => _toc.FirstOrDefault(t => filePage >= t.FileFrom && filePage <= t.FileTo);

    /// <summary>
    /// Aralık kurallarını denetler; ilk ihlalin yerelleştirme anahtarını döner (geçerliyse null). Her aralık
    /// 1 ≤ FileFrom ≤ FileTo ≤ <see cref="MaxFilePage"/>; küme içinde çakışma yok; ofset aralığının ilk bastırılmış
    /// sayfası ≥ 1 ve son bastırılmış sayfası ≤ <see cref="MaxFilePage"/>.
    /// </summary>
    public static string? Validate(
        IReadOnlyList<(int From, int To, int Offset)> offsets,
        IReadOnlyList<(int From, int To)> answerKeys,
        IReadOnlyList<(int From, int To)> toc)
    {
        if (offsets.Any(o => !IsValidRange(o.From, o.To)) || answerKeys.Any(r => !IsValidRange(r.From, r.To)) ||
            toc.Any(t => !IsValidRange(t.From, t.To)))
            return "labeling.books.invalidRange";
        if (offsets.Any(o => (long)o.From + o.Offset < 1 || (long)o.To + o.Offset > MaxFilePage))
            return "labeling.books.invalidOffset";
        if (Overlaps(offsets.Select(o => (o.From, o.To))))
            return "labeling.books.offsetOverlap";
        if (Overlaps(answerKeys))
            return "labeling.books.answerKeyOverlap";
        if (Overlaps(toc))
            return "labeling.books.tocOverlap";
        return null;
    }

    private static bool IsValidRange(int from, int to) => from >= 1 && from <= to && to <= MaxFilePage;

    private static bool Overlaps(IEnumerable<(int From, int To)> ranges)
    {
        var sorted = ranges.OrderBy(r => r.From).ToList();
        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].From <= sorted[i - 1].To)
                return true;
        }
        return false;
    }
}
