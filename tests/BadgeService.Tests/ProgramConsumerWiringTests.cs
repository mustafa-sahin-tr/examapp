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

        // ReceiveEndpoint bloğunu (cfg.ReceiveEndpoint("badge-service", e => { ... }); ) izole et — yalnızca
        // o blok içindeki ConfigureConsumer çağrıları sayılır (AddConsumer'ı da eşleştirip yanlış pozitif
        // vermemesi için).
        var endpointBlockMatch = Regex.Match(
            programSource,
            @"cfg\.ReceiveEndpoint\(""badge-service"",\s*e\s*=>\s*\{(?<body>.*?)\}\s*\);",
            RegexOptions.Singleline);
        endpointBlockMatch.Success.ShouldBeTrue("Program.cs içinde \"badge-service\" ReceiveEndpoint bloğu bulunamadı.");

        var endpointBody = endpointBlockMatch.Groups["body"].Value;
        var wiredConsumers = Regex.Matches(endpointBody, @"ConfigureConsumer<(?<name>\w+)>")
            .Select(m => m.Groups["name"].Value)
            .ToHashSet();

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
