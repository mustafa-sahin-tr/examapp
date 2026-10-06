using System;
using System.Collections.Generic;
using System.Globalization;
using Hangfire.Common;
using Hangfire.Logging;
using Hangfire.States;
using Hangfire.Storage;

namespace ExamApp.Api.Services.StudentReset;

/// <summary>
/// issue #396: <see cref="StudentResetJob"/> tüm otomatik denemeleri tüketip <see cref="FailedState"/>'e geçtiğinde
/// (ara denemeler Scheduled'a gider, burada görünmez) Error seviyesinde, kullanıcı id'siyle bir alarm logu yazar.
/// Hangfire'ın kendi genel "Failed to process the job" logu iş tipini/kullanıcıyı söylemez; bu log alarm kuralının
/// bağlanacağı sabit bir metin verir: <see cref="AlertMarker"/>.
/// <para>
/// Logger, Hangfire'ın <see cref="LogProvider"/>'ından alınır — <c>AddHangfire</c> onu ASP.NET Core
/// <c>ILoggerFactory</c>'ye bağlar (kategori: <c>ExamApp.Api.Services.StudentReset.StudentResetJob</c>). Attribute
/// DI'dan servis alamadığı için global filtre + ILogger yerine bu yol seçildi (global filtre statik koleksiyondadır,
/// test host'larında birikir).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class StudentResetFailureAlertAttribute : JobFilterAttribute, IApplyStateFilter
{
    public const string AlertMarker = "[StudentReset] FAILED";

    private readonly Func<ILog> _log;

    public StudentResetFailureAlertAttribute() : this(() => LogProvider.GetLogger(typeof(StudentResetJob)))
    {
    }

    /// <summary>Test seam: Hangfire'ın süreç-genel LogProvider'ına dokunmadan logger verir.</summary>
    internal StudentResetFailureAlertAttribute(Func<ILog> log) => _log = log;

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context.NewState is not FailedState failed)
            return;

        _log()
            .ErrorException(Describe(context.BackgroundJob?.Id, context.BackgroundJob?.Job?.Args), failed.Exception);
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }

    /// <summary>
    /// Alarm metni. Job argümanları <c>RunAsync(userId, studentId, keycloakUserId)</c>; Keycloak sub loga yazılmaz
    /// (userId/studentId yeterli, iş id'siyle Hangfire panelinden ayrıntıya gidilir).
    /// </summary>
    internal static string Describe(string? jobId, IReadOnlyList<object?>? args)
    {
        string Arg(int i) => args != null && args.Count > i && args[i] != null
            ? Convert.ToString(args[i], CultureInfo.InvariantCulture) ?? "?"
            : "?";

        return $"{AlertMarker}: öğrenci sıfırlama işi tüm denemelerden sonra başarısız oldu; exam verisi BadgeService " +
               $"sıfırlanmadan silinmez, öğrenci verisi sıfırlanmadı. jobId={jobId ?? "?"} userId={Arg(0)} studentId={Arg(1)}. " +
               "Hangfire panelinden işi yeniden kuyruğa alın ya da öğrenci tekrar sıfırlama istesin.";
    }
}
