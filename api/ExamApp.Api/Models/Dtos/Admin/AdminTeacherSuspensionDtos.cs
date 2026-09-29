using System;

namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// <c>POST api/admin/teachers/{id}/suspend</c> gövdesi (issue #289): <c>{ "reason": "..." }</c>. Neden zorunlu, trim'lenir,
/// 1-500 karakter; servis doğrular ve controller yerelleştirilmiş <c>{ message, errorCode }</c> ile 400 döner.
/// <c>[Required]</c> bilinçli olarak yok: [ApiController]'ın otomatik ProblemDetails yanıtı yerelleştirilmiş mesajı ve
/// errorCode'u gölgelerdi (<see cref="AdminAccountStatusRequestDto"/> ile aynı gerekçe).
/// </summary>
public sealed class AdminTeacherSuspendRequestDto
{
    public string? Reason { get; init; }
}

/// <summary>
/// issue #289: suspend/unsuspend başarılı yanıtı —
/// <c>{ teacherId, accountApproved, accountSuspended, accountApprovedAt, accountSuspendedAt }</c>.
/// Neden yanıtta tekrar dönülmez (admin listesinde <see cref="AdminTeacherListItemDto.AccountSuspensionReason"/>).
/// </summary>
public sealed class AdminTeacherSuspensionResponseDto
{
    /// <summary>Teacher.Id.</summary>
    public int TeacherId { get; init; }

    /// <summary>Öğretmen özellikleri açık mı (askıya almada false, askıyı kaldırmada true).</summary>
    public bool AccountApproved { get; init; }

    public bool AccountSuspended { get; init; }

    /// <summary>Askıyı kaldırmada yeni hesap onayı anı (UTC); askıya almada null.</summary>
    public DateTime? AccountApprovedAt { get; init; }

    /// <summary>Askıya almada askı anı (UTC); askıyı kaldırmada null.</summary>
    public DateTime? AccountSuspendedAt { get; init; }
}

/// <summary>issue #289: suspend/unsuspend hata gövdelerindeki <c>errorCode</c> değerleri (UI özel davranış için okur).</summary>
public static class AdminTeacherSuspensionErrorCodes
{
    public const string ReasonRequired = "SuspensionReasonRequired";
    public const string ReasonTooLong = "SuspensionReasonTooLong";
    public const string TeacherNotFound = "TeacherNotFound";
    public const string AccountNotApproved = "TeacherAccountNotApproved";
    public const string AlreadySuspended = "TeacherAlreadySuspended";
    public const string NotSuspended = "TeacherNotSuspended";
    public const string ConcurrentChange = "ConcurrentChange";
}
