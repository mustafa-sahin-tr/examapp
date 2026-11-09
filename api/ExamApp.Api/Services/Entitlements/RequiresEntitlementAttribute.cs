using ExamApp.Api.Controllers;
using ExamApp.Api.Models.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>
/// Issue #1087: uç noktayı çağıranın planına bağlar. Planda yoksa (sınır 0) 403 <c>entitlement.notInPlan</c>, aylık sınır dolduysa
/// 429 <c>entitlement.limitReached</c> (<see cref="EntitlementLimitResponse"/>). Rol muafiyetleri (Admin, içerik ekibi)
/// <see cref="IEntitlementService.CheckAsync"/>'tedir. Kullanım sayılmaz: sayılan uç noktalar başarıdan sonra kendisi kaydeder
/// (önbellekten dönen sonuç gibi bedava çağrılar sayılmasın). Yetki filtrelerinden sonra, model bağlamadan sonra çalışır.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresEntitlementAttribute(string featureKey) : Attribute, IAsyncActionFilter
{
    public string FeatureKey { get; } = featureKey;

    /// <summary>Yalnız "planda var mı"ya bakılır, dolu sınır engellemez (ör. sayılmış işi yeniden denemek).</summary>
    public bool InPlanOnly { get; init; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.Controller is not BaseController controller)
            throw new InvalidOperationException($"{nameof(RequiresEntitlementAttribute)} yalnız {nameof(BaseController)} üzerinde kullanılır.");

        var ct = context.HttpContext.RequestAborted;
        var user = await controller.ResolveUserForFilterAsync(ct);
        // Çözülemeyen kullanıcıyı uç noktanın kendi yanıtı karşılar (UserNotResolved); burada hak sorulmaz.
        if (BaseController.IsResolvedUser(user))
        {
            var entitlements = context.HttpContext.RequestServices.GetRequiredService<IEntitlementService>();
            var check = await entitlements.CheckAsync(EntitlementSubject.FromPrincipal(user.Id, context.HttpContext.User), FeatureKey, ct);
            if (check.Limit == 0)
            {
                context.Result = EntitlementLimitResponse.NotInPlan(FeatureKey, check);
                return;
            }
            if (!check.Allowed && !InPlanOnly)
            {
                context.Result = EntitlementLimitResponse.Create(FeatureKey, check);
                return;
            }
        }
        await next();
    }
}
