using System;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services;

public class QuestionService : IQuestionService
{

    private readonly AppDbContext _context;

    private readonly ImageHelper _imageHelper;
    private readonly IMinIoService _minioService;
    public QuestionService(AppDbContext context, ImageHelper imageHelper, IMinIoService minioService)
    {
        _imageHelper = imageHelper;
        _context = context;
        _minioService = minioService;
    }

    public async Task<QuestionDto?> GetQuestionById(int id)
    {
        var question = await _context.Questions
            .Include(q => q.Answers)
            .Include(q => q.Subject)
            .Include(q => q.QuestionSubTopics)
                .ThenInclude(qst => qst.SubTopic)
            .Where(q => q.Id == id)
            .Select(q => new QuestionDto
            {
                Id = q.Id,
                Text = q.Text,
                SubText = q.SubText,
                ImageUrl = q.ImageUrl,
                SubjectId = q.SubjectId,
                TopicId = q.TopicId,
                CategoryName = q.Subject.Name,
                Point = q.Point,
                X = q.X,
                Y = q.Y,
                Width = q.Width,
                Height = q.Height,
                Answers = q.Answers.Select(a => new AnswerDto
                {
                    Id = a.Id,
                    Text = a.Text,
                    ImageUrl = a.ImageUrl,
                    X = a.X,
                    Y = a.Y,
                    Width = a.Width,
                    Height = a.Height,

                }).ToList(),
                IsExample = q.IsExample,
                PracticeCorrectAnswer = q.PracticeCorrectAnswer,
                Passage = q.PassageId.HasValue ? new PassageDto
                {
                    Id = q.Passage.Id,
                    Title = q.Passage.Title,
                    Text = q.Passage.Text,
                    ImageUrl = q.Passage.ImageUrl,
                    X = q.Passage.X,
                    Y = q.Passage.Y,
                    Width = q.Passage.Width,
                    Height = q.Passage.Height,
                } : null,
                CorrectAnswerId = q.CorrectAnswerId,
                AnswerColCount = q.AnswerColCount
                // Subtopics = q.QuestionSubTopics.Select(qst => new Sub
                // {
                //     qst.SubTopicId,
                //     qst.SubTopic.Name
                // }).ToList()
            })
            .FirstOrDefaultAsync();

        return question;
    }

    public async Task<List<PassageDto>> GetLastTenPassages()
    {
        return await _context.Passage
            .OrderByDescending(p => p.Id)
            .Take(10)
            .Select(p => new PassageDto
            {
                Id = p.Id,
                Title = p.Title,
                Text = p.Text,
                ImageUrl = p.ImageUrl,
                X = p.X,
                Y = p.Y,
                Width = p.Width,
                Height = p.Height,
            })
            .ToListAsync();
    }

    public async Task<List<QuestionDto>> GetQuestionByTestId(int testid)
    {
        var questionList = await _context.TestQuestions
            .Include(tq => tq.Question)
                .ThenInclude(q => q.Answers)
            .Include(tq => tq.Question)
                .ThenInclude(q => q.Subject)
            .Include(tq => tq.Question)
                .ThenInclude(q => q.Passage)
            .Include(tq => tq.Question)
                .ThenInclude(q => q.QuestionSubTopics)
                    .ThenInclude(qst => qst.SubTopic)
            .Where(tq => tq.TestId == testid && !tq.IsDeleted)
            .Select(tq => new QuestionDto
            {
                Id = tq.Question.Id,
                Text = tq.Question.Text,
                SubText = tq.Question.SubText,
                ImageUrl = tq.Question.ImageUrl,
                SubjectId = tq.Question.SubjectId,
                TopicId = tq.Question.TopicId,
                CategoryName = tq.Question.Subject.Name,
                Point = tq.Question.Point,
                X = tq.Question.X,
                Y = tq.Question.Y,
                Width = tq.Question.Width,
                Height = tq.Question.Height,
                Answers = tq.Question.Answers.Select(a => new AnswerDto
                {
                    Id = a.Id,
                    Text = a.Text,
                    ImageUrl = a.ImageUrl,
                    X = a.X,
                    Y = a.Y,
                    Width = a.Width,
                    Height = a.Height,
                }).ToList(),
                IsExample = tq.Question.IsExample,
                PracticeCorrectAnswer = tq.Question.PracticeCorrectAnswer,
                Passage = tq.Question.PassageId.HasValue ? new PassageDto
                {
                    Id = tq.Question.Passage.Id,
                    Title = tq.Question.Passage.Title,
                    Text = tq.Question.Passage.Text,
                    ImageUrl = tq.Question.Passage.ImageUrl,
                    X = tq.Question.Passage.X,
                    Y = tq.Question.Passage.Y,
                    Width = tq.Question.Passage.Width,
                    Height = tq.Question.Passage.Height,
                } : null,
                CorrectAnswerId = tq.Question.CorrectAnswerId,
                AnswerColCount = tq.Question.AnswerColCount,
                // SubTopics = tq.Question.QuestionSubTopics.Select(qst => new
                // {
                //     qst.SubTopicId,
                //     qst.SubTopic.Name
                // }).ToList()
            })
            .ToListAsync();

        return questionList;
    }

