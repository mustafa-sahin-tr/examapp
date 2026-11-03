using Microsoft.AspNetCore.Http.Features;

/// <summary>
/// Kestrel'in varsayılan gövde sınırı 30 MB'dır ve gateway'de de geçerlidir: exam API'nin izin verdiği görselli soru paketi buraya gelmeden 413 alır.
/// Sınır yalnız paket yükleme yolunda, API'deki değere (<c>RequestSizeLimit</c>) yükseltilir; diğer yollar varsayılanda kalır.
/// Rol kapısından (<see cref="RouteRoleAuthExtensions"/>) sonra çalışır: admin olmayanın gövdesi hiç okunmaz.
/// </summary>
public static class UploadLimitExtensions
{
    public const long QuestionTransferPackageBytes = 200_000_000;
    private static readonly PathString QuestionTransferImports = "/api/exam/question-transfer/imports";

    public static IApplicationBuilder UseQuestionTransferUploadLimit(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method)
                && context.Request.Path.StartsWithSegments(QuestionTransferImports)
                && context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            {
                limit.MaxRequestBodySize = QuestionTransferPackageBytes;
            }
            return next();
        });
}
