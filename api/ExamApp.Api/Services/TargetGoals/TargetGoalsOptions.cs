using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.TargetGoals;

/// <summary>Issue #695: taban puanların dağıtımı hukuken incelenmedi; ayar kapanınca bütün yüzey (uçlar, menü) kapanır.</summary>
public sealed class TargetGoalsOptions
{
    public const string SectionName = "TargetGoals";

    public bool Enabled { get; set; } = true;
}

/// <summary>Kapalıyken uç yokmuş gibi 404 döner; yetki denetimi önce çalışır.</summary>
public sealed class RequireTargetGoalsAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<TargetGoalsOptions>>();
        if (!options.CurrentValue.Enabled)
            context.Result = new NotFoundResult();
    }
}

/// <summary>İstek kurallara uymuyor; <see cref="Exception.Message"/> ekranın çevirdiği koddur.</summary>
public sealed class TargetGoalRuleException(string code) : Exception(code);
