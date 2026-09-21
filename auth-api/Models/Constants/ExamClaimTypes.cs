using System;

namespace ExamApp.Api.Models.Constants;

public class ExamClaimTypes
{
    public const string StudentId = "StudentId";
    public const string TeacherId = "TeacherId";
    public const string ParentId = "ParentId";

    /// <summary>
    /// issue #189: tenant bağlamı ipucu. JWT'deki bu claim yalnızca bir ipucudur — yetki
    /// kararları için tek başına güvenilmez, exam API sunucu tarafında DB'den doğrular.
    /// </summary>
    public const string SchoolId = "school_id";
}
