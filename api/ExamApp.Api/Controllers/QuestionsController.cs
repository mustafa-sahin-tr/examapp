using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

[ApiController]
[Route("api/questions")]
public class QuestionsController : BaseController
{
    private readonly IMinIoService _minioService;
    private readonly ImageHelper _imageHelper;  

    private readonly IQuestionService _questionService;  

    public QuestionsController(IMinIoService minioService, ImageHelper imageHelper, IQuestionService questionService)
        : base()
    {        
        _minioService = minioService;
        _imageHelper = imageHelper;
        _questionService = questionService;
    }

    // 🟢 GET /api/questions/{id} - ID ile Soru Çekme
    [HttpGet("{id}")]
    public async Task<IActionResult> GetQuestionById(int id)
    {
        var response = await _questionService.GetQuestionById(id);
        if (response == null)
        {
            return NotFound(new { message = "Soru bulunamadı!" });
        }
        return Ok(response);
    }

    [HttpGet("passages")]
    public async Task<IActionResult> GetLastTenPassages() 
    {
        var passages = await _questionService.GetLastTenPassages();
        return Ok(passages);
    }

    // 🟢 GET /api/questions/{id} - ID ile Soru Çekme
    [HttpGet("bytest/{testid}")]
    public async Task<IActionResult> GetQuestionByTestId(int testid)
    {
        var questionList = await _questionService.GetQuestionByTestId(testid);
        return Ok(questionList);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateOrUpdateQuestion([FromBody] QuestionDto questionDto)
    {
        var response = await _questionService.CreateOrUpdateQuestion(questionDto);
        if (response == null)
        {
            return BadRequest(new { message = "Soru kaydedilemedi!" });
        }
        return Ok(response);
    }

    [HttpPost("save")]
    public async Task<IActionResult> KaydetSoruSeti([FromBody] BulkQuestionCreateDto soruDto)
    {
        if (soruDto == null)
        {
            return BadRequest("Geçersiz veri.");
        }

        var reponse = await _questionService.SaveBulkQuestion(soruDto);
        if (reponse == null || !reponse.Success == false)
        {
            return BadRequest("Soru seti kaydedilemedi.");
        }
        return Ok(reponse);
    }
}


//     [HttpDelete("{id}")]
//     [Authorize]
//     public async Task<IActionResult> DeleteQuestion(int id)
//     {
//         try
//         {
//             var question = await _context.Questions
//                 .Include(q => q.Answers)
//                 .FirstOrDefaultAsync(q => q.Id == id);

//             if (question == null)
//             {
//                 return NotFound(new { message = "Soru bulunamadı!" });
//             }

//             _context.Answers.RemoveRange(question.Answers);
//             _context.Questions.Remove(question);
//             await _context.SaveChangesAsync();

//             return Ok(new { message = "Soru başarıyla silindi!" });
//         }
//         catch (Exception ex)
//         {
//             return BadRequest(new { error = ex.Message });
//         }
//     }

