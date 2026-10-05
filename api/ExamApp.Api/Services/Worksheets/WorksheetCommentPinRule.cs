using System;
using System.Linq.Expressions;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// issue #326 + #334: sorumlu öğretmen ile yorum yazarı arasındaki OKUL koşulu — TEK tanım. Hem sabitleme anında
/// (<see cref="ResponsibleTeacherRule.Decide"/>: atayan / sahip sorumlu olabilir mi) hem de sabitlenmiş yorumun her
/// kullanımında (okuma kapsamı, cevap, moderasyon, şikayet listesi, bildirim alıcısı yeniden doğrulaması) aynı ifade ağacı
/// çalışır: SQL'de <see cref="HoldsOn"/>/<see cref="HoldsForReply(int, int?)"/>, bellekte <see cref="Holds"/>/<see cref="SchoolAllows"/>
/// (aynı ağacın derlenmiş hali).
/// <para>
/// Kural: öğretmenin GÜNCEL okulu (<see cref="UserSchoolResolver"/>) yazarın okuluyla (yorumda <see cref="WorksheetComment.AuthorSchoolId"/>,
/// sabitleme anında öğrencinin okulu) AYNI olmalı — iki taraf da okullu ve eşit (<c>null == null</c> eşleşme SAYILMAZ, #326).
/// Tek istisna (#334, PO seçenek a): kaynak <see cref="ResponsibleTeacherSource.Assignment"/> VE öğretmen okulsuz (bağımsız)
/// VE yazar da okulsuz → bağımsız öğretmenin kendi atama ilişkisi korunur. Okullu öğretmen okulsuz/başka okul yazarda (atama
/// olsa bile) yetkili DEĞİL; sahip kaynaklı (Owner/CopyOwner) ya da kaynağı bilinmeyen (legacy) sabitte istisna yok.
/// </para>
/// <para>
/// Öğrenci kökünün altındaki cevaplar (#334 review): sabit kökte durur ama her cevap KENDİ <see cref="WorksheetComment.AuthorSchoolId"/>'si
/// ile de aynı koşuldan geçer (<see cref="HoldsForReply(int, int?)"/>) — okul değiştiren öğrencinin eski köke yeni okulundan yazdığı cevap
/// eski okulun öğretmenine açılmaz. Cevap YAZMA ayrıca kök yazarının GÜNCEL okulunu da ister (<see cref="CanReplyTo"/>): öğretmen
/// eski kökü okumaya devam eder ama okuldan çıkmış öğrenciye cevap (ve StudentReply bildirimi) gönderemez.
/// </para>
/// <para>
/// Okul belirsizliği (security Düşük-1): birden fazla canlı satır (<see cref="UserSchool.Ambiguous"/>) "okulsuz" sayılmaz —
/// <see cref="ForRule"/> onu hiçbir okulla eşleşmeyen ve null OLMAYAN <see cref="AmbiguousSchoolId"/>'ye çevirir; böylece ne aynı
/// okul ne de bağımsız istisnası sağlanır (fail-closed). Teacher profili olmayan (ör. profilsiz admin) atayan okulsuz sayılır:
/// yalnız okulsuz öğrencide sorumlu olabilir (CR-2 kararı — admin okul öğretmeni değil, #326 sahip kuralıyla tutarlı).
/// </para>
/// <para>
/// Okul koşulu okuma anında güncel okulla uygulanır (veri silinmez): öğretmen yazarın okuluna dönerse sabit yeniden geçerli.
/// Aynı okulda kalındıkça #105 kuralı değişmez — atama bitse de sabitlenmiş öğretmen cevap yazar (atamanın aktifliğine
/// burada bakılmaz).
/// </para>
/// </summary>
public static class WorksheetCommentPinRule
{
    /// <summary>
    /// Belirsiz (çoklu canlı satır) okul için kural girdisi: null değil (bağımsız istisnası yok) ve hiçbir gerçek okul Id'si
    /// olamaz (identity pozitif) — hiçbir yazarla eşleşmez.
    /// </summary>
    public const int AmbiguousSchoolId = int.MinValue;

