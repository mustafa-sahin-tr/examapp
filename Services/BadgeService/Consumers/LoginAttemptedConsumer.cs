using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>
/// Login denemesi (başarılı/başarısız) tüketicisi (issue #84). auth-api'nin outbox'a yazdığı
/// <see cref="LoginAttemptedEvent"/>'i alır ve exam API'nin <c>POST /api/login-events</c>
/// ucuna servis-servis token'ıyla (bkz. <see cref="GeminiQuestionClassifier"/> — aynı desen)
/// yazar. exam API'de kalıcı olan gerçek kayıt burada değil orada tutulur; bu consumer sadece
/// köprüdür.
///
/// Idempotency: aynı mesaj tekrar teslim edilebilir (broker redelivery veya nadir durumda
/// OutboxPublisher'ın aynı satırı iki kez publish etmesi). Dedup, event'in üretici tarafından
/// atanan <see cref="LoginAttemptedEvent.EventId"/>'si üzerinden yapılır (zaman damgasına
/// GÜVENİLMEZ — aynı UTC tick'ine düşen iki farklı deneme yanlışlıkla aynı sayılabilir).
/// <see cref="ProcessedLoginAttempt"/> tablosunda <c>EventId</c> üzerinde unique index tutulur —
/// POST başarılı olduktan SONRA bu satır eklenir; ikinci teslimde exists kontrolü veya unique
/// violation ile no-op olur.
///
/// Hata yolu: exam API çağrısı başarısız olursa (5xx, timeout, ağ hatası) exception fırlatılır ve
/// yutulmaz — <see cref="LoginAttemptedConsumerDefinition"/> 3 kez immediate retry uygular, hâlâ
/// başarısızsa mesaj <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır.
/// </summary>
public class LoginAttemptedConsumer : IConsumer<LoginAttemptedEvent>
{
    private readonly BadgeDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceTokenProvider _tokenProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LoginAttemptedConsumer> _logger;

    public LoginAttemptedConsumer(
        BadgeDbContext db,
        IHttpClientFactory httpClientFactory,
        IServiceTokenProvider tokenProvider,
        IConfiguration configuration,
        ILogger<LoginAttemptedConsumer> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<LoginAttemptedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        var alreadyProcessed = await _db.ProcessedLoginAttempts.AnyAsync(
            p => p.EventId == e.EventId,
            ct);
        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "LoginAttempted zaten işlenmiş (EventId={EventId}, KeycloakUserId={KeycloakUserId}); atlanıyor.",
                e.EventId, e.KeycloakUserId);
            return;
        }

        var examApiBaseUrl = _configuration["ExamApi:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(examApiBaseUrl))
        {
            _logger.LogWarning("⚠️ ExamApi:BaseUrl yapılandırılmamış. LoginAttempted event'i atlanıyor.");
            return;
        }

        var token = await _tokenProvider.GetAccessTokenAsync(ct);
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new
        {
            keycloakUserId = e.KeycloakUserId,
            role = e.Role,
            occurredAtUtc = e.OccurredAtUtc,
            success = e.Success
        };
        var bodyJson = JsonSerializer.Serialize(body);
        using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"{examApiBaseUrl}/api/login-events", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"login-events yazımı başarısız: {(int)response.StatusCode} {response.ReasonPhrase}. " +
                $"Request: {bodyJson}. Response: {Truncate(payload)}");
        }

        _db.ProcessedLoginAttempts.Add(new ProcessedLoginAttempt
        {
            EventId = e.EventId,
            KeycloakUserId = e.KeycloakUserId,
            OccurredAtUtc = e.OccurredAtUtc,
            Success = e.Success
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Eşzamanlı ikinci teslim unique index'e (EventId) takıldı — login-events tarafında
            // zaten yazıldı, idempotency defterine ikinci kez düşmesi engellendi. Aynı satırın exam
            // API'de iki kez oluşması riski kalır (dar bir eşzamanlılık penceresi) ama bu senaryo
            // login audit'i için kabul edilebilir; gerçek tekillik gereksinimi doğarsa login-events
            // ucuna da unique constraint eklenmeli (issue #6 kapsamı).
            _logger.LogInformation(
                "LoginAttempted eşzamanlı duplicate (EventId={EventId}); atlanıyor.", e.EventId);
            return;
        }

        _logger.LogInformation(
            "LoginAttempted işlendi. KeycloakUserId={KeycloakUserId}, Success={Success}",
            e.KeycloakUserId, e.Success);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    private static string Truncate(string value) => value.Length <= 2000 ? value : value[..2000];
}
