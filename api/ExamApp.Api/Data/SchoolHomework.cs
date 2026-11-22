using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>Düz ödevin hedefi (issue #1282).</summary>
public enum SchoolHomeworkTarget
{
    /// <summary>Okulun bir sınıfındaki doğrulanmış öğrenciler (öğrencinin güncel okulu ve sınıfıyla çözülür).</summary>
    Grade,

    /// <summary>Bir şubenin aktif üyeleri (üyelik bitince ödev düşer).</summary>
    Section,

    /// <summary>Seçilen öğrenciler (doğrudan hedef; öğrenci okul değiştirse de kalır).</summary>
    Students
}

/// <summary>Öğretmenin öğrenci başına kontrol sonucu (issue #1282). Puan yok.</summary>
public enum SchoolHomeworkCheckStatus
{
    Done,
    Partial,
    NotDone
}

/// <summary>
/// issue #1282 (#1071 S7): düz ödev ("Türkçe: X kitabı s. 8–10"). Oluşturulduğu okulu damgalar (sınıf atamaları gibi, #540 K4):
/// öğretmen okul değiştirse de ödev eski okulun öğrencilerine teslim tarihine kadar görünür, öğretmen kendi listesinde görür /
/// siler ama eski okulun öğrencilerini ve durumlarını görmez. Alıcılar kaydedilmez, okunurken çözülür (şube / sınıf hedefi
/// öğrencinin güncel durumunu izler); yalnız <see cref="SchoolHomeworkTarget.Students"/> hedefi öğrenci listesini saklar.
/// Silme yumuşaktır (<see cref="DeletedAt"/>); öğrenci silinmiş ödevi görmez.
/// </summary>
public class SchoolHomework
{
    public const int BookNameMaxLength = 120;
    public const int PagesMaxLength = 60;
    public const int NoteMaxLength = 500;
    public const int TargetLabelMaxLength = 64;

    public int Id { get; set; }

    public int SchoolId { get; set; }

    public School School { get; set; } = null!;

    public int TeacherUserId { get; set; }

    public int SubjectId { get; set; }

    public Subject Subject { get; set; } = null!;

    [MaxLength(BookNameMaxLength)]
    public string? BookName { get; set; }

    [MaxLength(PagesMaxLength)]
    public string? Pages { get; set; }

    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }

    /// <summary>Teslim günü (yerel takvim, Europe/Istanbul); bu günün sonuna kadar gecikmiş sayılmaz.</summary>
    public DateOnly DueDate { get; set; }

    public SchoolHomeworkTarget Target { get; set; }

    public int? TargetGradeId { get; set; }

    public int? TargetSectionId { get; set; }

    /// <summary>Oluşturma anındaki hedef adı (sınıf ya da "sınıf / şube"); seçili öğrencilerde null.</summary>
    [MaxLength(TargetLabelMaxLength)]
    public string? TargetLabel { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public ICollection<SchoolHomeworkStudent> Students { get; set; } = new List<SchoolHomeworkStudent>();
}

/// <summary>issue #1282: <see cref="SchoolHomeworkTarget.Students"/> hedefinin öğrencileri.</summary>
public class SchoolHomeworkStudent
{
    public int HomeworkId { get; set; }

    public SchoolHomework Homework { get; set; } = null!;

    public int StudentId { get; set; }

    public Student Student { get; set; } = null!;
}

/// <summary>
/// issue #1282: öğrencinin ödevdeki durumu — öğrencinin "yaptım" işareti ve öğretmenin kontrolü (onaylar ya da üzerine yazar).
/// Satır ilk işarette / kontrolde açılır; (HomeworkId, StudentId) tekil.
/// </summary>
public class SchoolHomeworkProgress
{
    public const int TeacherNoteMaxLength = 300;

    public int Id { get; set; }

    public int HomeworkId { get; set; }

    public SchoolHomework Homework { get; set; } = null!;

    public int StudentId { get; set; }

    public Student Student { get; set; } = null!;

    public DateTime? StudentDoneAt { get; set; }

    public SchoolHomeworkCheckStatus? TeacherStatus { get; set; }

    [MaxLength(TeacherNoteMaxLength)]
    public string? TeacherNote { get; set; }

    public DateTime? CheckedAt { get; set; }

    public int? CheckedByUserId { get; set; }
}
