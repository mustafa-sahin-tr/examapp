namespace BadgeService.Services;

public interface IServiceTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
