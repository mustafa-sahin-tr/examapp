using System.Globalization;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// issue #315: randevu iptali, erteleme önerisi/kararı ve no-show bildirimi (<see cref="BookingLifecycleEvent"/>).
/// <list type="bullet">
/// <item>Karşı taraf (öğretmen ve/veya öğrenci, event'teki <c>NotifyTeacher</c>/<c>NotifyStudent</c>): her olay türü için
/// <c>Booking{Kind}</c> bildirimi.</item>
/// <item>Öğrencinin Active velileri: iptal, erteleme önerisi (kabulden ÖNCE haberdar olsunlar — security review O1), kabul
/// edilen erteleme ve no-show — <c>ParentBooking{Kind}</c>. Ret/geri çekme veliye gürültü olur, gönderilmez.</item>
/// </list>
/// Yazım ve idempotency <see cref="BookingParentNotifier"/> ile: alıcı başına <c>(Type, SourceEventId)</c> deterministik anahtar
/// (<c>booking-lifecycle:{EventId}</c> + UserId) — tekrar teslim yalnız eksik alıcıyı yazar. Sub çözülemeyen alıcı diğerlerini
/// engellemez; hata sonda fırlatılır (retry → <c>badge-service_error</c>).
/// </summary>
public class BookingLifecycleConsumer : IConsumer<BookingLifecycleEvent>
{
    public const string TypePrefix = "Booking";
    public const string ParentTypePrefix = "ParentBooking";

    /// <summary>Veliye bildirilen olay türleri.</summary>
    internal static readonly IReadOnlySet<string> ParentKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        BookingLifecycleKinds.Cancelled, BookingLifecycleKinds.RescheduleProposed, BookingLifecycleKinds.RescheduleAccepted,
        BookingLifecycleKinds.NoShowReported
    };

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<BookingLifecycleConsumer> _logger;

    public BookingLifecycleConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<BookingLifecycleConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public static string TypeFor(string kind) => TypePrefix + kind;

    public static string ParentTypeFor(string kind) => ParentTypePrefix + kind;

    public async Task Consume(ConsumeContext<BookingLifecycleEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        if (!IsKnownKind(e.Kind))
        {
            // Üretici ile sözleşme dışı tür: yeniden denemek düzeltmez → error kuyruğu.
            throw new InvalidOperationException($"BookingLifecycleEvent bilinmeyen Kind: '{e.Kind}' (BookingId={e.BookingId}).");
        }

        var scope = $"booking-lifecycle:{e.EventId:N}";
        var log = $"BookingId={e.BookingId}, Kind={e.Kind}";
        var data = new { bookingId = e.BookingId, studentId = e.StudentId, kind = e.Kind };
        var when = BookingWhenText.Format(e.Date, e.StartTime, e.EndTime);
        var newWhen = e.NewDate is DateOnly d && e.NewStartTime is TimeOnly s && e.NewEndTime is TimeOnly en
            ? BookingWhenText.Format(d, s, en)
            : string.Empty;

        Exception? failure = null;

        async Task Run(Func<Task> notify)
        {
            try
            {
                await notify();
            }
            catch (RecipientSubUnresolvedException ex)
            {
                failure ??= ex; // diğer alıcılar yine yazılsın
            }
        }

        var type = TypeFor(e.Kind);
        if (e.NotifyTeacher && e.TeacherUserId > 0)
        {
            await Run(() => BookingParentNotifier.NotifyAsync(_db, _hub, _localeResolver, _logger,
                [new BookingParentRecipient { ParentUserId = e.TeacherUserId, KeycloakId = e.TeacherKeycloakId }],
                type, scope, culture => CounterpartyText(e, culture, actorIsTeacher: false, when, newWhen), data, log,
                excludeParentUserId: null, ct));
        }

        if (e.NotifyStudent && e.StudentUserId > 0)
        {
            await Run(() => BookingParentNotifier.NotifyAsync(_db, _hub, _localeResolver, _logger,
                [new BookingParentRecipient { ParentUserId = e.StudentUserId, KeycloakId = e.StudentKeycloakId }],
                type, scope, culture => CounterpartyText(e, culture, actorIsTeacher: true, when, newWhen), data, log,
                excludeParentUserId: null, ct));
        }

        if (ParentKinds.Contains(e.Kind) && e.Parents is { Count: > 0 })
        {
            await Run(() => BookingParentNotifier.NotifyAsync(_db, _hub, _localeResolver, _logger, e.Parents,
                ParentTypeFor(e.Kind), scope, culture => ParentText(e, culture, when, newWhen), data, log,
                excludeParentUserId: null, ct));
        }

        if (failure != null)
            throw failure;
    }

    internal static bool IsKnownKind(string kind) => kind is BookingLifecycleKinds.Cancelled
        or BookingLifecycleKinds.RescheduleProposed or BookingLifecycleKinds.RescheduleAccepted
        or BookingLifecycleKinds.RescheduleDeclined or BookingLifecycleKinds.RescheduleWithdrawn
        or BookingLifecycleKinds.NoShowReported;

    /// <summary>{0} işlemi yapan, {1} mevcut zaman, {2} yeni zaman, {3} gerekçe eki.</summary>
    private LocalizedNotificationText CounterpartyText(
        BookingLifecycleEvent e, CultureInfo culture, bool actorIsTeacher, string when, string newWhen)
    {
        var actor = actorIsTeacher
            ? BookingParentNotifier.Teacher(_texts, culture, e.TeacherName)
            : Student(culture, e.StudentName);
        return _texts.Build(TypeFor(e.Kind), culture, actor, when, newWhen, ReasonSuffix(e, culture));
    }

    /// <summary>{0} öğretmen, {1} mevcut zaman, {2} yeni zaman, {3} gerekçe eki, {4} çocuk adı.</summary>
    private LocalizedNotificationText ParentText(BookingLifecycleEvent e, CultureInfo culture, string when, string newWhen)
    {
        var key = e.Kind == BookingLifecycleKinds.NoShowReported
            ? ParentTypeFor(e.Kind) + (e.NoShowParty == "Teacher" ? "Teacher" : "Student")
            : ParentTypeFor(e.Kind);
        return _texts.Build(key, culture, BookingParentNotifier.Teacher(_texts, culture, e.TeacherName), when, newWhen,
            ReasonSuffix(e, culture), BookingParentNotifier.Child(_texts, culture, e.StudentName));
    }

    private string ReasonSuffix(BookingLifecycleEvent e, CultureInfo culture)
    {
        var reason = CommentNotificationSupport.CleanBody(e.Reason);
        return string.IsNullOrWhiteSpace(reason)
            ? string.Empty
            : _texts.Resolve("notifications.common.rejectionReasonSuffix", culture, reason);
    }

    private string Student(CultureInfo culture, string? name)
    {
        var clean = CommentNotificationSupport.CleanName(name);
        return string.IsNullOrWhiteSpace(clean) ? _texts.Resolve("notifications.common.defaultStudent", culture) : clean;
    }
}

public class BookingLifecycleConsumerDefinition : ConsumerDefinition<BookingLifecycleConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<BookingLifecycleConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r => r.Immediate(3));
    }
}
