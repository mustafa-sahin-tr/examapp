using System;
using System.Linq;
using System.Linq.Expressions;
using ExamApp.Api.Data;

namespace ExamApp.Api.Services.Questions;

/// <summary>
/// Issue #826: soru, testinden bağımsız olarak <see cref="Question.BankSharing"/> ile öğretmene açık mı. Havuz araması,
/// görüntüleme (<see cref="QuestionOwnershipGuard.CanViewQuestionAsync"/>) ve teste bağlama aynı koşulu kullanır.
/// Yalnız sahibi damgalı soru paylaşılır; okul dalı sahibin okulunu istekçininkiyle karşılaştırır, istekçi okulsuzsa üretilmez.
/// </summary>
internal static class QuestionBankSharingAccess
{
    public static Expression<Func<Question, bool>> SharedWithTeacherPredicate(AppDbContext context, int? requesterSchoolId)
    {
        if (!requesterSchoolId.HasValue)
            return q => q.CreateUserId > 0 && q.BankSharing == QuestionBankSharing.Everyone;

        var schoolId = requesterSchoolId.Value;
        return q => q.CreateUserId > 0
            && (q.BankSharing == QuestionBankSharing.Everyone
                || (q.BankSharing == QuestionBankSharing.School
                    && context.Teachers.Any(te => te.UserId == q.CreateUserId && te.SchoolId == schoolId)));
    }
}