    public async Task<QuestionSavedDto> CreateOrUpdateQuestion(QuestionDto questionDto)
    {
        try
        {
            Question question;

            // 📌 Eğer ID varsa, veritabanından o soruyu bulup güncelle
            if (questionDto.Id > 0)
            {
                question = await _context.Questions
                        .Include(q => q.Answers)
                        .FirstOrDefaultAsync(q => q.Id == questionDto.Id) ?? throw new InvalidOperationException("Soru bulunamadı!");

                if (question == null)
                {
                    return new QuestionSavedDto
                    {
                        Success = false,
                        Message = "Soru bulunamadı!"
                    };
                    // return NotFound(new { error = "Soru bulunamadı!" });
                }

                question.Text = questionDto.Text;
                question.SubText = questionDto.SubText;
                question.Point = questionDto.Point;
                // question.SubjectId = questionDto.SubjectId;
                // question.TopicId = questionDto.TopicId;
                question.AnswerColCount = questionDto.AnswerColCount;

                // 📌 Eğer yeni resim varsa, güncelle
                if (!string.IsNullOrEmpty(questionDto.Image) &&
                    _imageHelper.IsBase64String(questionDto.Image))
                {
                    byte[] imageBytes = Convert.FromBase64String(questionDto.Image.Split(',')[1]);
                    await using var imageStream = new MemoryStream(imageBytes);
                    question.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"questions/{questionDto.TestId}/{Guid.NewGuid()}.jpg");
                }

                // 📌 Cevapları Güncelle
                question.Answers.Clear(); // Önce mevcut şıkları temizle
                if (questionDto.IsExample)
                {
                    question.IsExample = true;
                    question.PracticeCorrectAnswer = questionDto.PracticeCorrectAnswer;
                }
                else
                {
                    List<Answer> answers = new();
                    Answer? correctAnswer = null;

                    foreach (var answerDto in questionDto.Answers.Where(a => !string.IsNullOrEmpty(a.Text) || !string.IsNullOrEmpty(a.Image)))
                    {
                        var answer = new Answer
                        {
                            Text = answerDto.Text,
                            QuestionId = question.Id // Foreign Key Set
                        };

                        if (!string.IsNullOrEmpty(answerDto.Image) &&
                            _imageHelper.IsBase64String(answerDto.Image))
                        {
                            byte[] imageBytes = Convert.FromBase64String(answerDto.Image.Split(',')[1]);
                            await using var imageStream = new MemoryStream(imageBytes);
                            answer.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"answers/{questionDto.Id}/{Guid.NewGuid()}.jpg");
                        }

                        answers.Add(answer);
                        if (answerDto.IsCorrect)
                        {
                            correctAnswer = answer;
                        }
                    }

                    _context.Answers.AddRange(answers);
                    await _context.SaveChangesAsync(); // 📌 Önce Answer'ları kaydet!

                    if (correctAnswer != null)
                    {
                        question.CorrectAnswerId = correctAnswer.Id;
                    }
                }

                if (questionDto.Passage != null)
                {
                    if (questionDto.Passage.Id > 0)
                    {
                        var passage = await _context.Passage
                        .FirstOrDefaultAsync(p => p.Id == questionDto.Passage.Id) ?? throw new InvalidOperationException("Kapsam bulunamadı!");
                    }
                    else
                    {
                        var passage = new Passage
                        {
                            Title = questionDto.Passage.Title,
                            Text = questionDto.Passage.Text,
                            ImageUrl = questionDto.Passage.ImageUrl
                        };
                        _context.Passage.Add(passage);
                        question.PassageId = passage.Id;
                    }
                }

                _context.Questions.Update(question);

                // Update QuestionSubTopics
                var existingSubTopics = await _context.QuestionSubTopics
                    .Where(qst => qst.QuestionId == questionDto.Id)
                    .ToListAsync();
                _context.QuestionSubTopics.RemoveRange(existingSubTopics);

                // var newSubTopics = questionDto.QuestionSubTopics.Select(qst => new QuestionSubTopic
                // {
                //     QuestionId = questionDto.Id.Value,
                //     SubTopicId = qst.SubtopicId
                // }).ToList();
                // _context.QuestionSubTopics.AddRange(newSubTopics);

                await _context.SaveChangesAsync();
            }
            else
            {
                // 📌 Yeni Soru Oluştur (INSERT)
                question = new Question
                {
                    Text = questionDto.Text,
                    SubText = questionDto.SubText,
                    Point = questionDto.Point,
                    // SubjectId = questionDto.SubjectId,
                    // TopicId = questionDto.TopicId,
                    AnswerColCount = questionDto.AnswerColCount
                };

                if (!string.IsNullOrEmpty(questionDto.Image) &&
                    _imageHelper.IsBase64String(questionDto.Image))
                {
                    byte[] imageBytes = Convert.FromBase64String(questionDto.Image.Split(',')[1]);
                    await using var imageStream = new MemoryStream(imageBytes);
                    question.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"questions/{questionDto.TestId}/{Guid.NewGuid()}.jpg");
                }


