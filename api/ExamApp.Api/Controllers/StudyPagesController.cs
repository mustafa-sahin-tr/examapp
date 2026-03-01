using System.Collections.Generic;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

[ApiController]
[Route("api/study-pages")]
public class StudyPagesController : BaseController
{
    private readonly IStudyPageService _studyPageService;

    public StudyPagesController(IStudyPageService studyPageService)
        : base()
    {
        _studyPageService = studyPageService;
    }

    [Authorize(Roles = "Teacher,Student")]
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] StudyPageFilterDto filter)
    {
        var user = await GetAuthenticatedUserAsync();
        var result = await _studyPageService.GetPagedAsync(filter, user);
        return Ok(result);
    }

    [Authorize(Roles = "Teacher,Student")]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var user = await GetAuthenticatedUserAsync();
        var result = await _studyPageService.GetByIdAsync(id, user);
        if (result == null)
        {
            return NotFound();
        }
        return Ok(result);
    }

    [Authorize(Roles = "Teacher")]
    [HttpPost]
    public async Task<IActionResult> Create([FromForm] CreateStudyPageRequestDto request, [FromForm] List<IFormFile> images)
    {
        if (images == null || images.Count == 0)
        {
            return BadRequest(new { message = "En az bir resim eklemelisiniz." });
        }

        var user = await GetAuthenticatedUserAsync();
        var result = await _studyPageService.CreateAsync(request, images, user);
        return Ok(result);
    }

    [Authorize(Roles = "Teacher")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromForm] UpdateStudyPageRequestDto request, [FromForm] List<IFormFile> images)
    {
        var user = await GetAuthenticatedUserAsync();
        var result = await _studyPageService.UpdateAsync(id, request, images, user);
        if (result == null)
        {
            return NotFound();
        }
        return Ok(result);
    }

    [Authorize(Roles = "Teacher")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await GetAuthenticatedUserAsync();
        var result = await _studyPageService.DeleteAsync(id, user);
        if (!result.Success)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }
}
