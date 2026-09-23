public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionHandlingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (UnauthorizedAccessException ex) when (!context.Response.HasStarted)
        {
            // issue #255: UnauthorizedAccessException servis katmanında sahiplik/erişim ihlali anlamında
            // fırlatılıyor (oturum geçersizliği değil). 401 dönmek UI'ın refresh → logout akışını (#241)
            // tetikleyip kullanıcıyı gereksiz yere oturumdan atıyordu → 403.
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = ex.Message });
        }
        catch (Exception)
        {
            throw;
        }
    }
}
