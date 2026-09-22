namespace ExamApp.Api.Services.Seed.Cleanup;

/// <summary><c>seed-cleanup</c> parametreleri (issue #218). Varsayılan dry-run; silme yalnızca <c>--apply</c> ile.</summary>
public sealed record SeedCleanupOptions
{
    /// <summary>false (varsayılan): hiçbir sistemde yazma yapılmaz, rapor üretilir. true: hard delete.</summary>
    public bool Apply { get; init; }

    /// <summary>
    /// Müsaitlik verisi (TeacherAvailabilitySlot, RecurringAvailabilityRule) ya da yazdığı worksheet/soru olan seed
    /// öğretmenler varsayılan olarak ATLANIR. true ise müsaitlik satırları öğretmenle birlikte silinir (kademeli);
    /// worksheet/soru hiçbir zaman silinmez (sahipsiz kalır, raporlanır). Gerçek öğrenci randevusu (Booking) olan
    /// öğretmen --force ile de silinmez — seed-dışı satıra dokunulmaz.
    /// </summary>
    public bool Force { get; init; }

    /// <summary>Staging'de --apply için zorunlu onay (yanlış hedefe karşı ikinci sigorta).</summary>
    public bool Yes { get; init; }

    /// <summary>auth-api'yi (Keycloak + identity) hiç çağırma — yalnızca exam DB. Rapor tutor il kırılımını e-postadan çıkaramaz.</summary>
    public bool SkipAuthApi { get; init; }

    /// <summary>
    /// Keycloak'ta seed desenli (<c>seed.*@seed.examapp.local</c>) ama identity'de HİÇ satırı olmayan yetim hesapları da sil
    /// (kesilmiş seed koşusu kalıntısı). Gerçek alan adlarına ve identity'de <c>IsSeedData=false</c> satırı olan hesaplara
    /// yine dokunulmaz. Varsayılan false: yetimler raporda "yabancı" olarak sayılır.
    /// </summary>
    public bool IncludeOrphans { get; init; }
}
