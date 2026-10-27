using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Storage;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SkiaSharp;

namespace ExamApp.Api.Services;

public class QuestionService : IQuestionService
{

    private readonly AppDbContext _context;

    private readonly ImageHelper _imageHelper;
    private readonly IMinIoService _minioService;
    private readonly StorageAreaPolicy _storagePolicy;

    // Client'a donen mesajlar (ResponseBaseDto.Message ve istemciye sizan exception metinleri)
    // buradan gelir (issue #184). Log mesajlari cevrilmez. DI her zaman gercek localizer'i
    // verir; parametre yalnizca DI'siz kurulan (birim test) senaryolar icin opsiyonel.
    private readonly IStringLocalizer<Messages> _localizer;

    // issue #574: tipe özgü plan/çözüm doğrulaması (kayıtta) ve "etkileşimli mi" kararı; DI'sız kurulumda yerleşik kontrolcüler.
    private readonly IInteractionGraderRegistry _graders;

    public QuestionService(
        AppDbContext context,
        ImageHelper imageHelper,
        IMinIoService minioService,
        IStringLocalizer<Messages>? localizer = null,
        StorageAreaPolicy? storagePolicy = null,
        IInteractionGraderRegistry? graders = null)
    {
        _graders = graders ?? InteractionGraderRegistry.Default;
        _imageHelper = imageHelper;
        _context = context;
        _minioService = minioService;
        // DI her zaman yapılandırılmış politikayı verir; DI'sız (birim test) kurulumda varsayılan bucket.
        _storagePolicy = storagePolicy ?? new StorageAreaPolicy((string?)null);
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    public async Task<QuestionSavedDto> CreateOrUpdateQuestion(QuestionDto questionDto, int actingUserId = 0, bool isAdmin = false,
        SolutionActor? solutionActor = null)
    {
        // issue #287 H1: audit alanları (CreateUserId = soru sahibi) istek sahibine damgalanır.
        if (actingUserId > 0)
            _context.SetCurrentUser(actingUserId);

        // issue #365 (S2): yeni paragrafın görseli base64 değilse istemcinin gönderdiği adrestir. Hiçbir şey yazılmadan
        // ÖNCE /img/{varsayılan bucket}/passages/... biçimine indirgenir (imzalı URL'nin query'si atılır); başka her şey
        // (tam URL, question-transfer/, yabancı bucket, questions/ ya da answers/ nesnesi, & / %) InvalidInput → 400.
        // Mevcut paragrafa bağlanırken (Id > 0) istemcinin geri gönderdiği adres kullanılmadığı için doğrulanmaz.
        if (questionDto.Passage is { Id: null or <= 0 } newPassage &&
            !string.IsNullOrWhiteSpace(newPassage.ImageUrl) && !_imageHelper.IsBase64String(newPassage.ImageUrl))
        {
            if (!_storagePolicy.TryNormalizeClientUrl(newPassage.ImageUrl, StorageArea.QuestionImage,
                    StorageAreaPolicy.PassageImagePrefix, out var normalizedPassageImage))
            {
                return new QuestionSavedDto
                {
                    Success = false,
                    InvalidInput = true,
                    Message = _localizer["questions.image.invalidUrl"]
                };
            }
            newPassage.ImageUrl = normalizedPassageImage;
        }

        // issue #573 (security review): yönerge düz metin — kontrol karakteri varsa hiçbir şey yazılmadan 400 (DTO doğrulamasının
        // servis tarafı kopyası; DI'sız/doğrudan çağıranlar için).
        if (questionDto.Passage?.Instruction is { } instruction
            && (instruction.Length > Passage.MaxInstructionLength || PlainTextAttribute.ContainsControlCharacters(instruction)))
        {
            return new QuestionSavedDto
            {
                Success = false,
                InvalidInput = true,
                Message = _localizer["questions.passage.invalidInstruction"]
            };
        }

        // issue #580: çözüm kaynağı Key | AI | Human (boş = Human); bilinmeyen değer hiçbir şey yazılmadan 400.
        if (!QuestionSolutionApproval.TryParseSource(questionDto.SolutionSource, out var requestedSolutionSource))
        {
            return new QuestionSavedDto
            {
                Success = false,
                InvalidInput = true,
                Message = _localizer["questions.solution.invalidSource"]
            };
        }

        try
        {
            Question question;

            // 📌 Eğer ID varsa, veritabanından o soruyu bulup güncelle
            if (questionDto.Id > 0)
            {
                question = await _context.Questions
                        .Include(q => q.Answers)
                        .FirstOrDefaultAsync(q => q.Id == questionDto.Id) ?? throw new InvalidOperationException(_localizer["questions.notFound"]);

                if (question == null)
                {
                    return new QuestionSavedDto
                    {
                        Success = false,
                        Message = _localizer["questions.notFound"]
                    };
                    // return NotFound(new { error = "Soru bulunamadı!" });
                }

                question.Text = questionDto.Text;
                question.SubText = questionDto.SubText;
                question.BookName = questionDto.BookName;
                question.Point = questionDto.Point;
                // question.SubjectId = questionDto.SubjectId;
                // question.TopicId = questionDto.TopicId;
                question.AnswerColCount = questionDto.AnswerColCount;
                // review #574 (L3): kontrolcüsü olan tip kanonik yazılır ("MatrixChoice" → "matrixChoice"); MCQ/boş/bilinmeyen aynen.
                var previousInteractionType = question.InteractionType;
                question.InteractionType = _graders.CanonicalType(questionDto.InteractionType);
                // issue #570: plan çözümsüz saklanır; çözüm ayrı alandan, yoksa plandan (eski istemci), o da yoksa mevcut değer.
                if (!InteractionPlanSolution.TryResolveForWrite(questionDto.InteractionPlan, questionDto.InteractionSolution,
                        question.InteractionSolution, out var storedPlan, out var storedSolution))
                {
                    return new QuestionSavedDto
                    {
                        Success = false,
                        InvalidInput = true,
                        Message = _localizer["questions.interactionSolution.invalid"]
                    };
                }
                // issue #574: tipe özgü tanım (ör. matrixChoice şeması + çözümün planla tutarlılığı); MCQ/drag-drop'ta her zaman geçer.
                if (!_graders.Resolve(questionDto.InteractionType).ValidateDefinition(storedPlan, storedSolution))
                {
                    return new QuestionSavedDto
                    {
                        Success = false,
                        InvalidInput = true,
                        Message = _localizer["questions.interactionDefinition.invalid"]
                    };
                }
                // review #576 (H1/M1): tanım değiştiyse saklanacak biçime (dragPlace kartları / match sağ öğeleri karışık sıra +
                // opak id); aynı tanım yeniden kaydedilince dokunulmaz (kayıtlı cevapların id'leri bozulmasın).
                // issue #580: işaretleme taslağından gelen çözüm — kaynak/onay sunucuda taslak satırından türetilir.
                var (draftOk, solutionDraft) = await ResolveSolutionDraftAsync(questionDto.DraftSolutionId, questionDto.InteractionType, storedSolution);
                if (!draftOk)
                {
                    return new QuestionSavedDto
                    {
                        Success = false,
                        InvalidInput = true,
                        Message = _localizer["questions.solution.draftMismatch"]
                    };
                }
                // issue #580 (review C1): "çözüm değişti" yalnız çözüm JSON'u (kanonik) değişince — kutu/plan değişikliği onayı aklamaz.
                var solutionChanged = !CanonicalJson.Equal(storedSolution, question.InteractionSolution);
                var definitionChanged = !string.Equals(previousInteractionType, question.InteractionType, StringComparison.Ordinal)
                    || !string.Equals(storedPlan, question.InteractionPlan, StringComparison.Ordinal)
                    || !string.Equals(storedSolution, question.InteractionSolution, StringComparison.Ordinal);
                if (definitionChanged)
                {
                    (storedPlan, storedSolution) = _graders.Resolve(questionDto.InteractionType).PrepareForStorage(storedPlan, storedSolution);
                }
                question.InteractionPlan = storedPlan;
                question.InteractionSolution = storedSolution;
                // issue #580: kaynak/onay kuralları (önceki durum sorunun mevcut alanları).
                QuestionSolutionApproval.Apply(question,
                    hasSolution: IsInteractive(question.InteractionType) && !string.IsNullOrWhiteSpace(storedSolution),
                    solutionChanged,
                    requestedSolutionSource, questionDto.SolutionApproved == true, questionDto.SolutionPromptVersion,
                    solutionActor, DateTime.UtcNow, solutionDraft);
                question.ShowPassageFirst = questionDto.ShowPassageFirst;


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
                        .FirstOrDefaultAsync(p => p.Id == questionDto.Passage.Id) ?? throw new InvalidOperationException(_localizer["questions.passageNotFound"]);
                        // issue #402: güncellemede var olan paragraf artık gerçekten bağlanır (eskiden yalnız varlığı kontrol
                        // edilip bağlanmıyordu). Sahiplik controller'da (IQuestionOwnershipGuard.CanUsePassageAsync) doğrulanır.
                        question.PassageId = passage.Id;
                        ApplyPassageActivityFields(passage, questionDto.Passage, isNew: false, CanEditExistingPassage(passage, actingUserId, isAdmin));
                    }
                    else
                    {
                        var passage = new Passage
                        {
                            Title = questionDto.Passage.Title,
                            Text = questionDto.Passage.Text
                        };
                        ApplyPassageActivityFields(passage, questionDto.Passage, isNew: true, canEdit: true);
                        // issue #365 (S2): istemci değeri olduğu gibi saklanmaz. Base64 ise (oluşturma dalıyla aynı)
                        // yüklenir — eskiden data URI ham hâliyle ImageUrl'e yazılıyordu; değilse metodun başında
                        // passages/ allowlist'ine normalize edilmiş (query'siz) adrestir.
                        passage.ImageUrl = await ResolveNewPassageImageAsync(questionDto.Passage.ImageUrl, questionDto.TestId);
                        _context.Passage.Add(passage);
                        // Navigation ile bağla: passage henüz kaydedilmediği için Id = 0'dı ve PassageId = 0 FK hatasıyla
                        // tüm güncellemeyi düşürüyordu (yeni paragraflı güncelleme hiç çalışmıyordu; #365 testinde ortaya çıktı).
                        question.Passage = passage;
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
                    BookName = questionDto.BookName,
                    Point = questionDto.Point,
                    // SubjectId = questionDto.SubjectId,
                    // TopicId = questionDto.TopicId,
                    AnswerColCount = questionDto.AnswerColCount,

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
                        .FirstOrDefaultAsync(p => p.Id == questionDto.Passage.Id) ?? throw new InvalidOperationException(_localizer["questions.passageNotFound"]);

                        question.PassageId = passage.Id;
                        ApplyPassageActivityFields(passage, questionDto.Passage, isNew: false, CanEditExistingPassage(passage, actingUserId, isAdmin));
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
                        ApplyPassageActivityFields(passage, questionDto.Passage, isNew: true, canEdit: true);
                        // 📌 Base64 görsel yüklenir; değilse metodun başında passages/ allowlist'ine normalize edilmiş adres (#365).
                        passage.ImageUrl = await ResolveNewPassageImageAsync(questionDto.Passage.ImageUrl, questionDto.TestId);

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

                // 🟢 Record QuestionCreated event to Outbox for new questions only (OutboxPublisher will publish)
                if (questionDto.Id == 0) // Yeni soru oluşturuldı
                {
                    // Issue #565: sınıflandırıcı taksonomiyi testin sınıfına daraltsın; testsiz kayıtta sınıf bilinmiyor.
                    int? gradeId = null;
                    if (questionDto.TestId.HasValue)
                    {
                        var worksheetGradeId = await _context.Worksheets
                            .Where(w => w.Id == questionDto.TestId.Value)
                            .Select(w => (int?)w.GradeId)
                            .FirstOrDefaultAsync();
                        gradeId = worksheetGradeId is > 0 ? worksheetGradeId : null;
                    }

                    var @event = new QuestionCreatedEvent
                    {
                        QuestionId = question.Id,
                        SubjectId = question.SubjectId,
                        TopicId = question.TopicId,
                        GradeId = gradeId,
                        ClassificationSource = question.ClassificationSource?.ToString(),
                        CreatedAt = DateTime.UtcNow,
                        Text = question.Text
                    };

                    var outbox = new OutboxMessage
                    {
                        Type = OutboxEventRegistry.NameFor<QuestionCreatedEvent>(),
                        Content = JsonSerializer.Serialize(@event),
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.OutboxMessages.Add(outbox);
                    await _context.SaveChangesAsync();
                }
            }

            return new QuestionSavedDto
            {
                Success = questionDto.Id > 0,
                QuestionId = question.Id,
                Message = questionDto.Id > 0 ? _localizer["questions.updated"] : _localizer["questions.created"]
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

    /// <summary>
    /// Yeni paragrafın saklanacak görsel adresi: base64 ise MinIO'ya yüklenir, değilse (metodun başında passages/
    /// allowlist'ine normalize edilmiş) değer aynen döner; boşsa null.
    /// </summary>
    /// <summary>
    /// issue #573: kapsamın etkinlik alanları. Yeni kapsamda null tür = Reading. Var olan kapsamda (bağlama) yalnız gönderilen
    /// alanlar değişir — alanı göndermeyen eski istemci türü/yönergeyi sıfırlamaz. Boş/boşluk yönerge = temizle.
    /// <paramref name="canEdit"/> false ise (var olan kapsamın sahibi değil, admin değil) alanlar yok sayılır; bağlama aynen yapılır.
    /// </summary>
    internal static void ApplyPassageActivityFields(Passage passage, PassageDto dto, bool isNew, bool canEdit)
    {
        if (!isNew && !canEdit)
            return;

        if (dto.Kind.HasValue)
            passage.Kind = dto.Kind.Value;
        else if (isNew)
            passage.Kind = PassageKind.Reading;

        if (dto.Instruction != null || isNew)
            passage.Instruction = NormalizeInstruction(dto.Instruction);
    }

    /// <summary>
    /// issue #573 (security review): var olan kapsamın tür/yönergesini yalnız kapsamın sahibi (<c>CreateUserId</c>) ya da admin
    /// değiştirebilir. <c>IQuestionOwnershipGuard.CanUsePassageAsync</c> bağlamaya (paylaşılan kapsam, legacy kapsamda bağlı
    /// sorunun sahibi) izin verse de bu, başkasının kapsamını (ve ona bağlı diğer soruları) değiştirme yetkisi değildir. Legacy
    /// (sahipsiz, CreateUserId 0/null) kapsam: admin dışında hiç değiştirilmez — bağlı başka kullanıcının sorusu olup olmadığını
    /// türetmek yerine daha güvenli olan seçildi.
    /// </summary>
    internal static bool CanEditExistingPassage(Passage passage, int actingUserId, bool isAdmin)
        => isAdmin || (actingUserId > 0 && passage.CreateUserId is { } owner && owner > 0 && owner == actingUserId);

    /// <summary>issue #573: boş/boşluk yönerge null saklanır; uzunluk DTO doğrulamasında sınırlıdır.</summary>
    internal static string? NormalizeInstruction(string? instruction)
        => string.IsNullOrWhiteSpace(instruction) ? null : instruction.Trim();

    private async Task<string?> ResolveNewPassageImageAsync(string? image, int? testId)
    {
        if (string.IsNullOrEmpty(image))
            return null;
        if (!_imageHelper.IsBase64String(image))
            return image;

        byte[] imageBytes = Convert.FromBase64String(image.Split(',')[1]);
        await using var imageStream = new MemoryStream(imageBytes);
        return await _minioService.UploadFileAsync(imageStream, PassageObjectKey(testId));
    }

    /// <summary>
    /// Paragraf görseli anahtarı: <c>passages/{testId}/{guid}.jpg</c>; test yoksa <c>passages/{guid}.jpg</c>
    /// (eskiden <c>passages//{guid}.jpg</c> — boş klasör segmenti üretiyordu).
    /// </summary>
    internal static string PassageObjectKey(int? testId)
        => testId is > 0
            ? $"{StorageAreaPolicy.PassageImagePrefix}{testId}/{Guid.NewGuid()}.jpg"
            : $"{StorageAreaPolicy.PassageImagePrefix}{Guid.NewGuid()}.jpg";

    public async Task<StudyPageAttachImageResponseDto> AttachImageToStudyPage(StudyPageAttachImageDto request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ImageData))
            {
                return new StudyPageAttachImageResponseDto
                {
                    Success = false,
                    Message = _localizer["questions.studyPage.imageDataRequired"]
                };
            }

            if (!_imageHelper.IsBase64String(request.ImageData))
            {
                return new StudyPageAttachImageResponseDto
                {
                    Success = false,
                    Message = _localizer["questions.studyPage.invalidImageDataFormat"]
                };
            }

            var payload = request.ImageData.Contains(',')
                ? request.ImageData.Split(',')[1]
                : request.ImageData;

            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(payload);
            }
            catch (FormatException)
            {
                return new StudyPageAttachImageResponseDto
                {
                    Success = false,
                    Message = _localizer["questions.studyPage.imageDataDecodeFailed"]
                };
            }

            await using var imageStream = new MemoryStream(imageBytes);
            var objectName = $"books/{Guid.NewGuid()}.jpg";
            var imageUrl = await _minioService.UploadFileAsync(imageStream, objectName, "study-pages", "image/jpeg");

            return new StudyPageAttachImageResponseDto
            {
                Success = true,
                Message = _localizer["questions.studyPage.uploaded"],
                ImageUrl = imageUrl
            };
        }
        catch (Exception ex)
        {
            return new StudyPageAttachImageResponseDto
            {
                Success = false,
                Message = _localizer["questions.studyPage.uploadError", ex.Message]
            };
        }
    }

    /// <summary>
    /// issue #574: MCQ kuralı dışında kendi kontrolcüsü olan tip (dragDropLabeling, matrixChoice, slotFill, dragPlace, match). Bilinmeyen tip MCQ
    /// kontrolcüsüne düştüğü için etkileşimli sayılmaz (bugünkü davranış: doğru şık zorunlu, question-v2 kırpılır).
    /// </summary>
    /// <summary>
    /// Issue #580: <paramref name="draftId"/> verildiyse işaretleme taslağı (<c>LabelingActivitySolutions</c>) yüklenir; tipi ve çözümü
    /// (kanonik JSON, <c>PrepareForStorage</c> öncesi taslak id'leriyle) kaydedilenle aynı olmalı — değilse false (400). Kaynak, sürüm
    /// ve onay taslaktan gelir; istemcinin bildirdiği kaynak yok sayılır.
    /// </summary>
    private async Task<(bool Ok, SolutionDraftInfo? Draft)> ResolveSolutionDraftAsync(int? draftId, string? interactionType, string? solution)
    {
        if (draftId is null)
            return (true, null);
        var row = await _context.LabelingActivitySolutions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == draftId.Value);
        if (row?.SolutionJson is null
            || !string.Equals(row.InteractionType, _graders.CanonicalType(interactionType), StringComparison.Ordinal)
            || !CanonicalJson.Equal(row.SolutionJson, solution))
            return (false, null);
        return (true, new SolutionDraftInfo(row.Source, row.PromptVersion, row.Status == SolutionStatus.Approved,
            row.ApprovedByKeycloakId, row.ApprovedByName, row.ApprovedAt));
    }

