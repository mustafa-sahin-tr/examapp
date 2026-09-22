using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

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

    // Client'a donen mesajlar (ResponseBaseDto.Message) buradan gelir (issue #184). Log mesajlari
    // cevrilmez. DI her zaman gercek localizer'i verir; parametre yalnizca DI'siz (birim test) icin opsiyonel.
    private readonly IStringLocalizer<Messages> _localizer;

    public WorksheetAuthoringService(AppDbContext context, ImageHelper imageHelper, IMinIoService minioService, IStringLocalizer<Messages>? localizer = null)
    {
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
        _context = context;
        _imageHelper = imageHelper;
        _minioService = minioService;
    }

    /// <summary>
    /// Authoring uçlarındaki "hiç görünmüyorsa NotFound" kapısı: WorksheetAccess.CanView + issue #191 okul
    /// çifti (SchoolOnly için Teachers'tan çözülür; diğer durumlarda sorgu atılmaz).
    /// </summary>
    private async Task<bool> CanViewAsync(Worksheet worksheet, int userId, bool isAdmin, CancellationToken ct = default)
    {
        var (ownerSchoolId, requesterSchoolId) = await _context.ResolveSchoolContextAsync(worksheet, userId, isAdmin, ct);
        return WorksheetAccess.CanView(worksheet.CreateUserId, userId, isAdmin, worksheet.TeacherSharing, worksheet.StudentVisibility,
            requesterSchoolId, ownerSchoolId);
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
                Message = _localizer["worksheets.authoring.background.imageRequired"]
            };
        }

        if (file.Length > MaxBackgroundImageBytes)
        {
            return new UpdateWorksheetBackgroundImageDto
            {
                Success = false,
                Message = _localizer["worksheets.authoring.background.tooLarge"]
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
                Message = _localizer["worksheets.authoring.background.unsupportedType"]
            };
        }

        // Yetki modeli: "sahibi VEYA admin". Legacy (CreateUserId 0/null) kayıtlar owner sayılmaz.
        // issue #11: worksheet caller'a hiç görünmüyorsa (Private/legacy, başkasının) varlık sızmasın
        // diye NotFound; görünüyor (Public* paylaşım) ama düzenleme yetkisi yoksa Forbidden (403).
        var worksheet = await _context.Worksheets
            .FirstOrDefaultAsync(w => w.Id == worksheetId && !w.IsDeleted);
        if (worksheet == null || !await CanViewAsync(worksheet, userId, isAdmin))
        {
            return new UpdateWorksheetBackgroundImageDto
            {
                Success = false,
                NotFound = true,
                Message = _localizer["worksheets.authoring.background.worksheetNotFound"]
            };
        }

        if (!WorksheetAccess.CanModify(worksheet.CreateUserId, userId, isAdmin))
        {
            return new UpdateWorksheetBackgroundImageDto
            {
                Success = false,
                Forbidden = true,
                Message = _localizer["worksheets.authoring.background.editForbidden"]
            };
        }

        await using var stream = file.OpenReadStream();
        var detectedContentType = await DetectImageContentTypeAsync(stream);
        if (detectedContentType == null || !string.Equals(detectedContentType, declaredContentType, StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateWorksheetBackgroundImageDto
            {
                Success = false,
                Message = _localizer["worksheets.authoring.background.invalidContent"]
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
            Message = _localizer["worksheets.authoring.background.updated"],
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
                Message = _localizer["worksheets.authoring.examInfoMissing"]
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
                    Message = _localizer["worksheets.authoring.bookNotSelected"]
                };
            }

            if (examDto.BookTestId == 0 && string.IsNullOrWhiteSpace(examDto.NewBookTestName))
            {
                // return BadRequest(new { error = "Kipta Test seçilmedi!" });
                return new ExamSavedDto
                {
                    Success = false,
                    Message = _localizer["worksheets.authoring.bookTestNotSelected"]
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
                        Message = _localizer["worksheets.authoring.bookNotSelected"]
                    };
                }

                if (string.IsNullOrWhiteSpace(examDto.NewBookTestName))
                {
                    return new ExamSavedDto
                    {
                        Success = false,
                        Message = _localizer["worksheets.authoring.bookTestNotSelected"]
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
                        Message = _localizer["worksheets.authoring.bookNotFound"]
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
                            Message = _localizer["worksheets.authoring.bookTestNotSelected"]
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
                        Message = _localizer["worksheets.authoring.testNotFound"]
                    };
                }

                // Yetki modeli: "sahibi VEYA admin". Legacy kayıtlar owner sayılmaz.
                // issue #11: hiç görünmüyorsa (Private/legacy, başkasının) NotFound; görünüyor
                // (Public* paylaşım) ama düzenleme yetkisi yoksa Forbidden (403) — read-only açılır.
                if (!await CanViewAsync(examination, userId, isAdmin))
                {
                    return new ExamSavedDto
                    {
                        Success = false,
                        NotFound = true,
                        Message = _localizer["worksheets.authoring.testNotFound"]
                    };
                }

                if (!WorksheetAccess.CanModify(examination.CreateUserId, userId, isAdmin))
                {
                    return new ExamSavedDto
                    {
                        Success = false,
                        Forbidden = true,
                        Message = _localizer["worksheets.authoring.editForbidden"]
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
                    if (!await CanViewAsync(existingExam, userId, isAdmin))
                    {
                        return new ExamSavedDto
                        {
                            Success = false,
                            NotFound = true,
                            Message = _localizer["worksheets.authoring.testNotFound"]
                        };
                    }

                    if (!WorksheetAccess.CanModify(existingExam.CreateUserId, userId, isAdmin))
                    {
                        return new ExamSavedDto
                        {
                            Success = false,
                            Forbidden = true,
                            Message = _localizer["worksheets.authoring.editForbidden"]
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
                            _localizer["worksheets.authoring.updated"] : _localizer["worksheets.authoring.created"],
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
            Message = _localizer["worksheets.authoring.bulkCompleted"]
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
        if (worksheet == null || worksheet.IsDeleted || !await CanViewAsync(worksheet, userId, isAdmin))
        {
            response.Success = false;
            response.NotFound = true;
            response.Message = _localizer["worksheets.authoring.worksheetNotFound"];
            return response;
        }

        if (!WorksheetAccess.CanModify(worksheet.CreateUserId, userId, isAdmin))
        {
            response.Success = false;
            response.Forbidden = true;
            response.Message = _localizer["worksheets.authoring.deleteForbidden"];
            return response;
        }

        worksheet.IsDeleted = true;
        worksheet.DeleteTime = DateTime.UtcNow;
        worksheet.DeleteUserId = userId;

        await _context.SaveChangesAsync();

        response.Success = true;
        response.Message = _localizer["worksheets.authoring.deleted"];

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
            response.Message = _localizer["worksheets.authoring.worksheetNotFound"];
            return response;
        }

        // Bu endpoint için (issue #10) yetkisiz çağrı 403 döner — diğer authoring metotlarındaki
        // "varlık sızmasın" amaçlı NotFound kalıbından bilinçli olarak farklı.
        if (!WorksheetAccess.CanModify(worksheet.CreateUserId, userId, isAdmin))
        {
            response.Success = false;
            response.Forbidden = true;
            response.Message = _localizer["worksheets.authoring.visibilityForbidden"];
            return response;
        }

        // issue #191: SchoolOnly, sahibin bir okulu olmasını gerektirir — okul, worksheet SAHİBİNİN
        // Teachers.SchoolId'sinden (sunucu tarafı) çözülür; istekçinin (admin olabilir) okulu değil.
        // Okulsuz/bağımsız sahip veya legacy (owner'sız) worksheet için 400: aksi halde admin dışında
        // kimsenin göremeyeceği bir sınav oluşurdu.
        int? ownerSchoolId = null;
        if (dto.TeacherSharing == WorksheetTeacherSharing.SchoolOnly)
        {
            ownerSchoolId = worksheet.CreateUserId.HasValue && worksheet.CreateUserId.Value > 0
                ? await _context.ResolveTeacherSchoolIdAsync(worksheet.CreateUserId.Value)
                : null;
            if (!ownerSchoolId.HasValue)
            {
                response.Success = false;
                response.Message = _localizer["worksheets.authoring.schoolOnlyRequiresSchool"];
                return response;
            }
        }

        // PublicView/PublicAssignable -> Private geçişinde mevcut WorksheetAssignment kayıtları
        // (başka öğretmenler tarafından yapılmış olsa dahi) bilinçli olarak iptal edilmiyor.
        worksheet.TeacherSharing = dto.TeacherSharing;
        worksheet.StudentVisibility = dto.StudentVisibility;

        // issue #13: sınav Private'a çekilince atama izni akışındaki tüm aktif grant'lar ve
        // bekleyen talepler sessizce iptal edilir (outbox event üretilmez — issue bildirim demiyor).
        // issue #191: SchoolOnly'ye çekilince aynı iptal yalnızca sahibin okulu DIŞINDAKİ (farklı okul
        // veya okulsuz) öğretmenlerin grant/talepleri için uygulanır — aynı okul zaten atayabildiği için
        // grant'i zararsızdır ve korunur (Teachers alt sorgusu; sonradan süzme değil).
        if (dto.TeacherSharing is WorksheetTeacherSharing.Private or WorksheetTeacherSharing.SchoolOnly)
        {
            var now = DateTime.UtcNow;

            var grantQuery = _context.WorksheetAccessGrants
                .Where(g => g.WorksheetId == worksheetId && g.RevokedAt == null);
            var requestQuery = _context.WorksheetAccessRequests
                .Where(r => r.WorksheetId == worksheetId && r.Status == WorksheetAccessRequestStatus.Pending);

            if (dto.TeacherSharing == WorksheetTeacherSharing.SchoolOnly)
            {
                var schoolId = ownerSchoolId!.Value;
                grantQuery = grantQuery.Where(g =>
                    !_context.Teachers.Any(te => te.UserId == g.TeacherUserId && te.SchoolId == schoolId));
                requestQuery = requestQuery.Where(r =>
                    !_context.Teachers.Any(te => te.UserId == r.RequesterUserId && te.SchoolId == schoolId));
            }

            var activeGrants = await grantQuery.ToListAsync();
            foreach (var grant in activeGrants)
            {
                grant.RevokedAt = now;
            }

            var pendingRequests = await requestQuery.ToListAsync();
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
        response.Message = _localizer["worksheets.authoring.visibilityUpdated"];
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
        // issue #191: SchoolOnly kaynak yalnızca aynı okuldan kopyalanabilir (okul çifti DB'den).
        var (sourceOwnerSchoolId, copierSchoolId) = source != null
            ? await _context.ResolveSchoolContextAsync(source, userId, isAdmin, ct)
            : (null, null);
        if (source == null ||
            !WorksheetAccess.CanCopy(source.CreateUserId, userId, isAdmin, source.TeacherSharing, source.StudentVisibility,
                copierSchoolId, sourceOwnerSchoolId))
        {
            result.Success = false;
            result.NotFound = true;
            result.Message = _localizer["worksheets.authoring.worksheetNotFound"];
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
        result.Message = _localizer["worksheets.authoring.copied"];
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
