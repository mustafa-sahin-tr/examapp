using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace ExamApp.Api.Services;

public class ExamService : IExamService
{
    private readonly AppDbContext _context;

    private readonly ImageHelper _imageHelper;
    private readonly IMinIoService _minioService;
    public ExamService(AppDbContext context, ImageHelper imageHelper, IMinIoService minioService)
    {
        _imageHelper = imageHelper;
        _context = context;
        _minioService = minioService;
    }

    public async Task<Paged<WorksheetDto>> GetWorksheetsForTeacherAsync(ExamFilterDto dto, UserProfileDto userProfile)
    {
        var query = _context.Worksheets.AsQueryable();

        if (dto.id > 0)
        {
            query = query.Where(t => t.Id == dto.id);
        }
        else
        {
            if (dto.gradeIds != null && dto.gradeIds.Any())
                query = query.Where(t => dto.gradeIds.Contains(t.GradeId));

            if (dto.subjectIds != null && dto.subjectIds.Any())
                query = query.Where(t => t.SubjectId.HasValue && dto.subjectIds.Contains(t.SubjectId.Value));

            if (dto.bookTestId > 0)
                query = query.Where(t => t.BookTestId == dto.bookTestId);

            if (!string.IsNullOrEmpty(dto.search))
            {
                var normalizedSearch = dto.search.ToLower(new CultureInfo("tr-TR"));
                query = query.Where(t =>
                    EF.Functions.Like(t.Name.ToLower(), $"%{normalizedSearch}%") ||
                    (t.Subtitle != null && EF.Functions.Like(t.Subtitle.ToLower(), $"%{normalizedSearch}%")) ||
                    EF.Functions.Like(t.Description.ToLower(), $"%{normalizedSearch}%"));
            }
        }
        // Her worksheet için kaç benzersiz öğrenci instance oluşturmuş

        var totalCount = await query.CountAsync(); // Toplam kayıt sayısı
        var tests = await query
            .Include(t => t.BookTest)
                .ThenInclude(bt => bt.Book)
            .Include(t => t.WorksheetQuestions)
                .ThenInclude(tq => tq.Question)
            .OrderBy(t => t.Name) // Sıralama için
            .Skip((dto.pageNumber - 1) * dto.pageSize) // Sayfalama için
            .Take(dto.pageSize)
            .ToListAsync();

        var worksheetDtos = tests.Select(t =>
        {
            return new WorksheetDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                GradeId = t.GradeId,
                SubjectId = t.SubjectId,
                TopicId = t.TopicId,
                SubTopicId = t.SubTopicId,
                MaxDurationSeconds = t.MaxDurationSeconds,
                IsPracticeTest = t.IsPracticeTest,
                Subtitle = t.Subtitle,
                ImageUrl = t.ImageUrl,
                BadgeText = t.BadgeText,
                BookTestId = t.BookTestId,
                BookId = t.BookTest?.BookId,
                QuestionCount = t.WorksheetQuestions.Count()
            };
        }).ToList();

