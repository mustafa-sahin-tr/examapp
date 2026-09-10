using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.TeacherApprovals;

public class TeacherApprovalService : ITeacherApprovalService
{
    private const int RejectionReasonMaxLength = 500;

    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApiClient;

    public TeacherApprovalService(AppDbContext context, IAuthApiClient authApiClient)
    {
        _context = context;
        _authApiClient = authApiClient;
    }

    public async Task<List<PendingTeacherApplicationDto>> GetPendingApplicationsAsync(CancellationToken ct = default)
    {
        var pending = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.IsIndependentTutor && t.ApprovalStatus == TeacherApprovalStatus.Pending)
            .OrderBy(t => t.CreateTime)
            .Select(t => new PendingTeacherApplicationDto
            {
                TeacherId = t.Id,
                UserId = t.UserId,
                AppliedAt = t.CreateTime
            })
            .ToListAsync(ct);

        if (pending.Count == 0)
            return pending;

        var users = await ResolveUsersAsync(pending.Select(p => p.UserId).Distinct().ToList(), ct);
        foreach (var item in pending)
        {
            if (users.TryGetValue(item.UserId, out var user))
            {
                item.FullName = user.FullName;
                item.Email = user.Email;
            }
        }

        return pending;
    }

    public async Task<ResponseBaseDto> ApproveAsync(int teacherId, int adminUserId, CancellationToken ct = default)
    {
        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.Id == teacherId, ct);
        var guardError = GuardPendingIndependent(teacher);
        if (guardError != null)
            return guardError;

        _context.SetCurrentUser(adminUserId);
        teacher!.ApprovalStatus = TeacherApprovalStatus.Approved;
        teacher.RejectionReason = null;
        await _context.SaveChangesAsync(ct);
        return Ok("Başvuru onaylandı.", teacher.Id);
    }

    public async Task<ResponseBaseDto> RejectAsync(int teacherId, string reason, int adminUserId, CancellationToken ct = default)
    {
        var trimmedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedReason))
            return Fail("Red nedeni boş olamaz.");

        if (trimmedReason.Length > RejectionReasonMaxLength)
            return Fail($"Red nedeni en fazla {RejectionReasonMaxLength} karakter olabilir.");

        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.Id == teacherId, ct);
        var guardError = GuardPendingIndependent(teacher);
        if (guardError != null)
            return guardError;

        _context.SetCurrentUser(adminUserId);
        teacher!.ApprovalStatus = TeacherApprovalStatus.Rejected;
        teacher.RejectionReason = trimmedReason;
        await _context.SaveChangesAsync(ct);
        return Ok("Başvuru reddedildi.", teacher.Id);
    }

    /// <summary>
    /// Yalnızca bağımsız + Pending kayıtlar karar alabilir. Okula bağlı öğretmen hiçbir zaman uygun değildir;
    /// zaten karar verilmiş başvuru tekrar onaylanamaz/reddedilemez (idempotency).
    /// </summary>
    private static ResponseBaseDto? GuardPendingIndependent(Teacher? teacher)
    {
        if (teacher == null)
            return new ResponseBaseDto { Success = false, NotFound = true, Message = "Başvuru bulunamadı." };

        if (!teacher.IsIndependentTutor)
            return Fail("Bu öğretmen bağımsız öğretmen başvurusu değil.");

        if (teacher.ApprovalStatus != TeacherApprovalStatus.Pending)
            return new ResponseBaseDto { Success = false, Conflict = true, Message = "Bu başvuru için zaten karar verilmiş." };

        return null;
    }

    /// <summary>
    /// UserId'leri tek batch çağrıyla ad/e-postaya çevirir (TeacherService.ResolveStudentNamesAsync ile aynı desen).
    /// Auth-api erişilemezse boş sözlük döner — liste yine de dönmeli.
    /// </summary>
    private async Task<Dictionary<int, UserLookupResultDto>> ResolveUsersAsync(List<int> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return new Dictionary<int, UserLookupResultDto>();

        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(userIds, ct);
            return users
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new Dictionary<int, UserLookupResultDto>();
        }
    }

    private static ResponseBaseDto Fail(string message) => new() { Success = false, Message = message };

    private static ResponseBaseDto Ok(string message, int id) =>
        new() { Success = true, Message = message, ObjectId = id };
}
