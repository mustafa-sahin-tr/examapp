using System;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Tutors;

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

    /// <summary>
    /// Issue #55: öğretmenin sahip olduğu worksheet'lerde geride kalan (düşük tamamlama ve/veya
    /// süresi geçmiş) öğrenci-worksheet çiftleri. Sadece en az bir bayrağı true olan satırlar döner; boşsa [].
    /// </summary>
    Task<List<TeacherLaggingStudentDto>> GetLaggingStudentsAsync(int teacherId, CancellationToken ct = default);

    /// <summary>
    /// Issue #95: authenticated kullanıcının kendi tutor profili. Teacher kaydı yoksa NotFound,
    /// IsIndependentTutor=false ise Forbidden bayrağıyla döner.
    /// </summary>
    Task<TutorProfileResultDto> GetTutorProfileAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// Issue #95: sadece IsIndependentTutor=true olan kendi kaydını günceller (onay durumu fark etmez).
    /// En az 1 ders, en az 1 mod (online/yüz yüze) ve HourlyRate &gt; 0 zorunlu; aksi halde Success=false.
    /// </summary>
    Task<TutorProfileResultDto> UpdateTutorProfileAsync(int userId, UpdateTutorProfileDto dto, CancellationToken ct = default);

    /// <summary>Issue #95: öğrenci için onaylı bağımsız öğretmen araması. Sadece Approved kayıtlar döner.</summary>
    Task<List<TeacherSearchResultDto>> SearchTutorsAsync(TeacherSearchFilterDto filter, CancellationToken ct = default);

    /// <summary>
    /// Issue #95: tekil öğretmen public profili. Kayıt yoksa, bağımsız değilse ya da Approved değilse
    /// hepsi null döner (var/yok ayrımı sızdırılmaz).
    /// </summary>
    Task<TeacherPublicProfileDto?> GetPublicProfileAsync(int teacherId, CancellationToken ct = default);
}
