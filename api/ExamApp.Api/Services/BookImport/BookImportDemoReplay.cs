using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.BookImport;

/// <summary>
/// Issue #1032: demo işini gerçek aşama sırasıyla oynatır (sayfalar, kurulum, onay, sorular, inceleme). Claude ya da depolama
/// çağrısı yoktur: sorular kaynak işin taslak testinden işin sahibine kopyalanır, görsel ve pasaj dosyaları paylaşılır.
/// </summary>
internal sealed class BookImportDemoReplay(AppDbContext db, TimeSpan step, ILogger logger)
{
    public const int MaxQuestions = 5;
    public const int DefaultStepMilliseconds = 1500;

    private const int StageTicks = 4;

    private static readonly BookImportTestStatus[] SourceStatuses =
        [BookImportTestStatus.Done, BookImportTestStatus.NeedsReview, BookImportTestStatus.Approved];

    /// <summary>Kaynağın oynatılan testi: sorusu olan ilk test.</summary>
    internal static async Task<BookImportJobTest?> SourceTestAsync(AppDbContext db, Guid sourceJobId, CancellationToken ct)
    {
        var tests = await db.BookImportJobTests.AsNoTracking()
            .Where(t => t.JobId == sourceJobId && t.WorksheetId != null && SourceStatuses.Contains(t.Status))
            .OrderBy(t => t.Order)
            .ToListAsync(ct);
        foreach (var test in tests)
        {
            if (await SourceQuestionIds(db, test.WorksheetId!.Value).AnyAsync(ct))
                return test;
        }
        return null;
    }

    internal static IQueryable<int> SourceQuestionIds(AppDbContext db, int worksheetId) =>
        db.TestQuestions.IgnoreQueryFilters([DraftQueryFilter.Key]).AsNoTracking()
            .Where(wq => wq.TestId == worksheetId && !wq.IsDeleted && !wq.Question.IsDeleted)
            .OrderBy(wq => wq.Order).ThenBy(wq => wq.Id)
            .Select(wq => wq.QuestionId);

