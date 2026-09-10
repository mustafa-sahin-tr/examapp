using System.Collections.Generic;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Çalışma etkinliği (eski adı: StudyPage) CRUD endpoint'leri.
/// </summary>
[ApiController]
[Route("api/study-items")]
[Route("api/study-pages")] // TODO(#140): api/study-pages alias'ı frontend rename tamamlanınca kaldırılacak
public class StudyItemsController : BaseController
{
    private readonly IStudyItemService _studyItemService;

    public StudyItemsController(IStudyItemService studyItemService)
        : base()
    {
        _studyItemService = studyItemService;
    }

    [Authorize(Roles = "Teacher,Student")]
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] StudyItemFilterDto filter)
    {
        var user = await GetAuthenticatedUserAsync();
        var result = await _studyItemService.GetPagedAsync(filter, user);
        return Ok(result);
    }

    [Authorize(Roles = "Teacher,Student")]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var user = await GetAuthenticatedUserAsync();
        var result = await _studyItemService.GetByIdAsync(id, user);
        if (result == null)
        {
            return NotFound();
        }
        return Ok(result);
    }

    [Authorize(Roles = "Teacher")]
    [HttpPost]
    public async Task<IActionResult> Create([FromForm] CreateStudyItemRequestDto request, [FromForm] List<IFormFile> images)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { message = "Baslik zorunludur." });
        }

        var user = await GetAuthenticatedUserAsync();
        var result = await _studyItemService.CreateAsync(request, images ?? new List<IFormFile>(), user);
        if (result.Error != null)
        {
            return BadRequest(new { message = result.Error });
        }
        return Ok(result.Item);
    }

    [Authorize(Roles = "Teacher")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromForm] UpdateStudyItemRequestDto request, [FromForm] List<IFormFile> images)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { message = "Baslik zorunludur." });
        }

        var user = await GetAuthenticatedUserAsync();
        var result = await _studyItemService.UpdateAsync(id, request, images ?? new List<IFormFile>(), user);
        if (result.NotFound)
        {
            return NotFound();
        }
        if (result.Error != null)
        {
            return BadRequest(new { message = result.Error });
        }
        return Ok(result.Item);
    }

    [Authorize(Policy = "TeacherOrService")]
    [HttpPost("attach-image-by-subtopics")]
    public async Task<IActionResult> AttachImageBySubTopics([FromBody] AttachStudyItemImageBySubTopicsRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            return BadRequest(new { message = "imageUrl zorunludur." });
        }

        if (request.SubTopicIds == null || request.SubTopicIds.Count == 0)
        {
            return BadRequest(new { message = "subTopicIds en az bir eleman icermelidir." });
        }

        var user = await GetAuthenticatedUserAsync();

        var result = await _studyItemService.AttachImageBySubTopicsAsync(request, user);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [Authorize(Roles = "Teacher")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await GetAuthenticatedUserAsync();
        var result = await _studyItemService.DeleteAsync(id, user);
        if (!result.Success)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }
}