    private bool IsInteractive(string? interactionType) =>
        !_graders.Resolve(interactionType).InteractionType.Equals(McqGrader.Type, StringComparison.OrdinalIgnoreCase);

    public async Task<ResponseBaseDto> SaveBulkQuestion(BulkQuestionCreateDto soruDto, int actingUserId = 0,
        SolutionActor? solutionActor = null)
    {
        // issue #287 H1: audit alanları (CreateUserId = soru sahibi) istek sahibine damgalanır.
        if (actingUserId > 0)
            _context.SetCurrentUser(actingUserId);

        // issue #573 (security review): kapsam yönergeleri yüklemeden önce doğrulanır (düz metin, uzunluk).
        if ((soruDto.Passages ?? new List<BulkPassageDto>()).Any(p => p?.Instruction is { } i
                && (i.Length > Passage.MaxInstructionLength || PlainTextAttribute.ContainsControlCharacters(i))))
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = _localizer["questions.passage.invalidInstruction"]
            };
        }

        // issue #570: tüm soruların plan/çözümü transaction'dan ve MinIO yüklemelerinden ÖNCE doğrulanıp ayrılır — bozuk bir
        // öğe hiçbir şey yüklenmeden/yazılmadan reddedilir (ham istisna mesajı dönmez).
        var interactionByQuestion = new Dictionary<BulkQuestionDto, (string? Plan, string? Solution)>(ReferenceEqualityComparer.Instance);
        var draftByQuestion = new Dictionary<BulkQuestionDto, SolutionDraftInfo?>(ReferenceEqualityComparer.Instance);
        foreach (var bulkQuestion in soruDto.Questions ?? new List<BulkQuestionDto>())
        {
            if (bulkQuestion == null)
                continue;
            if (!InteractionPlanSolution.TryResolveForWrite(bulkQuestion.InteractionPlan, bulkQuestion.InteractionSolution,
                    existingSolution: null, out var resolvedPlan, out var resolvedSolution))
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = _localizer["questions.bulk.invalidInteractionSolution", bulkQuestion.Name]
                };
            }
            // issue #574: tipe özgü tanım doğrulaması da yüklemeden önce.
            if (!_graders.Resolve(bulkQuestion.InteractionType).ValidateDefinition(resolvedPlan, resolvedSolution))
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = _localizer["questions.bulk.invalidInteractionDefinition", bulkQuestion.Name]
                };
            }
            // issue #580: çözüm kaynağı Key | AI | Human (boş = Human).
            if (!QuestionSolutionApproval.TryParseSource(bulkQuestion.SolutionSource, out _))
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = _localizer["questions.solution.invalidSource"]
                };
            }
            // issue #580: taslak çözüm (draftSolutionId) — tip ve eşlenmiş çözüm taslakla aynı olmalı.
            var (bulkDraftOk, bulkDraft) = await ResolveSolutionDraftAsync(bulkQuestion.DraftSolutionId, bulkQuestion.InteractionType, resolvedSolution);
            if (!bulkDraftOk)
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = _localizer["questions.solution.draftMismatch"]
                };
            }
            draftByQuestion[bulkQuestion] = bulkDraft;
            // review #576 (H1/M1): saklanacak biçim (dragPlace / match: karışık sıra + opak id).
            interactionByQuestion[bulkQuestion] = _graders.Resolve(bulkQuestion.InteractionType).PrepareForStorage(resolvedPlan, resolvedSolution);
        }

        // Wrapped in CreateExecutionStrategy so EF Core's retry-on-failure
        // (enabled by the Aspire Npgsql client integration) can retry the
        // whole transaction as one unit instead of rejecting it outright.
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
        using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            try
            {
                var createdQuestions = new List<Question>(); // 🟢 Track created questions for events

                var worksheet = _context.Worksheets.FirstOrDefault(worksheet => worksheet.Id == soruDto.Header.TestId);
                if (worksheet != null)
                {
                    if (soruDto.Header.SubjectId == null || soruDto.Header.SubjectId == 0)
                    {
                        soruDto.Header.SubjectId = worksheet.SubjectId;
                    }

                    if (soruDto.Header.TopicId == null || soruDto.Header.TopicId == 0)
                    {
                        soruDto.Header.TopicId = worksheet.TopicId;
                    }

                    if (soruDto.Header.Subtopics == null || !soruDto.Header.Subtopics.Any())
                    {
                        soruDto.Header.Subtopics = new List<int>();
                        if (worksheet.SubTopicId != null && worksheet.SubTopicId > 0)
                        {
                            soruDto.Header.Subtopics.Add(worksheet.SubTopicId.Value);
                        }
                    }

                }

                // Issue #565: sınıflandırıcıya gidecek sınıf — test varsa testin sınıfı esastır (istekteki farklı sınıf
                // reddedilir); test yoksa istekte açıkça gelen sınıf (işaretleme ekranı, doğrulanır); ikisi de yoksa null.
                // Tüm sorular için tek kez çözülür (döngüde sorgu yok).
                int? eventGradeId;
                if (worksheet != null)
                {
                    if (soruDto.GradeId.HasValue && soruDto.GradeId.Value != worksheet.GradeId)
                        throw new InvalidOperationException(_localizer[
                            "questions.bulk.gradeMismatch", soruDto.GradeId.Value, worksheet.GradeId]);
                    eventGradeId = worksheet.GradeId > 0 ? worksheet.GradeId : null;
                }
                else if (soruDto.GradeId.HasValue)
                {
                    var explicitGradeId = soruDto.GradeId.Value;
                    if (explicitGradeId <= 0 || !await _context.Grades.AnyAsync(g => g.Id == explicitGradeId))
                        throw new InvalidOperationException(_localizer["questions.bulk.invalidGradeId", explicitGradeId]);
                    eventGradeId = explicitGradeId;
                }
                else
                {
                    eventGradeId = null;
                }

                // Validate classification consistency (header + issue #454 per-question overrides):
                // - All provided SubTopics must belong to the same Topic.
                // - That Topic must match the TopicId being set on the Question.
                // - The Topic's SubjectId must match the SubjectId being set on the Question.
                // Lookups are cached per request so per-question validation does not cause N+1.
                var classificationLookups = new ClassificationLookupCache();
                await ValidateClassificationAsync(
                    soruDto.Header.SubjectId, soruDto.Header.TopicId, soruDto.Header.Subtopics, classificationLookups);

                var headerClassificationSource = ClassificationSource.Human;
                if (!string.IsNullOrEmpty(soruDto.Header.ClassificationSource) &&
                    Enum.TryParse<ClassificationSource>(soruDto.Header.ClassificationSource, ignoreCase: true, out var parsedHeaderSource) &&
                    Enum.IsDefined(parsedHeaderSource))
                {
                    headerClassificationSource = parsedHeaderSource;
                }

                // Issue #454: questions with a Classification object get their own effective values
                // (field-level fallback to the header); questions without one keep the header path below.
                var questionClassifications = new Dictionary<BulkQuestionDto, EffectiveQuestionClassification>(ReferenceEqualityComparer.Instance);
                var headerSubTopicIds = NormalizeSubTopicIds(soruDto.Header.Subtopics);
                foreach (var questionDto in soruDto.Questions)
                {
                    if (questionDto.Classification == null)
                    {
                        continue;
                    }

                    var effective = ResolveQuestionClassification(
                        questionDto.Classification, soruDto.Header, headerSubTopicIds, headerClassificationSource);

                    var differsFromHeader = effective.SubjectId != soruDto.Header.SubjectId
                        || effective.TopicId != soruDto.Header.TopicId
                        || !effective.SubTopicIds.ToHashSet().SetEquals(headerSubTopicIds);
                    if (differsFromHeader)
                    {
                        await ValidateClassificationAsync(
                            effective.SubjectId, effective.TopicId, effective.SubTopicIds, classificationLookups);
                    }

                    questionClassifications[questionDto] = effective;
                }
                string imageUrl = string.Empty;
                // 🔹 1. Resmi MinIO'ya kaydetmeden önce, her Question ve Passage için crop işlemi yapıp ayrı dosya olarak sakla
                // Ana görseli byte[] olarak hazırla
                byte[]? mainImageBytes = null;
                if (!string.IsNullOrEmpty(soruDto.ImageData) && _imageHelper.IsBase64String(soruDto.ImageData))
                {
                    mainImageBytes = Convert.FromBase64String(soruDto.ImageData.Split(',')[1]);
                }

                // #559: sayfa bir kez çözülür (piksel sınırı SkiaImaging'de); tüm kırpmalar bu bitmap'ten yapılır.
                var mainImageFormat = SKEncodedImageFormat.Png;
                using var mainImage = mainImageBytes != null ? _imageHelper.LoadImage(mainImageBytes, out mainImageFormat) : null;

                // Passage'lar için crop ve upload işlemi
                var passages = new List<Passage>();
                foreach (var p in soruDto.Passages)
                {
                    string passageImageUrl = string.Empty;
                    if (mainImage != null)
                    {
                        using var passageBitmap = _imageHelper.CropBitmap(mainImage, (int)p.X, (int)p.Y, (int)p.Width, (int)p.Height);
                        var croppedPassageImage = _imageHelper.EncodeImage(passageBitmap, mainImageFormat);
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
                        ImageUrl = passageImageUrl,
                        // issue #573: etkinlik kapsamı (tür + yönerge). Title yukarıda istemci geçici id'si olarak kalır
                        // (soru → kapsam eşlemesi passageLookup ile ona bakar).
                        Kind = p.Kind ?? PassageKind.Reading,
                        Instruction = NormalizeInstruction(p.Instruction)
                    });
                }
                _context.Passage.AddRange(passages);
                await _context.SaveChangesAsync();
                var passageLookup = passages
                    .Where(p => !string.IsNullOrWhiteSpace(p.Title))
                    .ToDictionary(p => p.Title!, p => p, StringComparer.OrdinalIgnoreCase);

                // Sorular için crop ve upload işlemi
                foreach (var questionDto in soruDto.Questions)
                {
                    var questionFolderId = Guid.NewGuid().ToString();
                    var questionFolderPath = $"questions/{questionFolderId}";

                    var interactionType = string.IsNullOrWhiteSpace(questionDto.InteractionType)
                        ? "mcq"
                        : _graders.CanonicalType(questionDto.InteractionType.Trim())!;

                    byte[]? croppedQuestionImage = null;
                    string questionImageUrl = string.Empty;

                    var sourceAnswers = questionDto.Answers ?? new List<BulkAnswerDto>();
                    var answerInfos = sourceAnswers
                        .Select(answer => new AnswerCropInfo(answer, answer.X - questionDto.X, answer.Y - questionDto.Y))
                        .ToList();

                    var answerImagePayloads = new List<byte[]?>(answerInfos.Count);
                    for (var i = 0; i < answerInfos.Count; i++)
                    {
                        answerImagePayloads.Add(null);
                    }

                    int effectiveQuestionHeight = questionDto.Height;
                    byte[]? questionV2Image = null;

                    if (mainImage != null)
                    {
                        using var questionBitmap = _imageHelper.CropBitmap(mainImage, (int)questionDto.X, (int)questionDto.Y, questionDto.Width, questionDto.Height);
                        croppedQuestionImage = _imageHelper.EncodeImage(questionBitmap, mainImageFormat);

                        await using (var questionStream = new MemoryStream(croppedQuestionImage))
                        {
                            questionImageUrl = await _minioService.UploadFileAsync(questionStream, $"{questionFolderPath}/question.jpg");
                        }

                        questionV2Image = croppedQuestionImage;
                        // For MCQ we trim question-v2 above the answer block to improve UI.
                        // For interactive types (dragDropLabeling, matrixChoice, slotFill, dragPlace, match) the targets are inside the question image,
                        // so we must keep full height.
                        if (!IsInteractive(interactionType) && answerInfos.Any())
                        {
                            var topAnswerRelativeY = answerInfos.Min(a => a.RelativeY);
                            var sanitizedHeight = (int)Math.Round(topAnswerRelativeY);
                            sanitizedHeight = Math.Clamp(sanitizedHeight, 1, questionDto.Height);

                            if (sanitizedHeight > 0 && sanitizedHeight < questionDto.Height)
                            {
                                effectiveQuestionHeight = sanitizedHeight;
                                using var questionV2Bitmap = _imageHelper.CropBitmap(questionBitmap, 0, 0, questionDto.Width, sanitizedHeight);
                                questionV2Image = _imageHelper.EncodeImage(questionV2Bitmap, mainImageFormat);
                            }
                        }

                        if (questionV2Image != null)
                        {
                            await using var questionV2Stream = new MemoryStream(questionV2Image);
                            await _minioService.UploadFileAsync(questionV2Stream, $"{questionFolderPath}/question-v2.jpg");
                        }

                        for (var index = 0; index < answerInfos.Count; index++)
                        {
                            var info = answerInfos[index];

                            int cropX = Math.Clamp((int)Math.Round(info.RelativeX), 0, Math.Max(0, questionDto.Width - 1));
                            int cropY = Math.Clamp((int)Math.Round(info.RelativeY), 0, Math.Max(0, questionDto.Height - 1));

                            var cropWidth = Math.Min(info.Source.Width, questionDto.Width - cropX);
                            var cropHeight = Math.Min(info.Source.Height, questionDto.Height - cropY);

                            if (cropWidth <= 0 || cropHeight <= 0)
                            {
                                info.EffectiveWidth = Math.Max(info.EffectiveWidth, 0);
                                info.EffectiveHeight = Math.Max(info.EffectiveHeight, 0);
                                continue;
                            }

                            info.EffectiveWidth = cropWidth;
                            info.EffectiveHeight = cropHeight;

                            using var answerBitmap = _imageHelper.CropBitmap(questionBitmap, cropX, cropY, cropWidth, cropHeight);
                            answerImagePayloads[index] = _imageHelper.EncodeImage(answerBitmap, mainImageFormat);
                        }
                    }

                    foreach (var info in answerInfos)
                    {
                        info.Source.X = info.RelativeX;
                        info.Source.Y = info.RelativeY;

                        if (info.EffectiveWidth > 0)
                        {
                            info.Source.Width = info.EffectiveWidth;
                        }

                        if (info.EffectiveHeight > 0)
                        {
                            info.Source.Height = info.EffectiveHeight;
                        }
                    }

                    Passage? matchedPassage = null;
                    if (!string.IsNullOrWhiteSpace(questionDto.PassageId))
                    {
                        passageLookup.TryGetValue(questionDto.PassageId, out matchedPassage);
                    }


                    // Issue #454: per-question classification (already validated above) or the header.
                    questionClassifications.TryGetValue(questionDto, out var questionClassification);
                    var classificationSource = questionClassification?.Source ?? headerClassificationSource;

                    // issue #570: plan çözümsüz saklanır; çözüm ayrı alandan ya da (eski istemci) plan içindeki solution'dan
                    // (metodun başında doğrulandı).
                    var (storedPlan, storedSolution) = interactionByQuestion[questionDto];

                    var question = new Question
                    {
                        ImageUrl = questionImageUrl,
                        BookName = questionDto.BookName,
                        X = 0,
                        Y = 0,
                        Width = questionDto.Width,
                        Height = questionDto.Height,
                        SanitizedHeight = effectiveQuestionHeight,
                        IsExample = questionDto.IsExample,
                        PracticeCorrectAnswer = questionDto.IsExample ? questionDto.ExampleAnswer : null,
                        IsCanvasQuestion = true,
                        InteractionType = interactionType,
                        InteractionPlan = storedPlan,
                        InteractionSolution = storedSolution,
                        ShowPassageFirst = questionDto.ShowPassageFirst,
                        SubjectId = questionClassification != null ? questionClassification.SubjectId : soruDto.Header.SubjectId,
                        TopicId = questionClassification != null ? questionClassification.TopicId : soruDto.Header.TopicId,
                        PassageId = matchedPassage?.Id,
                        ClassificationSource = classificationSource
                    };
                    // issue #580: yeni soru — çözümü varsa kaynak/onay kuralları (Key/AI onay bekler, Human onaylı).
                    QuestionSolutionApproval.TryParseSource(questionDto.SolutionSource, out var bulkSolutionSource);
                    QuestionSolutionApproval.Apply(question,
                        hasSolution: IsInteractive(interactionType) && !string.IsNullOrWhiteSpace(storedSolution),
                        solutionChanged: true, bulkSolutionSource, questionDto.SolutionApproved == true,
                        questionDto.SolutionPromptVersion, solutionActor, DateTime.UtcNow, draftByQuestion.GetValueOrDefault(questionDto));

                    if (questionClassification != null)
                    {
                        if (questionClassification.SubTopicIds.Count > 0)
                        {
                            question.QuestionSubTopics = questionClassification.SubTopicIds.Select(subTopicId => new QuestionSubTopic
                            {
                                SubTopicId = subTopicId
                            }).ToList();
                        }
                    }
                    else if (soruDto.Header.Subtopics != null && soruDto.Header.Subtopics.Any())
                    {
                        question.QuestionSubTopics = soruDto.Header.Subtopics.Select(subTopicId => new QuestionSubTopic
                        {
                            SubTopicId = subTopicId
                        }).ToList();
                    }

                    _context.Questions.Add(question);
                    await _context.SaveChangesAsync();

                    createdQuestions.Add(question); // 🟢 Track for OutboxMessage

                    // Issue #577: kitap kaynağı (yalnız sunucu içi çağrıdan, işaretleme ekranı). Hash yüklenen question.jpg
                    // baytlarından — kayıt öncesi tekrar kontrolü (QuestionCropHash.Compute) aynı değeri üretir.
                    if (questionDto.Source is { } source && croppedQuestionImage != null)
                    {
                        _context.QuestionSources.Add(new QuestionSource
                        {
                            QuestionId = question.Id,
                            BookId = source.BookId,
                            FilePage = source.FilePage,
                            PrintedPage = source.PrintedPage,
                            ActivityLabel = source.ActivityLabel,
                            ItemLabel = source.ItemLabel,
                            CropSha256 = ExamApp.Api.Services.Labeling.QuestionCropHash.Of(croppedQuestionImage),
                        });
                        await _context.SaveChangesAsync();
                    }

                    if (!questionDto.IsExample)
                    {
                        var answers = sourceAnswers.Select(a => new Answer
                        {
                            QuestionId = question.Id,
                            Text = a.Label,
                            X = a.X,
                            Y = a.Y,
                            Width = a.Width,
                            Height = a.Height,
                            IsCanvasQuestion = true
                        }).ToList();

                        // Etkileşimli soruda (MCQ dışı) doğruluk InteractionSolution'dadır; doğru şık gerekmez.
                        var isInteractive = IsInteractive(interactionType);
                        var correctLabel = isInteractive
                            ? null
                            : sourceAnswers.FirstOrDefault(a => a.IsCorrect)?.Label;

                        if (!isInteractive && correctLabel == null)
                        {
                            throw new InvalidOperationException(_localizer["questions.bulk.correctAnswerMissing", questionDto.Name]);
                        }

                        _context.Answers.AddRange(answers);
                        await _context.SaveChangesAsync();

                        if (croppedQuestionImage != null && answerImagePayloads.Count == answers.Count)
                        {
                            for (var index = 0; index < answers.Count; index++)
                            {
                                var answerImage = answerImagePayloads[index];
                                if (answerImage == null)
                                {
                                    continue;
                                }

                                await using var answerStream = new MemoryStream(answerImage);
                                var answerImageUrl = await _minioService.UploadFileAsync(answerStream, $"{questionFolderPath}/{answers[index].Id}.jpg");
                                answers[index].ImageUrl = answerImageUrl;
                            }

                            await _context.SaveChangesAsync();
                        }

                        if (!isInteractive)
                        {
                            question.CorrectAnswerId = answers.FirstOrDefault(a => a.Text == correctLabel)?.Id;
                            await _context.SaveChangesAsync();
                        }
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
                // Record QuestionCreated events to Outbox for all newly created questions
                foreach (var question in createdQuestions)
                {
                    var @event = new QuestionCreatedEvent
                    {
                        QuestionId = question.Id,
                        SubjectId = question.SubjectId,
                        TopicId = question.TopicId,
                        GradeId = eventGradeId,
                        ClassificationSource = question.ClassificationSource?.ToString(),
                        // Issue #577: sınıflandırıcı içindekilerden gelen alt konuyu bilsin (Book + alt konu → atla).
                        SubTopicIds = question.QuestionSubTopics?.Select(st => st.SubTopicId).ToList() ?? new List<int>(),
                        CreatedAt = DateTime.UtcNow,
                        Text = question.Text
                    };

                    var outbox = new OutboxMessage
                    {
                        Type = OutboxEventRegistry.NameFor<QuestionCreatedEvent>(),
                        Content = JsonSerializer.Serialize(@event),
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.OutboxMessages.Add(outbox);
                }
                await _context.SaveChangesAsync();

                //     }
                // }

                await transaction.CommitAsync();
                return new ResponseBaseDto
                {
                    Success = true,
                    Message = _localizer["questions.bulk.saved"]
                };
                // return Ok(new { message = "Sorular başarıyla kaydedildi!" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = _localizer["questions.bulk.saveError", ex.Message]
                };
            }
        }
        });

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
                    Message = _localizer["questions.notFound"]
                };
            }
            if (string.IsNullOrEmpty(question.ImageUrl))
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = _localizer["questions.image.notFound"]
                };
            }
            // Resmi MinIO'dan indir
            var imageStream = await _minioService.GetFileStreamAsync(question.ImageUrl);
            if (imageStream == null)
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = _localizer["questions.image.downloadFailed"]
                };
            }

            // Resmi boyutlandır
            var resizedImage = await _imageHelper.ResizeImageAsync(imageStream, scale);
            if (resizedImage == null)
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = _localizer["questions.image.resizeFailed"]
                };
            }

            // Boyutlandırılmış resmi aynı bucket'ta, aynı questions/ klasöründe YENİ bir key'e yükle (issue #365: eskiden
            // saklanan URL'nin kendisi key olarak veriliyordu → `/img/{bucket}/img/{bucket}/...` çift URL ve anonim
            // okunabilir questions/* prefix'i dışında bir nesne). Yeni key tarayıcı önbelleğindeki eski görseli de aşar.
            if (!MinioObjectUrl.TryParse(question.ImageUrl, out var bucket, out var oldKey) ||
                !oldKey.StartsWith("questions/", StringComparison.Ordinal))
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = _localizer["questions.image.uploadFailed"]
                };
            }
            var folder = oldKey[..oldKey.LastIndexOf('/')];
            var newKey = $"{folder}/{Guid.NewGuid()}{Path.GetExtension(oldKey)}";
            var newUrl = await _minioService.UploadFileAsync(resizedImage, newKey, bucket);
            if (string.IsNullOrEmpty(newUrl))
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = _localizer["questions.image.uploadFailed"]
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
            if (question.SanitizedHeight != null)
                question.SanitizedHeight = (int)(question.SanitizedHeight * scale);

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
                Message = _localizer["questions.image.resized"]
            };
        }
        catch (Exception ex)
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = _localizer["questions.image.resizeError", ex.Message]
            };
        }
    }


    /// <summary>
    /// Issue #454: effective per-question classification. Field-level fallback to the header:
    /// SubjectId/TopicId null or 0 → header value; SubTopicIds empty → header subtopics, but only when the
    /// effective topic equals the header topic (header subtopics belong to the header topic, inheriting them
    /// under a different topic would always fail validation); Source null/unparseable → header source.
    /// </summary>
    private static EffectiveQuestionClassification ResolveQuestionClassification(
        BulkQuestionClassificationDto classification,
        HeaderInfo header,
        List<int> headerSubTopicIds,
        ClassificationSource headerSource)
    {
        var subjectId = classification.SubjectId is > 0 ? classification.SubjectId : header.SubjectId;
        var topicId = classification.TopicId is > 0 ? classification.TopicId : header.TopicId;

        var ownSubTopicIds = NormalizeSubTopicIds(classification.SubTopicIds);
        var subTopicIds = ownSubTopicIds.Count > 0
            ? ownSubTopicIds
            : topicId == header.TopicId ? headerSubTopicIds : new List<int>();

        var source = headerSource;
        if (!string.IsNullOrWhiteSpace(classification.Source) &&
            Enum.TryParse<ClassificationSource>(classification.Source.Trim(), ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed))
        {
            source = parsed;
        }

        return new EffectiveQuestionClassification(subjectId, topicId, subTopicIds, source);
    }

    private static List<int> NormalizeSubTopicIds(IEnumerable<int>? ids) =>
        ids?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();

    /// <summary>
    /// Classification consistency check shared by the header and per-question overrides (#454):
    /// the topic must exist and belong to the subject; all subtopics must exist, share one topic and that
    /// topic must equal <paramref name="topicId"/>; subtopics without a topic are rejected.
    /// Throws <see cref="InvalidOperationException"/> with the localized message (caller rolls back).
    /// </summary>
    private async Task ValidateClassificationAsync(
        int? subjectId, int? topicId, IEnumerable<int>? subtopicIds, ClassificationLookupCache cache)
    {
        Topic? topic = null;
        Subject? subject = null;

        if (subjectId.HasValue && subjectId.Value > 0)
        {
            subject = await GetSubjectAsync(subjectId.Value, cache);

            if (subject == null)
            {
                throw new InvalidOperationException(_localizer["questions.bulk.invalidSubjectId", subjectId.Value]);
            }
        }

        if (topicId.HasValue && topicId.Value > 0)
        {
            topic = await GetTopicAsync(topicId.Value, cache);

            if (topic == null)
            {
                throw new InvalidOperationException(_localizer["questions.bulk.invalidTopicId", topicId.Value]);
            }

            if (subjectId.HasValue && subjectId.Value > 0)
            {
                var questionSubjectId = subjectId.Value;
                if (topic.SubjectId != questionSubjectId)
                {
                    var unknownName = _localizer["questions.unknownName"].Value;
                    var subjectName = subject?.Name ?? unknownName;
                    var topicSubjectName = topic.Subject?.Name ?? unknownName;
                    throw new InvalidOperationException(_localizer[
                        "questions.bulk.subjectTopicMismatch",
                        questionSubjectId, subjectName, topic.Id, topic.Name,
                        topic.SubjectId, topicSubjectName]);
                }
            }
        }

        var normalizedSubtopicIds = NormalizeSubTopicIds(subtopicIds);
        if (normalizedSubtopicIds.Count == 0)
        {
            return;
        }

        var subtopics = await GetSubTopicsAsync(normalizedSubtopicIds, cache);

        var missingSubtopics = normalizedSubtopicIds.Except(subtopics.Select(st => st.Id)).ToList();
        if (missingSubtopics.Count > 0)
        {
            throw new InvalidOperationException(_localizer[
                "questions.bulk.invalidSubTopicIds", string.Join(", ", missingSubtopics)]);
        }

        var distinctTopicIds = subtopics
            .Select(st => st.TopicId)
            .Distinct()
            .ToList();

        if (distinctTopicIds.Count > 1)
        {
            var details = string.Join(", ", subtopics.Select(st =>
                $"SubTopic(Id={st.Id}, Name='{st.Name}') -> TopicId={st.TopicId} ('{st.Topic?.Name ?? "?"}')"));

            throw new InvalidOperationException(_localizer[
                "questions.bulk.subTopicsFromMultipleTopics", details]);
        }

        var subtopicsTopicId = distinctTopicIds[0];

        // If TopicId is missing but subtopics are present, we cannot reliably set Question.TopicId.
        if (!topicId.HasValue || topicId.Value <= 0)
        {
            var details = string.Join(", ", subtopics.Select(st =>
                $"SubTopic(Id={st.Id}, Name='{st.Name}') TopicId={st.TopicId} ('{st.Topic?.Name ?? "?"}')"));

            throw new InvalidOperationException(_localizer[
                "questions.bulk.topicIdMissingWithSubTopics", subtopicsTopicId, details]);
        }

        var questionTopicId = topicId.Value;
        if (subtopicsTopicId != questionTopicId)
        {
            var unknownName = _localizer["questions.unknownName"].Value;
            var questionTopicName = topic?.Name ?? unknownName;
            var subTopicExample = subtopics.First();
            var subTopicTopicName = subTopicExample.Topic?.Name ?? unknownName;

            throw new InvalidOperationException(_localizer[
                "questions.bulk.subTopicTopicMismatch",
                questionTopicId, questionTopicName, subTopicExample.Id,
                subTopicExample.Name, subTopicExample.TopicId, subTopicTopicName]);
        }
    }

    private async Task<Subject?> GetSubjectAsync(int id, ClassificationLookupCache cache)
    {
        if (!cache.Subjects.TryGetValue(id, out var subject))
        {
            subject = await _context.Subjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            cache.Subjects[id] = subject;
        }

        return subject;
    }

    private async Task<Topic?> GetTopicAsync(int id, ClassificationLookupCache cache)
    {
        if (!cache.Topics.TryGetValue(id, out var topic))
        {
            topic = await _context.Topics
                .AsNoTracking()
                .Include(t => t.Subject)
                .FirstOrDefaultAsync(t => t.Id == id);
            cache.Topics[id] = topic;
        }

        return topic;
    }

    /// <summary>Returns the existing subtopics among <paramref name="ids"/> (one query for the uncached ids).</summary>
    private async Task<List<SubTopic>> GetSubTopicsAsync(List<int> ids, ClassificationLookupCache cache)
    {
        var unknownIds = ids
            .Where(id => !cache.SubTopics.ContainsKey(id) && !cache.MissingSubTopicIds.Contains(id))
            .ToList();

        if (unknownIds.Count > 0)
        {
            var loaded = await _context.SubTopics
                .AsNoTracking()
                .Include(st => st.Topic)
                .Where(st => unknownIds.Contains(st.Id))
                .ToListAsync();

            foreach (var st in loaded)
            {
                cache.SubTopics[st.Id] = st;
            }

            foreach (var id in unknownIds.Where(id => !cache.SubTopics.ContainsKey(id)))
            {
                cache.MissingSubTopicIds.Add(id);
            }
        }

        return ids
            .Where(cache.SubTopics.ContainsKey)
            .Select(id => cache.SubTopics[id])
            .ToList();
    }

    private sealed record EffectiveQuestionClassification(
        int? SubjectId, int? TopicId, List<int> SubTopicIds, ClassificationSource Source);

    private sealed class ClassificationLookupCache
    {
        public Dictionary<int, Subject?> Subjects { get; } = new();
        public Dictionary<int, Topic?> Topics { get; } = new();
        public Dictionary<int, SubTopic> SubTopics { get; } = new();
        public HashSet<int> MissingSubTopicIds { get; } = new();
    }

    private sealed class AnswerCropInfo
    {
        public AnswerCropInfo(BulkAnswerDto source, double relativeX, double relativeY)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            RelativeX = relativeX;
            RelativeY = relativeY;
            EffectiveWidth = source.Width;
            EffectiveHeight = source.Height;
        }

        public BulkAnswerDto Source { get; }
        public double RelativeX { get; }
        public double RelativeY { get; }
        public int EffectiveWidth { get; set; }
        public int EffectiveHeight { get; set; }
    }

}
