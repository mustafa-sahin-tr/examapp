using System;
using System.Collections.Generic;
using System.Linq;

namespace ExamApp.Api.Services.BookImport;

/// <summary>Sayfa pikseli dikdörtgen.</summary>
public readonly record struct LayoutBox(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public double CenterX => X + Width / 2d;
    public double CenterY => Y + Height / 2d;
}

/// <summary>Tespit edilen soru kutusu ve şıkları.</summary>
public sealed record LayoutQuestion(LayoutBox Box, IReadOnlyList<LayoutBox> Options);

/// <summary>Sorudan ayrı çerçevedeki uyaran (<c>info_box</c>); <paramref name="SharedQuestions"/> kaç soruya ortak olduğu.</summary>
public sealed record LayoutStimulus(LayoutBox Box, int SharedQuestions = 1);

/// <summary>Soruların ortak pasajı; <paramref name="QuestionIndexes"/> okuma sırasındaki soru listesine göre.</summary>
public sealed record LayoutPassage(LayoutBox Box, IReadOnlyList<int> QuestionIndexes);

/// <summary>
/// Issue #1022: tespit edilen soru / şık kutularını kayda hazırlar. Kurallar el ile aktarım aracındaki Python portuyla
/// (<c>process_bts.py</c>) aynı; şık hizalama işaretleme ekranının "Cevapları Hizala"sı (<c>alignAnswers</c>):
/// tek satır → aynı y / yükseklik; tek sütun → aynı x / genişlik; ızgara → sütun x / genişlik + satır y / yükseklik.
/// Toleranslar 2363 px genişliğe göre ölçeklenir (sayfa görselleri o genişlikte üretilir). Saf; birim testli.
/// </summary>
public static class BookQuestionLayout
{
    /// <summary>Ölçek tabanı: sayfa görsellerinin genişliği (<c>BookImportJobRunner</c> 2363 px üretir).</summary>
    public const double BaseWidth = 2363;

    /// <summary>
    /// Okuma sırası: önce sütun (sayfanın %60'ından geniş ya da merkezi sol yarıda olan → sol), sonra yukarıdan aşağı.
    /// </summary>
    public static List<LayoutQuestion> Order(IEnumerable<LayoutQuestion> questions, int pageWidth) =>
        questions.OrderBy(q => q.Box.Width > pageWidth * 0.6 || q.Box.CenterX < pageWidth / 2d ? 0 : 1)
            .ThenBy(q => q.Box.Y)
            .ToList();

    /// <summary>
    /// Issue #1062: uyaranları altlarındaki soruya bağlar. Sahip, aynı sütunda (yatay örtüşme, darın %30'u) kutunun tepesinin
    /// altında başlayan ilk sorudur; kutu sahibin tepesinde kesilir. Bir sorunun birden çok kutusu tek pasajda birleşir, ortak
    /// kutu sonraki sorulara da bağlanır. Soruyla örtülen, altında soru olmayan ya da kesince çok ince kalan kutu atlanır.
    /// </summary>
    public static List<LayoutPassage> AttachPassages(IReadOnlyList<LayoutQuestion> ordered, IEnumerable<LayoutStimulus> stimuli, int pageWidth)
    {
        var scale = pageWidth / BaseWidth;
        var topTolerance = 10 * scale;
        var minHeight = 30 * scale;
        var byOwner = new SortedDictionary<int, (LayoutBox Box, int Shared)>();
        foreach (var stimulus in stimuli)
        {
            var box = stimulus.Box;
            var column = ordered.Select((q, index) => (q.Box, index))
                .Where(q => Math.Min(q.Box.Right, box.Right) - Math.Max(q.Box.X, box.X) > 0.3 * Math.Min(q.Box.Width, box.Width))
                .OrderBy(q => q.Box.Y)
                .ToList();
            if (column.Any(q => q.Box.Y <= box.Y + topTolerance && q.Box.Bottom >= box.Y + 0.6 * box.Height))
                continue;
            var owner = column.FirstOrDefault(q => q.Box.Y > box.Y + topTolerance);
            if (owner == default)
                continue;
            var bottom = Math.Min(box.Bottom, owner.Box.Y);
            if (bottom - box.Y < minHeight)
                continue;
            var clipped = box with { Height = bottom - box.Y };
            var shared = Math.Max(stimulus.SharedQuestions, 1);
            byOwner[owner.index] = byOwner.TryGetValue(owner.index, out var current)
                ? (Union(current.Box, clipped), Math.Max(current.Shared, shared))
                : (clipped, shared);
        }
        // Ortak kutu kendi pasajı olan soruya ya da listenin dışına taşmaz.
        return byOwner.Select(entry =>
        {
            var indexes = new List<int> { entry.Key };
            for (var next = entry.Key + 1; next < ordered.Count && indexes.Count < entry.Value.Shared && !byOwner.ContainsKey(next); next++)
                indexes.Add(next);
            return new LayoutPassage(entry.Value.Box, indexes);
        }).ToList();
    }

