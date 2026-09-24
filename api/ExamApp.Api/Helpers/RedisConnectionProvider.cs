using System;
using System.Threading;
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
/// </summary>
public sealed class RedisConnectionProvider : IRedisConnectionProvider, IDisposable
{
    public const string ConfigurationKey = "Redis:Configuration";

    private readonly Lazy<Task<IConnectionMultiplexer>> _connection;

    public RedisConnectionProvider(IConfiguration configuration)
    {
        var connectionString = configuration[ConfigurationKey];
        _connection = new Lazy<Task<IConnectionMultiplexer>>(() => ConnectAsync(connectionString),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public Task<IConnectionMultiplexer> GetConnectionAsync() => _connection.Value;

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
        if (_connection.IsValueCreated && _connection.Value.IsCompletedSuccessfully)
            _connection.Value.Result.Dispose();
    }
}
