using System;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

public interface IAuthApiClient
{
    Task<UserProfileDto> GetUserProfileAsync();
}
