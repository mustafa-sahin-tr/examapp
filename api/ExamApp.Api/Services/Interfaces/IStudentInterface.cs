using System;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Tenancy;

namespace ExamApp.Api.Services.Interfaces;

public interface IStudentService
{
    Task<List<GradeDto>> GetGradesAsync(CancellationToken ct = default);

    Task<StudentProfileDto> GetStudentProfile(int userId);

    Task<ResponseBaseDto> Save(int userId, RegisterStudentDto dto);

    Task<ResponseBaseDto> UpdateStudentGrade(int studentId, int gradeId);

    /// <summary>
    /// issue #190: öğrenci lookup listesi istek sahibinin okuluyla sınırlıdır (sorgu düzeyinde,
    /// <see cref="ISchoolAccessPolicy.ApplyScope{T}"/>). Admin/servis tüm okulları görür; okulsuz istek
    /// sahibi yalnızca okulsuz öğrencileri görür (bağımsız öğretmen daraltması #192).
    /// </summary>
    Task<List<StudentLookupDto>> GetStudentLookupsAsync(SchoolScope requester, CancellationToken ct = default);

    Task<UpdateThemeDto> UpdateStudentTheme(int userId, string themePreset, string? themeCustomConfig);
}
