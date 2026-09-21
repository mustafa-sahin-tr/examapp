using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Services.Questions;

/// <summary>
/// Post-hoc edits to a question's classification and its place in a test:
/// setting the correct answer, (re)assigning subject/topic/subtopic + difficulty,
/// and soft-removing a question from a test. Extracted from QuestionService.
/// </summary>
public class QuestionClassificationService : IQuestionClassificationService
{
    private readonly AppDbContext _context;

    // Client'a dönen ResponseBaseDto.Message metinleri buradan gelir (issue #184).
    // DI her zaman gerçek localizer'ı verir; parametre yalnızca DI'sız kurulan (birim test)
    // senaryolarda varsayılan dile düşebilmek için opsiyonel.
    private readonly IStringLocalizer<Messages> _localizer;

    public QuestionClassificationService(
        AppDbContext context,
        IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    public async Task<ResponseBaseDto> UpdateCorrectAnswer(
        int questionId,
        int correctAnswerId)
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
                    Message = _localizer["questions.notFound"]
                };
            }

            // Verify that the correct answer ID belongs to this question
            var correctAnswer = question.Answers.FirstOrDefault(a => a.Id == correctAnswerId);
            if (correctAnswer == null)
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = _localizer["questions.classification.answerNotInQuestion"]
                };
            }

            // Update the correct answer ID
            question.CorrectAnswerId = correctAnswerId;

            _context.Questions.Update(question);
            await _context.SaveChangesAsync();

            return new ResponseBaseDto
            {
                Success = true,
                Message = _localizer["questions.classification.correctAnswerUpdated"]
            };
        }
        catch (Exception ex)
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = _localizer["questions.classification.correctAnswerUpdateError", ex.Message]
            };
        }
    }

    public async Task<ResponseBaseDto> UpdateQuestionClassification(
        int questionId,
        int? subjectId = null,
        int? topicId = null,
        int? subTopicId = null,
        int[]? subTopicIds = null,
        string? classificationSourceStr = null,
        int? difficulty = null)
    {
        try
        {
            var question = await _context.Questions
                .Include(q => q.QuestionSubTopics)
                .FirstOrDefaultAsync(q => q.Id == questionId);

            if (question == null)
            {
                return new ResponseBaseDto { Success = false, Message = _localizer["questions.notFound"] };
            }

            // Resolve effective subtopic IDs: subTopicIds takes precedence over subTopicId.
            int[] effectiveSubTopicIds;
            if (subTopicIds != null)
                effectiveSubTopicIds = subTopicIds.Where(id => id > 0).Distinct().ToArray();
            else if (subTopicId != null && subTopicId.Value > 0)
                effectiveSubTopicIds = new[] { subTopicId.Value };
            else
                effectiveSubTopicIds = Array.Empty<int>();

            if (effectiveSubTopicIds.Length > 0)
            {
                // Validate all subtopic IDs and load Topics in one query for derivation.
                var foundSubTopics = await _context.SubTopics
                    .Include(st => st.Topic)
                    .Where(st => effectiveSubTopicIds.Contains(st.Id))
                    .ToListAsync();

                var missing = effectiveSubTopicIds.Except(foundSubTopics.Select(st => st.Id)).ToArray();
                if (missing.Length > 0)
                {
                    return new ResponseBaseDto { Success = false, Message = _localizer["questions.classification.invalidSubTopicId"] };
                }

                // Derive topicId and subjectId from the first subtopic's topic.
                // Topic.SubjectId is already scoped to Topic.GradeId, so no extra lookup needed.
                var derivedTopic = foundSubTopics.First().Topic;
                question.TopicId = derivedTopic.Id;
                question.SubjectId = derivedTopic.SubjectId;

                // Replace subtopic mappings.
                if (question.QuestionSubTopics.Count > 0)
                {
                    _context.QuestionSubTopics.RemoveRange(question.QuestionSubTopics);
                    question.QuestionSubTopics.Clear();
                }

                foreach (var id in effectiveSubTopicIds)
                {
                    question.QuestionSubTopics.Add(new QuestionSubTopic
                    {
                        QuestionId = question.Id,
                        SubTopicId = id
                    });
                }
            }
            else
            {
                // No subtopics provided — apply explicit subjectId / topicId if given.
                if (subjectId != null)
                {
                    var normalized = subjectId.Value > 0 ? subjectId.Value : (int?)null;
                    if (normalized != null)
                    {
                        var exists = await _context.Subjects.AnyAsync(s => s.Id == normalized.Value);
                        if (!exists)
                            return new ResponseBaseDto { Success = false, Message = _localizer["questions.classification.invalidSubjectId"] };
                    }
                    question.SubjectId = normalized;
                }

                if (topicId != null)
                {
                    var normalized = topicId.Value > 0 ? topicId.Value : (int?)null;
                    if (normalized != null)
                    {
                        var exists = await _context.Topics.AnyAsync(t => t.Id == normalized.Value);
                        if (!exists)
                            return new ResponseBaseDto { Success = false, Message = _localizer["questions.classification.invalidTopicId"] };
                    }
                    question.TopicId = normalized;
                }

                // If subTopicIds was explicitly passed as an empty array, clear all mappings.
                if (subTopicIds != null && question.QuestionSubTopics.Count > 0)
                {
                    _context.QuestionSubTopics.RemoveRange(question.QuestionSubTopics);
                    question.QuestionSubTopics.Clear();
                }
            }

            // Default to "Human" if not provided
            var sourceStr = !string.IsNullOrEmpty(classificationSourceStr) ? classificationSourceStr : "Human";
            if (Enum.TryParse<ClassificationSource>(sourceStr, ignoreCase: true, out var parsedSource))
                question.ClassificationSource = parsedSource;
            else
                return new ResponseBaseDto { Success = false, Message = _localizer["questions.classification.invalidSource", sourceStr] };

            if (difficulty.HasValue)
            {
                if (difficulty.Value < 1 || difficulty.Value > 10)
                    return new ResponseBaseDto { Success = false, Message = _localizer["questions.classification.invalidDifficulty"] };

                question.DifficultyLevel = difficulty.Value;
            }

            _context.Questions.Update(question);
            await _context.SaveChangesAsync();

            return new ResponseBaseDto { Success = true, Message = _localizer["questions.classification.updated"] };
        }
        catch (Exception ex)
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = _localizer["questions.classification.updateError", ex.Message]
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
                    Message = _localizer["questions.classification.notInTest"]
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
                Message = _localizer["questions.classification.removedFromTest"]
            };
        }
        catch (Exception ex)
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = _localizer["questions.classification.removeFromTestError", ex.Message]
            };
        }
    }
}
