using System.Collections.Generic;
using ExamApp.Api.Models.Dtos;
using Microsoft.AspNetCore.Http;

namespace ExamApp.Api.Services.Interfaces;

public interface IStudyPageService
{
    Task<Paged<StudyPageDto>> GetPagedAsync(StudyPageFilterDto filter, UserProfileDto user);
    Task<StudyPageDto?> GetByIdAsync(int id, UserProfileDto user);
    Task<StudyPageDto> CreateAsync(CreateStudyPageRequestDto request, List<IFormFile> images, UserProfileDto user);
    Task<StudyPageDto?> UpdateAsync(int id, UpdateStudyPageRequestDto request, List<IFormFile> newImages, UserProfileDto user);
    Task<ResponseBaseDto> DeleteAsync(int id, UserProfileDto user);
}
