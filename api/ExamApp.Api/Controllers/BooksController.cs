using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BooksController : BaseController
    {
        private readonly IBookService _bookService;
        public BooksController(IBookService bookService)
            : base()
        {
            _bookService = bookService;
        }

        [HttpGet]
        public async Task<IActionResult> GetBooksAsync()
        {
            var books = await _bookService.GetAllBooksAsync();
            return Ok(books);
        }

        [HttpGet("{bookId}/tests")]
        public async Task<IActionResult> GetBookTestsByBookId(int bookId)
        {
            var tests = await _bookService.GetBookTestsByBookIdAsync(bookId);
            return Ok(tests);
        }

    }
}
    
