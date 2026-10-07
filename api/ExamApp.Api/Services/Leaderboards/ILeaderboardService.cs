using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Leaderboards;

/// <summary>
/// issue #193: okul bazlı / global liderlik tablosu. Puan kaynağı <c>StudentPoints.XP</c>
/// (profil ekranındaki XP ile aynı formül); sıralama XP azalan, eşitlikte StudentId artan.
/// </summary>
public interface ILeaderboardService
{
    /// <summary>
    /// <see cref="LeaderboardRequest.Scope"/> = School ve <see cref="LeaderboardRequest.CurrentSchoolId"/> null ise
    /// Success=false + <c>student.leaderboard.schoolScopeUnavailable</c> mesajı döner (controller → 400).
    /// </summary>
    Task<LeaderboardDto> GetLeaderboardAsync(LeaderboardRequest request, CancellationToken ct = default);

    /// <summary>
    /// issue #422: XP'si çağıran tarafından zaten okunmuş TEK bir öğrencinin (exam DB <c>Student.Id</c>) kapsamdaki sırası +
    /// kapsamdaki öğrenci sayısı — liderlik tablosuyla AYNI kural (XP azalan, eşitlikte Id artan), ama başka öğrenciye ait hiçbir
    /// satır dönmez (veli paneli). Öğrencinin kapsamda olduğunu çağıran garanti eder (School: doğrulanmış üyesi olduğu okul).
    /// </summary>
    Task<LeaderboardRank> GetRankForXpAsync(int studentId, int xp, LeaderboardScope scope, int? schoolId, CancellationToken ct = default);
}

/// <summary>issue #422: öğrencinin 1 tabanlı sırası ve kapsamdaki öğrenci sayısı.</summary>
public sealed record LeaderboardRank(int Rank, int TotalCount);
