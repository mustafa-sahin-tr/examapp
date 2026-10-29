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
/// Creating, updating and deleting worksheets and their background image.
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

    /// <summary>
    /// issue #573: istenen tür (null = mevcut korunur; yeni kayıtta Test) uygulanır. Etkinlik süresiz ve "çalışma testi"
    /// değildir: <c>MaxDurationSeconds = 0</c>, <c>IsPracticeTest = false</c> zorlanır (istemci ne gönderirse göndersin).
    /// </summary>
    internal static void ApplyKind(Worksheet worksheet, WorksheetKind? requested)
    {
        if (requested.HasValue)
            worksheet.Kind = requested.Value;
        if (worksheet.Kind == WorksheetKind.Activity)
        {
            worksheet.MaxDurationSeconds = 0;
            worksheet.IsPracticeTest = false;
        }
    }

    /// <summary>
    /// issue #573: türü değişen (Test ⇄ Activity) ve en az bir oturumu olan (soft-delete edilmiş dahil — IgnoreQueryFilters)
    /// sayfa → 409 <see cref="WorksheetAuthoringErrorCodes.WorksheetKindLocked"/>; aksi halde null. Oturumun puanlama/kontrol
    /// kuralları türe bağlı olduğundan (skor listeleri, kontrol ucu, end-test) başlamış oturumun türü değişmemeli.
    /// </summary>
    private async Task<ExamSavedDto?> KindChangeLockedAsync(Worksheet worksheet, WorksheetKind? requested)
    {
        if (!IsKindChange(worksheet, requested))
            return null;

        // issue #583: açık uçlu maddesi olan etkinlik Test'e çevrilemez (409).
        if (requested == WorksheetKind.Test && await OpenTextTestGuard.WorksheetHasOpenTextAsync(_context, worksheet.Id))
        {
            return new ExamSavedDto
            {
                Success = false,
                Conflict = true,
                ErrorCode = WorksheetAuthoringErrorCodes.OpenTextNotAllowedInTest,
                Message = _localizer[OpenTextTestGuard.MessageKey]
            };
        }

        return await HasAnyInstanceAsync(worksheet.Id) ? KindLockedResult() : null;
    }

    private static bool IsKindChange(Worksheet worksheet, WorksheetKind? requested)
        => requested.HasValue && requested.Value != worksheet.Kind;

    private Task<bool> HasAnyInstanceAsync(int worksheetId)
        => _context.TestInstances.IgnoreQueryFilters().AnyAsync(ti => ti.WorksheetId == worksheetId);

    /// <summary>
    /// issue #573: tür değişen sayfanın son kaydı. Transaction + <see cref="WorksheetKindLock"/> (StartTestAsync'in oturum
    /// INSERT'iyle aynı kilit) altında oturum yeniden kontrol edilir: kilitsiz ön kontrol ile yazım arasında açılan oturum
    /// türü değiştirmeyi engeller (409, hiçbir şey yazılmaz). Yeniden denemede izlenen değişiklikler korunur
    /// (<c>acceptAllChangesOnSuccess: false</c>, commit sonrası <c>AcceptAllChanges</c>). null = kaydedildi.
    /// </summary>
    private async Task<ExamSavedDto?> SaveKindChangeUnderLockAsync(int worksheetId)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        var locked = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            await _context.Database.AcquireWorksheetKindLockAsync(worksheetId);
            if (await HasAnyInstanceAsync(worksheetId))
            {
                await tx.RollbackAsync();
                return true;
            }

            await _context.SaveChangesAsync(acceptAllChangesOnSuccess: false); // burada audit çalışır
            await tx.CommitAsync();
            return false;
        });

        if (locked)
        {
            _context.ChangeTracker.Clear(); // yazılmayan değişiklikler bu isteğin sonraki işlerine sızmasın
            return KindLockedResult();
        }

        _context.ChangeTracker.AcceptAllChanges();
        return null;
    }

    private ExamSavedDto KindLockedResult()
        => new ExamSavedDto
        {
            Success = false,
            Conflict = true,
            ErrorCode = WorksheetAuthoringErrorCodes.WorksheetKindLocked,
            Message = _localizer["worksheets.authoring.kindLocked"]
        };

    /// <summary>Kitabın istenen testi, yoksa ilk testi; kitapsız sayfada (#610) null.</summary>
    private static int? ResolveBookTestId(Book? book, int? bookTestId)
        => book is null ? null : book.BookTests.FirstOrDefault(bt => bt.Id == bookTestId)?.Id ?? book.BookTests.First().Id;

    public async Task<ExamSavedDto> CreateOrUpdateAsync(ExamDto examDto, int userId, bool isAdmin)
    {
        if (examDto == null)
        {
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
            // issue #610: kitap isteğe bağlı — hiç kitap verilmezse sayfa kitapsız (BookTestId null) oluşur/kaydedilir.
            // Soru tuvalinin hızlı oluşturma formu kitap sormaz; eskiden 200 + Success=false dönüp form sessizce kapanıyordu.
            var hasBook = examDto.BookId is > 0 || !string.IsNullOrWhiteSpace(examDto.NewBookName);

            if (hasBook && (examDto.BookTestId is null or 0) && string.IsNullOrWhiteSpace(examDto.NewBookTestName))
            {
                // return BadRequest(new { error = "Kipta Test seçilmedi!" });
                return new ExamSavedDto
                {
                    Success = false,
                    Message = _localizer["worksheets.authoring.bookTestNotSelected"]
                };
            }

            Book? book = null;
            if (!hasBook)
            {
                // kitapsız sayfa: kitap / kitap testi oluşturulmaz
            }
            else if (examDto.BookId is null || examDto.BookId == 0)
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
            // issue #573: tür değişen var olan sayfa — son kayıt tür kilidinin altında yeniden kontrolle yapılır.
            int? kindChangeWorksheetId = null;
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

                if (await KindChangeLockedAsync(examination, examDto.Kind) is { } kindLocked)
                    return kindLocked;
                if (IsKindChange(examination, examDto.Kind))
                    kindChangeWorksheetId = examination.Id;

                examination.Name = examDto.Name;
                examination.Description = examDto.Description;
                examination.GradeId = examDto.GradeId;
                examination.MaxDurationSeconds = examDto.MaxDurationSeconds;
                examination.IsPracticeTest = examDto.IsPracticeTest;
                ApplyKind(examination, examDto.Kind);
                // issue #105: null = mevcut değer korunur.
                if (examDto.CommentsEnabled.HasValue)
                    examination.CommentsEnabled = examDto.CommentsEnabled.Value;
                examination.Subtitle = examDto.Subtitle;
                examination.BookTestId = ResolveBookTestId(book, examDto.BookTestId);
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
                var bookTestId = ResolveBookTestId(book, examDto.BookTestId);
                // Aynı ad + kitap testi var olan kayda yazılır; kitapsız sayfada (#610) ad tek başına kimlik değil, hep yeni kayıt.
                var existingExam = bookTestId is null
                    ? null
                    : await _context.Worksheets
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

                    if (await KindChangeLockedAsync(existingExam, examDto.Kind) is { } kindLocked)
                        return kindLocked;
                    if (IsKindChange(existingExam, examDto.Kind))
                        kindChangeWorksheetId = existingExam.Id;

                    // Update existing exam
                    existingExam.Name = examDto.Name;
                    existingExam.Description = examDto.Description;
                    existingExam.GradeId = examDto.GradeId;
                    existingExam.MaxDurationSeconds = examDto.MaxDurationSeconds;
                    existingExam.IsPracticeTest = examDto.IsPracticeTest;
                    ApplyKind(existingExam, examDto.Kind);
                    if (examDto.CommentsEnabled.HasValue)
                        existingExam.CommentsEnabled = examDto.CommentsEnabled.Value;
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
                        // issue #105: varsayılan açık.
                        CommentsEnabled = examDto.CommentsEnabled ?? true,
                        Subtitle = examDto.Subtitle,
                        BookTestId = bookTestId,
                        SubjectId = examDto.SubjectId,
                        TopicId = examDto.TopicId,
                        SubTopicId = examDto.SubTopicId,
                    };
                    ApplyKind(examination, examDto.Kind);
                    if (!string.IsNullOrEmpty(newImageUrl))
                    {
                        examination.ImageUrl = newImageUrl;
                    }
                    _context.Worksheets.Add(examination);

                }
            }

            _context.SetCurrentUser(userId);
            if (kindChangeWorksheetId is { } lockedWorksheetId)
            {
                // issue #573: tür değişimi atomik — görseller yukarıda yüklendi; kilit (start-test ile aynı) altında oturum yeniden
                // kontrol edilir, varsa hiçbir şey yazılmaz (409).
                if (await SaveKindChangeUnderLockAsync(lockedWorksheetId) is { } raceLocked)
                    return raceLocked;
            }
            else
            {
                await _context.SaveChangesAsync(); // burada audit çalışır
            }
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

        // issue #583: (eski/kaçak veride) açık uçlu maddesi olan test kopyalanmaz — kopya da test olurdu.
        if (source.Kind == WorksheetKind.Test && await OpenTextTestGuard.WorksheetHasOpenTextAsync(_context, source.Id, ct))
        {
            result.Success = false;
            result.ErrorCode = OpenTextTestGuard.ErrorCode;
            result.Message = _localizer[OpenTextTestGuard.MessageKey];
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
            Kind = source.Kind, // issue #573: kopya kaynağın türünü taşır (etkinlik kopyası etkinliktir)
            CommentsEnabled = true, // issue #105 (review D8): kopyalayan yeni sahip — ayar devralınmaz, varsayılan açık başlar
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
