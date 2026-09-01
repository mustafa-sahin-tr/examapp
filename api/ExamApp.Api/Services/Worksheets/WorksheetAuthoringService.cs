using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// Creating, updating and deleting worksheets (single + bulk) and their background image.
/// Extracted from ExamService.
/// </summary>
public class WorksheetAuthoringService : IWorksheetAuthoringService
{
    private readonly AppDbContext _context;
    private readonly ImageHelper _imageHelper;
    private readonly IMinIoService _minioService;

    public WorksheetAuthoringService(AppDbContext context, ImageHelper imageHelper, IMinIoService minioService)
    {
        _context = context;
        _imageHelper = imageHelper;
        _minioService = minioService;
    }

    public async Task<UpdateWorksheetBackgroundImageDto> UpdateWorksheetBackgroundImageAsync(int worksheetId, IFormFile file, int userId)
    {
        if (file == null || file.Length == 0)
        {
            return new UpdateWorksheetBackgroundImageDto
            {
                Success = false,
                Message = "Yüklemek için geçerli bir görsel seçin."
            };
        }

        if (string.IsNullOrWhiteSpace(file.ContentType) || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateWorksheetBackgroundImageDto
            {
                Success = false,
                Message = "Sadece görsel dosyaları yüklenebilir."
            };
        }

        var worksheet = await _context.Worksheets.FirstOrDefaultAsync(w => w.Id == worksheetId);
        if (worksheet == null)
        {
            return new UpdateWorksheetBackgroundImageDto
            {
                Success = false,
                Message = "Worksheet bulunamadı."
            };
        }

        var previousImageUrl = worksheet.ImageUrl;
        var fileName = $"{worksheetId}-background.png";
        await using var stream = file.OpenReadStream();
        var uploadedPath = await _minioService.UploadFileAsync(stream, fileName, "worksheets", "image/png");

        worksheet.ImageUrl = uploadedPath;
        await _context.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(previousImageUrl) &&
            !string.Equals(previousImageUrl, uploadedPath, StringComparison.OrdinalIgnoreCase))
        {
            await _minioService.DeleteFileByUrlAsync(previousImageUrl);
        }