    /// <summary>(kaynak, öğretmenin güncel okulu, yazarın okulu) → okul koşulu sağlanıyor mu. Kuralın TEK ifadesi.</summary>
    private static readonly Expression<Func<ResponsibleTeacherSource?, int?, int?, bool>> SchoolCondition =
        (source, teacherSchoolId, authorSchoolId) =>
            (teacherSchoolId != null && teacherSchoolId != AmbiguousSchoolId && authorSchoolId == teacherSchoolId)
            || (teacherSchoolId == null && authorSchoolId == null && source == ResponsibleTeacherSource.Assignment);

    private static readonly Func<ResponsibleTeacherSource?, int?, int?, bool> SchoolConditionCompiled = SchoolCondition.Compile();

    /// <summary><see cref="UserSchoolResolver.ResolveManyDetailedAsync"/> sonucunu kural girdisine çevirir (belirsiz → <see cref="AmbiguousSchoolId"/>).</summary>
    public static int? ForRule(UserSchool school) => school.Ambiguous ? AmbiguousSchoolId : school.SchoolId;

    /// <summary>
    /// Okul koşulu (bellek içi): <paramref name="source"/> kaynağıyla sorumlu olan/olacak öğretmenin güncel okulu
    /// <paramref name="teacherSchoolId"/>, yazarın okulu <paramref name="authorSchoolId"/>.
    /// </summary>
    public static bool SchoolAllows(ResponsibleTeacherSource? source, int? teacherSchoolId, int? authorSchoolId) =>
        SchoolConditionCompiled(source, teacherSchoolId, authorSchoolId);

    /// <summary>
    /// <paramref name="pinned"/> yorumundaki sabit <paramref name="teacherUserId"/> için HÂLÂ geçerli mi: sabit bu öğretmene ve
    /// okul koşulu (<see cref="SchoolAllows"/>) güncel okulla sağlanıyor. <see cref="HoldsOn"/>'un bellek içi karşılığı.
    /// </summary>
    public static bool Holds(WorksheetComment pinned, int teacherUserId, int? teacherSchoolId) =>
        pinned.ResponsibleTeacherUserId == teacherUserId
        && SchoolAllows(pinned.ResponsibleTeacherSource, teacherSchoolId, pinned.AuthorSchoolId);

    /// <summary>
    /// Öğrenci kökü <paramref name="root"/> altındaki <paramref name="reply"/> bu öğretmenin kapsamında mı: kökteki sabit geçerli
    /// VE cevabın kendi yazar okulu kökün kaynağıyla aynı koşulu sağlıyor. <see cref="HoldsForReply(int, int?)"/>'nin bellek içi karşılığı.
    /// </summary>
    public static bool HoldsForReply(WorksheetComment root, WorksheetComment reply, int teacherUserId, int? teacherSchoolId) =>
        Holds(root, teacherUserId, teacherSchoolId)
        && SchoolAllows(root.ResponsibleTeacherSource, teacherSchoolId, reply.AuthorSchoolId);

    /// <summary>
    /// Öğretmen bu öğrenci köküne cevap yazabilir mi: sabit geçerli VE kök yazarının GÜNCEL okulu
    /// (<paramref name="rootAuthorCurrentSchoolId"/>, <see cref="ForRule"/> ile) da koşulu sağlıyor — okuldan çıkmış öğrenciye
    /// okullar arası kanal açılmaz.
    /// </summary>
    public static bool CanReplyTo(WorksheetComment root, int teacherUserId, int? teacherSchoolId, int? rootAuthorCurrentSchoolId) =>
        Holds(root, teacherUserId, teacherSchoolId)
        && SchoolAllows(root.ResponsibleTeacherSource, teacherSchoolId, rootAuthorCurrentSchoolId);

