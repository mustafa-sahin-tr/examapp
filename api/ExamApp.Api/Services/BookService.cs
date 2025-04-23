using System;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services;

public class BookService : IBookService
{
    private readonly AppDbContext _context;
    public BookService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<Book>> GetAllBooksAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Books.ToListAsync(cancellationToken);
    }

    public async Task<List<BookTest>> GetBookTestsByBookIdAsync(int bookId, CancellationToken cancellationToken = default)
    {
        return await _context.BookTests.Where(bt => bt.BookId == bookId).ToListAsync(cancellationToken);
    }
}
