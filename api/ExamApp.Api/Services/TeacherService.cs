using System;
using System.Net.Http;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Tutors;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.TeacherApprovals;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services;

public class TeacherService : ITeacherService
{
    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApiClient;

    // Client'a ulasan mesajlar sozlukten gelir (issue #184). Localizer opsiyoneldir: DI disinda
    // olusturulan (birim test) ornekler varsayilan dile kilitli fallback'e duser.
    private readonly IStringLocalizer<Messages> _localizer;

    // issue #222: bağımsız (okulsuz) öğretmenin dashboard/overview/lagging görünümlerinde direkt öğrenci hedefleri
    // Approved Booking kapsamına daraltılır. DI her zaman kayıtlı policy'yi verir; parametre yalnızca DI'siz kurulan
    // (birim test) senaryolar için opsiyonel — varsayılan aynı kuralı uygulayan gerçek policy'dir.
    private readonly ISchoolAccessPolicy _schoolAccessPolicy;

    // issue #277 (madde 2): retten sonra yeni okul talebi bekleme süresi. DI her zaman bağlanmış options'ı verir;
    // DI'siz (birim test) kurulumda varsayılan (24 saat).
    private readonly TeacherSchoolRequestOptions _schoolRequestOptions;

    // issue #265: aktivite pencereleri yerel (varsayılan Europe/Istanbul) takvim günüdür. DI'siz kurulumda varsayılan takvim.
    private readonly ILocalDayCalendar _dayCalendar;

    // issue #265: iki aktivite ucunun ortak toplaması tek hesapta paylaşılır. DI'siz (birim test) kurulumda önbellek yok.
    private readonly ITeacherActivityCache? _activityCache;

    public TeacherService(AppDbContext context, IAuthApiClient authApiClient,
        IStringLocalizer<Messages>? localizer = null,
        ISchoolAccessPolicy? schoolAccessPolicy = null,
        IOptions<TeacherSchoolRequestOptions>? schoolRequestOptions = null,
        ILocalDayCalendar? dayCalendar = null,
        ITeacherActivityCache? activityCache = null)
    {
        _context = context;
        _authApiClient = authApiClient;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
        _schoolAccessPolicy = schoolAccessPolicy ?? new SchoolAccessPolicy(context);
        _schoolRequestOptions = schoolRequestOptions?.Value ?? new TeacherSchoolRequestOptions();
        _dayCalendar = dayCalendar ?? LocalDayCalendar.Default;
        _activityCache = activityCache;
    }

    /// <summary>
    /// issue #222/#235: dashboard/overview/lagging uçlarında atama hedeflerinin öğrenciye nasıl genişletileceği.
    /// <see cref="ExpandGradeAssignments"/>=false ise sınıf (grade-only) atamaları hiç dikkate alınmaz;
    /// <see cref="StudentScope"/> doluysa genişletilen öğrenci kümesinin TAMAMI (direkt + sınıf) o scope ile
    /// (<see cref="ISchoolAccessPolicy.ApplyScope{T}"/>) daraltılır. Yalnızca admin/servis için null.
    /// </summary>
    private readonly record struct StudentTargetScope(bool ExpandGradeAssignments, SchoolScope? StudentScope)
    {
        /// <summary>Admin/servis: kapsam yok (grade genişletmesi yalnızca atamanın SchoolId'siyle sınırlı, platform geneli).</summary>
        public static StudentTargetScope Unrestricted => new(true, null);

        /// <summary>Doğrulanmış okullu öğretmen: grade genişletmesi açık, öğrenci kümesi <paramref name="scope"/> ile sınırlı (#235).</summary>
        public static StudentTargetScope SchoolBound(SchoolScope scope) => new(true, scope);

        /// <summary>Bağımsız/okulsuz/doğrulanamayan: grade genişletmesi yok, öğrenciler Approved Booking'e daraltılır (#222).</summary>
        public static StudentTargetScope Narrow(int userId) => new(false, SchoolScope.For(userId, null));
    }

    /// <summary>
    /// issue #222/#235: tek karar noktası. Unrestricted → kapsamsız. Aksi halde okul ÖĞRETMEN kaydından (deterministik)
    /// doğrulanır (security Ö2: çok rollü hesapta scope okulu Students'tan gelebilir):
    /// okullu ve scope ile uyumlu → grade genişletmesi açık, öğrenci kümesi öğretmenin okuluyla sınırlı (#235 — başka
    /// okulun öğretmeni/admin'in bu worksheet'e yaptığı atamalar başka okulun öğrencisini göstermez);
    /// bağımsız, onay bekleyen okulsuz, öğretmen kaydı yok veya uyuşmazlık → en dar davranış (grade genişletmesi yok,
    /// öğrenciler Approved Booking'e daraltılır).
    /// </summary>
    private async Task<StudentTargetScope> ResolveStudentTargetScopeAsync(SchoolScope requester, CancellationToken ct)
    {
        if (requester.IsUnrestricted)
            return StudentTargetScope.Unrestricted;

        var teacher = await _context.ResolveTeacherRecordAsync(requester.UserId, ct);
        if (teacher.Exists && teacher.SchoolId.HasValue && teacher.SchoolId == requester.SchoolId)
            // Okul bilerek öğretmen kaydından alınır; requester.SchoolId ile eşitliği yukarıda doğrulandı.
            return StudentTargetScope.SchoolBound(SchoolScope.For(requester.UserId, teacher.SchoolId));

        return StudentTargetScope.Narrow(requester.UserId);
    }

    /// <summary>Üç panel ucunun ortak öğrenci kaynağı: kapsam (#235) burada, tek yerde uygulanır.</summary>
    private IQueryable<Student> TargetStudents(StudentTargetScope target)
    {
        var students = _context.Students.AsNoTracking();
        return target.StudentScope is { } scope ? _schoolAccessPolicy.ApplyScope(students, scope) : students;
    }

    public async Task<Teacher?> GetTeacher(int userId)
    {
        return await _context.Teachers
            .Where(t => t.UserId == userId)
            .FirstOrDefaultAsync();
    }

