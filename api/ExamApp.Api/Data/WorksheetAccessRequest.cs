using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

public enum WorksheetAccessRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>
/// Atama izni akışı (issue #13): bir öğretmenin, kendisine ait olmayan bir sınav (worksheet)
/// için sahibinden atama izni talebi. Talebin yaşam döngüsünü (Pending/Approved/Rejected) izler.
/// Onaylanınca kalıcı yetki ayrıca <see cref="WorksheetAccessGrant"/> olarak tutulur.
/// </summary>
public class WorksheetAccessRequest : BaseEntity
{
    public int Id { get; set; }

    public int WorksheetId { get; set; }

    [ForeignKey(nameof(WorksheetId))]
    public Worksheet Worksheet { get; set; } = default!;

    /// <summary>Talebi yapan öğretmenin exam/auth user id'si.</summary>
    public int RequesterUserId { get; set; }

    /// <summary>
    /// Talebi yapan öğretmenin Keycloak subject'i, talep oluşturulurken (HTTP context varken)
    /// yakalanır. Karar outbox event'inde SignalR hedeflemesi için auth-api'ye tekrar sormadan kullanılır.
    /// </summary>
    public string? RequesterKeycloakId { get; set; }

    public WorksheetAccessRequestStatus Status { get; set; } = WorksheetAccessRequestStatus.Pending;

    /// <summary>Talep eden öğretmenin bıraktığı serbest not (opsiyonel).</summary>
    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>Onay/ret kararının verildiği an (UTC).</summary>
    public DateTime? DecisionAt { get; set; }

    /// <summary>Kararı veren kullanıcının id'si (sahibi veya admin).</summary>
    public int? DecidedByUserId { get; set; }
}
