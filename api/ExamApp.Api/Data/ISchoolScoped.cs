namespace ExamApp.Api.Data;

/// <summary>
/// issue #190: okul (tenant) sınırına tabi entity'ler. <see cref="Services.Tenancy.ISchoolAccessPolicy.ApplyScope{T}"/>
/// bu arayüz üzerinden sorgu düzeyinde <c>Where(x => x.SchoolId == requesterSchoolId)</c> uygular.
/// Şema etkisi yoktur (yalnızca arayüz), migration gerekmez.
/// </summary>
public interface ISchoolScoped
{
    /// <summary>null = okulsuz/bağımsız kayıt (epic #160 Karar 2: legacy kayıtlar da bu kurala tabi).</summary>
    int? SchoolId { get; }
}