    public async Task<TeacherRegistrationResultDto> Save(int userId, RegisterTeacherDto dto)
    {
        // issue #277 (madde 1): okul adı, talep bildirimi (TeacherSchoolRequestSubmittedEvent) metni için aynı sorguda okunur.
        string? requestedSchoolName = null;
        if (dto.SchoolId.HasValue)
        {
            var school = await _context.Schools.AsNoTracking()
                .Where(s => s.Id == dto.SchoolId.Value)
                .Select(s => new { s.Name })
                .FirstOrDefaultAsync();
            if (school == null)
            {
                return new TeacherRegistrationResultDto
                {
                    Success = false,
                    Message = _localizer["teacher.schoolNotFound"]
                };
            }

            requestedSchoolName = school.Name;
        }

        // issue #234: okul üyeliği kullanıcı tarafından KURULAMAZ. İstekteki okul yalnızca bir talep olarak
        // RequestedSchoolId'ye yazılır; Teacher.SchoolId admin onayına kadar null kalır (okulsuz sayılır).
        // Bağımsız öğretmen kaydında okul talebi anlamsızdır — gönderilse de yok sayılır.
        var requestedSchoolId = dto.IsIndependentTutor ? null : dto.SchoolId;

        // issue #234 (security): öğretmen ve öğrenci kaydı birbirini dışlar (bkz. StudentService.Save). Öğrenci kaydı
        // olan kullanıcı Teacher rolü alıp öğrenci okul bağını öğretmen kapsamına taşıyamaz.
        if (await _context.Students.AnyAsync(s => s.UserId == userId))
        {
            return new TeacherRegistrationResultDto
            {
                Success = false,
                Conflict = true,
                Message = _localizer["teacher.studentRecordExists"]
            };
        }

        // Canlı satır tektir (#259 filtreli unique index); OrderBy ResolveTeacherRecordAsync ile aynı deterministik sıra.
        var existingTeacher = await _context.Teachers
            .OrderBy(t => t.Id)
            .FirstOrDefaultAsync(s => s.UserId == userId);
        var isUpdate = existingTeacher != null;

        // Karar mantığı execution strategy lambda'sının DIŞINDA: retry'da lambda yeniden çalışır ve
        // tracked entity'nin IsIndependentTutor alanı ilk denemede zaten dto değerine set edilmiş
        // olacağından "değişti mi" karşılaştırması ikinci denemede yanlış sonuç verirdi.
        Teacher teacher;
        var shouldPublishIndependentTeacherEvent = false;
        var shouldPublishApplicationSubmittedEvent = false;
        var shouldPublishSchoolRequestEvent = false;
        var schoolApprovalPending = false;

        if (existingTeacher != null)
        {
            teacher = existingTeacher;

            // issue #234: mevcut kaydın onaylı okul bağı (SchoolId) bu uçla DEĞİŞTİRİLEMEZ.
            //  - Bağımsız (Pending/Approved/Rejected) → okula bağlı geçiş: 409 (okul bağı + kendini onaylama yolu).
            //  - Onaylı SchoolId varken farklı okul: 409. Aynı okul: idempotent.
            //  - Bekleyen (Pending) okul talebi varken farklı okul: 409. Aynı okul: idempotent.
            //  - Okulsuz ve bekleyen talebi yok (talebi reddedilmiş ya da eski Approved okulsuz kayıt): yeni okul
            //    talebi açılır → RequestedSchoolId=yeni, Pending, RejectionReason=null; SchoolId null kalır.
            var leavesIndependence = teacher.IsIndependentTutor && !dto.IsIndependentTutor;
            var hasPendingSchoolRequest = teacher.RequestedSchoolId.HasValue
                && teacher.ApprovalStatus == TeacherApprovalStatus.Pending;

            var requestsDifferentSchool = !teacher.IsIndependentTutor && requestedSchoolId.HasValue && (
                teacher.SchoolId.HasValue
                    ? requestedSchoolId != teacher.SchoolId
                    : hasPendingSchoolRequest && requestedSchoolId != teacher.RequestedSchoolId);

            var opensSchoolRequest = !teacher.IsIndependentTutor && !dto.IsIndependentTutor
                && requestedSchoolId.HasValue && !teacher.SchoolId.HasValue && !hasPendingSchoolRequest;

            if (requestsDifferentSchool || leavesIndependence)
            {
                return new TeacherRegistrationResultDto
                {
                    Success = false,
                    Conflict = true,
                    Message = _localizer["teacher.registrationChangeNotAllowed"],
                    ObjectId = teacher.Id,
                    SchoolId = teacher.SchoolId,
                    RequestedSchoolId = teacher.RequestedSchoolId,
                    ApprovalStatus = teacher.ApprovalStatus,
                    AccountApproved = teacher.AccountApprovedAt != null
                };
            }

            // issue #277 (madde 2): reddedilen öğretmen, son ret anından itibaren bekleme süresi
            // (TeacherApprovals:SchoolRequestCooldownHours, varsayılan 24 saat) dolmadan yeni okul talebi açamaz → 429.
            // Yalnızca YENİ talep açılışını keser; idempotent tekrarlar ve bağımsızlığa geçiş etkilenmez.
            if (opensSchoolRequest && teacher.ApprovalStatus == TeacherApprovalStatus.Rejected
                && teacher.LastRejectedAt.HasValue && _schoolRequestOptions.SchoolRequestCooldownHours > 0)
            {
                var retryAfterUtc = teacher.LastRejectedAt.Value.AddHours(_schoolRequestOptions.SchoolRequestCooldownHours);
                var remaining = retryAfterUtc - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    return new TeacherRegistrationResultDto
                    {
                        Success = false,
                        TooManyRequests = true,
                        RetryAfterUtc = retryAfterUtc,
                        Message = _localizer["teacher.schoolRequestCooldown", (int)Math.Ceiling(remaining.TotalHours)],
                        ObjectId = teacher.Id,
                        SchoolId = teacher.SchoolId,
                        RequestedSchoolId = teacher.RequestedSchoolId,
                        ApprovalStatus = teacher.ApprovalStatus,
                        AccountApproved = teacher.AccountApprovedAt != null
                    };
                }
            }

            // Tek izin verilen geçiş: okula bağlı → bağımsız (issue #92). ApprovalStatus yalnızca bu geçişte
            // Pending'e çekilir; aksi halde admin'in verdiği karar tekrar register çağrısıyla değişmez.
            // Bekleyen okul talebi varsa geri çekilir (bağımsız başvuruda okul talebi anlamsız).
            // issue #277 (madde 3, #235 security notu): onaylı okul bağı (SchoolId) bu geçişte HEMEN kaldırılır — eskiden
            // korunuyordu ve bağımsız başvuru onay beklerken (hatta reddedilse de) eski okulun kapsamı (okul öğrencileri,
            // sınıf atamaları, SchoolOnly paylaşımlar) sürüyordu. Öğretmen HESABI onaylı kalır (#287: AccountApprovedAt'e
            // dokunulmaz) — yalnızca okul kapsamlı veri erişimini kaybeder. Mevcut worksheet/atama verisi silinmez
            // (atamaların kendi SchoolId'si korunur; bkz. #277 raporu). Okul bağı bu uçtan geri KURULAMAZ
            // (bağımsız → okullu geçiş 409); yeniden bağlanma admin yoluyla.
            var becomesIndependent = !teacher.IsIndependentTutor && dto.IsIndependentTutor;
            if (becomesIndependent)
            {
                teacher.IsIndependentTutor = true;
                teacher.ApprovalStatus = TeacherApprovalStatus.Pending;
                teacher.RequestedSchoolId = null;
                teacher.SchoolId = null;
            }
            else if (opensSchoolRequest)
            {
                teacher.RequestedSchoolId = requestedSchoolId;
                teacher.ApprovalStatus = TeacherApprovalStatus.Pending;
                teacher.RejectionReason = null;
                schoolApprovalPending = true;
            }

            // Bağımsız geçişte ve yeni okul talebinde aynı transaction içinde ilgili outbox event'leri yazılır.
            // Aynı değerle tekrar submit (idempotent) event üretmez.
            shouldPublishIndependentTeacherEvent = becomesIndependent;
            shouldPublishApplicationSubmittedEvent = becomesIndependent;
            shouldPublishSchoolRequestEvent = opensSchoolRequest && !becomesIndependent;
        }
        else
        {
            // issue #287: HER yeni öğretmen kaydı admin onayı bekler — bağımsız öğretmen (#92), okul talebi (#234) ve
            // okul talebi olmayan/bağımsız olmayan kayıt (eskiden onaylı başlıyordu). Hesap onayı (AccountApprovedAt)
            // null başlar; öğretmen özellikleri ilk admin onayına kadar kapalıdır (ApprovedTeacher policy). Keycloak
            // Teacher rolü yine kayıtta verilir (TeacherController.RegisterTeacher).
            schoolApprovalPending = requestedSchoolId.HasValue;
            teacher = new Teacher
            {
                UserId = userId,
                SchoolId = null,
                RequestedSchoolId = requestedSchoolId,
                IsIndependentTutor = dto.IsIndependentTutor,
                ApprovalStatus = TeacherApprovalStatus.Pending,
                AccountApprovedAt = null
            };

            // Yeni bağımsız öğretmen kaydı hem yeni bağımsız kayıt event'ini hem Pending başvuru event'ini üretir.
            // Okul bağlantısı talebi TeacherApplicationSubmittedEvent YAZMAZ (onun tüketicisi metni "bağımsız öğretmen
            // başvurusu" olarak üretir); issue #277 (madde 1): talep için ayrı TeacherSchoolRequestSubmittedEvent yazılır.
            shouldPublishIndependentTeacherEvent = dto.IsIndependentTutor;
            shouldPublishApplicationSubmittedEvent = dto.IsIndependentTutor;
            shouldPublishSchoolRequestEvent = requestedSchoolId.HasValue;
        }

        // Başvuran adı auth-api'den best-effort çözülür (issue #94 / #277 admin bildirimi için).
        // Dış HTTP çağrısı transaction/retry lambda'sının DIŞINDA tutulur ki retry'da tekrarlanmasın.
        var applicantName = (shouldPublishIndependentTeacherEvent || shouldPublishApplicationSubmittedEvent
                             || shouldPublishSchoolRequestEvent)
            ? await ResolveApplicantNameAsync(userId)
            : null;

        // issue #277 (madde 1): okul talebi event'inin kimliği ve anı lambda DIŞINDA bir kez belirlenir — retry'da aynı talep
        // için aynı EventId yazılır (tüketici tekilleştirmesi). Outbox satırı her denemede yeniden kurulur (aşağıya bkz.).
        var schoolRequestEventId = Guid.NewGuid();
        var schoolRequestSubmittedAt = DateTime.UtcNow;

