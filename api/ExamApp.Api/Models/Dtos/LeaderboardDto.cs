using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos;

/// <summary>issue #193: liderlik tablosu kapsamı. Query string'de <c>scope=global|school</c>.</summary>
public enum LeaderboardScope
{
    /// <summary>Tüm platform (varsayılan; parametre verilmediğinde de bu).</summary>
    Global = 0,

    /// <summary>Yalnızca istek sahibinin okulundaki öğrenciler. Okul sunucu tarafında çözülür.</summary>
    School = 1
}

/// <summary>
/// Servise giden liderlik isteği. <see cref="CurrentSchoolId"/> her zaman sunucu tarafında
/// (<c>BaseController.GetSchoolScopeAsync</c>, issue #189) çözülmüş değerdir — client'tan gelen
/// schoolId parametresiyle ASLA doldurulmaz. <see cref="Skip"/>/<see cref="Take"/> controller'da doğrulanır.
/// </summary>
public sealed record LeaderboardRequest(
    LeaderboardScope Scope,
    int RequesterUserId,
    int? CurrentSchoolId,
    int Skip,
    int Take);

/// <summary>
/// PII minimizasyonu: global listede başka okulların öğrencileri de görünür; bu yüzden UserId, StudentId,
/// StudentNumber ve SchoolId dışarı verilmez. Yalnızca görüntü alanları.
/// </summary>
public class LeaderboardEntryDto
{
    /// <summary>Kapsam içindeki sıra (1 tabanlı). XP azalan, eşitlikte StudentId artan.</summary>
    public int Rank { get; set; }
    public int Xp { get; set; }
    public int Level { get; set; }

    /// <summary>auth-api'den çözülür; erişilemezse boş kalır (lookup ucuyla aynı davranış).</summary>
    public string FullName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;

    /// <summary>Satır istek sahibinin kendi öğrenci kaydı mı.</summary>
    public bool IsMe { get; set; }
}

/// <summary>
/// GET /api/leaderboard yanıtı. Başarısız (Success=false) durumda controller 400 döner;
/// başarılı durumda <see cref="ResponseBaseDto.Success"/> = true.
/// </summary>
public class LeaderboardDto : ResponseBaseDto
{
    /// <summary>"global" | "school" — uygulanan kapsam.</summary>
    public string Scope { get; set; } = "global";

    /// <summary>School kapsamında sunucu tarafında çözülen okul; global'de null.</summary>
    public int? SchoolId { get; set; }

    /// <summary>
    /// İstek sahibinin (DB'den doğrulanmış) bir okulu var mı — UI "Okulum" sekmesini buna göre
    /// gösterir/gizler. Global çağrıda da dolu döner ki UI ayrı istek atmasın.
    /// </summary>
    public bool SchoolScopeAvailable { get; set; }

    /// <summary>Kapsamdaki toplam öğrenci sayısı (sayfalamadan bağımsız).</summary>
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }

    public List<LeaderboardEntryDto> Entries { get; set; } = new();

    /// <summary>İstek sahibinin kapsam içindeki sırası; öğrenci değilse veya kapsam dışıysa null.</summary>
    public int? MyRank { get; set; }
    public int? MyXp { get; set; }
}
