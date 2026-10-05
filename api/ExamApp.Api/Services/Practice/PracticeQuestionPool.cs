using System.Collections.Generic;
using System.Linq;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Practice;

/// <summary>
/// Pratik soru havuzu (issue #62) — serbest pratik (<see cref="PracticeSessionService"/>) ve "Günün soruları"
/// (<see cref="DailyQuestionSetService"/>, issue #99) aynı kuralı paylaşır: soru, sınıfı öğrencininkiyle eşleşen ve
/// <see cref="WorksheetStudentVisibility.Normal"/> olan en az bir worksheet'te bulunmalı; <c>TopicId</c> doluysa
/// <c>Topic.GradeId</c> de eşleşmeli; değerlendirme <c>CorrectAnswerId</c>'ye dayandığı için o alan dolu olmalı.
/// </summary>
internal static class PracticeQuestionPool
{
    public static IQueryable<Question> Build(AppDbContext context, int gradeId, IReadOnlyCollection<int> subjectIds, IReadOnlyCollection<int> topicIds)
    {
        var pool = context.Questions
            .AsNoTracking()
            // Havuz filtresi: sınıfı eşleşen ve Normal görünürlükte en az bir worksheet'te olmalı.
            .Where(q => q.WorksheetQuestions.Any(wq =>
                wq.Worksheet.GradeId == gradeId &&
                wq.Worksheet.StudentVisibility == WorksheetStudentVisibility.Normal))
            // Sınıf eşleşmesi: TopicId doluysa Topic.GradeId belirleyici; boşsa worksheet sınıfı (yukarıda) yeter.
            .Where(q => q.TopicId == null || q.Topic.GradeId == gradeId)
            // Pratik değerlendirmesi SelectedAnswerId <-> CorrectAnswerId karşılaştırmasına dayanır;
            // sürükle-bırak gibi CorrectAnswerId'siz etkileşim tipleri bu MVP'de değerlendirilemez.
            .Where(q => q.CorrectAnswerId != null);

        if (subjectIds.Count > 0)
            pool = pool.Where(q => q.SubjectId != null && subjectIds.Contains(q.SubjectId.Value));

        if (topicIds.Count > 0)
            pool = pool.Where(q => q.TopicId != null && topicIds.Contains(q.TopicId.Value));

        return pool;
    }
}
