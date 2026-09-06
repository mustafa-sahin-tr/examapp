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

    // Allow-list: SVG deliberately excluded (inline-served SVG = stored XSS vector).
    private const long MaxBackgroundImageBytes = 2 * 1024 * 1024; // 2 MB

    private static readonly Dictionary<string, string[]> AllowedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = new[] { ".png" },
        ["image/jpeg"] = new[] { ".jpg", ".jpeg" },
        ["image/webp"] = new[] { ".webp" },
    };

    public async Task<UpdateWorksheetBackgroundImageDto> UpdateWorksheetBackgroundImageAsync(int worksheetId, IFormFile file, int userId, bool isAdmin)
    {
        if (file == null || file.Length == 0)
        {
            return new UpdateWorksheetBackgroundImageDto
            {
                Success = false,
                Message = "Yüklemek için geçerli bir görsel seçin."
            };
        }

        if (file.Length > MaxBackgroundImageBytes)
        {
            return new UpdateWorksheetBackgroundImageDto
            {
                Success = false,
                Message = "Görsel boyutu en fazla 2 MB olabilir."
            };
        }

        var declaredContentType = file.ContentType?.Trim() ?? string.Empty;
        var extension = Path.GetExtension(file.FileName)?.Trim() ?? string.Empty;

        if (!AllowedImageTypes.TryGetValue(declaredContentType, out var allowedExtensions) ||
            !allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return new UpdateWorksheetBackgroundImageDto
            {
                Success = false,
                Message = "Sadece PNG, JPEG veya WEBP görselleri yüklenebilir."
            };
        }

        // Yetki modeli: "sahibi VEYA admin". Legacy (CreateUserId 0/null) kayıtlar owner sayılmaz.
        // issue #11: worksheet caller'a hiç görünmüyorsa (Private/legacy, başkasının) varlık sızmasın
        // diye NotFound; görünüyor (Public* paylaşım) ama düzenleme yetkisi yoksa Forbidden (403).
        var worksheet = await _context.Worksheets
            .FirstOrDefaultAsync(w => w.Id == worksheetId && !w.IsDeleted);
        if (worksheet == null || !WorksheetAccess.CanView(worksheet.CreateUserId, userId, isAdmin, worksheet.TeacherSharing, worksheet.StudentVisibility))
        {
            return new UpdateWorksheetBackgroundImageDto
            {
                Success = false,
                NotFound = true,
                Message = "Worksheet bulunamadı."
            };
        }

        if (!WorksheetAccess.CanModify(worksheet.CreateUserId, userId, isAdmin))
        {
            return new UpdateWorksheetBackgroundImageDto
            {
                Success = false,
                Forbidden = true,
                Message = "Bu worksheet'i düzenleme yetkiniz yok."
            };
        }

        await using var stream = file.OpenReadStream();
        var detectedContentType = await DetectImageContentTypeAsync(stream);
        if (detectedContentType == null || !string.Equals(detectedContentType, declaredContentType, StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateWorksheetBackgroundImageDto
            {
                Success = false,
                Message = "Dosya içeriği geçerli bir PNG, JPEG veya WEBP görseli değil."
            };
        }
        if (stream.CanSeek) stream.Position = 0;

        var previousImageUrl = worksheet.ImageUrl;
        var fileExtension = AllowedImageTypes[detectedContentType][0];
        var fileName = $"{worksheetId}-background{fileExtension}";
        var uploadedPath = await _minioService.UploadFileAsync(stream, fileName, "worksheets", detectedContentType);

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

    public async Task<ExamSavedDto> CreateOrUpdateAsync(ExamDto examDto, int userId, bool isAdmin)
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
                        NotFound = true,
                        Message = "Test bulunamadı!"
                    };
                }

                // Yetki modeli: "sahibi VEYA admin". Legacy kayıtlar owner sayılmaz.
                // issue #11: hiç görünmüyorsa (Private/legacy, başkasının) NotFound; görünüyor
                // (Public* paylaşım) ama düzenleme yetkisi yoksa Forbidden (403) — read-only açılır.
                if (!WorksheetAccess.CanView(examination.CreateUserId, userId, isAdmin, examination.TeacherSharing, examination.StudentVisibility))
                {
                    return new ExamSavedDto
                    {
                        Success = false,
                        NotFound = true,
                        Message = "Test bulunamadı!"
                    };
                }

                if (!WorksheetAccess.CanModify(examination.CreateUserId, userId, isAdmin))
                {
                    return new ExamSavedDto
                    {
                        Success = false,
                        Forbidden = true,
                        Message = "Bu testi düzenleme yetkiniz yok."
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
                    // Aynı isim/kitap-test ile mevcut kayda denk gelen "create" isteği aslında bir update'e
                    // dönüşüyor — yabancı bir kaydı ezmemek için burada da yetki kapısı koy.
                    // issue #11: hiç görünmüyorsa NotFound; görünüyor (Public* paylaşım) ama düzenleme
                    // yetkisi yoksa Forbidden.
                    if (!WorksheetAccess.CanView(existingExam.CreateUserId, userId, isAdmin, existingExam.TeacherSharing, existingExam.StudentVisibility))
                    {
                        return new ExamSavedDto
                        {
                            Success = false,
                            NotFound = true,
                            Message = "Test bulunamadı!"
                        };
                    }

                    if (!WorksheetAccess.CanModify(existingExam.CreateUserId, userId, isAdmin))
                    {
                        return new ExamSavedDto
                        {
                            Success = false,
                            Forbidden = true,
                            Message = "Bu testi düzenleme yetkiniz yok."
                        };
                    }

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

    public async Task<BulkExamResultDto> CreateBulkExamsAsync(BulkExamCreateDto bulkExamDto, int userId, bool isAdmin)
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
                var savedExam = await CreateOrUpdateAsync(examDto, userId, isAdmin);

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

    public async Task<ResponseBaseDto> DeleteWorksheetAsync(int worksheetId, int userId, bool isAdmin)
    {
        var response = new ResponseBaseDto();

        var worksheet = await _context.Worksheets
            .FirstOrDefaultAsync(w => w.Id == worksheetId);

        // Yetki modeli: "sahibi VEYA admin". Legacy (CreateUserId 0/null) kayıtlar owner sayılmaz.
        // issue #11: yok/silinmiş/hiç görünmeyen (Private, başkasının) → NotFound (varlık sızmasın).
        // Görünen (Public* paylaşım) ama sahibi/admin değilse → Forbidden (403).
        if (worksheet == null || worksheet.IsDeleted ||
            !WorksheetAccess.CanView(worksheet.CreateUserId, userId, isAdmin, worksheet.TeacherSharing, worksheet.StudentVisibility))
        {
            response.Success = false;
            response.NotFound = true;
            response.Message = "Worksheet bulunamadı.";
            return response;
        }

        if (!WorksheetAccess.CanModify(worksheet.CreateUserId, userId, isAdmin))
        {
            response.Success = false;
            response.Forbidden = true;
            response.Message = "Bu worksheet'i silme yetkiniz yok.";
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

    public async Task<ResponseBaseDto> UpdateVisibilityAsync(int worksheetId, UpdateWorksheetVisibilityDto dto, int userId, bool isAdmin)
    {
        var response = new ResponseBaseDto();

        var worksheet = await _context.Worksheets
            .FirstOrDefaultAsync(w => w.Id == worksheetId);

        if (worksheet == null || worksheet.IsDeleted)
        {
            response.Success = false;
            response.NotFound = true;
            response.Message = "Worksheet bulunamadı.";
            return response;
        }

        // Bu endpoint için (issue #10) yetkisiz çağrı 403 döner — diğer authoring metotlarındaki
        // "varlık sızmasın" amaçlı NotFound kalıbından bilinçli olarak farklı.
        if (!WorksheetAccess.CanModify(worksheet.CreateUserId, userId, isAdmin))
        {
            response.Success = false;
            response.Forbidden = true;
            response.Message = "Bu worksheet'in görünürlüğünü değiştirme yetkiniz yok.";
            return response;
        }

        // PublicView/PublicAssignable -> Private geçişinde mevcut WorksheetAssignment kayıtları
        // (başka öğretmenler tarafından yapılmış olsa dahi) bilinçli olarak iptal edilmiyor.
        worksheet.TeacherSharing = dto.TeacherSharing;
        worksheet.StudentVisibility = dto.StudentVisibility;

        // issue #13: sınav Private'a çekilince atama izni akışındaki tüm aktif grant'lar ve
        // bekleyen talepler sessizce iptal edilir (outbox event üretilmez — issue bildirim demiyor).
        if (dto.TeacherSharing == WorksheetTeacherSharing.Private)
        {
            var now = DateTime.UtcNow;

            var activeGrants = await _context.WorksheetAccessGrants
                .Where(g => g.WorksheetId == worksheetId && g.RevokedAt == null)
                .ToListAsync();
            foreach (var grant in activeGrants)
            {
                grant.RevokedAt = now;
            }

            var pendingRequests = await _context.WorksheetAccessRequests
                .Where(r => r.WorksheetId == worksheetId && r.Status == WorksheetAccessRequestStatus.Pending)
                .ToListAsync();
            foreach (var pending in pendingRequests)
            {
                pending.Status = WorksheetAccessRequestStatus.Rejected;
                pending.DecisionAt = now;
                pending.DecidedByUserId = userId;
            }
        }

        _context.SetCurrentUser(userId);
        await _context.SaveChangesAsync();

        response.Success = true;
        response.ObjectId = worksheetId;
        response.Message = "Worksheet görünürlüğü güncellendi.";
        return response;
    }

    public async Task<CopyWorksheetResultDto> CopyWorksheetAsync(int sourceWorksheetId, int userId, bool isAdmin, CancellationToken ct = default)
    {
        var result = new CopyWorksheetResultDto();

        var source = await _context.Worksheets
            .Include(w => w.WorksheetQuestions)
            .FirstOrDefaultAsync(w => w.Id == sourceWorksheetId && !w.IsDeleted, ct);

        // issue #16: kopyalama yetkisi CanView ile aynı semantikte (CanCopy -> CanView).
        // Kaynak caller'a hiç görünmüyorsa varlık sızmasın diye NotFound.
        if (source == null ||
            !WorksheetAccess.CanCopy(source.CreateUserId, userId, isAdmin, source.TeacherSharing, source.StudentVisibility))
        {
            result.Success = false;
            result.NotFound = true;
            result.Message = "Worksheet bulunamadı.";
            return result;
        }

        var newWorksheet = new Worksheet
        {
            Name = source.Name,
            Description = source.Description,
            GradeId = source.GradeId,
            SubjectId = source.SubjectId,
            TopicId = source.TopicId,
            SubTopicId = source.SubTopicId,
            MaxDurationSeconds = source.MaxDurationSeconds,
            IsPracticeTest = source.IsPracticeTest,
            Subtitle = source.Subtitle,
            BadgeText = source.BadgeText,
            ImageUrl = source.ImageUrl, // aynı MinIO objesine referans
            BookTestId = source.BookTestId,
            TeacherSharing = WorksheetTeacherSharing.Private,
            StudentVisibility = WorksheetStudentVisibility.Normal,
            SourceWorksheetId = sourceWorksheetId,
            CreateUserId = userId
        };

        foreach (var q in source.WorksheetQuestions)
        {
            newWorksheet.WorksheetQuestions.Add(new WorksheetQuestion
            {
                Order = q.Order,
                QuestionId = q.QuestionId,
                Worksheet = newWorksheet
            });
        }

        _context.Worksheets.Add(newWorksheet);
        _context.SetCurrentUser(userId);
        await _context.SaveChangesAsync(ct);

        result.Success = true;
        result.Message = "Sınav kopyalandı.";
        result.ObjectId = newWorksheet.Id;
        result.WorksheetId = newWorksheet.Id;
        return result;
    }

    /// <summary>
    /// Verifies the actual file content via magic bytes and returns the canonical
    /// MIME type, or null if the content is not a supported raster image.
    /// SVG / XML / HTML content will not match any signature and is rejected.
    /// </summary>
    private static async Task<string?> DetectImageContentTypeAsync(Stream stream)
    {
        var header = new byte[12];
        var read = await ReadExactlyAsync(stream, header);
        if (stream.CanSeek) stream.Position = 0;
        if (read < 12) return null;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return "image/png";

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return "image/jpeg";

        // WEBP: "RIFF" .... "WEBP"
        if (header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
            header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
            return "image/webp";

        return null;
    }

    private static async Task<int> ReadExactlyAsync(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total));
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
