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
}