        // issue #277 takip (retry güvenliği): execution strategy (Aspire Npgsql retry-on-failure) geçici bir hatada —
        // özellikle SaveChanges'ler başarılı olup COMMIT düştüğünde — lambda'yı baştan çalıştırır. Önceki denemenin
        // SaveChanges'i değişiklikleri "kabul etmiş" olur (Unchanged + DB'de geri alınmış Id), bu yüzden eskiden retry'da
        // teacher/outbox satırları sessizce YAZILMADAN commit edilebiliyordu. Artık her deneme temiz başlar:
        //  - ChangeTracker temizlenir;
        //  - güncellemede kayıt transaction içinde yeniden okunur ve YALNIZCA kararla değişen alanlar (aşağıdaki anlık
        //    görüntü) üzerine yazılır — karar mantığı lambda dışında kalır (retry'da "değişti mi" karşılaştırması bozulmasın);
        //  - yeni kayıtta geri alınmış denemenin Id'si sıfırlanıp entity yeniden eklenir;
        //  - outbox satırları her denemede yeni nesnelerle eklenir.
        var modifiedTeacherValues = isUpdate
            ? _context.Entry(teacher).Properties
                .Where(p => p.IsModified)
                .Select(p => (Name: p.Metadata.Name, Value: p.CurrentValue))
                .ToList()
            : null;

        // issue #277 review (NIT 4): karar (bağımsıza geçiş / yeni okul talebi) kilitsiz okunan duruma göre verildi. Aynı
        // kullanıcının eşzamanlı iki isteği (ör. iki paralel okul talebi → iki admin bildirimi, son yazan kazanır) aynı kararı
        // verebilirdi. Geçiş varsa karar girdileri kilidin ALTINDA yeniden okunan satırla karşılaştırılır; değişmişse yazmadan
        // 409 (registrationConflict) — ikinci istek, birincinin commit'inden sonra güncel durumla tekrar denenebilir.
        (bool IsIndependentTutor, TeacherApprovalStatus ApprovalStatus, int? RequestedSchoolId, int? SchoolId)? decisionSnapshot =
            isUpdate && modifiedTeacherValues!.Count > 0 ? DecisionInputs(_context.Entry(teacher), original: true) : null;

        var teacherId = 0;
        var studentRecordRace = false;
        var concurrentDecisionChange = false;
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                studentRecordRace = false;
                concurrentDecisionChange = false;

                await using var tx = await _context.Database.BeginTransactionAsync();

                // issue #277 (madde 9): öğretmen/öğrenci kaydı birbirini dışlar; yukarıdaki kontrol kilitsizdir. Eşzamanlı
                // student/register ile yarışı kapatmak için kontrol, kullanıcı kaydı kilidi (StudentService.Save ile aynı
                // anahtar) altında transaction içinde TEKRARLANIR. Öğrenci satırı varsa hiçbir şey yazılmadan geri alınır.
                await _context.Database.AcquireUserRegistrationLockAsync(userId);
                if (await _context.Students.AnyAsync(s => s.UserId == userId))
                {
                    studentRecordRace = true;
                    return; // commit yok → dispose'da rollback
                }

                if (isUpdate)
                {
                    var tracked = await _context.Teachers.AsTracking().FirstAsync(t => t.Id == teacher.Id);
                    var entry = _context.Entry(tracked);
                    if (decisionSnapshot is { } expected && expected != DecisionInputs(entry, original: false))
                    {
                        concurrentDecisionChange = true;
                        return; // commit yok → dispose'da rollback
                    }

                    foreach (var (name, value) in modifiedTeacherValues!)
                        entry.Property(name).CurrentValue = value;
                }
                else
                {
                    teacher.Id = 0; // geri alınmış önceki denemenin identity değeri taşınmasın
                    _context.Teachers.Add(teacher);
                }

                await _context.SaveChangesAsync();
                var savedTeacherId = teacher.Id; // güncellemede kayıt Id'si değişmez; yeni kayıtta identity burada set edildi

                if (shouldPublishIndependentTeacherEvent)
                {
                    // Aynı Pending başvuru için iki ayrı tüketici ucu var:
                    //  - IndependentTeacherRegisteredEvent (issue #92, bilgi amaçlı log consumer'ı)
                    //  - TeacherApplicationSubmittedEvent (issue #94, admin bildirimi + SignalR)
                    // Her ikisi de Teacher.Id identity ile üretildiği için ilk SaveChanges'ten sonra yazılır.
                    var now = DateTime.UtcNow;
                    _context.OutboxMessages.Add(new OutboxMessage
                    {
                        Type = OutboxEventRegistry.NameFor<IndependentTeacherRegisteredEvent>(),
                        Content = JsonSerializer.Serialize(new IndependentTeacherRegisteredEvent
                        {
                            TeacherId = savedTeacherId,
                            UserId = userId,
                            IsNewRegistration = !isUpdate,
                            RegisteredAt = now
                        }),
                        CreatedAt = now
                    });
                    AddTeacherApplicationSubmittedOutbox(savedTeacherId, userId, applicantName);
                }

                if (shouldPublishSchoolRequestEvent)
                {
                    _context.OutboxMessages.Add(new OutboxMessage
                    {
                        Type = OutboxEventRegistry.NameFor<TeacherSchoolRequestSubmittedEvent>(),
                        CreatedAt = schoolRequestSubmittedAt,
                        Content = JsonSerializer.Serialize(new TeacherSchoolRequestSubmittedEvent
                        {
                            EventId = schoolRequestEventId,
                            TeacherId = savedTeacherId,
                            UserId = userId,
                            RequestedSchoolId = teacher.RequestedSchoolId!.Value,
                            RequestedSchoolName = requestedSchoolName,
                            ApplicantName = applicantName,
                            IsNewRegistration = !isUpdate,
                            SubmittedAtUtc = schoolRequestSubmittedAt
                        })
                    });
                }

                if (shouldPublishIndependentTeacherEvent || shouldPublishApplicationSubmittedEvent || shouldPublishSchoolRequestEvent)
                {
                    await _context.SaveChangesAsync();
                }

