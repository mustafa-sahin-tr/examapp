using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>Self-servis plan uçlarının (#1028 kullanıcının kendisi, #1091 veli çocuğuna) ortak hata gövdesi <c>{ code }</c>.</summary>
public static class SubscriptionPlanErrorCodes
{
    public const string TrialUsed = "subscription.trialUsed";
    public const string PlanNotAvailable = "subscription.planNotAvailable";
    public const string UnknownPlan = "subscription.unknownPlan";

    /// <summary>#1108: kapsam / süre bu plan ve sınıf için satılmıyor.</summary>
    public const string InvalidOption = "subscription.invalidOption";

    /// <summary>#1091: öğrenci planını kendisi alamaz; velisi alır.</summary>
    public const string ParentPays = "subscription.parentPays";

    public static ActionResult ToResult(ControllerBase controller, SelfServicePlanError error) => error switch
    {
        SelfServicePlanError.TrialAlreadyUsed => controller.Conflict(new { code = TrialUsed }),
        SelfServicePlanError.NotAvailable => controller.BadRequest(new { code = PlanNotAvailable }),
        SelfServicePlanError.InvalidOption => controller.BadRequest(new { code = InvalidOption }),
        _ => controller.BadRequest(new { code = UnknownPlan }),
    };
}