    private static LayoutBox Union(LayoutBox a, LayoutBox b)
    {
        var (x, y) = (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
        return new LayoutBox(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }

    /// <summary>
    /// Tespit bazen şıksız bir kök ve hemen altında şıkları ikinci kutu olarak verir: ikisi birleştirilir (okuma sırasındaki
    /// liste üzerinde; x farkı genişliğin %5'inden az, dikey boşluk yüksekliğin -%1..%2'si).
    /// </summary>
    public static List<LayoutQuestion> MergeSplit(IReadOnlyList<LayoutQuestion> ordered, int pageWidth, int pageHeight)
    {
        var result = new List<LayoutQuestion>();
        foreach (var q in ordered)
        {
            var prev = result.Count > 0 ? result[^1] : null;
            var gap = q.Box.Y - prev?.Box.Bottom;
            if (prev is not null && prev.Options.Count == 0 && q.Options.Count > 0 &&
                Math.Abs(q.Box.X - prev.Box.X) < pageWidth * 0.05 &&
                gap > -pageHeight * 0.01 && gap < pageHeight * 0.02)
            {
                var x0 = Math.Min(prev.Box.X, q.Box.X);
                var x1 = Math.Max(prev.Box.Right, q.Box.Right);
                result[^1] = q with { Box = new LayoutBox(x0, prev.Box.Y, x1 - x0, q.Box.Bottom - prev.Box.Y) };
            }
            else
            {
                result.Add(q);
            }
        }
        return result;
    }

    /// <summary>
    /// Kayda gidecek soru: soru kutusu taşan şıkları (dolgu dahil) içerecek kadar büyür; şıklar okuma sırasına (satır, x)
    /// dizilir, dolgulanır, hizalanır ve komşuya taşan dolgu tespit kutularının ortasından kesilir.
    /// </summary>
    public static LayoutQuestion Prepare(LayoutQuestion question, int pageWidth, int pageHeight)
    {
        var pad = (int)(25 * pageWidth / BaseWidth);
        var q = question.Box;
        var raw = question.Options;
        if (raw.Count > 0)
        {
            var x0 = Math.Max(0, Math.Min(q.X, raw.Min(o => o.X - pad)));
            var y0 = Math.Max(0, Math.Min(q.Y, raw.Min(o => o.Y - pad)));
            var x1 = Math.Min(pageWidth, Math.Max(q.Right, raw.Max(o => o.Right + pad)));
            var y1 = Math.Min(pageHeight, Math.Max(q.Bottom, raw.Max(o => o.Bottom + pad)));
            q = new LayoutBox(x0, y0, x1 - x0, y1 - y0);
        }

        var row = 60 * pageWidth / BaseWidth;
        var ordered = raw.OrderBy(o => (int)(o.Y / row)).ThenBy(o => o.X).ToList();
        var options = TrimOverlaps(ordered, AlignChoices(ordered.Select(o => Padded(o, q, pad)).ToList(), pageWidth));
        return new LayoutQuestion(q, options);
    }

    /// <summary>Tespit edilen şık son harfi kesebilir: biraz büyütülür, soru kutusunun içinde kalarak.</summary>
    public static LayoutBox Padded(LayoutBox option, LayoutBox question, int pad)
    {
        var x0 = Math.Max(question.X, option.X - pad);
        var y0 = Math.Max(question.Y, option.Y - pad);
        var x1 = Math.Min(question.Right, option.Right + pad);
        var y1 = Math.Min(question.Bottom, option.Bottom + pad);
        return new LayoutBox(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>
    /// "Cevapları Hizala" kuralları; satırlar merkez y'ye göre (tolerans 60 px), ızgarada sütunlar ilk satırın x'ine en
    /// yakın şıkla (tolerans 40 px) eşlenir. Girdi sırası korunur.
    /// </summary>
    public static List<LayoutBox> AlignChoices(IReadOnlyList<LayoutBox> boxes, int pageWidth)
    {
        if (boxes.Count == 0)
            return [];
        var scale = pageWidth / BaseWidth;
        double rowTolerance = 60 * scale, columnTolerance = 40 * scale;
        var output = boxes.ToArray();

        var rows = new List<List<int>>();
        foreach (var i in Enumerable.Range(0, output.Length).OrderBy(i => output[i].Y))
        {
            var row = rows.FirstOrDefault(r => Math.Abs(r.Average(j => output[j].CenterY) - output[i].CenterY) <= rowTolerance);
            if (row is null)
                rows.Add([i]);
            else
                row.Add(i);
        }
        foreach (var row in rows)
            row.Sort((a, b) => output[a].X.CompareTo(output[b].X));

        void SameY(IReadOnlyList<int> group)
        {
            var y0 = group.Min(i => output[i].Y);
            var y1 = group.Max(i => output[i].Bottom);
            foreach (var i in group)
                output[i] = output[i] with { Y = y0, Height = y1 - y0 };
        }

        void SameX(IReadOnlyList<int> group)
        {
            var x0 = group.Min(i => output[i].X);
            var x1 = group.Max(i => output[i].Right);
            foreach (var i in group)
                output[i] = output[i] with { X = x0, Width = x1 - x0 };
        }

        if (rows.Count == 1)
        {
            SameY(rows[0]);
        }
        else if (rows.All(r => r.Count == 1))
        {
            SameX(rows.Select(r => r[0]).ToList());
        }
        else
        {
            if (rows.All(r => r.Count == rows[0].Count))
            {
                for (var c = 0; c < rows[0].Count; c++)
                {
                    var baseX = output[rows[0][c]].X;
                    var column = rows.Select(r => r.MinBy(i => Math.Abs(output[i].X - baseX))).ToList();
                    if (column.All(i => Math.Abs(output[i].X - baseX) <= columnTolerance))
                        SameX(column);
                }
            }
            foreach (var row in rows)
                SameY(row);
        }
        return output.ToList();
    }

    /// <summary>Dolgu komşu şıkka uzanmasın: her örtüşme tespit kutularının (<paramref name="raw"/>) ortasından kesilir.</summary>
    public static List<LayoutBox> TrimOverlaps(IReadOnlyList<LayoutBox> raw, IReadOnlyList<LayoutBox> boxes)
    {
        var output = boxes.ToArray();
        for (var i = 0; i < raw.Count; i++)
        {
            for (var j = 0; j < raw.Count; j++)
            {
                if (i == j)
                    continue;
                var a = raw[i];
                var b = raw[j];
                var xOverlap = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
                var yOverlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
                if (xOverlap > 0 && a.Bottom <= b.Y + 5)
                {
                    // a, b'nin üstünde.
                    var mid = FloorHalf(a.Bottom + b.Y);
                    var bottom = output[j].Bottom;
                    output[i] = output[i] with { Height = Math.Min(output[i].Height, mid - output[i].Y) };
                    var top = Math.Max(output[j].Y, mid);
                    output[j] = output[j] with { Y = top, Height = bottom - top };
                }
                else if (yOverlap > 0 && a.Right <= b.X + 5)
                {
                    // a, b'nin solunda.
                    var mid = FloorHalf(a.Right + b.X);
                    var right = output[j].Right;
                    output[i] = output[i] with { Width = Math.Min(output[i].Width, mid - output[i].X) };
                    var left = Math.Max(output[j].X, mid);
                    output[j] = output[j] with { X = left, Width = right - left };
                }
            }
        }
        return output.ToList();
    }

    private static int FloorHalf(int value) => (int)Math.Floor(value / 2d);
}
