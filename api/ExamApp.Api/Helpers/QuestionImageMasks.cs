using System;
using System.Collections.Generic;
using ExamApp.Api.Models.Dtos;
using SkiaSharp;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Issue #581: soru maskeleri (basılı cevap) opak beyazla boyanır. Maskeler soru bölgesine göredir; sayfa koordinatına
/// çevrilip (bölge X/Y + maske X/Y, bölgeye kırpılarak) <b>sayfaya</b> herhangi bir kırpmadan önce boyanır — böylece
/// sorunun kendi görseli, <c>question-v2</c>, şık kırpmaları, üst üste binen başka soru ve kapsam (passage / etkinlik)
/// görselleri de maskeli içeriği taşımaz. Maskeler ayrıca saklanmaz (görsel esastır): kayıtlı bir soru hiçbir akışta asıl
/// sayfadan yeniden kırpılmaz.
/// </summary>
public static class QuestionImageMasks
{
    /// <summary>Soru başına en çok maske.</summary>
    public const int MaxMasks = 20;

    /// <summary>
    /// null/boş liste geçerli. Her maske: sonlu, x/y ≥ 0, genişlik/yükseklik ≥ 1; en çok <see cref="MaxMasks"/>. Bölge
    /// boyutu verilirse maske bölgeyle kesişmeli (taşan kısım boyamada kırpılır; hiç kesişmeyen reddedilir).
    /// </summary>
    public static bool IsValid(IReadOnlyList<RegionBoxDto?>? masks, int? regionWidth = null, int? regionHeight = null)
    {
        if (masks is null)
            return true;
        if (masks.Count > MaxMasks)
            return false;
        foreach (var m in masks)
        {
            if (m is null
                || !double.IsFinite(m.X) || !double.IsFinite(m.Y) || !double.IsFinite(m.Width) || !double.IsFinite(m.Height)
                || m.X < 0 || m.Y < 0 || m.Width < 1 || m.Height < 1)
                return false;
            // x, y ≥ 0 ve kenarlar ≥ 1 iken kesişim boş değil ⇔ x < genişlik ve y < yükseklik.
            if ((regionWidth is { } w && m.X >= w) || (regionHeight is { } h && m.Y >= h))
                return false;
        }
        return true;
    }

    /// <summary>Maskeleri bitmap'in kendisine (bölge = tüm bitmap) boyar. Boyanan maske sayısını döner.</summary>
    public static int Apply(SKBitmap bitmap, IReadOnlyList<RegionBoxDto?>? masks)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        return ApplyToPage(bitmap, 0, 0, bitmap.Width, bitmap.Height, masks);
    }

    /// <summary>
    /// Bölgeye göreli maskeleri sayfaya boyar: kesirli kenarlar dışa yuvarlanır, maske önce bölgeye sonra sayfaya kırpılır,
    /// kesişimi boş olan atlanır. Sayfa herhangi bir kırpmadan önce boyanmalı (<see cref="SkiaImaging.CropResize"/> ve
    /// <see cref="ImageHelper.EncodeImage"/> bitmap'i değişmez yapar). Boyanan maske sayısını döner.
    /// </summary>
    public static int ApplyToPage(
        SKBitmap page, int regionX, int regionY, int regionWidth, int regionHeight, IReadOnlyList<RegionBoxDto?>? masks)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (masks is null || masks.Count == 0)
            return 0;

        var painted = 0;
        foreach (var m in masks)
        {
            if (m is null)
                continue;
            var left = regionX + Math.Max(0, Math.Floor(m.X));
            var top = regionY + Math.Max(0, Math.Floor(m.Y));
            var right = regionX + Math.Min(regionWidth, Math.Ceiling(m.X + m.Width));
            var bottom = regionY + Math.Min(regionHeight, Math.Ceiling(m.Y + m.Height));
            left = Math.Max(0, left);
            top = Math.Max(0, top);
            right = Math.Min(page.Width, right);
            bottom = Math.Min(page.Height, bottom);
            if (right <= left || bottom <= top)
                continue;

            page.Erase(SKColors.White, SKRectI.Create((int)left, (int)top, (int)(right - left), (int)(bottom - top)));
            painted++;
        }
        if (painted > 0)
            page.NotifyPixelsChanged();
        return painted;
    }
}
