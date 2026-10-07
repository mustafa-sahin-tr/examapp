using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #419 (epic #407 V1) — aktif veli–öğrenci bağlantısı taraflardan biri tarafından koparıldığında (soft revoke:
/// Status=Revoked) exam API tarafından AYNI transaction'da outbox'a yazılır. Tüketici V5'te (#423) gelir.
/// Yalnızca id'ler taşınır (bkz. <see cref="ParentLinkedEvent"/>).
/// </summary>
public class ParentUnlinkedEvent
{
    /// <summary>Idempotency anahtarı — bu koparma için üretilen tek event.</summary>
    public Guid EventId { get; set; }

    public int LinkId { get; set; }

    public int ParentId { get; set; }

    public int ParentUserId { get; set; }

    public int StudentId { get; set; }

    public int StudentUserId { get; set; }

    /// <summary>Koparan taraf: "Student" ya da "Parent".</summary>
    public string RevokedByRole { get; set; } = string.Empty;

    /// <summary>Koparan kullanıcının exam/auth user id'si.</summary>
    public int RevokedByUserId { get; set; }

    /// <summary>Koparma anı (UTC).</summary>
    public DateTime RevokedAtUtc { get; set; }
}
