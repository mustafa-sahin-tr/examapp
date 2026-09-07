using System;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

public interface ITeacherService
{
    Task<ResponseBaseDto> Save(int userId, RegisterTeacherDto dto);

    Task<Teacher?> GetTeacher(int userId);

    Task<UpdateThemeDto> UpdateTeacherTheme(int userId, string themePreset, string? themeCustomConfig);

    /// <summary>
    /// Issue #53: öğretmenin sahip olduğu worksheet sayısı ve bu worksheet'lerin atamalarındaki
    /// benzersiz öğrenci sayısı (direkt + sınıf bazlı atamalar genişletilerek).
    /// </summary>
    Task<TeacherDashboardSummaryDto> GetDashboardSummaryAsync(int teacherId, CancellationToken ct = default);

    /// <summary>
    /// Issue #54: öğretmenin sahip olduğu her worksheet için atanan benzersiz öğrenci sayısı ve
    /// tamamlanma yüzdesi. Sahiplik/hedefleme mantığı GetDashboardSummaryAsync ile aynıdır.
    /// </summary>
    Task<List<TeacherWorksheetOverviewDto>> GetWorksheetsOverviewAsync(int teacherId, CancellationToken ct = default);
}