                await tx.CommitAsync();
                teacherId = savedTeacherId;
            });
        }
        catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
        {
            // issue #259: eşzamanlı ilk kayıt — diğer istek aynı kullanıcı için canlı Teachers satırını önce yazdı
            // (filtreli unique index). Transaction geri alındı (outbox satırı da yazılmadı); ikinci satır açılmaz, #234
            // okul kilidi çift satırla aşılamaz. Unique ihlali geçici hata değil → execution strategy retry etmez.
            return new TeacherRegistrationResultDto
            {
                Success = false,
                Conflict = true,
                Message = _localizer["teacher.registrationConflict"]
            };
        }

        if (concurrentDecisionChange)
        {
            return new TeacherRegistrationResultDto
            {
                Success = false,
                Conflict = true,
                Message = _localizer["teacher.registrationConflict"]
            };
        }

        if (studentRecordRace)
        {
            return new TeacherRegistrationResultDto
            {
                Success = false,
                Conflict = true,
                Message = _localizer["teacher.studentRecordExists"]
            };
        }

        return new TeacherRegistrationResultDto
        {
            Success = true,
            Message = schoolApprovalPending
                ? _localizer["teacher.savedSchoolApprovalPending"]
                : isUpdate ? _localizer["teacher.updated"] : _localizer["teacher.savedApprovalPending"],
            ObjectId = teacherId,
            SchoolId = teacher.SchoolId,
            RequestedSchoolId = teacher.RequestedSchoolId,
            ApprovalStatus = teacher.ApprovalStatus,
            AccountApproved = teacher.AccountApprovedAt != null
        };
    }

    /// <summary>issue #277 review (NIT 4): Save'in kararını belirleyen alanlar (orijinal ya da mevcut değerleriyle).</summary>
    private static (bool IsIndependentTutor, TeacherApprovalStatus ApprovalStatus, int? RequestedSchoolId, int? SchoolId)
        DecisionInputs(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<Teacher> entry, bool original)
    {
        T Get<T>(string name) => (T)(original ? entry.Property(name).OriginalValue : entry.Property(name).CurrentValue)!;
        return (Get<bool>(nameof(Teacher.IsIndependentTutor)),
            Get<TeacherApprovalStatus>(nameof(Teacher.ApprovalStatus)),
            (int?)(original ? entry.Property(nameof(Teacher.RequestedSchoolId)).OriginalValue : entry.Property(nameof(Teacher.RequestedSchoolId)).CurrentValue),
            (int?)(original ? entry.Property(nameof(Teacher.SchoolId)).OriginalValue : entry.Property(nameof(Teacher.SchoolId)).CurrentValue));
    }

    /// <summary>
    /// Bağımsız öğretmen onay akışı (issue #94): yeni bir Pending başvuru oluştuğunda
    /// (yeni kayıt VEYA IsIndependentTutor=false→true geçişi ile Pending'e düşen kayıt)
    /// <see cref="TeacherApplicationSubmittedEvent"/>'i outbox'a ekler. Çağıranın, teacherId'yi
    /// zaten bilmesi gerekir — yeni kayıt yolunda bu yalnızca ilk SaveChangesAsync (identity insert)
    /// tamamlandıktan sonra mümkündür, bkz. çağrı yeri. applicantName çağıran tarafından
    /// transaction dışında çözülür (<see cref="ResolveApplicantNameAsync"/>).
    /// </summary>
    private void AddTeacherApplicationSubmittedOutbox(int teacherId, int userId, string? applicantName)
    {
        _context.OutboxMessages.Add(new OutboxMessage
        {
            Type = OutboxEventRegistry.NameFor<TeacherApplicationSubmittedEvent>(),
            Content = JsonSerializer.Serialize(new TeacherApplicationSubmittedEvent
            {
                TeacherId = teacherId,
                UserId = userId,
                ApplicantName = applicantName,
                SubmittedAt = DateTime.UtcNow
            }),
            CreatedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// UserId'yi isme çevirir (WorksheetAccessRequestService ile aynı best-effort desen).
    /// auth-api erişilemezse null döner — event yine de yazılır, consumer "Bir öğretmen" fallback'i kullanır.
    /// </summary>
    private async Task<string?> ResolveApplicantNameAsync(int userId)
    {
        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(new[] { userId });
            return users.FirstOrDefault(u => u.Id == userId)?.FullName;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    public async Task<UpdateThemeDto> UpdateTeacherTheme(int userId, string themePreset, string? themeCustomConfig)
    {
        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == userId);
        if (teacher == null)
        {
            return new UpdateThemeDto
            {
                Success = false,
                Message = _localizer["teacher.notFound"]
            };
        }

        teacher.ThemePreset = themePreset;
        teacher.ThemeCustomConfig = themeCustomConfig;

        await _context.SaveChangesAsync();

        return new UpdateThemeDto
        {
            Success = true,
            Message = _localizer["teacher.themeUpdated"],
            ObjectId = teacher.Id,
            ThemePreset = teacher.ThemePreset,
            ThemeCustomConfig = teacher.ThemeCustomConfig
        };
    }

    public async Task<TeacherDashboardSummaryDto> GetDashboardSummaryAsync(SchoolScope requester, CancellationToken ct = default)
    {
        var teacherId = requester.UserId;

        // Sahiplik: sadece CreateUserId == teacherId olan worksheet'ler; paylaşılanlar hariç.
        // Soft-delete edilmiş kayıtlar AppDbContext global query filter ile zaten dışarıda.
        var ownedWorksheets = _context.Worksheets
            .AsNoTracking()
            .Where(w => w.CreateUserId == teacherId);

        var totalWorksheets = await ownedWorksheets.CountAsync(ct);

        if (totalWorksheets == 0)
        {
            return new TeacherDashboardSummaryDto { TotalWorksheets = 0, TotalUniqueStudents = 0 };
        }

        // Sahip olunan worksheet'lerin atamaları; sadece hedefleme alanları projeksiyonlanır.
        // issue #222: kapsam daraltılmışsa sınıf atamaları hiç çekilmez (grade→öğrenci genişletmesi yok).
        var target = await ResolveStudentTargetScopeAsync(requester, ct);
        var expandGrades = target.ExpandGradeAssignments;
        var assignmentTargets = await _context.WorksheetAssignments
            .AsNoTracking()
            .Where(wa => ownedWorksheets.Any(w => w.Id == wa.WorksheetId))
            .Where(wa => expandGrades || wa.StudentId != null)
            .Select(wa => new { wa.StudentId, wa.GradeId, wa.SchoolId })
            .ToListAsync(ct);

        if (assignmentTargets.Count == 0)
        {
            return new TeacherDashboardSummaryDto { TotalWorksheets = totalWorksheets, TotalUniqueStudents = 0 };
        }

        // WorksheetAssignmentService ile aynı genişletme mantığı:
        //  - StudentId dolu -> direkt öğrenci
        //  - GradeId dolu, StudentId boş -> o sınıftaki tüm öğrenciler (SchoolId doluysa o okulla sınırlı)
        var directStudentIds = assignmentTargets
            .Where(a => a.StudentId.HasValue)
            .Select(a => a.StudentId!.Value)
            .Distinct()
            .ToList();

        var gradeTargets = assignmentTargets
            .Where(a => a.GradeId.HasValue && !a.StudentId.HasValue)
            .Select(a => new { GradeId = a.GradeId!.Value, a.SchoolId })
            .Distinct()
            .ToList();

        var gradeIds = gradeTargets.Select(g => g.GradeId).Distinct().ToList();

        // Direkt StudentId atamaları da Students tablosu üzerinden doğrulanır ki
        // soft-delete edilmiş öğrenciler sınıf-bazlı yolla tutarlı biçimde dışlansın.
        var existingDirectStudentIds = directStudentIds.Count > 0
            ? await TargetStudents(target)
                .Where(s => directStudentIds.Contains(s.Id))
                .Select(s => s.Id)
                .ToListAsync(ct)
            : new List<int>();

        var targetStudentIds = new HashSet<int>(existingDirectStudentIds);

        if (gradeIds.Count > 0)
        {
            // issue #235: sınıf genişletmesi de kapsamlı kaynaktan (TargetStudents) — sayaç okul dışı öğrenciyi saymaz.
            var gradeStudents = await TargetStudents(target)
                .Where(s => s.GradeId.HasValue && gradeIds.Contains(s.GradeId.Value))
                .Select(s => new { s.Id, GradeId = s.GradeId!.Value, s.SchoolId })
                .ToListAsync(ct);

            foreach (var student in gradeStudents)
            {
                var matches = gradeTargets.Any(g =>
                    g.GradeId == student.GradeId &&
                    (!g.SchoolId.HasValue || g.SchoolId == student.SchoolId));

                if (matches)
                {
                    targetStudentIds.Add(student.Id);
                }
            }
        }

        return new TeacherDashboardSummaryDto
        {
            TotalWorksheets = totalWorksheets,
            TotalUniqueStudents = targetStudentIds.Count
        };
    }

    public async Task<List<TeacherWorksheetOverviewDto>> GetWorksheetsOverviewAsync(SchoolScope requester, CancellationToken ct = default)
    {
        var teacherId = requester.UserId;

        // 1) Sahip olunan worksheet'ler (GetDashboardSummaryAsync ile aynı sahiplik kuralı).
        var ownedWorksheets = await _context.Worksheets
            .AsNoTracking()
            .Where(w => w.CreateUserId == teacherId)
            .OrderBy(w => w.Name)
            .Select(w => new { w.Id, w.Name })
            .ToListAsync(ct);

        if (ownedWorksheets.Count == 0)
        {
            return new List<TeacherWorksheetOverviewDto>();
        }

        var worksheetIds = ownedWorksheets.Select(w => w.Id).ToList();

        // 2) Bu worksheet'lere ait TÜM atamalar tek sorguda (N+1 yok).
        //    issue #222: kapsam daraltılmışsa sınıf atamaları hiç çekilmez (grade→öğrenci genişletmesi yok).
        var target = await ResolveStudentTargetScopeAsync(requester, ct);
        var expandGrades = target.ExpandGradeAssignments;
        var assignments = await _context.WorksheetAssignments
            .AsNoTracking()
            .Where(wa => worksheetIds.Contains(wa.WorksheetId))
            .Where(wa => expandGrades || wa.StudentId != null)
            .Select(wa => new
            {
                wa.WorksheetId,
                wa.StudentId,
                wa.GradeId,
                wa.SchoolId,
                wa.StartAt,
                wa.EndAt
            })
            .ToListAsync(ct);

        // 3) Hedef öğrenciler tek sorguda: direkt atananlar + ilgili sınıflardaki tüm öğrenciler.
        var directStudentIds = assignments
            .Where(a => a.StudentId.HasValue)
            .Select(a => a.StudentId!.Value)
            .Distinct()
            .ToList();

        var gradeIds = assignments
            .Where(a => a.GradeId.HasValue && !a.StudentId.HasValue)
            .Select(a => a.GradeId!.Value)
            .Distinct()
            .ToList();

        var students = (directStudentIds.Count == 0 && gradeIds.Count == 0)
            ? new List<StudentTarget>()
            : await TargetStudents(target)
                .Where(s => directStudentIds.Contains(s.Id)
                            || (s.GradeId.HasValue && gradeIds.Contains(s.GradeId.Value)))
                .Select(s => new StudentTarget(s.Id, s.GradeId, s.SchoolId))
                .ToListAsync(ct);

        var studentsById = students.ToDictionary(s => s.Id);
        var studentsByGrade = students
            .Where(s => s.GradeId.HasValue)
            .GroupBy(s => s.GradeId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 4) Worksheet bazında hedef öğrenci kümesi + her öğrencinin ilgili atama pencereleri.
        //    Aynı öğrenci bir worksheet'e birden fazla atamayla hedeflenebilir; distinct sayılır.
        var targetsByWorksheet = new Dictionary<int, Dictionary<int, List<AssignmentWindow>>>();

        foreach (var assignment in assignments)
        {
            if (!targetsByWorksheet.TryGetValue(assignment.WorksheetId, out var studentWindows))
            {
                studentWindows = new Dictionary<int, List<AssignmentWindow>>();
                targetsByWorksheet[assignment.WorksheetId] = studentWindows;
            }

            var window = new AssignmentWindow(assignment.StartAt, assignment.EndAt);

            if (assignment.StudentId.HasValue)
            {
                // Direkt atama: öğrenci Students tablosunda mevcutsa (soft-delete dışlanır) hedeftir.
                if (studentsById.ContainsKey(assignment.StudentId.Value))
                {
                    AddWindow(studentWindows, assignment.StudentId.Value, window);
                }
            }
            else if (assignment.GradeId.HasValue
                     && studentsByGrade.TryGetValue(assignment.GradeId.Value, out var gradeStudents))
            {
                foreach (var student in gradeStudents)
                {
                    if (!assignment.SchoolId.HasValue || assignment.SchoolId == student.SchoolId)
                    {
                        AddWindow(studentWindows, student.Id, window);
                    }
                }
            }
        }

        // 5) İlgili test instance'ları tek sorguda (worksheet + hedef öğrenci filtresiyle).
        var allTargetStudentIds = targetsByWorksheet.Values
            .SelectMany(d => d.Keys)
            .Distinct()
            .ToList();

        var instances = allTargetStudentIds.Count == 0
            ? new List<InstanceSnapshot>()
            : await _context.TestInstances
                .AsNoTracking()
                .Where(ti => worksheetIds.Contains(ti.WorksheetId) && allTargetStudentIds.Contains(ti.StudentId))
                .Select(ti => new InstanceSnapshot(ti.WorksheetId, ti.StudentId, ti.StartTime, ti.EndTime, ti.Status))
                .ToListAsync(ct);

        var instancesByWorksheetStudent = instances
            .GroupBy(i => (i.WorksheetId, i.StudentId))
            .ToDictionary(g => g.Key, g => g.ToList());

        // 6) Bellekte hesapla.
        var result = new List<TeacherWorksheetOverviewDto>(ownedWorksheets.Count);

        foreach (var worksheet in ownedWorksheets)
        {
            var assignedCount = 0;
            var completedCount = 0;

            if (targetsByWorksheet.TryGetValue(worksheet.Id, out var studentWindows))
            {
                assignedCount = studentWindows.Count;

                foreach (var (studentId, windows) in studentWindows)
                {
                    instancesByWorksheetStudent.TryGetValue((worksheet.Id, studentId), out var studentInstances);

                    if (IsCompletedInAnyWindow(windows, studentInstances))
                    {
                        completedCount++;
                    }
                }
            }

            result.Add(new TeacherWorksheetOverviewDto
            {
                WorksheetId = worksheet.Id,
                Name = worksheet.Name,
                AssignedStudentCount = assignedCount,
                CompletionPercentage = assignedCount == 0
                    ? 0
                    : Math.Round(completedCount * 100.0 / assignedCount, 2)
            });
        }

        return result;
    }

    public async Task<List<TeacherLaggingStudentDto>> GetLaggingStudentsAsync(SchoolScope requester, CancellationToken ct = default)
    {
        var teacherId = requester.UserId;
        var now = DateTime.UtcNow;

        // 1) Sahip olunan worksheet'ler (GetWorksheetsOverviewAsync ile aynı sahiplik kuralı).
        var ownedWorksheets = await _context.Worksheets
            .AsNoTracking()
            .Where(w => w.CreateUserId == teacherId)
            .Select(w => new { w.Id, w.Name })
            .ToListAsync(ct);

        if (ownedWorksheets.Count == 0)
        {
            return new List<TeacherLaggingStudentDto>();
        }

        var worksheetNameById = ownedWorksheets.ToDictionary(w => w.Id, w => w.Name);
        var worksheetIds = ownedWorksheets.Select(w => w.Id).ToList();

        // 2-4) Atamalar → kapsamlı hedef öğrenciler → (worksheet, öğrenci) çifti bazında atama pencereleri.
        //      issue #56: ortak yardımcıya taşındı (öğretmen aktivite uçları aynı kümeyi kullanır).
        var target = await ResolveStudentTargetScopeAsync(requester, ct);
        var (studentsById, windowsByPair) = await ResolveAssignedPairsAsync(worksheetIds, target, ct);

        if (windowsByPair.Count == 0)
        {
            return new List<TeacherLaggingStudentDto>();
        }

        // 5) İlgili test instance'ları tek sorguda.
        var targetStudentIds = windowsByPair.Keys.Select(k => k.StudentId).Distinct().ToList();

        var instances = await _context.TestInstances
            .AsNoTracking()
            .Where(ti => worksheetIds.Contains(ti.WorksheetId) && targetStudentIds.Contains(ti.StudentId))
            .Select(ti => new InstanceSnapshot(ti.WorksheetId, ti.StudentId, ti.StartTime, ti.EndTime, ti.Status))
            .ToListAsync(ct);

        var instancesByPair = instances
            .GroupBy(i => (i.WorksheetId, i.StudentId))
            .ToDictionary(g => g.Key, g => g.ToList());

        // 6) Bellekte hesapla: çift başına "en ilgili" atama = başlamış (StartAt <= now) olanlar arasında en son başlayan.
        //    Henüz başlamamış (Scheduled) atamalar geride kalma sayılmaz.
        var laggingRows = new List<(int WorksheetId, AssignedStudentTarget Student, bool IsCompleted, bool IsExpired)>();

        foreach (var ((worksheetId, studentId), windows) in windowsByPair)
        {
            var relevantWindow = windows
                .Where(w => w.StartAt <= now)
                .OrderByDescending(w => w.StartAt)
                .FirstOrDefault();

            if (relevantWindow == null)
            {
                continue;
            }

            instancesByPair.TryGetValue((worksheetId, studentId), out var pairInstances);

            var isCompleted = IsCompletedInWindow(relevantWindow, pairInstances);
            var isExpired = !isCompleted
                            && relevantWindow.EndAt.HasValue
                            && relevantWindow.EndAt.Value < now;

            // CompletionPercentage 0/100 olduğu için IsLowCompletion == !isCompleted; ikisi de false ise satır dahil edilmez.
            if (isCompleted && !isExpired)
            {
                continue;
            }

            laggingRows.Add((worksheetId, studentsById[studentId], isCompleted, isExpired));
        }

        if (laggingRows.Count == 0)
        {
            return new List<TeacherLaggingStudentDto>();
        }

        // 7) Öğrenci adları auth-api'den tek batch çağrıyla; erişilemezse StudentNumber fallback'i.
        var userIds = laggingRows.Select(r => r.Student.UserId).Distinct().ToList();
        var nameByUserId = await ResolveStudentNamesAsync(userIds, ct);

        return laggingRows
            .Select(r =>
            {
                var completionPercentage = r.IsCompleted ? 100d : 0d;

                return new TeacherLaggingStudentDto
                {
                    StudentId = r.Student.Id,
                    StudentName = nameByUserId.TryGetValue(r.Student.UserId, out var fullName)
                        ? fullName
                        : _localizer["teacher.fallbackStudentName", r.Student.StudentNumber],
                    WorksheetId = r.WorksheetId,
                    WorksheetName = worksheetNameById[r.WorksheetId],
                    CompletionPercentage = completionPercentage,
                    IsLowCompletion = completionPercentage < 50,
                    IsExpired = r.IsExpired
                };
            })
            .OrderBy(dto => dto.StudentName)
            .ThenBy(dto => dto.WorksheetName)
            .ToList();
    }

    // ---------------------------------------------------------------------
    // Issue #56: öğretmen dashboard aktivite kartları + "En Aktif Öğrenciler"
    // ---------------------------------------------------------------------

    /// <summary>Aktivite uçlarında <c>days</c> için izin verilen aralık (controller doğrular).</summary>
    public const int ActivityMinDays = 1;
    public const int ActivityMaxDays = 90;

    /// <summary>"En Aktif Öğrenciler" tablosunun sabit üst sınırı.</summary>
    public const int ActivityTopStudentsLimit = 10;

    public async Task<TeacherOwnActivitySummaryDto> GetOwnActivitySummaryAsync(SchoolScope requester, int days, CancellationToken ct = default)
    {
        var teacherId = requester.UserId;
        var cutoff = ActivityCutoff(days);

        // Öğretmenin kendi eylemleri: son N günde oluşturduğu worksheet'ler ve atamalar (CreateUserId == teacherId).
        // Soft-delete edilmiş kayıtlar global query filter ile zaten dışarıda.
        var worksheetsCreated = await _context.Worksheets
            .AsNoTracking()
            .CountAsync(w => w.CreateUserId == teacherId && w.CreateTime >= cutoff, ct);

        var assignmentsCreated = await _context.WorksheetAssignments
            .AsNoTracking()
            .CountAsync(wa => wa.CreateUserId == teacherId && wa.CreateTime >= cutoff, ct);

        var activity = await GetStudentActivityCachedAsync(requester, cutoff, ct);

        return new TeacherOwnActivitySummaryDto
        {
            WorksheetsCreated = worksheetsCreated,
            AssignmentsCreated = assignmentsCreated,
            ActiveStudents = activity.Count
        };
    }

    public async Task<TeacherStudentsActivitySummaryDto> GetStudentsActivitySummaryAsync(SchoolScope requester, int days, CancellationToken ct = default)
    {
        var activity = await GetStudentActivityCachedAsync(requester, ActivityCutoff(days), ct);

        var top = activity
            .OrderByDescending(a => a.QuestionsSolved)
            .ThenByDescending(a => a.CorrectCount)
            .ThenBy(a => a.TimeSeconds)
            .ThenBy(a => a.Student.Id)
            .Take(ActivityTopStudentsLimit)
            .ToList();

        // Adlar yalnızca listelenecek öğrenciler için, tek batch çağrıyla (N+1 yok); erişilemezse StudentNumber fallback'i.
        var nameByUserId = await ResolveStudentNamesAsync(top.Select(a => a.Student.UserId).Distinct().ToList(), ct);

        return new TeacherStudentsActivitySummaryDto
        {
            TotalQuestionsSolved = activity.Sum(a => a.QuestionsSolved),
            TotalCorrectCount = activity.Sum(a => a.CorrectCount),
            TotalTimeSeconds = activity.Sum(a => a.TimeSeconds),
            TopStudents = top
                .Select(a => new TeacherActiveStudentDto
                {
                    StudentId = a.Student.Id,
                    StudentName = nameByUserId.TryGetValue(a.Student.UserId, out var fullName)
                        ? fullName
                        : _localizer["teacher.fallbackStudentName", a.Student.StudentNumber],
                    QuestionsSolved = a.QuestionsSolved,
                    CorrectCount = a.CorrectCount,
                    TimeSeconds = a.TimeSeconds
                })
                .ToList()
        };
    }

    /// <summary>
    /// Bugün dahil son <paramref name="days"/> YEREL takvim gününün başlangıcı (UTC anı) — issue #265: gün sınırı
    /// <c>Dashboard:TimeZone</c> (varsayılan Europe/Istanbul); TR 00:00-03:00 arası artık bir önceki güne sayılmaz.
    /// Admin dashboard trendleriyle (DashboardService.GetTrendsAsync) aynı pencere tanımı (<see cref="ILocalDayCalendar"/>).
    /// </summary>
    private DateTime ActivityCutoff(int days)
        => _dayCalendar.LastDays(Math.Clamp(days, ActivityMinDays, ActivityMaxDays)).StartUtc;

    /// <summary>
    /// issue #265: <see cref="GetStudentActivityAsync"/>'i iki aktivite ucu arasında paylaştırır (bkz. <see cref="ITeacherActivityCache"/>).
    /// Anahtar = öğretmen + ÇÖZÜLMÜŞ öğrenci kapsamı + pencere başlangıcı. Kapsam, token'daki okul değil öğretmen KAYDINDAN
    /// doğrulanmış kapsamdır (<see cref="ResolveStudentTargetScopeAsync"/>; hafif tek sorgu, her istekte önbellek dışında
    /// çalışır) — öğretmenin okulu/onayı TTL içinde değişirse (ör. okul bağlantısı onaylandı/kaldırıldı) eski kapsamla
    /// hesaplanmış sonuç yeni kapsama DÖNMEZ (security review LOW-1). Pencere başlangıcı days'i, saat dilimini ve yerel tarihi
    /// kodlar. Paylaşılan liste salt okunur kullanılır; girdi boyutu liste uzunluğuyla orantılıdır (LOW-2).
    /// </summary>
    private async Task<IReadOnlyList<StudentActivity>> GetStudentActivityCachedAsync(
        SchoolScope requester, DateTime cutoff, CancellationToken ct)
    {
        var target = await ResolveStudentTargetScopeAsync(requester, ct);
        if (_activityCache is null)
            return await GetStudentActivityAsync(requester.UserId, target, cutoff, ct);

        var scopeKey = target.StudentScope is { } scope
            ? $"{(target.ExpandGradeAssignments ? "school" : "narrow")}:{scope.SchoolId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}"
            : "unrestricted";
        var key = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"teacher-activity:v2:{requester.UserId}:{scopeKey}:{cutoff.Ticks}");
        return await _activityCache.GetOrCreateAsync<IReadOnlyList<StudentActivity>>(
            key,
            async token => await GetStudentActivityAsync(requester.UserId, target, cutoff, token),
            list => list.Count,
            ct);
    }

    /// <summary>
    /// issue #56: öğretmenin kendi worksheet'lerine atanan öğrencilerin (dashboard/lagging ile aynı kapsam, #222/#235)
    /// <paramref name="cutoff"/> sonrası çözdüğü sorular, öğrenci bazında. Yalnızca en az bir soru çözmüş öğrenciler döner.
    /// Bir cevap ancak (worksheet, öğrenci) çifti bir atamayla hedefleniyorsa sayılır — öğrencinin başka öğretmenin
    /// sınavında ya da bu öğretmenin kendisine atanmamış bir worksheet'inde çözdüğü sorular dahil edilmez.
    /// Ürün kararı (#56): çift atanmışsa cevap, atamanın StartAt/EndAt penceresinden bağımsız sayılır.
    /// </summary>
    private async Task<List<StudentActivity>> GetStudentActivityAsync(
        int teacherId, StudentTargetScope target, DateTime cutoff, CancellationToken ct)
    {
        var worksheetIds = await _context.Worksheets
            .AsNoTracking()
            .Where(w => w.CreateUserId == teacherId)
            .Select(w => w.Id)
            .ToListAsync(ct);

        if (worksheetIds.Count == 0)
            return new List<StudentActivity>();

        var (studentsById, windowsByPair) = await ResolveAssignedPairsAsync(worksheetIds, target, ct);

        if (windowsByPair.Count == 0)
            return new List<StudentActivity>();

        var targetStudentIds = windowsByPair.Keys.Select(k => k.StudentId).Distinct().ToList();

        // "Çözülen soru" DashboardService.GetTrendsAsync ile aynı tanım: satır test başlarken boş açılır, cevap
        // verildiğinde SelectedAnswerId/AnswerPayload set edilir ve UpdateTime cevap anını taşır.
        // Süre = soru bazlı TimeTaken (saniye; BadgeService StudentDailyActivity.TotalTimeSeconds ile aynı kaynak).
        // SQL tarafında (worksheet, öğrenci) bazında gruplanır; çift filtresi bellekte uygulanır.
        var rows = await _context.TestInstanceQuestions
            .AsNoTracking()
            .Where(q => (q.SelectedAnswerId != null || q.AnswerPayload != null)
                        && q.UpdateTime != null && q.UpdateTime >= cutoff)
            .Where(q => worksheetIds.Contains(q.WorksheetInstance.WorksheetId)
                        && targetStudentIds.Contains(q.WorksheetInstance.StudentId))
            .GroupBy(q => new { q.WorksheetInstance.WorksheetId, q.WorksheetInstance.StudentId })
            .Select(g => new
            {
                g.Key.WorksheetId,
                g.Key.StudentId,
                Solved = g.Count(),
                Correct = g.Count(x => x.IsCorrect),
                TimeSeconds = g.Sum(x => x.TimeTaken > 0 ? x.TimeTaken : 0)
            })
            .ToListAsync(ct);

        return rows
            .Where(r => windowsByPair.ContainsKey((r.WorksheetId, r.StudentId)))
            .GroupBy(r => r.StudentId)
            .Select(g => new StudentActivity(
                studentsById[g.Key],
                g.Sum(r => r.Solved),
                g.Sum(r => r.Correct),
                g.Sum(r => r.TimeSeconds)))
            .Where(a => a.QuestionsSolved > 0)
            .ToList();
    }

    /// <summary>
    /// Sahip olunan worksheet'lerin atamalarını kapsamlı hedef öğrencilere genişletir ve (worksheet, öğrenci) çifti
    /// bazında atama pencerelerini döner (GetWorksheetsOverviewAsync ile aynı genişletme kuralı):
    /// direkt atama → öğrenci Students'ta (soft-delete dışı) ve kapsamdaysa; sınıf ataması → o sınıftaki kapsam içi
    /// öğrenciler (atamanın SchoolId'si doluysa o okulla sınırlı). Tüm atamalar ve öğrenciler birer sorguda (N+1 yok).
    /// </summary>
    private async Task<(Dictionary<int, AssignedStudentTarget> StudentsById,
        Dictionary<(int WorksheetId, int StudentId), List<AssignmentWindow>> WindowsByPair)> ResolveAssignedPairsAsync(
        List<int> worksheetIds, StudentTargetScope target, CancellationToken ct)
    {
        var studentsById = new Dictionary<int, AssignedStudentTarget>();
        var windowsByPair = new Dictionary<(int WorksheetId, int StudentId), List<AssignmentWindow>>();

        // issue #222: kapsam daraltılmışsa sınıf atamaları hiç çekilmez (grade→öğrenci genişletmesi yok).
        var expandGrades = target.ExpandGradeAssignments;
        var assignments = await _context.WorksheetAssignments
            .AsNoTracking()
            .Where(wa => worksheetIds.Contains(wa.WorksheetId))
            .Where(wa => expandGrades || wa.StudentId != null)
            .Select(wa => new
            {
                wa.WorksheetId,
                wa.StudentId,
                wa.GradeId,
                wa.SchoolId,
                wa.StartAt,
                wa.EndAt
            })
            .ToListAsync(ct);

        if (assignments.Count == 0)
            return (studentsById, windowsByPair);

        var directStudentIds = assignments
            .Where(a => a.StudentId.HasValue)
            .Select(a => a.StudentId!.Value)
            .Distinct()
            .ToList();

        var gradeIds = assignments
            .Where(a => a.GradeId.HasValue && !a.StudentId.HasValue)
            .Select(a => a.GradeId!.Value)
            .Distinct()
            .ToList();

        var students = (directStudentIds.Count == 0 && gradeIds.Count == 0)
            ? new List<AssignedStudentTarget>()
            : await TargetStudents(target)
                .Where(s => directStudentIds.Contains(s.Id)
                            || (s.GradeId.HasValue && gradeIds.Contains(s.GradeId.Value)))
                .Select(s => new AssignedStudentTarget(s.Id, s.UserId, s.StudentNumber, s.GradeId, s.SchoolId))
                .ToListAsync(ct);

        if (students.Count == 0)
            return (studentsById, windowsByPair);

        studentsById = students.ToDictionary(s => s.Id);
        var studentsByGrade = students
            .Where(s => s.GradeId.HasValue)
            .GroupBy(s => s.GradeId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var assignment in assignments)
        {
            var window = new AssignmentWindow(assignment.StartAt, assignment.EndAt);

            if (assignment.StudentId.HasValue)
            {
                if (studentsById.ContainsKey(assignment.StudentId.Value))
                {
                    AddPairWindow(windowsByPair, assignment.WorksheetId, assignment.StudentId.Value, window);
                }
            }
            else if (assignment.GradeId.HasValue
                     && studentsByGrade.TryGetValue(assignment.GradeId.Value, out var gradeStudents))
            {
                foreach (var student in gradeStudents)
                {
                    if (!assignment.SchoolId.HasValue || assignment.SchoolId == student.SchoolId)
                    {
                        AddPairWindow(windowsByPair, assignment.WorksheetId, student.Id, window);
                    }
                }
            }
        }

        return (studentsById, windowsByPair);
    }

    // ---------------------------------------------------------------------
    // Issue #95: bağımsız öğretmen tutor profili + öğrenci araması
    // ---------------------------------------------------------------------

    private const int SearchMaxTake = 100;
    private const int SearchBioPreviewLength = 160;

    public async Task<TutorProfileResultDto> GetTutorProfileAsync(int userId, CancellationToken ct = default)
    {
        var teacher = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new
            {
                t.Id,
                t.IsIndependentTutor,
                t.ApprovalStatus,
                t.HourlyRate,
                t.TeachesOnline,
                t.TeachesInPerson,
                t.Bio,
                Subjects = t.TeacherSubjects
                    .OrderBy(ts => ts.Subject.Name)
                    .Select(ts => new TutorSubjectDto { SubjectId = ts.SubjectId, Name = ts.Subject.Name })
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (teacher == null)
            return new TutorProfileResultDto { Success = false, NotFound = true, Message = _localizer["teacher.recordNotFound"] };

        if (!teacher.IsIndependentTutor)
            return new TutorProfileResultDto { Success = false, Forbidden = true, Message = _localizer["teacher.tutorProfile.independentOnly"] };

        return new TutorProfileResultDto
        {
            Success = true,
            ObjectId = teacher.Id,
            Profile = new TutorProfileDto
            {
                TeacherId = teacher.Id,
                ApprovalStatus = teacher.ApprovalStatus,
                Subjects = teacher.Subjects,
                HourlyRate = teacher.HourlyRate,
                TeachesOnline = teacher.TeachesOnline,
                TeachesInPerson = teacher.TeachesInPerson,
                Bio = teacher.Bio
            }
        };
    }

    public async Task<TutorProfileResultDto> UpdateTutorProfileAsync(int userId, UpdateTutorProfileDto dto, CancellationToken ct = default)
    {
        // 1) İş kuralı validasyonu — DataAnnotation'lar yalnızca şekil kontrolü yapar.
        var subjectIds = (dto.SubjectIds ?? new List<int>()).Distinct().ToList();

        if (subjectIds.Count == 0)
            return Fail(_localizer["teacher.tutorProfile.subjectsRequired"]);

        if (!dto.TeachesOnline && !dto.TeachesInPerson)
            return Fail(_localizer["teacher.tutorProfile.teachingModeRequired"]);

        if (dto.HourlyRate <= 0)
            return Fail(_localizer["teacher.tutorProfile.hourlyRateInvalid"]);

        // 2) Sahiplik + bağımsız öğretmen kontrolü. ApprovalStatus fark etmez: onay beklerken de
        //    doldurulabilir; aramada görünürlük ayrıca ApprovalStatus=Approved ile filtrelenir.
        var teacher = await _context.Teachers
            .Include(t => t.TeacherSubjects)
            .FirstOrDefaultAsync(t => t.UserId == userId, ct);

        if (teacher == null)
            return new TutorProfileResultDto { Success = false, NotFound = true, Message = _localizer["teacher.recordNotFound"] };

        if (!teacher.IsIndependentTutor)
            return new TutorProfileResultDto { Success = false, Forbidden = true, Message = _localizer["teacher.tutorProfile.independentOnlyUpdate"] };

        // 3) SubjectId'ler gerçekten var mı (soft-delete edilmişler global filter ile zaten dışarıda).
        var existingSubjectIds = await _context.Subjects
            .AsNoTracking()
            .Where(s => subjectIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(ct);

        var missing = subjectIds.Except(existingSubjectIds).ToList();
        if (missing.Count > 0)
            return Fail(_localizer["teacher.tutorProfile.invalidSubjects", string.Join(", ", missing)]);

        // 4) Alanları güncelle + ders listesini diff'le (kaldırılanları hard delete, yenileri ekle).
        _context.SetCurrentUser(userId);

        teacher.HourlyRate = dto.HourlyRate;
        teacher.TeachesOnline = dto.TeachesOnline;
        teacher.TeachesInPerson = dto.TeachesInPerson;
        teacher.Bio = string.IsNullOrWhiteSpace(dto.Bio) ? null : dto.Bio.Trim();

        var current = teacher.TeacherSubjects.ToList();
        var toRemove = current.Where(ts => !subjectIds.Contains(ts.SubjectId)).ToList();
        var currentIds = current.Select(ts => ts.SubjectId).ToHashSet();
        var toAdd = subjectIds.Where(id => !currentIds.Contains(id)).ToList();

        if (toRemove.Count > 0)
            _context.TeacherSubjects.RemoveRange(toRemove);

        foreach (var subjectId in toAdd)
            teacher.TeacherSubjects.Add(new TeacherSubject { TeacherId = teacher.Id, SubjectId = subjectId });

        await _context.SaveChangesAsync(ct);

        var result = await GetTutorProfileAsync(userId, ct);
        result.Message = _localizer["teacher.tutorProfile.updated"];
        return result;

        static TutorProfileResultDto Fail(string message) => new() { Success = false, Message = message };
    }

    public async Task<List<TeacherSearchResultDto>> SearchTutorsAsync(TeacherSearchFilterDto filter, CancellationToken ct = default)
    {
        var take = filter.Take <= 0 ? 20 : Math.Min(filter.Take, SearchMaxTake);
        var skip = Math.Max(filter.Skip, 0);

        // Sadece onaylı bağımsız öğretmenler (kabul kriteri: ApprovalStatus=Approved).
        var query = _context.Teachers
            .AsNoTracking()
            .Where(t => t.IsIndependentTutor && t.ApprovalStatus == TeacherApprovalStatus.Approved);

        if (filter.SubjectId.HasValue)
            query = query.Where(t => t.TeacherSubjects.Any(ts => ts.SubjectId == filter.SubjectId.Value));

        if (filter.MinPrice.HasValue)
            query = query.Where(t => t.HourlyRate.HasValue && t.HourlyRate.Value >= filter.MinPrice.Value);

        if (filter.MaxPrice.HasValue)
            query = query.Where(t => t.HourlyRate.HasValue && t.HourlyRate.Value <= filter.MaxPrice.Value);

        if (filter.Online == true)
            query = query.Where(t => t.TeachesOnline);

        if (filter.InPerson == true)
            query = query.Where(t => t.TeachesInPerson);

        // Tek sorgu: dersler subquery projeksiyonu ile gelir (N+1 yok).
        var rows = await query
            .OrderBy(t => t.HourlyRate ?? decimal.MaxValue)
            .ThenBy(t => t.Id)
            .Skip(skip)
            .Take(take)
            .Select(t => new
            {
                t.Id,
                t.UserId,
                t.HourlyRate,
                t.TeachesOnline,
                t.TeachesInPerson,
                t.Bio,
                Subjects = t.TeacherSubjects
                    .OrderBy(ts => ts.Subject.Name)
                    .Select(ts => new TutorSubjectDto { SubjectId = ts.SubjectId, Name = ts.Subject.Name })
                    .ToList()
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return new List<TeacherSearchResultDto>();

        // Ad-soyad tek batch auth-api çağrısıyla; erişilemezse fallback.
        var users = await ResolveUsersAsync(rows.Select(r => r.UserId).Distinct().ToList(), ct);

        return rows.Select(r => new TeacherSearchResultDto
        {
            TeacherId = r.Id,
            FullName = users.TryGetValue(r.UserId, out var user) && !string.IsNullOrWhiteSpace(user.FullName)
                ? user.FullName
                : _localizer["teacher.fallbackTeacherName", r.Id],
            Subjects = r.Subjects,
            HourlyRate = r.HourlyRate,
            TeachesOnline = r.TeachesOnline,
            TeachesInPerson = r.TeachesInPerson,
            Bio = TruncateBio(r.Bio)
        }).ToList();
    }

    public async Task<TeacherPublicProfileDto?> GetPublicProfileAsync(int teacherId, CancellationToken ct = default)
    {
        // Var/yok ayrımı sızdırılmaz: bağımsız değilse veya Approved değilse de null (controller 404).
        var row = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.Id == teacherId
                        && t.IsIndependentTutor
                        && t.ApprovalStatus == TeacherApprovalStatus.Approved)
            .Select(t => new
            {
                t.Id,
                t.UserId,
                t.HourlyRate,
                t.TeachesOnline,
                t.TeachesInPerson,
                t.Bio,
                Subjects = t.TeacherSubjects
                    .OrderBy(ts => ts.Subject.Name)
                    .Select(ts => new TutorSubjectDto { SubjectId = ts.SubjectId, Name = ts.Subject.Name })
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (row == null)
            return null;

        var users = await ResolveUsersAsync(new List<int> { row.UserId }, ct);
        users.TryGetValue(row.UserId, out var user);

        return new TeacherPublicProfileDto
        {
            TeacherId = row.Id,
            FullName = !string.IsNullOrWhiteSpace(user?.FullName) ? user!.FullName : _localizer["teacher.fallbackTeacherName", row.Id],
            Avatar = user?.Avatar ?? string.Empty,
            Subjects = row.Subjects,
            HourlyRate = row.HourlyRate,
            TeachesOnline = row.TeachesOnline,
            TeachesInPerson = row.TeachesInPerson,
            Bio = row.Bio
        };
    }

    private static string? TruncateBio(string? bio)
    {
        if (string.IsNullOrWhiteSpace(bio))
            return null;

        return bio.Length <= SearchBioPreviewLength
            ? bio
            : bio[..SearchBioPreviewLength].TrimEnd() + "…";
    }

    /// <summary>
    /// UserId'leri tek batch çağrıyla kullanıcı bilgisine çevirir (TeacherApprovalService.ResolveUsersAsync ile aynı desen).
    /// Auth-api erişilemezse boş sözlük döner — liste yine de dönmeli.
    /// </summary>
    private async Task<Dictionary<int, UserLookupResultDto>> ResolveUsersAsync(List<int> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return new Dictionary<int, UserLookupResultDto>();

        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(userIds, ct);
            return users
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new Dictionary<int, UserLookupResultDto>();
        }
    }

    private static void AddPairWindow(
        Dictionary<(int WorksheetId, int StudentId), List<AssignmentWindow>> windowsByPair,
        int worksheetId,
        int studentId,
        AssignmentWindow window)
    {
        var key = (worksheetId, studentId);
        if (!windowsByPair.TryGetValue(key, out var windows))
        {
            windows = new List<AssignmentWindow>();
            windowsByPair[key] = windows;
        }

        windows.Add(window);
    }

    /// <summary>
    /// UserId'leri tek batch çağrıyla isme çevirir (WorksheetCalendarService.ResolveTeacherNamesAsync ile aynı desen).
    /// Auth-api erişilemezse boş sözlük döner — liste yine de dönmeli.
    /// </summary>
    private async Task<Dictionary<int, string>> ResolveStudentNamesAsync(List<int> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return new Dictionary<int, string>();

        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(userIds, ct);
            return users
                .Where(u => !string.IsNullOrWhiteSpace(u.FullName))
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First().FullName);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new Dictionary<int, string>();
        }
    }

    private static void AddWindow(Dictionary<int, List<AssignmentWindow>> studentWindows, int studentId, AssignmentWindow window)
    {
        if (!studentWindows.TryGetValue(studentId, out var windows))
        {
            windows = new List<AssignmentWindow>();
            studentWindows[studentId] = windows;
        }

        windows.Add(window);
    }

    /// <summary>
    /// WorksheetAssignmentService.ResolveStudentAssignmentStatus ile aynı "Completed" kuralı:
    /// atama penceresi içindeki en son instance Completed ise ya da Started olup EndTime dolmuşsa tamamlanmıştır.
    /// Öğrenci birden fazla atamayla hedeflenmişse herhangi birinde tamamlamış olması yeterlidir.
    /// </summary>
    private static bool IsCompletedInAnyWindow(List<AssignmentWindow> windows, List<InstanceSnapshot>? studentInstances)
    {
        if (studentInstances == null || studentInstances.Count == 0)
        {
            return false;
        }

        foreach (var window in windows)
        {
            if (IsCompletedInWindow(window, studentInstances))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tek atama penceresi için "Completed" kuralı: pencere içindeki en son instance
    /// Completed ise ya da Started olup EndTime dolmuşsa tamamlanmıştır.
    /// </summary>
    private static bool IsCompletedInWindow(AssignmentWindow window, List<InstanceSnapshot>? studentInstances)
    {
        if (studentInstances == null || studentInstances.Count == 0)
        {
            return false;
        }

        var relevantInstance = studentInstances
            .Where(ti => ti.StartTime >= window.StartAt
                         && (!window.EndAt.HasValue || ti.StartTime <= window.EndAt.Value))
            .OrderByDescending(ti => ti.StartTime)
            .FirstOrDefault();

        if (relevantInstance == null)
        {
            return false;
        }

        return relevantInstance.Status switch
        {
            WorksheetInstanceStatus.Completed => true,
            WorksheetInstanceStatus.Started => relevantInstance.EndTime.HasValue,
            _ => false
        };
    }

    private sealed record StudentTarget(int Id, int? GradeId, int? SchoolId);

    private sealed record AssignedStudentTarget(int Id, int UserId, string StudentNumber, int? GradeId, int? SchoolId);

    private sealed record AssignmentWindow(DateTime StartAt, DateTime? EndAt);

    private sealed record StudentActivity(AssignedStudentTarget Student, int QuestionsSolved, int CorrectCount, int TimeSeconds);

    private sealed record InstanceSnapshot(int WorksheetId, int StudentId, DateTime StartTime, DateTime? EndTime, WorksheetInstanceStatus Status);

}
