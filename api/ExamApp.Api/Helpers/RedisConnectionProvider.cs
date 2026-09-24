using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #262: exam API'nin TEK Redis bağlantısı (<c>Redis:Configuration</c>). Hem <c>IDistributedCache</c>
/// (<c>RedisCacheOptions.ConnectionMultiplexerFactory</c>, Program.cs) hem dağıtık rate limit sayacı
/// (<see cref="RedisFixedWindowCounterStore"/>) aynı multiplexer'ı kullanır — ayrı bağlantı havuzu açılmaz.
/// </summary>
public interface IRedisConnectionProvider
{
    Task<IConnectionMultiplexer> GetConnectionAsync();
}

/// <summary>
/// Tembel bağlanır (ilk kullanımda). <c>AbortOnConnectFail=false</c>: Redis açılışta kapalıysa uygulama yine açılır,
/// multiplexer arka planda yeniden bağlanmayı dener; komutlar o arada hızlıca <see cref="RedisConnectionException"/> alır.
/// Bağlantı KURULAMAZSA (ör. yapılandırma hatası, istisna) hatalı görev önbellekte tutulmaz: sonraki çağrı yeniden dener.
/// </summary>
public sealed class RedisConnectionProvider : IRedisConnectionProvider, IDisposable
{
    public const string ConfigurationKey = "Redis:Configuration";

    private readonly Func<Task<IConnectionMultiplexer>> _connect;
    private readonly object _gate = new();
    private Task<IConnectionMultiplexer>? _connection;

    public RedisConnectionProvider(IConfiguration configuration)
    {
        var connectionString = configuration[ConfigurationKey];
        _connect = () => ConnectAsync(connectionString);
    }

    /// <summary>Test/özel kurulum: bağlantı fabrikası doğrudan verilir.</summary>
    internal RedisConnectionProvider(Func<Task<IConnectionMultiplexer>> connect) => _connect = connect;

    public Task<IConnectionMultiplexer> GetConnectionAsync()
    {
        lock (_gate)
        {
            // Hatalı/iptal edilmiş görev önbellekte kalmaz; bekleyen ya da başarılı görev paylaşılır.
            if (_connection is null || _connection.IsFaulted || _connection.IsCanceled)
                _connection = StartConnect();
            return _connection;
        }
    }

    private Task<IConnectionMultiplexer> StartConnect()
    {
        try
        {
            return _connect();
        }
        catch (Exception ex)
        {
            return Task.FromException<IConnectionMultiplexer>(ex); // senkron hata da bir sonraki çağrıda yeniden denenir
        }
    }

    private static async Task<IConnectionMultiplexer> ConnectAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"{ConfigurationKey} is not configured.");

        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        return await ConnectionMultiplexer.ConnectAsync(options);
    }

    public void Dispose()
    {
        Task<IConnectionMultiplexer>? connection;
        lock (_gate)
            connection = _connection;
        if (connection is { IsCompletedSuccessfully: true })
            connection.Result.Dispose();
    }
}