        return new Paged<WorksheetDto>
        {
            PageNumber = dto.pageNumber,
            PageSize = dto.pageSize,
            TotalCount = totalCount,
            Items = worksheetDtos
        };
    }

    public async Task<Paged<WorksheetDto>> GetWorksheetsForStudentsAsync(ExamFilterDto dto, StudentProfileDto studentProfile)
    {
        var instanceQuery = _context.TestInstances
           .Where(ti => ti.StudentId == studentProfile.Id)
           .Include(ti => ti.WorksheetInstanceQuestions)
           .Include(ti => ti.Worksheet)
           .AsQueryable();


        var query = _context.Worksheets.AsQueryable();

        if (dto.id > 0)
        {
            query = query.Where(t => t.Id == dto.id);
            instanceQuery = instanceQuery.Where(ti => ti.WorksheetId == dto.id);
        }
        else
        {
            if (dto.gradeIds != null && dto.gradeIds.Any())
                query = query.Where(t => dto.gradeIds.Contains(t.GradeId));
            else if (studentProfile != null && studentProfile.GradeId.HasValue)
                query = query.Where(t => t.GradeId == studentProfile.GradeId);

            if (dto.subjectIds != null && dto.subjectIds.Any())
                query = query.Where(t => t.SubjectId.HasValue && dto.subjectIds.Contains(t.SubjectId.Value));

            if (dto.bookTestId > 0)
                query = query.Where(t => t.BookTestId == dto.bookTestId);

            if (!string.IsNullOrEmpty(dto.search))
            {
                var normalizedSearch = dto.search.ToLower(new CultureInfo("tr-TR"));
                query = query.Where(t =>
                    EF.Functions.Like(t.Name.ToLower(), $"%{normalizedSearch}%") ||
                    (t.Subtitle != null && EF.Functions.Like(t.Subtitle.ToLower(), $"%{normalizedSearch}%")) ||
                    EF.Functions.Like(t.Description.ToLower(), $"%{normalizedSearch}%"));
            }
        }
        // Her worksheet için kaç benzersiz öğrenci instance oluşturmuş
        var worksheetStudentCounts = await _context.TestInstances
            .GroupBy(ti => ti.WorksheetId)
            .Select(g => new
            {
                WorksheetId = g.Key,
                UniqueStudentCount = g.Select(ti => ti.StudentId).Distinct().Count()
            })
            .ToDictionaryAsync(x => x.WorksheetId, x => x.UniqueStudentCount);

        var instances = await instanceQuery.ToListAsync(); // 🔥 Burada `ToListAsync()` çağırarak veriyi hafızaya alıyoruz

        var totalCount = await query.CountAsync(); // Toplam kayıt sayısı
        var tests = await query
            .Include(t => t.BookTest)
                .ThenInclude(bt => bt.Book)
            .Include(t => t.WorksheetQuestions)
                .ThenInclude(tq => tq.Question)
            .OrderBy(t => t.Name) // Sıralama için
            .Skip((dto.pageNumber - 1) * dto.pageSize) // Sayfalama için
            .Take(dto.pageSize)
            .ToListAsync();

        var worksheetDtos = tests.Select(t =>
        {
            var instance = instances.FirstOrDefault(i => i.WorksheetId == t.Id);

            InstanceSummaryDto? instanceDto = null;
            if (instance != null)
            {
                var correct = instance.WorksheetInstanceQuestions.Count(wiq =>
                    wiq.SelectedAnswerId != null &&
                    t.WorksheetQuestions.Any(wq => wq.Id == wiq.WorksheetQuestionId
                        && wq.Question.CorrectAnswerId == wiq.SelectedAnswerId));

                var wrong = instance.WorksheetInstanceQuestions.Count(wiq =>
                    wiq.SelectedAnswerId != null &&
                    t.WorksheetQuestions.Any(wq => wq.Id == wiq.WorksheetQuestionId
                        && wq.Question.CorrectAnswerId != wiq.SelectedAnswerId));
                instanceDto = new InstanceSummaryDto
                {
                    Id = instance.Id,
                    Name = t.Name,
                    Status = (int)instance.Status,
                    ImageUrl = t.ImageUrl,
                    CompletedDate = instance.EndTime ?? DateTime.UtcNow,
                    DurationMinutes = instance.EndTime.HasValue ?
                (int)(instance.EndTime.Value - instance.StartTime).TotalMinutes : 0,
                    TotalQuestions = instance.WorksheetInstanceQuestions.Count,
                    CorrectAnswers = correct,
                    WrongAnswers = wrong,
                    Score = (correct * 100) / (instance.WorksheetInstanceQuestions.Count > 0 ? instance.WorksheetInstanceQuestions.Count : 1)
                };
            }

            return new WorksheetDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                GradeId = t.GradeId,
                SubjectId = t.SubjectId,
                TopicId = t.TopicId,
                SubTopicId = t.SubTopicId,
                MaxDurationSeconds = t.MaxDurationSeconds,
                IsPracticeTest = t.IsPracticeTest,
                Subtitle = t.Subtitle,
                ImageUrl = t.ImageUrl,
                BadgeText = t.BadgeText,
                BookTestId = t.BookTestId,
                BookId = t.BookTest?.BookId,
                QuestionCount = t.WorksheetQuestions.Count(),
                Instance = instanceDto, // 💡 Eklenen alan
                InstanceCount = worksheetStudentCounts.TryGetValue(t.Id, out var count) ? count : 0 // 💡 Yeni eklenen alan
            };
        }).ToList();

        return new Paged<WorksheetDto>
        {
            PageNumber = dto.pageNumber,
            PageSize = dto.pageSize,
            TotalCount = totalCount,
            Items = worksheetDtos
        };
    }

    public async Task<List<WorksheetDto>> GetLatestWorksheetsAsync(int pageNumber, int pageSize)
    {
        return await _context.Worksheets
            .OrderByDescending(t => t.CreateTime)
            .ThenByDescending(t => t.Id) // stable tiebreaker when CreateTime collides
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new WorksheetDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                GradeId = t.GradeId,
                SubjectId = t.SubjectId,
                MaxDurationSeconds = t.MaxDurationSeconds,
                IsPracticeTest = t.IsPracticeTest,
                Subtitle = t.Subtitle,
                ImageUrl = t.ImageUrl,
                BadgeText = t.BadgeText,
                BookTestId = t.BookTestId,
                BookId = t.BookTest != null ? t.BookTest.BookId : null,
                QuestionCount = t.WorksheetQuestions.Count()
            })
            .ToListAsync();
    }

    public async Task<List<QuestionDto>> GetExamQuestionsAsync()
    {
        return await _context.Questions
            .Include(q => q.Subject)
            .Select(q => new QuestionDto
            {
                Id = q.Id,
                Text = q.Text,
                SubText = q.SubText,
                ImageUrl = q.ImageUrl,
                CategoryName = q.Subject.Name,
                Point = q.Point
            })
            .ToListAsync();
    }

    public async Task<WorksheetDto?> GetWorksheetByIdAsync(int id)
    {
        var worksheet = await _context.Worksheets
            .Include(t => t.BookTest)
                .ThenInclude(bt => bt.Book)
            .Include(t => t.WorksheetQuestions)
                .ThenInclude(tq => tq.Question)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (worksheet == null)
            return null;

        return new WorksheetDto
        {
            Id = worksheet.Id,
            Name = worksheet.Name,
            Description = worksheet.Description,
            GradeId = worksheet.GradeId,
            SubjectId = worksheet.SubjectId,
            MaxDurationSeconds = worksheet.MaxDurationSeconds,
            IsPracticeTest = worksheet.IsPracticeTest,
            Subtitle = worksheet.Subtitle,
            ImageUrl = worksheet.ImageUrl,
            BadgeText = worksheet.BadgeText,
            BookTestId = worksheet.BookTestId,
            BookId = worksheet.BookTest?.BookId,
            QuestionCount = worksheet.WorksheetQuestions.Count()
        };
    }

    public async Task<List<WorksheetWithInstanceDto>> GetWorksheetAndInstancesAsync(StudentProfileDto student, int gradeId)
    {
        var worksheets = await _context.Worksheets
            .Include(w => w.WorksheetQuestions)
            .Include(w => w.BookTest)
            .Where(w => w.GradeId == gradeId)
            .ToListAsync();

        var worksheetIds = worksheets.Select(w => w.Id).ToList();

        var instances = await _context.TestInstances
            .Where(i => worksheetIds.Contains(i.WorksheetId) && i.StudentId == student.Id)
            .ToListAsync();

        var result = worksheets.Select(w => new WorksheetWithInstanceDto
        {
            Worksheet = new WorksheetDto
            {
                Id = w.Id,
                Name = w.Name,
                Description = w.Description,
                GradeId = w.GradeId,
                SubjectId = w.SubjectId,
                MaxDurationSeconds = w.MaxDurationSeconds,
                IsPracticeTest = w.IsPracticeTest,
                Subtitle = w.Subtitle,
                ImageUrl = w.ImageUrl,
                BadgeText = w.BadgeText,
                BookTestId = w.BookTestId,
                BookId = w.BookTest?.BookId,
                QuestionCount = w.WorksheetQuestions.Count
            },
            Instance = instances.FirstOrDefault(i => i.WorksheetId == w.Id)
        }).ToList();

        return result;
    }

    public async Task<List<WorksheetInstanceQuestionDto>> GetAllCanvasQuestions(bool includeAnswers = false, int maxId = 0)
    {
        var questions = await _context.Questions
            .Include(q => q.Answers)
            .Where(q => q.IsCanvasQuestion && q.Id > maxId)
            .Select(q => new WorksheetInstanceQuestionDto
            {
                Question = new QuestionDto
                {
                    Id = q.Id,
                    X = q.X,
                    Y = q.Y,
                    Width = q.Width,
                    Height = q.Height,
                    SanitizedHeight = q.SanitizedHeight,
                    ImageUrl = q.ImageUrl,
                    Answers = includeAnswers ? q.Answers.Select(a => new AnswerDto
                    {
                        X = a.X,
                        Y = a.Y,
                        Width = a.Width,
                        Height = a.Height
                    }).ToList() : new List<AnswerDto>(),
                }
            }
            )
            .ToListAsync();

        foreach (var question in questions)
        {
            var dto = question.Question;
            var fallbackColumns = dto.AnswerColCount > 0
                ? dto.AnswerColCount
                : Math.Max(1, Math.Min(dto.Answers?.Count ?? 0, 4));
        }

        return questions;
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

    public async Task<ExamAllStatisticsDto> GetGroupedStudentStatistics(int studentId)
    {
        var testInstances = await _context.TestInstances
            .Where(ti => ti.StudentId == studentId)
            .Include(ti => ti.Worksheet)
            .Include(ti => ti.WorksheetInstanceQuestions)
                .ThenInclude(wiq => wiq.WorksheetQuestion)
                    .ThenInclude(wq => wq.Question)
            .ToListAsync();

        // 🔹 Total verileri için
        int totalSolved = testInstances.Count;
        var completedTests = testInstances.Where(ti => ti.Status == WorksheetInstanceStatus.Completed).ToList();
        int completedCount = completedTests.Count;
        int totalCorrect = 0;
        int totalWrong = 0;
        int totalTimeTaken = 0;

        foreach (var instance in completedTests)
        {
            foreach (var question in instance.WorksheetInstanceQuestions)
            {
                totalTimeTaken += question.TimeTaken / 60;

                if (question.SelectedAnswerId.HasValue)
                {
                    var correctAnswerId = question.WorksheetQuestion.Question.CorrectAnswerId;
                    if (correctAnswerId.HasValue)
                    {
                        if (question.SelectedAnswerId == correctAnswerId)
                            totalCorrect++;
                        else
                            totalWrong++;
                    }
                }
            }
        }

        // 🔹 Gruplama verileri
        var grouped = testInstances
            .GroupBy(ti => new { ti.Worksheet.GradeId, ti.Worksheet.Name })
            .Select(group =>
            {
                var allTests = group.ToList();
                var completed = allTests.Where(ti => ti.Status == WorksheetInstanceStatus.Completed).ToList();

                int groupCorrect = 0;
                int groupWrong = 0;
                int groupTimeTaken = 0;

                foreach (var instance in completed)
                {
                    foreach (var question in instance.WorksheetInstanceQuestions)
                    {
                        groupTimeTaken += question.TimeTaken / 60;

                        if (question.SelectedAnswerId.HasValue)
                        {
                            var correctAnswerId = question.WorksheetQuestion.Question.CorrectAnswerId;
                            if (correctAnswerId.HasValue)
                            {
                                if (question.SelectedAnswerId == correctAnswerId)
                                    groupCorrect++;
                                else
                                    groupWrong++;
                            }
                        }
                    }
                }

                return new
                {
                    GradeId = group.Key.GradeId,
                    TestName = group.Key.Name,
                    TotalSolvedTests = allTests.Count,
                    CompletedTests = completed.Count,
                    TotalTimeSpentMinutes = groupTimeTaken,
                    TotalCorrectAnswers = groupCorrect,
                    TotalWrongAnswers = groupWrong
                };
            })
            .ToList();

        // 🔹 Response
        var response = new ExamAllStatisticsDto
        {
            Total = new ExamStatisticsDto
            {
                TotalSolvedTests = totalSolved,
                CompletedTests = completedCount,
                TotalTimeSpentMinutes = totalTimeTaken,
                TotalCorrectAnswers = totalCorrect,
                TotalWrongAnswers = totalWrong
            },
            Grouped = grouped.Select(g => new ExamStatisticsDto
            {
                TotalSolvedTests = g.TotalSolvedTests,
                CompletedTests = g.CompletedTests,
                TotalTimeSpentMinutes = g.TotalTimeSpentMinutes,
                TotalCorrectAnswers = g.TotalCorrectAnswers,
                TotalWrongAnswers = g.TotalWrongAnswers
            }).ToList()
        }; return response;
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

    public async Task<List<Grade>> GetGradesAsync()
    {
        return await _context.Grades.ToListAsync();
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
