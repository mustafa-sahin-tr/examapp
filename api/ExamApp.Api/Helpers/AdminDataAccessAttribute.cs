using System;
using ExamApp.Api.Data;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #262: admin kişisel veri ucunun audit kaynağını endpoint metadata'sı olarak işaretler. Rate limit reddi (429)
/// controller'a ulaşmadığı için <see cref="AdminUserListRateLimitPolicy"/> reddi hangi kaynağa yazacağını buradan okur.
/// <c>[EnableRateLimiting(AdminUserListRateLimiting.Policy)]</c> taşıyan her uçta bulunmalıdır; yoksa red audit'lenemez
/// (uyarı loglanır).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AdminDataAccessAttribute : Attribute
{
    public AdminDataAccessAttribute(AdminDataAccessResource resource) => Resource = resource;

    public AdminDataAccessResource Resource { get; }
}
