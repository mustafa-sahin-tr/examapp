using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExamApp.Api.Helpers;

/// <summary>
/// <see cref="DbUpdateException"/> sınıflandırması (issue #259). Yalnızca unique index ihlali tanınır; FK/bağlantı gibi
/// diğer hatalar çağıranda yukarı fırlamalıdır. Postgres: SqlState 23505. SQLite (yalnız test sağlayıcısı; Api projesi
/// paketi referanslamaz): mesajda "UNIQUE constraint failed". <c>RecurringAvailabilityService.IsUniqueViolation</c> ile
/// aynı kural.
/// </summary>
public static class DbUpdateExceptionClassifier
{
    public static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => true,
        DbException db when db.GetType().Name == "SqliteException"
            && db.Message.Contains("UNIQUE constraint failed", StringComparison.Ordinal) => true,
        _ => false
    };
}
