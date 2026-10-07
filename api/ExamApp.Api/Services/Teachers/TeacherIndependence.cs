using System;
using System.Linq.Expressions;
using ExamApp.Api.Data;

namespace ExamApp.Api.Services.Teachers;

/// <summary>
/// issue #418: "bağımsız öğretmen" (randevu/müsaitlik özelliğinin sahibi) kuralının TEK tanımı:
/// <c>IsIndependentTutor &amp;&amp; SchoolId == null</c>. Admin'in okula bağladığı bağımsız başvurulu (hibrit, #313) öğretmen
/// bağımsız SAYILMAZ. Randevu servisleri, öğretmen kilidi (<see cref="SqlCondition"/>), Pending talep süpürmesi ve
/// <c>/auth/refresh</c> profilindeki <c>TeacherDto.IsIndependentTutor</c> bu tanımı kullanır.
/// </summary>
public static class TeacherIndependence
{
    /// <summary>EF'e çevrilebilir biçim (sorgu koşulları; <c>PredicateComposer.Inline</c> ile gezinme üzerinden gömülebilir).</summary>
    public static readonly Expression<Func<Teacher, bool>> Holds =
        t => t.IsIndependentTutor && t.SchoolId == null;

    /// <summary>
    /// Ham SQL biçimi (<c>"Teachers"</c> satırı üzerinde, tablo öneki yok) — <c>FOR SHARE</c> kilidi altındaki yeniden doğrulama.
    /// <see cref="Holds"/> ile birebir aynı kalmalı.
    /// </summary>
    public const string SqlCondition = "\"IsIndependentTutor\" AND \"SchoolId\" IS NULL";

    /// <summary>Bellek içi biçim (projekte edilmiş alanlar üzerinde).</summary>
    public static bool IsIndependent(bool isIndependentTutor, int? schoolId) => isIndependentTutor && schoolId == null;

    public static bool IsIndependent(Teacher teacher) => IsIndependent(teacher.IsIndependentTutor, teacher.SchoolId);
}