        return new UpdateWorksheetBackgroundImageDto
        {
            Success = true,
            Message = "Arka plan görseli güncellendi.",
            ObjectId = worksheetId,
            ImageUrl = uploadedPath
        };
    }

    public async Task<ExamSavedDto> CreateOrUpdateAsync(ExamDto examDto, int userId)
    {
        if (examDto == null)
        {
            // TODO: bu kontrolleri contrrollerda yapabilirsin
            // return BadRequest(new { error = "Sınav bilgileri eksik!" });
            return new ExamSavedDto
            {
                Success = false,
                Message = "Sınav bilgileri eksik!"
            };
        }

        // eper newBookName ve newBookTestName dolu ise ve db'de zaten varsa o halde sucess = true olarak devam et
        if (!string.IsNullOrWhiteSpace(examDto.NewBookName) && !string.IsNullOrWhiteSpace(examDto.NewBookTestName))
        {
            var existingBook = await _context.Books
                .Include(b => b.BookTests)
                .FirstOrDefaultAsync(b => b.Name == examDto.NewBookName);

            if (existingBook != null)
            {
                examDto.BookId = existingBook.Id;
                var existingBookTest = existingBook.BookTests
                    .FirstOrDefault(bt => bt.Name == examDto.NewBookTestName);

                if (existingBookTest != null)
                {
                    examDto.BookTestId = existingBookTest.Id;
                }
            }


        }

        try
        {
            if (examDto.BookId == 0 && string.IsNullOrWhiteSpace(examDto.NewBookName))
            {
                // return BadRequest(new { error = "Kitap seçilmedi!" });
                return new ExamSavedDto
                {
                    Success = false,
                    Message = "Kitap seçilmedi!"
                };
            }

            if (examDto.BookTestId == 0 && string.IsNullOrWhiteSpace(examDto.NewBookTestName))
            {
                // return BadRequest(new { error = "Kipta Test seçilmedi!" });
                return new ExamSavedDto
                {
                    Success = false,
                    Message = "Kipta Test seçilmedi!"
                };
            }

            Book? book = null;
            if (examDto.BookId is null || examDto.BookId == 0)
            {
                if (string.IsNullOrWhiteSpace(examDto.NewBookName))
                {
                    return new ExamSavedDto
                    {
                        Success = false,
                        Message = "Kitap seçilmedi!"
                    };
                }

                if (string.IsNullOrWhiteSpace(examDto.NewBookTestName))
                {
                    return new ExamSavedDto
                    {
                        Success = false,
                        Message = "Kipta Test seçilmedi!"
                    };
                }

                book = await _context.Books
                            .Include(b => b.BookTests)
                        .FirstOrDefaultAsync(b => b.Name == examDto.NewBookName);

                if (book == null)
                {
                    book = new Book
                    {
                        Name = examDto.NewBookName
                    };
                    book.BookTests =
                    [
                    new BookTest
                    {
                        Name = examDto.NewBookTestName,
                        BookId = book.Id
                    },
                    ];
                    _context.Books.Add(book);
                }

                await _context.SaveChangesAsync();
                examDto.BookId = book.Id;
                examDto.BookTestId = book.BookTests.First(bt => bt.Name == examDto.NewBookTestName).Id;
            }
            else
            {
                book = await _context.Books
                            .Include(b => b.BookTests)
                        .FirstOrDefaultAsync(b => b.Id == examDto.BookId);

                if (book == null)
                {
                    return new ExamSavedDto
                    {
                        Success = false,
                        Message = "Kitap bulunamadı!"
                    };

                }

                // Eğer zaten bu kitap için test zaten mevcutsa o halde success olarak devam et
                if (examDto.BookTestId is null || examDto.BookTestId == 0)
                {
                    if (string.IsNullOrWhiteSpace(examDto.NewBookTestName))
                    {
                        // return BadRequest(new { error = "Kipta Test seçilmedi!" });
                        return new ExamSavedDto
                        {
                            Success = false,
                            Message = "Kipta Test seçilmedi!"
                        };
                    }
                    else
                    {
                        book.BookTests.Add(new BookTest
                        {
                            Name = examDto.NewBookTestName,
                            BookId = book.Id
                        });
                    }
                    await _context.SaveChangesAsync();
                    examDto.BookTestId = book.BookTests.First(bt => bt.Name == examDto.NewBookTestName).Id;
                }
            }


            Worksheet? examination;
            var newId = 0;
            if (examDto.Id > 0)
            {
                examination = await _context.Worksheets.FindAsync(examDto.Id);

                if (examination == null)
                {
                    // return NotFound(new { error = "Test bulunamadı!" });
                    return new ExamSavedDto
                    {
                        Success = false,
                        Message = "Test bulunamadı!"
                    };
                }

                examination.Name = examDto.Name;
                examination.Description = examDto.Description;
                examination.GradeId = examDto.GradeId;
                examination.MaxDurationSeconds = examDto.MaxDurationSeconds;
                examination.IsPracticeTest = examDto.IsPracticeTest;
                examination.Subtitle = examDto.Subtitle;
                examination.BookTestId = book.BookTests.FirstOrDefault(bt => bt.Id == examDto.BookTestId)?.Id ?? book.BookTests.First().Id;
                examination.SubjectId = examDto.SubjectId;
                examination.TopicId = examDto.TopicId;
                examination.SubTopicId = examDto.SubTopicId;

                // 📌 Eğer yeni resim varsa, güncelle
                if (!string.IsNullOrEmpty(examDto.ImageUrl) &&
                    _imageHelper.IsBase64String(examDto.ImageUrl))
                {
                    byte[] imageBytes = Convert.FromBase64String(examDto.ImageUrl.Split(',')[1]);
                    await using var imageStream = new MemoryStream(imageBytes);
                    examination.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"{Guid.NewGuid()}.jpg", "exams");
                }

                _context.Worksheets.Update(examination);
            }
            else
            {
                // 📌 Yeni Soru Oluştur (INSERT)
                var bookTestId = book.BookTests.FirstOrDefault(bt => bt.Id == examDto.BookTestId)?.Id ?? book.BookTests.First().Id;
                var existingExam = await _context.Worksheets
                    .FirstOrDefaultAsync(e => e.Name == examDto.Name && e.BookTestId == bookTestId);
                // 📌 Eğer yeni resim varsa, güncelle
                var newImageUrl = string.Empty;
                if (!string.IsNullOrEmpty(examDto.ImageUrl) &&
                    _imageHelper.IsBase64String(examDto.ImageUrl))
                {
                    byte[] imageBytes = Convert.FromBase64String(examDto.ImageUrl.Split(',')[1]);
                    await using var imageStream = new MemoryStream(imageBytes);
                    newImageUrl = await _minioService.UploadFileAsync(imageStream, $"{Guid.NewGuid()}.jpg", "exams");
                }

                if (existingExam != null)
                {
                    // Update existing exam
                    existingExam.Name = examDto.Name;
                    existingExam.Description = examDto.Description;
                    existingExam.GradeId = examDto.GradeId;
                    existingExam.MaxDurationSeconds = examDto.MaxDurationSeconds;
                    existingExam.IsPracticeTest = examDto.IsPracticeTest;
                    existingExam.Subtitle = examDto.Subtitle;
                    existingExam.BookTestId = bookTestId;
                    existingExam.SubjectId = examDto.SubjectId;
                    existingExam.TopicId = examDto.TopicId;
                    existingExam.SubTopicId = examDto.SubTopicId;

                    if (!string.IsNullOrEmpty(newImageUrl))
                    {
                        existingExam.ImageUrl = newImageUrl;
                    }
                    examination = existingExam;
                    _context.Worksheets.Update(examination);
                }
                else
                {
                    // Create new exam
                    examination = new Worksheet
                    {
                        Name = examDto.Name,
                        Description = examDto.Description,
                        GradeId = examDto.GradeId,
                        MaxDurationSeconds = examDto.MaxDurationSeconds,
                        IsPracticeTest = examDto.IsPracticeTest,
                        Subtitle = examDto.Subtitle,
                        BookTestId = bookTestId,
                        SubjectId = examDto.SubjectId,
                        TopicId = examDto.TopicId,
                        SubTopicId = examDto.SubTopicId,
                    };
                    if (!string.IsNullOrEmpty(newImageUrl))
                    {
                        examination.ImageUrl = newImageUrl;
                    }
                    _context.Worksheets.Add(examination);

                }
            }

            _context.SetCurrentUser(userId);
            await _context.SaveChangesAsync(); // burada audit çalışır
            return new ExamSavedDto
            {
                Success = true,
                Message = examDto.Id > 0 ?
                            "Test başarıyla güncellendi!" : "Test başarıyla kaydedildi!",
                ExamId = examination.Id,
                BookId = book?.Id,
                BookTestId = examination.BookTestId
            };
        }
        catch (Exception ex)
        {
            return new ExamSavedDto
            {
                Success = false,
                Message = ex.Message
            };
            // return BadRequest(new { error = ex.Message });
        }
    }

    public async Task<BulkExamResultDto> CreateBulkExamsAsync(BulkExamCreateDto bulkExamDto, int userId)
    {
        var result = new BulkExamResultDto
        {
            Success = true,
            Message = "Bulk exam creation completed"
        };

        var successfulExams = new List<ExamSavedDto>();
        var failedExams = new List<BulkExamErrorDto>();
        int rowNumber = 1;

        foreach (var examItem in bulkExamDto.Exams)
        {
            try
            {
                // Convert BulkExamItemDto to ExamDto
                var examDto = new ExamDto
                {
                    Name = examItem.Name,
                    Description = examItem.Description,
                    GradeId = examItem.GradeId,
                    MaxDurationSeconds = examItem.MaxDurationSeconds,
                    IsPracticeTest = examItem.IsPracticeTest,
                    Subtitle = examItem.Subtitle,
                    BadgeText = examItem.BadgeText,
                    BookTestId = examItem.BookTestId,
                    BookId = examItem.BookId,
                    NewBookName = examItem.NewBookName,
                    NewBookTestName = examItem.NewBookTestName,
                    SubjectId = examItem.SubjectId,
                    TopicId = examItem.TopicId,
                    SubTopicId = examItem.SubTopicId
                };

                // Use existing CreateOrUpdateAsync method
                var savedExam = await CreateOrUpdateAsync(examDto, userId);

                if (savedExam.Success)
                {
                    successfulExams.Add(savedExam);
                }
                else
                {
                    failedExams.Add(new BulkExamErrorDto
                    {
                        ExamName = examItem.Name,
                        ErrorMessage = savedExam.Message,
                        RowNumber = rowNumber
                    });
                }
            }
            catch (Exception ex)
            {
                failedExams.Add(new BulkExamErrorDto
                {
                    ExamName = examItem.Name,
                    ErrorMessage = ex.Message,
                    RowNumber = rowNumber
                });
            }

            rowNumber++;
        }

        result.SuccessfulExams = successfulExams;
        result.FailedExams = failedExams;
        result.TotalProcessed = bulkExamDto.Exams.Count;
        result.SuccessCount = successfulExams.Count;
        result.FailureCount = failedExams.Count;

        if (failedExams.Any())
        {
            result.Success = false;
            result.Message = $"Processed {result.TotalProcessed} exams: {result.SuccessCount} successful, {result.FailureCount} failed";
        }

        return result;
    }

    public async Task<ResponseBaseDto> DeleteWorksheetAsync(int worksheetId, int userId)
    {
        var response = new ResponseBaseDto();

        var worksheet = await _context.Worksheets.FindAsync(worksheetId);

        if (worksheet == null || worksheet.IsDeleted)
        {
            response.Success = false;
            response.Message = "Worksheet bulunamadı.";
            return response;
        }

        worksheet.IsDeleted = true;
        worksheet.DeleteTime = DateTime.UtcNow;
        worksheet.DeleteUserId = userId;

        await _context.SaveChangesAsync();

        response.Success = true;
        response.Message = "Worksheet başarıyla silindi.";

        return response;
    }
}
