using System.Reflection;
using System.Text.RegularExpressions;
using BadgeService.Consumers;
using MassTransit;

namespace BadgeService.Tests;

/// <summary>
/// Issue #157 review bulgusu: <c>x.AddConsumer&lt;T&gt;()</c> (DI kaydı) yeterli değil — consumer'ın
/// mesaj alabilmesi için aynı zamanda <c>cfg.ReceiveEndpoint("badge-service", e =&gt; e.ConfigureConsumer&lt;T&gt;(...))</c>
/// içinde de bağlanması gerekir. <see cref="TeacherApplicationDecisionConsumer"/> ilk sürümde yalnızca
/// <c>AddConsumer</c> edilip endpoint'e bağlanmamıştı — mesaj hiç tüketilmiyordu, hiçbir unit test bunu
/// yakalayamadı (consumer'lar doğrudan çağrılarak test ediliyor, gerçek MassTransit endpoint kaydı
/// üzerinden değil).
///
/// <c>Program.cs</c> top-level statements olduğu ve test edilebilir bir <c>IBusRegistrationConfigurator</c>
/// üretmediği için burada kaynak metni üzerinde statik bir tutarlılık kontrolü yapılır: assembly'deki her
/// <see cref="IConsumer{T}"/> implementasyonu için Program.cs içinde bir <c>ConfigureConsumer&lt;T&gt;</c>
/// çağrısı olmalı. Yeni bir consumer eklenip endpoint'e bağlanmazsa bu test derleme zamanında değil ama
/// CI'da hemen kırılır.
/// </summary>
public class ProgramConsumerWiringTests
{
    [Fact]
    public void Every_IConsumer_implementation_is_wired_to_the_badge_service_receive_endpoint()
    {
        var consumerTypes = typeof(TeacherApplicationDecisionConsumer).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>)))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        consumerTypes.ShouldNotBeEmpty();

        var programSource = File.ReadAllText(FindProgramCs());

        // ReceiveEndpoint bloklarını (cfg.ReceiveEndpoint(..., e => { ... }); ) izole et — yalnızca o bloklardaki
        // ConfigureConsumer çağrıları sayılır (AddConsumer'ı da eşleştirip yanlış pozitif vermemesi için). Soru
        // sınıflandırıcısı kendi kuyruğundadır (QuestionCreatedConsumerDefinition.QueueName).
        var endpoints = Regex.Matches(
                programSource,
                @"cfg\.ReceiveEndpoint\((?<name>[^,]+),\s*e\s*=>\s*\{(?<body>.*?)\}\s*\);",
                RegexOptions.Singleline)
            .ToDictionary(m => m.Groups["name"].Value.Trim(), m => Regex.Matches(m.Groups["body"].Value, @"ConfigureConsumer<(?<name>\w+)>")
                .Select(c => c.Groups["name"].Value).ToHashSet());
        endpoints.ShouldContainKey("\"badge-service\"", "Program.cs içinde \"badge-service\" ReceiveEndpoint bloğu bulunamadı.");
        endpoints["QuestionCreatedConsumerDefinition.QueueName"].ShouldBe(["QuestionCreatedConsumer"]);

        var wiredConsumers = endpoints.Values.SelectMany(v => v).ToHashSet();

        var missing = consumerTypes.Where(c => !wiredConsumers.Contains(c)).ToList();

        missing.ShouldBeEmpty(
            $"Şu consumer'lar AddConsumer ile kaydedilmiş ama badge-service ReceiveEndpoint'inde " +
            $"ConfigureConsumer ile bağlanmamış (mesaj hiç tüketilmez): {string.Join(", ", missing)}");
    }

    private static string FindProgramCs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Services", "BadgeService", "Program.cs");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Services/BadgeService/Program.cs bulunamadı: " + AppContext.BaseDirectory);
    }
}
