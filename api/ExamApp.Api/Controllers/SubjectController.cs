using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;

[ApiController]
[Route("api/subject")]
public class SubjectController : BaseController
{
    private readonly ISubjectService _subjectService;
    public SubjectController(ISubjectService subjectService)
        : base()
    {
        _subjectService = subjectService;
    }

    [HttpGet]
    public async Task<ActionResult<List<Subject>>> GetSubjectAsync()
    {
        return await _subjectService.GetAllSubjectsAsync();
    }

    [HttpGet("topics/{subjectId}")]
    public async Task<IActionResult> GetTopicsBySubject(int subjectId)
    {
        var topics = await _subjectService.GetTopicBySubjectIdAsync(subjectId);

        return Ok(topics);
    }

    [HttpGet("subtopics/{topicId}")]
    public async Task<IActionResult> GetSubTopicsByTopic(int topicId)
    {
        var subTopics = await _subjectService.GetSubTopicByTopicIdAsync(topicId);
        return Ok(subTopics);
    }

    [HttpGet("by-grade/{gradeId}")]
    public async Task<IActionResult> GetSubjectsByGrade(int gradeId)
    {
        var subjects = await _subjectService.GetSubjectsByGradeIdAsync(gradeId);
        return Ok(subjects);
    }

    [HttpGet("topics")]
    public async Task<IActionResult> GetTopicsBySubjectAndGrade([FromQuery] int subjectId, [FromQuery] int gradeId)
    {
        var topics = await _subjectService.GetTopicsBySubjectAndGradeAsync(subjectId, gradeId);
        return Ok(topics);
    }

}


