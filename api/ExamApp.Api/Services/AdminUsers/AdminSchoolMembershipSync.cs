using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// Admin okul değişikliklerinin (öğrenci #277 madde 8, öğretmen #313) DB yazımından SONRAKİ ortak yan etkileri:
/// <list type="number">
/// <item>Profil önbelleği düşürülür — okul kapsamı (<c>SchoolScope</c>) profil önbelleğinden gelir (#194); düşürülmezse
/// kullanıcı önbellek süresi (1 saat) boyunca ESKİ okulun kapsamında kalırdı. Geçici Redis hatası için birkaç kez
/// denenir (<see cref="CacheRetryDelays"/>); yine başarısızsa sonuç <c>false</c> döner (çağıran audit/yanıtta belirtir).</item>
/// <item>İkinci, gecikmeli düşürme (issue #313 review O2) Hangfire ile planlanır (<see cref="SecondInvalidationDelay"/>):
/// <c>UserProfileCacheService.GetOrSetAsync</c> önbellek kaçırmada önce DB'yi okur, sonra yazar. Bizim UPDATE'imizden
/// ÖNCE DB'yi okumuş eşzamanlı bir istek, ilk düşürmemizden SONRA eski SchoolId'yi önbelleğe geri yazabilir. Gecikmeli
/// ikinci düşürme bu pencereyi kapatır; yanıtı bloklamaz ve süreç yeniden başlasa da (Hangfire kalıcı) çalışır. Sürüm
/// damgası alternatifi tüm profil okuma yolunu (GetOrSet + her SetAsync çağıranı) değiştirmeyi gerektirirdi.</item>
/// <item>Keycloak <c>school_id</c> attribute'u güncellenir — JWT claim'i yalnızca ipucudur (#189), yetki DB'den çözülür;
/// yine de uyuşmazlık uyarısı üretmesin.</item>
/// </list>
/// Hepsi best-effort: Redis/Hangfire/Keycloak hatası DB değişikliğini geri almaz, yalnızca loglanır. Loglar yalnızca hedef
/// türü/id'si içerir (PII yok).
/// </summary>
internal sealed class AdminSchoolMembershipSync
{
    /// <summary>İlk denemeden sonraki bekleme süreleri (toplam deneme = 1 + eleman sayısı).</summary>
    internal static readonly IReadOnlyList<TimeSpan> CacheRetryDelays = [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(300)];

    /// <summary>GetOrSet yarışını kapatan ikinci düşürmenin gecikmesi (bir profil yükleme turundan rahatça uzun).</summary>
    internal static readonly TimeSpan SecondInvalidationDelay = TimeSpan.FromSeconds(5);

    private readonly IKeycloakService? _keycloak;
    private readonly UserProfileCacheService? _profileCache;
    private readonly IBackgroundJobClient? _jobs;
    private readonly ILogger _logger;

    public AdminSchoolMembershipSync(IKeycloakService? keycloak, UserProfileCacheService? profileCache,
        IBackgroundJobClient? jobs, ILogger logger)
    {
        _keycloak = keycloak;
        _profileCache = profileCache;
        _jobs = jobs;
        _logger = logger;
    }

    /// <returns><c>true</c> → profil önbelleği düşürüldü (ya da önbellek yapılandırılmamış); <c>false</c> → tüm denemeler
    /// başarısız, kullanıcı en geç önbellek süresi (1 saat) boyunca eski okulla görünebilir.</returns>
    public async Task<bool> ApplyAsync(string sub, int schoolId, AdminUserTargetType targetType, int targetId)
    {
        var invalidated = await TryInvalidateProfileAsync(sub, targetType, targetId);
        TryScheduleSecondInvalidation(sub, targetType, targetId);
        await TrySyncSchoolClaimAsync(sub, schoolId, targetType, targetId);
        return invalidated;
    }

    private async Task<bool> TryInvalidateProfileAsync(string sub, AdminUserTargetType targetType, int targetId)
    {
        if (_profileCache is null)
            return true;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await _profileCache.RemoveAsync(sub);
                return true;
            }
            catch (Exception ex) when (attempt < CacheRetryDelays.Count)
            {
                _logger.LogWarning(ex, "[AdminSchoolSync] Profil önbelleği düşürülemedi, yeniden denenecek ({Attempt}): {TargetType}#{TargetId}",
                    attempt + 1, targetType, targetId);
                await Task.Delay(CacheRetryDelays[attempt], CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AdminSchoolSync] Profil önbelleği düşürülemedi (eski okul en geç 1 saat sürebilir): {TargetType}#{TargetId}",
                    targetType, targetId);
                return false;
            }
        }
    }

    private void TryScheduleSecondInvalidation(string sub, AdminUserTargetType targetType, int targetId)
    {
        if (_jobs is null || _profileCache is null)
            return;
        try
        {
            _jobs.Schedule<UserProfileCacheService>(c => c.RemoveAsync(sub), SecondInvalidationDelay);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminSchoolSync] Gecikmeli profil önbelleği düşürmesi planlanamadı: {TargetType}#{TargetId}",
                targetType, targetId);
        }
    }

    private async Task TrySyncSchoolClaimAsync(string sub, int schoolId, AdminUserTargetType targetType, int targetId)
    {
        if (_keycloak is null)
            return;
        try
        {
            await _keycloak.SetSchoolIdAttributeAsync(sub, schoolId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminSchoolSync] Keycloak school_id attribute güncellenemedi: {TargetType}#{TargetId}",
                targetType, targetId);
        }
    }
}
