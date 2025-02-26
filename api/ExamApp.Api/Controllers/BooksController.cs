using ExamApp.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BooksController : BaseController
    {
        public BooksController(AppDbContext context)
            : base(context)
        {
        }

        [HttpGet]
        public async Task<IActionResult> GetBooks()
        {
            var books = await _context.Books.ToListAsync();
            return Ok(books);
        }

        [HttpGet("{bookId}/tests")]
        public async Task<IActionResult> GetBookTests(int bookId)
        {
            var tests = await _context.BookTests.Where(bt => bt.BookId == bookId).ToListAsync();
            return Ok(tests);
        }

    }
}
    