    /// <summary>
    /// <see cref="Holds"/>'un SQL'e çevrilebilir hali: <paramref name="pinnedComment"/> ile seçilen yorumun (kendisi ya da
    /// <c>c.ParentComment</c>) sabiti bu öğretmen için geçerli. Öğretmen id'si/okulu SQL parametresi olur (sorgu planı
    /// kullanıcıya göre çoğalmaz — EF Core 10'da sabit nesnenin üye erişimi parametreye çıkarılır; test:
    /// <c>Pin_expression_is_parameterized</c>). <see cref="PredicateComposer"/> ile başka koşullara gömülür.
    /// </summary>
    public static Expression<Func<WorksheetComment, bool>> HoldsOn(
        Expression<Func<WorksheetComment, WorksheetComment?>> pinnedComment, int teacherUserId, int? teacherSchoolId)
    {
        ArgumentNullException.ThrowIfNull(pinnedComment);
        var teacher = TeacherConstant(teacherUserId, teacherSchoolId);
        return Expression.Lambda<Func<WorksheetComment, bool>>(
            PinBody(pinnedComment.Body, teacher), pinnedComment.Parameters[0]);
    }

    /// <summary>
    /// <see cref="HoldsForReply(WorksheetComment, WorksheetComment, int, int?)"/>'nin SQL hali: <c>c.ParentComment</c>'teki
    /// sabit geçerli VE <c>c.AuthorSchoolId</c> kökün kaynağıyla okul koşulunu sağlıyor. Çağıran <c>c.ParentComment != null</c>
    /// ve kökün öğrenci olduğunu ayrıca koşullar.
    /// </summary>
    public static Expression<Func<WorksheetComment, bool>> HoldsForReply(int teacherUserId, int? teacherSchoolId)
    {
        var c = Expression.Parameter(typeof(WorksheetComment), "c");
        var parent = Expression.Property(c, nameof(WorksheetComment.ParentComment));
        var teacher = TeacherConstant(teacherUserId, teacherSchoolId);
        var replySchool = PredicateComposer.Inline(SchoolCondition,
            Expression.Property(parent, nameof(WorksheetComment.ResponsibleTeacherSource)),
            Expression.Property(teacher, nameof(PinTeacher.SchoolId)),
            Expression.Property(c, nameof(WorksheetComment.AuthorSchoolId)));
        return Expression.Lambda<Func<WorksheetComment, bool>>(
            Expression.AndAlso(PinBody(parent, teacher), replySchool), c);
    }

    /// <summary>"Sabit bu öğretmene + <see cref="SchoolCondition"/>" gövdesi; <paramref name="pinned"/> sabitin durduğu yorum ifadesi.</summary>
    private static Expression PinBody(Expression pinned, Expression teacher)
    {
        var pinnedToTeacher = Expression.Equal(
            Expression.Property(pinned, nameof(WorksheetComment.ResponsibleTeacherUserId)),
            Expression.Convert(Expression.Property(teacher, nameof(PinTeacher.UserId)), typeof(int?)));
        var school = PredicateComposer.Inline(SchoolCondition,
            Expression.Property(pinned, nameof(WorksheetComment.ResponsibleTeacherSource)),
            Expression.Property(teacher, nameof(PinTeacher.SchoolId)),
            Expression.Property(pinned, nameof(WorksheetComment.AuthorSchoolId)));
        return Expression.AndAlso(pinnedToTeacher, school);
    }

    // Kapanış (closure) gibi sabit nesnenin üyeleri: EF bunları sabit değil parametre olarak çevirir.
    private static Expression TeacherConstant(int teacherUserId, int? teacherSchoolId) =>
        Expression.Constant(new PinTeacher(teacherUserId, teacherSchoolId));

    private sealed class PinTeacher(int userId, int? schoolId)
    {
        public int UserId { get; } = userId;
        public int? SchoolId { get; } = schoolId;
    }
}
