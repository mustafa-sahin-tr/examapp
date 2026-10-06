using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Data;

/// <summary>
/// issue #376: <see cref="TeacherAvailabilitySlot"/>'un soft-delete query filter'ı adlıdır (<see cref="SoftDeleteKey"/>);
/// diğer tüm <c>BaseEntity</c> filtreleri adsız kalır.
/// <para>
/// Neden: <see cref="Booking.AvailabilitySlot"/> zorunlu navigasyon; EF filtreli zorunlu navigasyonu INNER JOIN + filtre ile
/// çevirir. Slotu (eski veri, yarış vb. yüzünden) soft-delete edilmiş bir randevu bu yüzden randevu sorgularından tamamen
/// düşüyordu ("Randevu bulunamadı.", listelerde görünmeme, çizim tahtası reddi). Karar: onaylı randevu slotu silinse de
/// geçerlidir; randevuyu okuyan sorgular slotun saatini silinmiş olsa bile okur.
/// </para>
/// <para>
/// <see cref="WithSoftDeletedSlots"/> YALNIZ slot filtresini kapatır (EF Core 10 adlı filtreler) — <c>IgnoreQueryFilters()</c>
/// gibi tüm sorguyu açmaz: silinmiş booking/öğretmen/öğrenci satırları yine gizli kalır. Not: kapatma sorgu
/// genelidir; aynı sorguda slotlar üzerinden başka bir şey okunuyorsa (ör. <c>Teacher.Slots</c>) onlar da silinmişleri içerir.
/// Slot listeleri (öğretmenin takvimi, öğrencinin açık slotları) bu uzantıyı KULLANMAZ.
/// </para>
/// </summary>
public static class AvailabilitySlotQueryFilter
{
    /// <summary>Slotun soft-delete filtresinin anahtarı.</summary>
    public const string SoftDeleteKey = "AvailabilitySlotSoftDelete";

    private static readonly string[] SoftDeleteKeys = [SoftDeleteKey];

    /// <summary>Randevu sorgusunda slot navigasyonu silinmiş slotu da okur; diğer soft-delete filtreleri geçerli kalır.</summary>
    public static IQueryable<Booking> WithSoftDeletedSlots(this IQueryable<Booking> bookings)
        => bookings.IgnoreQueryFilters(SoftDeleteKeys);
}