                _context.Questions.Add(question);
                await _context.SaveChangesAsync();

                List<Answer> answers = new();
                Answer? correctAnswer = null;

                if (questionDto.IsExample)
                {
                    question.IsExample = true;
                    question.PracticeCorrectAnswer = questionDto.PracticeCorrectAnswer;
                }
                else
                {

                    foreach (var answerDto in questionDto.Answers.Where(a =>
                                !string.IsNullOrEmpty(a.Text) || !string.IsNullOrEmpty(a.Image)))
                    {
                        var answer = new Answer
                        {
                            Text = answerDto.Text
                        };

                        if (!string.IsNullOrEmpty(answerDto.Image) &&
                            _imageHelper.IsBase64String(answerDto.Image))
                        {
                            byte[] imageBytes = Convert.FromBase64String(answerDto.Image.Split(',')[1]);
                            await using var imageStream = new MemoryStream(imageBytes);
                            answer.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"answers/{questionDto.Id}/{Guid.NewGuid()}.jpg");
                        }

                        answers.Add(answer);
                        if (answerDto.IsCorrect)
                        {
                            correctAnswer = answer;
                        }
                    }

                    foreach (var answer in answers)
                    {
                        answer.QuestionId = question.Id;
                    }

                    _context.Answers.AddRange(answers);
                    await _context.SaveChangesAsync();
                }

                if (correctAnswer != null)
                {
                    question.CorrectAnswerId = correctAnswer.Id;
                    _context.Questions.Update(question);
                    await _context.SaveChangesAsync();
                }

                if (questionDto.TestId.HasValue)
                {
                    var orderCount = await _context.TestQuestions
                        .Where(tq => tq.TestId == questionDto.TestId.Value)
                        .CountAsync();
                    // ** WorksheetQuestions tablosuna ekleme **
                    var worksheetQuestion = new WorksheetQuestion
                    {
                        TestId = questionDto.TestId.Value,
                        QuestionId = question.Id,
                        Order = orderCount + 1 // Varsayılan sıralama
                    };

                    _context.TestQuestions.Add(worksheetQuestion);
                    await _context.SaveChangesAsync();
                }

                if (questionDto.Passage != null)
                {
                    if (questionDto.Passage.Id > 0)
                    {
                        var passage = await _context.Passage
                        .FirstOrDefaultAsync(p => p.Id == questionDto.Passage.Id) ?? throw new InvalidOperationException("Kapsam bulunamadı!");

                        question.PassageId = passage.Id;
                        _context.Questions.Update(question);
                        await _context.SaveChangesAsync();

                    }
                    else
                    {

                        var passage = new Passage
                        {
                            Title = questionDto.Passage.Title,
                            Text = questionDto.Passage.Text
                        };
                        // 📌 Eğer yeni resim varsa, güncelle
                        if (!string.IsNullOrEmpty(questionDto.Passage.ImageUrl) &&
                            _imageHelper.IsBase64String(questionDto.Passage.ImageUrl))
                        {
                            byte[] imageBytes = Convert.FromBase64String(questionDto.Passage.ImageUrl.Split(',')[1]);
                            await using var imageStream = new MemoryStream(imageBytes);
                            passage.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"passages/{questionDto.TestId}/{Guid.NewGuid()}.jpg");
                        }

                        _context.Passage.Add(passage);
                        await _context.SaveChangesAsync();

                        question.PassageId = passage.Id;
                        _context.Questions.Update(question);
                        await _context.SaveChangesAsync();
                    }
                }

                // Add QuestionSubTopics
                // var newSubTopics = .Select(qst => new QuestionSubTopic
                // {
                //     QuestionId = question.Id,
                //     SubTopicId = qst.SubtopicId
                // }).ToList();
                // _context.QuestionSubTopics.AddRange(newSubTopics);

                await _context.SaveChangesAsync();
            }

            return new QuestionSavedDto
            {
                Success = questionDto.Id > 0,
                QuestionId = question.Id,
                Message = questionDto.Id > 0 ? "Soru başarıyla güncellendi!" : "Soru başarıyla kaydedildi!"
            };
        }
        catch (Exception ex)
        {
            return new QuestionSavedDto
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public async Task<ResponseBaseDto> SaveBulkQuestion(BulkQuestionCreateDto soruDto)
    {
        using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            try
            {

                var worksheet = _context.Worksheets.FirstOrDefault(worksheet => worksheet.Id == soruDto.Header.TestId);
                if (worksheet != null)
                {
                    if (soruDto.Header.SubjectId == null || soruDto.Header.SubjectId == 0)
                    {
                        Console.WriteLine("Worksheet SubjectId: " + worksheet.SubjectId);
                        Console.WriteLine("soruDto.Header.SubjectId: " + soruDto.Header.SubjectId);
                        soruDto.Header.SubjectId = worksheet.SubjectId;
                    }

                    if (soruDto.Header.TopicId == null || soruDto.Header.TopicId == 0)
                    {
                        Console.WriteLine("Worksheet TopicId: " + worksheet.TopicId);
                        Console.WriteLine("soruDto.Header.TopicId: " + soruDto.Header.TopicId);
                        soruDto.Header.TopicId = worksheet.TopicId;
                    }

                    if (soruDto.Header.Subtopics == null || !soruDto.Header.Subtopics.Any())
                    {
                        Console.WriteLine("Worksheet SubTopicId: " + worksheet.SubTopicId);
                        soruDto.Header.Subtopics = new List<int>();
                        if (worksheet.SubTopicId != null && worksheet.SubTopicId > 0)
                        {
                            soruDto.Header.Subtopics.Add(worksheet.SubTopicId.Value);
                        }
                    }

                }
                string imageUrl = string.Empty;
                // 🔹 1. Resmi MinIO'ya kaydetmeden önce, her Question ve Passage için crop işlemi yapıp ayrı dosya olarak sakla
                // Ana görseli byte[] olarak hazırla
                byte[]? mainImageBytes = null;
                if (!string.IsNullOrEmpty(soruDto.ImageData) && _imageHelper.IsBase64String(soruDto.ImageData))
                {
                    mainImageBytes = Convert.FromBase64String(soruDto.ImageData.Split(',')[1]);
                }

                // Passage'lar için crop ve upload işlemi
                var passages = new List<Passage>();
                foreach (var p in soruDto.Passages)
                {
                    string passageImageUrl = string.Empty;
                    if (mainImageBytes != null)
                    {
                        // Crop işlemi (ImageHelper'da CropImage metodu olmalı: byte[] CropImage(byte[] image, int x, int y, int width, int height))
                        var croppedPassageImage = _imageHelper.CropImage(mainImageBytes, (int)p.X, (int)p.Y, (int)p.Width, (int)p.Height);
                        await using var passageStream = new MemoryStream(croppedPassageImage);
                        passageImageUrl = await _minioService.UploadFileAsync(passageStream, $"passages/{Guid.NewGuid()}.jpg");
                    }
                    passages.Add(new Passage
                    {
                        X = p.X,
                        Y = p.Y,
                        Width = p.Width,
                        Height = p.Height,
                        Title = p.Id,
                        IsCanvasQuestion = true,
                        ImageUrl = passageImageUrl
                    });
                }
                _context.Passage.AddRange(passages);
                await _context.SaveChangesAsync();

                // Sorular için crop ve upload işlemi
                foreach (var questionDto in soruDto.Questions)
                {
                    string questionImageUrl = string.Empty;
                    if (mainImageBytes != null)
                    {
                        var croppedQuestionImage = _imageHelper.CropImage(mainImageBytes, (int)questionDto.X, (int)questionDto.Y, (int)questionDto.Width, (int)questionDto.Height);
                        await using var questionStream = new MemoryStream(croppedQuestionImage);
                        questionImageUrl = await _minioService.UploadFileAsync(questionStream, $"questions/{Guid.NewGuid()}.jpg");
                    }

                    var question = new Question
                    {
                        ImageUrl = questionImageUrl,
                        X = 0,
                        Y = 0,
                        Width = questionDto.Width,
                        Height = questionDto.Height,
                        IsExample = questionDto.IsExample,
                        PracticeCorrectAnswer = questionDto.IsExample ? questionDto.ExampleAnswer : null,
                        IsCanvasQuestion = true,
                        SubjectId = soruDto.Header.SubjectId,
                        TopicId = soruDto.Header.TopicId,
                        PassageId = passages.FirstOrDefault(p => p.Title == questionDto.PassageId)?.Id ?? null
                    };

                    // Answer'ların X,Y koordinatlarını crop edilen question'a göre modifiye et
                    foreach (var answer in questionDto.Answers)
                    {
                        answer.X = answer.X - questionDto.X;
                        answer.Y = answer.Y - questionDto.Y;
                    }

                    if (soruDto.Header.Subtopics != null && soruDto.Header.Subtopics.Any())
                    {
                        question.QuestionSubTopics = soruDto.Header.Subtopics.Select(subTopicId => new QuestionSubTopic
                        {
                            SubTopicId = subTopicId
                        }).ToList();
                    }

                    _context.Questions.Add(question);
                    await _context.SaveChangesAsync();

                    // 🔹 4. Şıkları kaydet
                    if (!questionDto.IsExample)
                    {
                        var answers = questionDto.Answers.Select(a => new Answer
                        {
                            QuestionId = question.Id,
                            Text = a.Label,
                            X = a.X,
                            Y = a.Y,
                            Width = a.Width,
                            Height = a.Height,
                            IsCanvasQuestion = true
                        }).ToList();

                        var correctLabel = questionDto.Answers.FirstOrDefault(a => a.IsCorrect)?.Label;

                        if (correctLabel == null)
                        {
                            throw new InvalidOperationException("Doğru cevap belirtilmemiş." + questionDto.Name);
                        }

                        _context.Answers.AddRange(answers);
                        await _context.SaveChangesAsync();

                        // 🔹 Doğru cevabı kaydet (ilk doğru olanı seç)
                        question.CorrectAnswerId = answers.FirstOrDefault(a => a.Text == correctLabel)?.Id;
                        await _context.SaveChangesAsync();
                    }

                    if (soruDto.Header.TestId.HasValue)
                    {
                        var orderCount = await _context.TestQuestions
                            .Where(tq => tq.TestId == soruDto.Header.TestId.Value)
                            .CountAsync();
                        var worksheetQuestion = new WorksheetQuestion
                        {
                            TestId = soruDto.Header.TestId.Value,
                            QuestionId = question.Id,
                            Order = orderCount + 1
                        };

                        _context.TestQuestions.Add(worksheetQuestion);
                        await _context.SaveChangesAsync();
                    }
                }

                // // 🔹 2. Passage'ları kaydet
                // var passages = soruDto.Passages.Select(p => new Passage
                // {
                //     X = p.X,
                //     Y = p.Y,
                //     Width = p.Width,
                //     Height = p.Height,
                //     Title = p.Id,
                //     IsCanvasQuestion = true
                // }).ToList();

                // _context.Passage.AddRange(passages);
                // await _context.SaveChangesAsync();

                // // 🔹 3. Soruları kaydet
                // foreach (var questionDto in soruDto.Questions)
                // {
                //     var question = new Question
                //     {
                //         // Text = questionDto.Name,
                //         ImageUrl = imageUrl,
                //         X = questionDto.X,
                //         Y = questionDto.Y,
                //         Width = questionDto.Width,
                //         Height = questionDto.Height,
                //         IsExample = questionDto.IsExample,
                //         PracticeCorrectAnswer = questionDto.IsExample ? questionDto.ExampleAnswer : null,
                //         IsCanvasQuestion = true,
                //         SubjectId = soruDto.Header.SubjectId,
                //         TopicId = soruDto.Header.TopicId,
                //         PassageId = passages.FirstOrDefault(p => p.Title == questionDto.PassageId)?.Id ?? null
                //     };

                //     if (soruDto.Header.Subtopics != null && soruDto.Header.Subtopics.Any())
                //     {
                //         question.QuestionSubTopics = soruDto.Header.Subtopics.Select(subTopicId => new QuestionSubTopic
                //         {
                //             SubTopicId = subTopicId
                //             // QuestionId will be set automatically when the question is saved
                //         }).ToList();
                //     }

                //     _context.Questions.Add(question);
                //     await _context.SaveChangesAsync();

                //     // 🔹 4. Şıkları kaydet
                //     if (!questionDto.IsExample)
                //     {
                //         var answers = questionDto.Answers.Select(a => new Answer
                //         {
                //             QuestionId = question.Id,
                //             Text = a.Label,
                //             X = a.X,
                //             Y = a.Y,
                //             Width = a.Width,
                //             Height = a.Height,
                //             IsCanvasQuestion = true
                //         }).ToList();

                //         var correctLabel = questionDto.Answers.FirstOrDefault(a => a.IsCorrect)?.Label;

                //         if (correctLabel == null)
                //         {
                //             throw new InvalidOperationException("Doğru cevap belirtilmemiş." + questionDto.Name);
                //         }

                //         _context.Answers.AddRange(answers);
                //         await _context.SaveChangesAsync();

                //         // 🔹 Doğru cevabı kaydet (ilk doğru olanı seç)
                //         question.CorrectAnswerId = answers.FirstOrDefault(a => a.Text == correctLabel)?.Id;
                //         await _context.SaveChangesAsync();
                //     }

                //     if (soruDto.Header.TestId.HasValue)
                //     {
                //         var orderCount = await _context.TestQuestions
                //             .Where(tq => tq.TestId == soruDto.Header.TestId.Value)
                //             .CountAsync();
                //         // ** WorksheetQuestions tablosuna ekleme **
                //         var worksheetQuestion = new WorksheetQuestion
                //         {
                //             TestId = soruDto.Header.TestId.Value,
                //             QuestionId = question.Id,
                //             Order = orderCount + 1 // Varsayılan sıralama
                //         };

                //         _context.TestQuestions.Add(worksheetQuestion);
                //         await _context.SaveChangesAsync();
                //     }
                // }

                await transaction.CommitAsync();
                return new ResponseBaseDto
                {
                    Success = true,
                    Message = "Sorular başarıyla kaydedildi!"
                };
                // return Ok(new { message = "Sorular başarıyla kaydedildi!" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                // return StatusCode(500, new { error = "Veri kaydedilirken hata oluştu.", details = ex.Message });
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = "Veri kaydedilirken hata oluştu."
                };
            }
        }
    }

    public async Task<ResponseBaseDto> ResizeQuestionImage(int questionId, double scale)
    {
        try
        {
            var question = await _context.Questions
                .FirstOrDefaultAsync(q => q.Id == questionId);

            if (question == null)
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = "Soru bulunamadı!"
                };
            }
            if (string.IsNullOrEmpty(question.ImageUrl))
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = "Soru resmi bulunamadı!"
                };
            }
            // Resmi MinIO'dan indir
            var imageStream = await _minioService.GetFileStreamAsync(question.ImageUrl);
            if (imageStream == null)
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = "Soru resmi indirilemedi!"
                };
            }

            // Resmi boyutlandır
            var resizedImage = await _imageHelper.ResizeImageAsync(imageStream, scale);
            if (resizedImage == null)
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = "Resim boyutlandırılamadı!"
                };
            }

            // Boyutlandırılmış resmi tekrar yükle
            var newUrl = await _minioService.UploadFileAsync(resizedImage, question.ImageUrl);
            if (string.IsNullOrEmpty(newUrl))
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = "Boyutlandırılmış resim yüklenemedi!"
                };
            }

            // Eski resmi silebiliriz.
            // await _minioService.RemoveFileAsync(question.ImageUrl);

            // Yeni URL'yi güncelle
            question.ImageUrl = newUrl;
            question.X *= scale;
            question.Y *= scale;
            if (question.Width != null)
                question.Width = (int)(question.Width * scale);
            if (question.Height != null)
                question.Height = (int)(question.Height * scale);

            foreach (var a in question.Answers)
            {
                a.X *= scale;
                a.Y *= scale;
                if (a.Width != null)
                    a.Width = (int?)(a.Width * scale);
                if (a.Height != null)
                    a.Height = (int?)(a.Height * scale);
            }
            _context.Questions.Update(question);
            await _context.SaveChangesAsync();

            return new ResponseBaseDto
            {
                Success = true,
                Message = "Soru resmi başarıyla boyutlandırıldı!"
            };
        }
        catch (Exception ex)
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = $"Soru resmi boyutlandırılırken hata oluştu: {ex.Message}"
            };
        }   

        return new ResponseBaseDto
        {
            Success = true,
            Message = "Soru yeniden boyutlandırıldı!"
        };
    }

    public async Task<ResponseBaseDto> UpdateCorrectAnswer(int questionId, int correctAnswerId)
    {
        try
        {
            var question = await _context.Questions
                .Include(q => q.Answers)
                .FirstOrDefaultAsync(q => q.Id == questionId);

            if (question == null)
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = "Soru bulunamadı!"
                };
            }

            // Verify that the correct answer ID belongs to this question
            var correctAnswer = question.Answers.FirstOrDefault(a => a.Id == correctAnswerId);
            if (correctAnswer == null)
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = "Seçilen cevap bu soruya ait değil!"
                };
            }

            // Update the correct answer ID
            question.CorrectAnswerId = correctAnswerId;
            _context.Questions.Update(question);
            await _context.SaveChangesAsync();

            return new ResponseBaseDto
            {
                Success = true,
                Message = "Doğru cevap başarıyla güncellendi!"
            };
        }
        catch (Exception ex)
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = $"Doğru cevap güncellenirken hata oluştu: {ex.Message}"
            };
        }
    }
    public async Task<ResponseBaseDto> RemoveQuestionFromTest(int testId, int questionId)
    {
        try
        {
            var testQuestion = await _context.TestQuestions
                .FirstOrDefaultAsync(tq => tq.TestId == testId && tq.QuestionId == questionId && !tq.IsDeleted);

            if (testQuestion == null)
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = "Soru bu testte bulunamadı!"
                };
            }

            // Soft delete: IsDeleted = true yapıyoruz
            testQuestion.IsDeleted = true;
            testQuestion.DeleteTime = DateTime.UtcNow;
            _context.TestQuestions.Update(testQuestion);
            await _context.SaveChangesAsync();

            return new ResponseBaseDto
            {
                Success = true,
                Message = "Soru başarıyla testten çıkarıldı!"
            };
        }
        catch (Exception ex)
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = $"Soru testten çıkarılırken hata oluştu: {ex.Message}"
            };
        }
    }
}
