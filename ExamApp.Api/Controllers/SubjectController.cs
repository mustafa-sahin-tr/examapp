using ExamApp.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;

[ApiController]
[Route("api/subject")]
public class SubjectController : ControllerBase
{
    private readonly AppDbContext _context;

    public SubjectController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<Subject>>> GetSubjectAsync()
    {
        return await _context.Subjects.ToListAsync();
    }

    [HttpGet("topics/{subjectId}")]
    public async Task<IActionResult> GetTopicsBySubject(int subjectId)
    {
        var topics = await _context.Topics
            .Where(t => t.SubjectId == subjectId)
            .Select(t => new { t.Id, t.Name })
            .ToListAsync();
        return Ok(topics);
    }

    [HttpGet("subtopics/{topicId}")]
    public async Task<IActionResult> GetSubTopicsByTopic(int topicId)
    {
        var subTopics = await _context.SubTopics
            .Where(st => st.TopicId == topicId)
            .Select(st => new { st.Id, st.Name })
            .ToListAsync();
        return Ok(subTopics);
    }


}


