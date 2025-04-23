using System;
using ExamApp.Api.Data;

namespace ExamApp.Api.Services.Interfaces;

public interface IBookService
{
    Task<List<Book>> GetAllBooksAsync(CancellationToken cancellationToken = default);

    Task<List<BookTest>> GetBookTestsByBookIdAsync(int bookId, CancellationToken cancellationToken = default);
}