    public async Task RunAsync(BookImportJob job)
    {
        job.Status = BookImportJobStatus.Running;
        job.StartedAt ??= DateTime.UtcNow;
        job.Error = null;
        await db.SaveChangesAsync();
        try
        {
            if (job.Stage < BookImportStage.SetupConfirmation)
            {
                await PlayStageAsync(job, BookImportStage.Pages, job.PageCount ?? StageTicks);
                await PlayStageAsync(job, BookImportStage.Setup, job.PageCount ?? StageTicks);
                job.Stage = BookImportStage.SetupConfirmation;
                job.Status = BookImportJobStatus.AwaitingConfirmation;
                job.StageTotal = 0;
                job.StageProcessed = 0;
                await db.SaveChangesAsync();
                return;
            }

            job.Stage = BookImportStage.Questions;
            await db.SaveChangesAsync();
            await CopyQuestionsAsync(job);
            job.Stage = BookImportStage.Review;
            job.Status = BookImportJobStatus.ReadyForReview;
            await db.SaveChangesAsync();
        }
        catch (BookImportCancelled)
        {
            logger.LogInformation("Book import demo {JobId} cancelled in stage {Stage}", job.Id, job.Stage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Book import demo {JobId} failed in stage {Stage}", job.Id, job.Stage);
            db.ChangeTracker.Clear();
            await db.BookImportJobs.Where(j => j.Id == job.Id && j.Status != BookImportJobStatus.Cancelled)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, BookImportJobStatus.Failed)
                    .SetProperty(j => j.Error, "The demo could not be prepared."));
        }
    }

    private async Task PlayStageAsync(BookImportJob job, BookImportStage stage, int total)
    {
        job.Stage = stage;
        job.StageTotal = total;
        job.StageProcessed = 0;
        await db.SaveChangesAsync();
        for (var tick = 1; tick <= StageTicks; tick++)
        {
            await WaitAsync(job);
            job.StageProcessed = total * tick / StageTicks;
            await db.SaveChangesAsync();
        }
    }

    private async Task WaitAsync(BookImportJob job)
    {
        if (step > TimeSpan.Zero)
            await Task.Delay(step);
        await BookQuestionImportStage.StopIfCancelledAsync(db, job.Id, default);
    }

    private async Task CopyQuestionsAsync(BookImportJob job)
    {
        // Yeniden denemede (Hangfire, admin) bitmiş test ve kopyalanmış sorular atlanır.
        var tests = await db.BookImportJobTests.Where(t => t.JobId == job.Id && t.Status != BookImportTestStatus.Done).OrderBy(t => t.Order).ToListAsync();
        var source = await SourceTestAsync(db, job.DemoSourceJobId!.Value, default)
                     ?? throw new InvalidOperationException("The demo source has no questions.");
        var sourceIds = await SourceQuestionIds(db, source.WorksheetId!.Value).Take(MaxQuestions).ToListAsync();
        var questions = await db.Questions.IgnoreQueryFilters([DraftQueryFilter.Key]).AsNoTracking()
            .Include(q => q.Answers)
            .Include(q => q.QuestionSubTopics)
            .Include(q => q.QuestionLearningOutcomes)
            .Include(q => q.QuestionSkills)
            .Where(q => sourceIds.Contains(q.Id))
            .ToListAsync();
        var sources = await db.QuestionSources.AsNoTracking().Where(s => sourceIds.Contains(s.QuestionId)).ToDictionaryAsync(s => s.QuestionId);

        db.SetCurrentUser(job.CreateUserId!.Value);
        foreach (var test in tests)
        {
            test.Status = BookImportTestStatus.Running;
            test.QuestionsFound = sourceIds.Count;
            job.StageTotal = sourceIds.Count;
            job.StageProcessed = 0;
            await db.SaveChangesAsync();

            var worksheet = test.WorksheetId is { } worksheetId
                ? await db.Worksheets.IgnoreQueryFilters([DraftQueryFilter.Key]).Include(w => w.WorksheetQuestions).FirstAsync(w => w.Id == worksheetId)
                : BookQuestionImportStage.NewDraftTest(job, test);
            test.Worksheet = worksheet;
            var order = worksheet.WorksheetQuestions.Count(q => !q.IsDeleted);
            foreach (var id in sourceIds.Skip(order))
            {
                await WaitAsync(job);
                var original = questions.Single(q => q.Id == id);
                var copy = await CopyAsync(original, sources.GetValueOrDefault(id));
                worksheet.WorksheetQuestions.Add(new WorksheetQuestion { QuestionId = copy.Id, Order = ++order });
                test.QuestionsSaved = order;
                job.StageProcessed = order;
                await db.SaveChangesAsync();
            }

            test.Status = BookImportTestStatus.Done;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Sorunun işin sahibine ait özel taslak kopyası. Kaynak satırı kitaba bağlanmaz ve kırpım özeti rastgeledir: kopya, gerçek
    /// aktarımda aynı sayfanın "tekrar" sayılmasına yol açmasın.
    /// </summary>
    private async Task<Question> CopyAsync(Question q, QuestionSource? source)
    {
        var answers = q.Answers.OrderBy(a => a.Order).ThenBy(a => a.Id).ToList();
        var copy = new Question
        {
            Text = q.Text, SubText = q.SubText, ImageUrl = q.ImageUrl, BookName = q.BookName, SubjectId = q.SubjectId, Point = q.Point,
            TopicId = q.TopicId, DifficultyLevel = q.DifficultyLevel, PassageId = q.PassageId, ShowPassageFirst = q.ShowPassageFirst,
            PracticeCorrectAnswer = q.PracticeCorrectAnswer, IsExample = q.IsExample, AnswerColCount = q.AnswerColCount,
            InteractionType = q.InteractionType, InteractionPlan = q.InteractionPlan, InteractionSolution = q.InteractionSolution,
            X = q.X, Y = q.Y, Width = q.Width, Height = q.Height, SanitizedHeight = q.SanitizedHeight, SourcePageWidth = q.SourcePageWidth,
            LayoutChoice = q.LayoutChoice, IsCanvasQuestion = q.IsCanvasQuestion, ClassificationSource = q.ClassificationSource,
            ClassifierRevision = q.ClassifierRevision, SourceType = q.SourceType, RightsHolder = q.RightsHolder,
            LicenseReference = q.LicenseReference, LicenseEndsAt = q.LicenseEndsAt, IsUnpublished = q.IsUnpublished,
            IsContextBased = q.IsContextBased, BankSharing = QuestionBankSharing.Private, IsDraft = true,
            Answers = answers.Select(a => new Answer
            {
                Text = a.Text, ImageUrl = a.ImageUrl, X = a.X, Y = a.Y, Width = a.Width, Height = a.Height,
                IsCanvasQuestion = a.IsCanvasQuestion, Tag = a.Tag, Order = a.Order,
            }).ToList(),
            QuestionSubTopics = q.QuestionSubTopics.Select(s => new QuestionSubTopic { SubTopicId = s.SubTopicId }).ToList(),
            QuestionLearningOutcomes = q.QuestionLearningOutcomes.Select(o => new QuestionLearningOutcome { LearningOutcomeId = o.LearningOutcomeId }).ToList(),
            QuestionSkills = q.QuestionSkills.Select(s => new QuestionSkill { SkillId = s.SkillId }).ToList(),
        };
        db.Questions.Add(copy);
        await db.SaveChangesAsync();

        var correct = answers.FindIndex(a => a.Id == q.CorrectAnswerId);
        if (correct >= 0)
            copy.CorrectAnswerId = copy.Answers.OrderBy(a => a.Order).ThenBy(a => a.Id).ElementAt(correct).Id;
        db.QuestionSources.Add(new QuestionSource
        {
            QuestionId = copy.Id,
            FilePage = source?.FilePage ?? 0,
            PrintedPage = source?.PrintedPage,
            ActivityLabel = source?.ActivityLabel,
            ItemLabel = source?.ItemLabel,
            CropSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"demo:{Guid.NewGuid()}"))),
        });
        await db.SaveChangesAsync();
        return copy;
    }
}
