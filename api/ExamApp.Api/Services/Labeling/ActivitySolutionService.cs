using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Labeling;

/// <summary>Kayıt yok → 404. <see cref="Exception.Message"/> yerelleştirme anahtarıdır.</summary>
public sealed class LabelingNotFoundException(string messageKey) : Exception(messageKey);

/// <summary>
/// Review W6: sayfada öğretmenin düzelttiği / sildiği maddeler var ve <c>force</c> yeniden okuması <c>confirmOverwrite</c> olmadan
/// istendi → 409 <c>LabelingAnswerKeyHasEdits</c>.
/// </summary>
public sealed class LabelingAnswerKeyHasEditsException() : Exception("labeling.answerKey.hasEdits");

/// <summary>Issue #580: istenen dosya sayfası kitabın cevap anahtarı aralığında değil → 400 <c>LabelingNotAnswerKeyPage</c>.</summary>
public sealed class LabelingNotAnswerKeyPageException() : Exception("labeling.answerKey.notAnswerKeyPage");

public interface IActivitySolutionService
{
    /// <summary>
    /// Cevap anahtarı sayfasını okur (ya da aynı görsel + model + talimat için kayıttan döner) ve kitap + dosya sayfası başına saklar;
    /// canlı okuma o sayfanın maddelerini değiştirir. Hatalar: 400 <see cref="LabelingValidationException"/> /
    /// <see cref="LabelingNotAnswerKeyPageException"/>, 404 <see cref="LabelingNotFoundException"/>, 429, 503, 502
    /// <see cref="LabelingAiException"/>.
    /// </summary>
    Task<AnswerKeyReadResponseDto> ReadAnswerKeyAsync(AnswerKeyReadRequestDto request, LabelingActor actor, CancellationToken ct = default);

    /// <summary>Kitabın anahtar maddeleri (filtreli); kitap yoksa null.</summary>
    /// <param name="filePage">Review W11: etkinliğin DOSYA sayfası — fasiküllü kitapta aynı fasikülün anahtarına daraltır; bastırılmış
    /// sayfa verilmediyse ondan türetilir.</param>
    Task<AnswerKeyListResponseDto?> ListAnswerKeyAsync(
        int bookId, int? printedPage, string? activityLabel, int skip, int? filePage = null, CancellationToken ct = default);

    /// <summary>Sayfadaki öğelerin anahtar maddeleri tek istekte (kural: <see cref="ActivitySolutionService.LookupAnswerKeyAsync"/>); kitap yoksa null.</summary>
    Task<AnswerKeyLookupResponseDto?> LookupAnswerKeyAsync(int bookId, AnswerKeyLookupRequestDto request, CancellationToken ct = default);

    /// <summary>Maddeyi düzeltir (öğretmen); yoksa null.</summary>
    Task<AnswerKeyEntryDto?> UpdateEntryAsync(int id, AnswerKeyEntryUpdateRequestDto request, LabelingActor actor, CancellationToken ct = default);

    /// <summary>Yumuşak silme (silen + zaman); yoksa / zaten silinmişse false.</summary>
    Task<bool> DeleteEntryAsync(int id, LabelingActor actor, CancellationToken ct = default);

    /// <summary>
    /// Taslak etkinliğin çözümü (anahtardan ya da AI). Claude hatası istisna değil: <c>Error</c> dolu yanıt (kaydedilmez).
    /// <c>Error</c> ve <c>ValidationErrors</c> yerelleştirme ANAHTARIDIR (controller çevirir).
    /// </summary>
    Task<LabelingActivitySolutionDto> SolveAsync(ActivitySolutionRequestDto request, LabelingActor actor, CancellationToken ct = default);

    /// <summary>İnsan düzeltmesi (ve isteğe bağlı onay); yoksa null.</summary>
    Task<LabelingActivitySolutionDto?> UpdateSolutionAsync(
        int id, ActivitySolutionUpdateRequestDto request, LabelingActor actor, CancellationToken ct = default);

    /// <summary>Onay; geçersiz çözüm onaylanamaz (400). Yoksa null.</summary>
    Task<LabelingActivitySolutionDto?> ApproveSolutionAsync(int id, LabelingActor actor, CancellationToken ct = default);

    /// <summary>Kırpmanın kayıtlı çözüm taslakları (yeniden açılışta geri yüklemek için).</summary>
    Task<ActivitySolutionListDto> FindSolutionsAsync(
        string? cropSha256, string? interactionType, string? planHash, CancellationToken ct = default);
}

/// <summary>
/// Issue #580: cevap anahtarı okuma ve etkinlik taslağı çözümü. Kota/bütçe #578/#579 ile ortak (<see cref="LabelingAiQuota"/>): her
/// canlı çağrı saatlik kotadan 1 düşer, en kötü durum maliyeti baştan bütçeden ayrılır ve çağrı bitince gerçek maliyete (ödenmiş
/// başarısız çağrı dahil) düzeltilir; kota alındıktan sonra çağrı istek iptalinden bağımsız koşar (ödenmiş sonuç kaydedilir). Kayıttan
/// dönenler dahil her okuma/çözüm isteği kullanıcı başına dakikalık sınıra (<c>Anthropic:ActivityStructureRequestsPerMinute</c>,
/// ayrı sayaç) tabidir.
/// <para>
/// Önbellek: anahtar okuması (kitap, dosya sayfası) satırında görsel SHA-256 + model + <c>answerKey</c> parmak izi; çözüm
/// (kırpma SHA-256, tip, plan hash'i, mod, anahtar girdisi hash'i, model) satırında modun talimat parmak izi + tipin çözüm revizyonu.
/// Bir talimat değişince yalnız onun sonuçları yeniden üretilir.
/// </para>
/// <para>
/// Onay: taslak çözüm her zaman <c>pending</c> başlar. Düzeltme (PUT) kaynağı değiştirmez, durumu pending'e çevirir ve düzelteni
/// kaydeder; onay (bu uçların hepsi Admin + QuestionTrainer) onaylayanı + zamanı kaydeder ve çözüm bir insan tarafından düzeltildiyse
/// kaynağı <c>Human</c> yapar (düzeltilmeden onaylanan Key/AI kalır).
/// </para>
/// </summary>
public sealed class ActivitySolutionService(
    AppDbContext db,
    IActivitySolutionAi ai,
    IBookRegistryService books,
    IInteractionGraderRegistry graders,
    ImageHelper imageHelper,
    IFixedWindowCounterStore rateLimitStore,
    IOptions<LabelingAiOptions> options,
    TimeProvider timeProvider,
    ILogger<ActivitySolutionService> logger,
    IHostApplicationLifetime? lifetime = null) : IActivitySolutionService
{
    public const string OriginLive = "live";
    public const string OriginCached = "cached";
    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string AiFailedKey = "labeling.solution.aiFailed";
    public const string InvalidSolutionKey = "labeling.solution.invalid";

    public const int MaxListEntries = 500;

    // activityLabel filtresi bellekte uygulanır: kitap başına okunacak üst sınır.
    private const int MaxLabelScan = 10_000;
    private const int MaxSolutionsListed = 10;
    private static readonly TimeSpan RequestRateWindow = TimeSpan.FromMinutes(1);

    /// <summary>Kota alındıktan sonraki canlı çağrı süresi (istek iptalinden bağımsız; yalnız bu süre ve uygulama kapanışı keser).</summary>
    internal TimeSpan LiveCallTimeout { get; init; } = TimeSpan.FromMinutes(3);

    // ================================================================== cevap anahtarı

    public async Task<AnswerKeyReadResponseDto> ReadAnswerKeyAsync(
        AnswerKeyReadRequestDto request, LabelingActor actor, CancellationToken ct = default)
    {
        await EnsureRequestRateAsync(actor, ct);
        var model = ResolveModel(request.Model);
        if (request.BookId <= 0)
            throw new LabelingNotFoundException("labeling.books.notFound");
        if (request.FilePage is { } filePage)
        {
            if (filePage < 1 || filePage > BookPageMap.MaxFilePage)
                throw new LabelingValidationException("labeling.books.invalidPage");
            var context = await books.ResolvePageAsync(request.BookId, filePage, ct)
                          ?? throw new LabelingNotFoundException("labeling.books.notFound");
            if (!context.IsAnswerKey)
                throw new LabelingNotAnswerKeyPageException();
        }
        else if (!await db.Books.AsNoTracking().AnyAsync(b => b.Id == request.BookId, ct))
        {
            throw new LabelingNotFoundException("labeling.books.notFound");
        }

        using var image = LabelingImage.Decode(request.ImageBase64);
        var prompt = await ActivityPromptStore.GetActiveAsync(db, ActivityStructureSettings.AnswerKeyKind, ct);

        var existing = await PageQuery(db.LabelingAnswerKeyPages.AsNoTracking(), request.BookId, request.FilePage, image.Sha256)
            .FirstOrDefaultAsync(ct);
        var hasEdits = existing is not null && await HasEditsAsync(existing.Id, ct);
        if (existing is not null && !request.Force)
        {
            var fresh = existing.ImageSha256 == image.Sha256 && existing.ModelName == model.Id && existing.PromptHash == prompt.Fingerprint;
            // Review W6: öğretmen düzeltmesi olan sayfa onaysız yeniden okunmaz — görsel / model / talimat farklı olsa da kayıtlı
            // maddeler döner (stale: true).
            if (fresh || hasEdits)
            {
                return new AnswerKeyReadResponseDto(OriginCached, existing.PromptVersion, existing.EstimatedCostUsd,
                    await EntriesQuery(LiveEntries().Where(e => e.PageId == existing.Id)).ToListAsync(ct),
                    existing.InputTokens, existing.OutputTokens, Stale: !fresh, HasEdits: hasEdits);
            }
        }
        if (hasEdits && !request.ConfirmOverwrite)
            throw new LabelingAnswerKeyHasEditsException();

        if (!ai.IsConfigured)
            throw new LabelingAiUnavailableException();

        var estimate = LabelingAiQuota.Cost(model,
            ClaudeActivitySolutionAi.WorstCaseAnswerKeyInputTokens(new Size(image.Width, image.Height), options.Value.MaxImageEdge),
            ClaudeActivitySolutionAi.AnswerKeyMaxOutputTokens);
        var reserved = await ReserveAsync(actor, estimate, ct);

        AnswerKeyReading reading;
        decimal cost;
        using (var callCts = LiveCallToken())
        {
            try
            {
                reading = await ai.ReadAnswerKeyAsync(image, prompt, model, callCts.Token);
                cost = LabelingAiQuota.Cost(model, reading.InputTokens, reading.OutputTokens);
            }
            catch (LabelingAiException ex)
            {
                var failed = ex.Usage is { } u ? LabelingAiQuota.Cost(model, u.Input, u.Output) : 0m;
                await LabelingAiQuota.SettleAsync(rateLimitStore, actor, reserved, failed);
                logger.LogWarning(ex, "[Labeling] Claude {Model} answer key book {Book} page {Page} failed (~${Cost:F4} charged)",
                    model.Id, request.BookId, request.FilePage, failed);
                throw;
            }
            catch (Exception ex)
            {
                // Zaman aşımı / kapanış ya da beklenmeyen hata: kullanım bilinmiyor.
                await LabelingAiQuota.SettleAsync(rateLimitStore, actor, reserved, 0m);
                logger.LogWarning(ex, "[Labeling] Claude {Model} answer key book {Book} page {Page} did not complete",
                    model.Id, request.BookId, request.FilePage);
                throw new LabelingAiException("Claude request did not complete.", ex);
            }
        }
        await LabelingAiQuota.SettleAsync(rateLimitStore, actor, reserved, cost);
        logger.LogInformation(
            "[Labeling] Claude {Model} answer key book {Book} page {Page} prompt v{Prompt}: {Count} entries, {In}/{Out} tokens, ~${Cost:F4}",
            model.Id, request.BookId, request.FilePage, prompt.Version, reading.Entries.Count, reading.InputTokens,
            reading.OutputTokens, cost);

        var pageId = await ReplaceEntriesAsync(request, image, prompt, model, reading, cost, actor);
        var entries = await EntriesQuery(LiveEntries().Where(e => e.PageId == pageId))
            .ToListAsync(CancellationToken.None);
        return new AnswerKeyReadResponseDto(OriginLive, prompt.Version, Math.Round(cost, 6), entries, reading.InputTokens,
            reading.OutputTokens);
    }

    /// <summary>
    /// Sayfa satırını yazar (yoksa açar) ve maddelerini tek transaction'da değiştirir; görsel cevapların kırpması sayfadan alınıp
    /// saklanır (uzun kenar ≤ <see cref="LabelingAnswerKeyEntry.MaxCropEdge"/>). Ödenmiş sonuç kaybolmasın diye iptal edilemez.
    /// </summary>
    private async Task<int> ReplaceEntriesAsync(
        AnswerKeyReadRequestDto request, LabelingImage image, ActivityPromptSettings prompt, LabelingAiModelOption model,
        AnswerKeyReading reading, decimal cost, LabelingActor actor)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Review D-3: sayfa başına kırpma baytı sınırlı — sınırı aşan görsel maddeler kutusuyla ama kırpmasız saklanır.
        var crops = new List<byte[]?>(reading.Entries.Count);
        var cropBytes = 0L;
        foreach (var e in reading.Entries)
        {
            var crop = e.Box is { } box ? image.Encode(LabelingAnswerKeyEntry.MaxCropEdge, box).Jpeg : null;
            if (crop is not null && cropBytes + crop.Length > LabelingAnswerKeyEntry.MaxCropBytesPerPage)
                crop = null;
            cropBytes += crop?.Length ?? 0;
            crops.Add(crop);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            for (var attempt = 0; ; attempt++)
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);
                var page = await PageQuery(db.LabelingAnswerKeyPages.AsTracking(), request.BookId, request.FilePage, image.Sha256)
                    .FirstOrDefaultAsync(CancellationToken.None);
                if (page is null)
                {
                    page = new LabelingAnswerKeyPage { BookId = request.BookId, FilePage = request.FilePage, CreatedAt = now };
                    db.LabelingAnswerKeyPages.Add(page);
                }
                page.ImageSha256 = image.Sha256;
                page.ModelName = model.Id;
                page.PromptHash = prompt.Fingerprint;
                page.PromptVersion = prompt.Version;
                page.InputTokens = reading.InputTokens;
                page.OutputTokens = reading.OutputTokens;
                page.EstimatedCostUsd = Math.Round(cost, 6);
                page.CreatedByKeycloakId = actor.KeycloakId;
                page.CreatedByName = Truncate(actor.Name, 200);
                page.UpdatedAt = now;

                try
                {
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                catch (DbUpdateException ex) when (attempt == 0 && DbUpdateExceptionClassifier.IsUniqueViolation(ex))
                {
                    // Eşzamanlı ilk okuma aynı sayfayı açtı: onun satırının üzerine yaz.
                    await tx.RollbackAsync(CancellationToken.None);
                    continue;
                }

                // Kontrol ile yazma arasında gelen düzeltme de onaysız silinmesin.
                if (!request.ConfirmOverwrite && await HasEditsAsync(page.Id, CancellationToken.None))
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    throw new LabelingAnswerKeyHasEditsException();
                }
                await db.LabelingAnswerKeyEntries.Where(e => e.PageId == page.Id).ExecuteDeleteAsync(CancellationToken.None);
                for (var i = 0; i < reading.Entries.Count; i++)
                {
                    var e = reading.Entries[i];
                    var crop = crops[i];
                    db.LabelingAnswerKeyEntries.Add(new LabelingAnswerKeyEntry
                    {
                        PageId = page.Id,
                        BookId = request.BookId,
                        FilePage = request.FilePage,
                        Order = i,
                        PrintedPage = e.PrintedPage,
                        ActivityLabel = e.ActivityLabel,
                        ItemLabel = e.ItemLabel,
                        AnswerText = e.AnswerText,
                        Visual = e.Visual,
                        BoxX = e.Box?.X,
                        BoxY = e.Box?.Y,
                        BoxWidth = e.Box?.Width,
                        BoxHeight = e.Box?.Height,
                        CropJpeg = crop,
                        CropSha256 = crop is null ? null : QuestionCropHash.Of(crop),
                    });
                }
                await db.SaveChangesAsync(CancellationToken.None);
                await tx.CommitAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                return page.Id;
            }
        });
    }

    public async Task<AnswerKeyListResponseDto?> ListAnswerKeyAsync(
        int bookId, int? printedPage, string? activityLabel, int skip, int? filePage = null, CancellationToken ct = default)
    {
        if (!await db.Books.AsNoTracking().AnyAsync(b => b.Id == bookId, ct))
            return null;
        if (skip < 0 || printedPage is < 1 or > BookPageMap.MaxFilePage || filePage is < 1 or > BookPageMap.MaxFilePage)
            throw new LabelingValidationException("labeling.books.invalidPage");

        BookPageMap? map = null;
        List<BookPageOffset> offsets = [];
        List<BookAnswerKeyRange> keyRanges = [];
        if (filePage is { } activityPage)
        {
            offsets = await db.BookPageOffsets.AsNoTracking().Where(o => o.BookId == bookId).OrderBy(o => o.FileFrom).ToListAsync(ct);
            keyRanges = await db.BookAnswerKeyRanges.AsNoTracking().Where(r => r.BookId == bookId).OrderBy(r => r.FileFrom).ToListAsync(ct);
            map = new BookPageMap(offsets, keyRanges);
            // Bastırılmış sayfa verilmediyse etkinliğin dosya sayfasından.
            printedPage ??= map.ToPrinted(activityPage);
        }

        var query = LiveEntries().Where(e => e.BookId == bookId);
        if (printedPage is { } p)
            query = query.Where(e => e.PrintedPage == p);

        if (filePage is null && string.IsNullOrWhiteSpace(activityLabel))
        {
            var total = await query.CountAsync(ct);
            var page = await EntriesQuery(query).Skip(skip).Take(MaxListEntries).ToListAsync(ct);
            return new AnswerKeyListResponseDto(page, total);
        }

        // Etiket eşlemesi normalize (ActivityLabelMatcher) ve fasikül daraltması SQL'e çevrilemez: kitabın (sayfaya göre süzülmüş)
        // maddeleri bellekte.
        if (!string.IsNullOrWhiteSpace(activityLabel) && ActivityLabelMatcher.Parse(activityLabel) is null)
            return new AnswerKeyListResponseDto([], 0);
        IEnumerable<AnswerKeyEntryDto> matched = await EntriesQuery(query).Take(MaxLabelScan).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(activityLabel))
            matched = matched.Where(e => ActivityLabelMatcher.Matches(e.ActivityLabel, activityLabel));
        var list = matched.ToList();
        if (filePage is { } fp)
            list = NarrowToFascicle(list, fp, offsets, keyRanges);
        return new AnswerKeyListResponseDto(list.Skip(skip).Take(MaxListEntries).ToList(), list.Count);
    }

    /// <summary>
    /// Issue #580 (kapsam genişlemesi): sayfadaki öğelerin anahtar maddeleri tek istekte. Kitabın silinmemiş maddeleri bir kez okunur.
    /// <list type="bullet">
    /// <item><c>activity</c>: maddenin bastırılmış sayfası = öğenin bastırılmış sayfası (istekte yoksa dosya sayfasından; o da
    /// bilinmiyorsa sayfa koşulu yok) ve etkinlik etiketi ≈ öğe etiketi (<see cref="ActivityLabelMatcher"/>); öğede madde etiketi
    /// varsa maddeninki de ≈ olmalı.</item>
    /// <item><c>mcq</c> (etiket = soru numarası): <c>sectionLabel</c> verildiyse maddenin etkinlik etiketi ≈ <c>sectionLabel</c> ve
    /// madde etiketi ≈ soru numarası; sayfa koşulu yok (test anahtarlarında sayfa çoğu zaman yazmaz) ama maddede sayfa yazıyorsa öğenin
    /// sayfasıyla aynı olmalı. <c>sectionLabel</c> yoksa: maddenin sayfası = öğenin sayfası ve madde etiketi ≈ soru numarası.</item>
    /// <item>Ardından <see cref="NarrowToFascicle"/> (etkinliğin dosya sayfasıyla).</item>
    /// </list>
    /// Etiketi boş öğe boş liste alır.
    /// </summary>
    public async Task<AnswerKeyLookupResponseDto?> LookupAnswerKeyAsync(
        int bookId, AnswerKeyLookupRequestDto request, CancellationToken ct = default)
    {
        if (!await db.Books.AsNoTracking().AnyAsync(b => b.Id == bookId, ct))
            return null;
        if (request.FilePage < 1 || request.FilePage > BookPageMap.MaxFilePage
            || request.PrintedPage is < 1 or > BookPageMap.MaxFilePage)
            throw new LabelingValidationException("labeling.books.invalidPage");
        var items = request.Items;
        if (items is null || items.Count is 0 or > AnswerKeyLookupRequestDto.MaxItems)
            throw new LabelingValidationException("labeling.answerKey.invalidLookup");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 64 || !keys.Add(item.Key)
                || item.Label is { Length: > 64 } || item.ItemLabel is { Length: > 64 }
                || item.Kind is not (null or "" or "mcq" or "activity"))
                throw new LabelingValidationException("labeling.answerKey.invalidLookup");
        }
        if (request.SectionLabel is { Length: > 64 })
            throw new LabelingValidationException("labeling.answerKey.invalidLookup");

        var offsets = await db.BookPageOffsets.AsNoTracking().Where(o => o.BookId == bookId).OrderBy(o => o.FileFrom).ToListAsync(ct);
        var keyRanges = await db.BookAnswerKeyRanges.AsNoTracking().Where(r => r.BookId == bookId).OrderBy(r => r.FileFrom).ToListAsync(ct);
        var printed = request.PrintedPage ?? new BookPageMap(offsets, keyRanges).ToPrinted(request.FilePage);
        var section = string.IsNullOrWhiteSpace(request.SectionLabel) ? null : request.SectionLabel.Trim();
        var entries = await EntriesQuery(LiveEntries().Where(e => e.BookId == bookId)).Take(MaxLabelScan).ToListAsync(ct);

        var results = new List<AnswerKeyLookupResultDto>(items.Count);
        foreach (var item in items)
        {
            var label = item.Label?.Trim();
            if (string.IsNullOrEmpty(label))
            {
                results.Add(new AnswerKeyLookupResultDto(item.Key!, []));
                continue;
            }
            var mcq = item.Kind == "mcq" || (string.IsNullOrEmpty(item.Kind) && section is not null
                                              && ActivityLabelMatcher.Parse(label) is { HasNumber: true, Kind.Length: 0 });
            IEnumerable<AnswerKeyEntryDto> matched;
            if (mcq && section is not null)
            {
                matched = entries.Where(e => ActivityLabelMatcher.Matches(e.ActivityLabel, section)
                                             && ActivityLabelMatcher.Matches(e.ItemLabel, label)
                                             && (e.PrintedPage is null || printed is null || e.PrintedPage == printed));
            }
            else if (mcq)
            {
                matched = entries.Where(e => printed is not null && e.PrintedPage == printed
                                             && ActivityLabelMatcher.Matches(e.ItemLabel, label));
            }
            else
            {
                var itemLabel = string.IsNullOrWhiteSpace(item.ItemLabel) ? null : item.ItemLabel.Trim();
                matched = entries.Where(e => (printed is null || e.PrintedPage == printed)
                                             && ActivityLabelMatcher.Matches(e.ActivityLabel, label)
                                             && (itemLabel is null || ActivityLabelMatcher.Matches(e.ItemLabel, itemLabel)));
            }
            results.Add(new AnswerKeyLookupResultDto(item.Key!,
                NarrowToFascicle(matched.ToList(), request.FilePage, offsets, keyRanges)));
        }
        return new AnswerKeyLookupResponseDto(results);
    }

    /// <summary>
    /// Review W11: fasiküllü kitapta aynı bastırılmış sayfa + etiket birden çok fasikülde görülür. Etkinliğin dosya sayfası
    /// (<paramref name="activityFilePage"/>) verildiyse aday maddeler şöyle daraltılır:
    /// <list type="number">
    /// <item>Anahtar sayfası etkinlikle AYNI ofset aralığında (fasikül) olan maddeler varsa yalnız onlar.</item>
    /// <item>Yoksa etkinlikten SONRAKİ ilk cevap anahtarı aralığındaki maddeler (fasikülün kendi anahtarı sonunda); hiç sonraki aralık
    /// yoksa etkinlikten önceki son aralık.</item>
    /// <item>Hiçbiri eşleşmezse daraltma yapılmaz (tüm adaylar).</item>
    /// </list>
    /// </summary>
    internal static List<AnswerKeyEntryDto> NarrowToFascicle(
        List<AnswerKeyEntryDto> candidates, int activityFilePage, IReadOnlyList<BookPageOffset> offsets,
        IReadOnlyList<BookAnswerKeyRange> keyRanges)
    {
        if (candidates.Count == 0)
            return candidates;
        int RangeOf(int filePage)
        {
            for (var i = 0; i < offsets.Count; i++)
            {
                if (filePage >= offsets[i].FileFrom && filePage <= offsets[i].FileTo)
                    return i;
            }
            return -1;
        }

        var fascicle = RangeOf(activityFilePage);
        if (fascicle >= 0)
        {
            var same = candidates.Where(e => e.FilePage is { } kp && RangeOf(kp) == fascicle).ToList();
            if (same.Count > 0)
                return same;
        }

        var target = keyRanges.FirstOrDefault(r => r.FileFrom >= activityFilePage)
                     ?? keyRanges.LastOrDefault(r => r.FileTo < activityFilePage);
        if (target is not null)
        {
            // Sayfası bilinmeyen (toplu yüklenen) maddeler hiçbir aralığa düşmez: hiç eşleşme yoksa tüm adaylar döner.
            var inKey = candidates.Where(e => e.FilePage >= target.FileFrom && e.FilePage <= target.FileTo).ToList();
            if (inKey.Count > 0)
                return inKey;
        }
        return candidates;
    }

    public async Task<AnswerKeyEntryDto?> UpdateEntryAsync(
        int id, AnswerKeyEntryUpdateRequestDto request, LabelingActor actor, CancellationToken ct = default)
    {
        if (request.PrintedPage is < 1 or > BookPageMap.MaxFilePage)
            throw new LabelingValidationException("labeling.books.invalidPage");
        var entry = await db.LabelingAnswerKeyEntries.AsTracking().FirstOrDefaultAsync(e => e.Id == id && e.DeletedAt == null, ct);
        if (entry is null)
            return null;

        var activity = ActivityDrafts.Label(request.ActivityLabel, LabelingAnswerKeyEntry.MaxLabelLength);
        var item = ActivityDrafts.Label(request.ItemLabel, LabelingAnswerKeyEntry.MaxLabelLength);
        var answer = ActivityDrafts.Label(request.AnswerText, LabelingAnswerKeyEntry.MaxAnswerLength);
        if (answer is null && !entry.Visual)
            throw new LabelingValidationException("labeling.answerKey.answerRequired");

        entry.PrintedPage = request.PrintedPage;
        entry.ActivityLabel = activity;
        entry.ItemLabel = item;
        entry.AnswerText = answer;
        entry.EditedByKeycloakId = actor.KeycloakId;
        entry.EditedByName = Truncate(actor.Name, 200);
        entry.EditedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return ToDto(entry);
    }

    /// <summary>Review W6: yumuşak silme — silen ve zaman kaydedilir; sayfa "düzeltilmiş" sayılır.</summary>
    public async Task<bool> DeleteEntryAsync(int id, LabelingActor actor, CancellationToken ct = default) =>
        await db.LabelingAnswerKeyEntries.Where(e => e.Id == id && e.DeletedAt == null)
            .ExecuteUpdateAsync(u => u
                .SetProperty(e => e.DeletedAt, (DateTime?)timeProvider.GetUtcNow().UtcDateTime)
                .SetProperty(e => e.DeletedByKeycloakId, actor.KeycloakId)
                .SetProperty(e => e.DeletedByName, Truncate(actor.Name, 200)), ct) > 0;

    /// <summary>
    /// Okumanın anahtarı: dosya sayfası biliniyorsa (kitap, dosya sayfası); bilinmiyorsa (kitap, görsel SHA-256, sayfasız) — toplu
    /// yüklemede aynı görsel aynı okumaya düşer, sayfası bilinen okumayla karışmaz.
    /// </summary>
    private static IQueryable<LabelingAnswerKeyPage> PageQuery(
        IQueryable<LabelingAnswerKeyPage> pages, int bookId, int? filePage, string imageSha256) =>
        filePage is { } fp
            ? pages.Where(p => p.BookId == bookId && p.FilePage == fp)
            : pages.Where(p => p.BookId == bookId && p.FilePage == null && p.ImageSha256 == imageSha256);

    /// <summary>Silinmemiş maddeler.</summary>
    private IQueryable<LabelingAnswerKeyEntry> LiveEntries() =>
        db.LabelingAnswerKeyEntries.AsNoTracking().Where(e => e.DeletedAt == null);

    /// <summary>Sayfada öğretmenin düzelttiği ya da sildiği madde var mı.</summary>
    private Task<bool> HasEditsAsync(int pageId, CancellationToken ct) =>
        db.LabelingAnswerKeyEntries.AnyAsync(e => e.PageId == pageId && (e.EditedAt != null || e.DeletedAt != null), ct);

    // Kırpma baytlarını yüklemeden DTO'ya.
    private static IQueryable<AnswerKeyEntryDto> EntriesQuery(IQueryable<LabelingAnswerKeyEntry> query) =>
        query.OrderBy(e => e.FilePage).ThenBy(e => e.Order).ThenBy(e => e.Id)
            .Select(e => new AnswerKeyEntryDto(e.Id, e.FilePage, e.PrintedPage, e.ActivityLabel, e.ItemLabel, e.AnswerText, e.Visual,
                e.BoxX != null && e.BoxY != null && e.BoxWidth != null && e.BoxHeight != null
                    ? new LabelingBoxDto(e.BoxX.Value, e.BoxY.Value, e.BoxWidth.Value, e.BoxHeight.Value)
                    : null,
                e.EditedByName, e.EditedAt));

    private static AnswerKeyEntryDto ToDto(LabelingAnswerKeyEntry e) =>
        new(e.Id, e.FilePage, e.PrintedPage, e.ActivityLabel, e.ItemLabel, e.AnswerText, e.Visual,
            e is { BoxX: { } x, BoxY: { } y, BoxWidth: { } w, BoxHeight: { } h } ? new LabelingBoxDto(x, y, w, h) : null,
            e.EditedByName, e.EditedAt);

    // ================================================================== çözüm

    public async Task<LabelingActivitySolutionDto> SolveAsync(
        ActivitySolutionRequestDto request, LabelingActor actor, CancellationToken ct = default)
    {
        await EnsureRequestRateAsync(actor, ct);
        var model = ResolveModel(request.Model);
        var type = ActivityStructureSettings.Canonical(request.InteractionType)
                   ?? throw new LabelingValidationException("labeling.activity.unknownType");
        var mode = request.Mode?.Trim().ToLowerInvariant() switch
        {
            LabelingActivitySolution.ModeKey => LabelingActivitySolution.ModeKey,
            LabelingActivitySolution.ModeAi => LabelingActivitySolution.ModeAi,
            _ => throw new LabelingValidationException("labeling.solution.invalidMode"),
        };
        var planJson = NormalizePlan(request.Plan, type);
        var block = ValidateBlock(request);
        var key = mode == LabelingActivitySolution.ModeKey ? await LoadKeyAsync(request.Key, ct) : [];

        using var image = LabelingImage.Decode(request.ImageBase64);
        var rect = Rectangle.Intersect(block, new Rectangle(0, 0, image.Width, image.Height));
        if (rect.Width < 1 || rect.Height < 1)
            throw new LabelingValidationException("labeling.invalidRegion");

        var cropHash = ActivityCropHash.Compute(imageHelper, image, [rect])[0];
        // Review: anahtar sırası / boşluk farkı aynı planı başka hash yapmasın (sıralı anahtarlı kanonik JSON).
        var planHash = Sha256(CanonicalJson.Normalize(planJson)!);
        var keyHash = mode == LabelingActivitySolution.ModeKey ? KeyInputHash(key) : string.Empty;
        var prompt = await ActivityPromptStore.GetActiveAsync(db, mode == LabelingActivitySolution.ModeKey
            ? ActivityStructureSettings.SolveFromKeyKind
            : ActivityStructureSettings.SolveAiKind, ct);
        var promptHash = ActivityStructureSettings.SolutionPromptHash(prompt, type);

        if (!request.Force)
        {
            // Review W5: geçmiş tutulur — anahtarı ve modun talimat parmak izi tutan EN YENİ satır.
            var cached = await db.LabelingActivitySolutions.AsNoTracking()
                .Where(s => s.CropSha256 == cropHash && s.InteractionType == type && s.PlanHash == planHash && s.Mode == mode
                            && s.KeyInputHash == keyHash && s.ModelName == model.Id && s.PromptHash == promptHash
                            && s.SolutionJson != null)
                .OrderByDescending(s => s.UpdatedAt).ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync(ct);
            if (cached is not null)
                return ToDto(cached, OriginCached);
        }

        if (!ai.IsConfigured)
            throw new LabelingAiUnavailableException();

        var keyCrops = key.Where(k => k.Entry.CropJpeg is { Length: > 0 })
            .Select(k => new Size(k.Entry.BoxWidth ?? LabelingAnswerKeyEntry.MaxCropEdge, k.Entry.BoxHeight ?? LabelingAnswerKeyEntry.MaxCropEdge));
        var estimate = LabelingAiQuota.Cost(model,
            ClaudeActivitySolutionAi.WorstCaseSolveInputTokens(rect, new Size(image.Width, image.Height), options.Value.MaxImageEdge,
                keyCrops, prompt.Instruction.Length + planJson.Length + ActivitySolutionDrafts.TypeExplanation(type).Length
                          + key.Sum(k => ClaudeActivitySolutionAi.Describe(new AnswerKeyInput(k.Entry.PrintedPage, k.Entry.ActivityLabel,
                              k.Entry.ItemLabel, k.Entry.AnswerText, null)).Length + 30)),
            ClaudeActivitySolutionAi.SolveMaxOutputTokens);
        var reserved = await ReserveAsync(actor, estimate, ct);

        ActivitySolving solving;
        decimal cost;
        var inputs = key.Select(k => new AnswerKeyInput(k.Entry.PrintedPage, k.Entry.ActivityLabel, k.Entry.ItemLabel,
            k.Entry.AnswerText, k.Entry.CropJpeg)).ToList();
        using (var callCts = LiveCallToken())
        {
            try
            {
                solving = await ai.SolveAsync(image, rect, type, planJson, prompt, inputs, model, callCts.Token);
                cost = LabelingAiQuota.Cost(model, solving.InputTokens, solving.OutputTokens);
            }
            catch (Exception ex)
            {
                var usage = (ex as LabelingAiException)?.Usage;
                var failed = usage is { } u ? LabelingAiQuota.Cost(model, u.Input, u.Output) : 0m;
                await LabelingAiQuota.SettleAsync(rateLimitStore, actor, reserved, failed);
                logger.LogWarning(ex, "[Labeling] Claude {Model} solution {Type}/{Mode} crop {Hash} failed (~${Cost:F4} charged)",
                    model.Id, type, mode, cropHash[..16], failed);
                return new LabelingActivitySolutionDto(null, null, null, null, prompt.Version, null, false, [],
                    usage is null ? null : Math.Round(failed, 6), AiFailedKey, cropHash, type, planHash, Parse(planJson), mode,
                    model.Id, key.Select(k => k.Entry.Id).ToList(), usage is { } i ? (int)Math.Min(i.Input, int.MaxValue) : null,
                    usage is { } o ? (int)Math.Min(o.Output, int.MaxValue) : null);
            }
        }
        await LabelingAiQuota.SettleAsync(rateLimitStore, actor, reserved, cost);
        logger.LogInformation(
            "[Labeling] Claude {Model} solution {Type}/{Mode} prompt v{Prompt} crop {Hash}: {In}/{Out} tokens, ~${Cost:F4}",
            model.Id, type, mode, prompt.Version, cropHash[..16], solving.InputTokens, solving.OutputTokens, cost);

        var row = await UpsertSolutionAsync(new LabelingActivitySolution
        {
            CropSha256 = cropHash,
            InteractionType = type,
            PlanHash = planHash,
            PlanJson = planJson,
            Mode = mode,
            KeyInputHash = keyHash,
            KeyEntryIdsJson = mode == LabelingActivitySolution.ModeKey
                ? JsonSerializer.Serialize(key.Select(k => k.Entry.Id).ToList())
                : null,
            ModelName = model.Id,
            PromptHash = promptHash,
            PromptVersion = prompt.Version,
            SolutionJson = solving.SolutionJson,
            Source = mode == LabelingActivitySolution.ModeKey ? SolutionSource.Key : SolutionSource.AI,
            Status = SolutionStatus.Pending,
            InputTokens = solving.InputTokens,
            OutputTokens = solving.OutputTokens,
            EstimatedCostUsd = Math.Round(cost, 6),
            CreatedByKeycloakId = actor.KeycloakId,
            CreatedByName = Truncate(actor.Name, 200),
        });
        return ToDto(row, OriginLive);
    }

    /// <summary>
    /// Canlı sonucu yazar. Aynı anahtarlı en yeni satır onaylanmamış ve insan düzeltmesi görmemişse onun üzerine, aksi halde YENİ satır
    /// (review W5: onaylayan / düzelten kaydı kaybolmaz; geri yükleme en yenisini önce döner). İptal edilemez; yazılamazsa loglanır ve
    /// kaydedilmemiş satır döner (yanıt yine oluşur, <c>DraftSolutionId</c> null).
    /// </summary>
    private async Task<LabelingActivitySolution> UpsertSolutionAsync(LabelingActivitySolution live)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        LabelingActivitySolution? row = null;
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                row = await db.LabelingActivitySolutions.AsTracking()
                    .Where(s => s.CropSha256 == live.CropSha256 && s.InteractionType == live.InteractionType
                                && s.PlanHash == live.PlanHash && s.Mode == live.Mode && s.KeyInputHash == live.KeyInputHash
                                && s.ModelName == live.ModelName)
                    .OrderByDescending(s => s.UpdatedAt).ThenByDescending(s => s.Id)
                    .FirstOrDefaultAsync(CancellationToken.None);
                if (row is not null && (row.Status == SolutionStatus.Approved || row.EditedAt is not null))
                {
                    db.Entry(row).State = EntityState.Detached;
                    row = null;
                }
                if (row is null)
                {
                    row = new LabelingActivitySolution
                    {
                        CropSha256 = live.CropSha256, InteractionType = live.InteractionType, PlanHash = live.PlanHash,
                        Mode = live.Mode, KeyInputHash = live.KeyInputHash, ModelName = live.ModelName, CreatedAt = now,
                    };
                    db.LabelingActivitySolutions.Add(row);
                }
                row.PlanJson = live.PlanJson;
                row.KeyEntryIdsJson = live.KeyEntryIdsJson;
                row.PromptHash = live.PromptHash;
                row.PromptVersion = live.PromptVersion;
                row.SolutionJson = live.SolutionJson;
                row.Source = live.Source;
                row.Status = SolutionStatus.Pending;
                row.InputTokens = live.InputTokens;
                row.OutputTokens = live.OutputTokens;
                row.EstimatedCostUsd = live.EstimatedCostUsd;
                row.CreatedByKeycloakId = live.CreatedByKeycloakId;
                row.CreatedByName = live.CreatedByName;
                row.UpdatedAt = now;
                row.EditedByKeycloakId = null;
                row.EditedByName = null;
                row.EditedAt = null;
                row.ApprovedByKeycloakId = null;
                row.ApprovedByName = null;
                row.ApprovedAt = null;

                try
                {
                    await db.SaveChangesAsync(CancellationToken.None);
                    return row;
                }
                catch (DbUpdateException ex) when (attempt == 0 && DbUpdateExceptionClassifier.IsUniqueViolation(ex))
                {
                    db.Entry(row).State = EntityState.Detached;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Labeling] activity solution for crop {Hash} ({Type}) could not be saved",
                live.CropSha256[..16], live.InteractionType);
            if (row is not null)
                db.Entry(row).State = EntityState.Detached;
            live.CreatedAt = now;
            live.UpdatedAt = now;
            return live;
        }
    }

    public async Task<LabelingActivitySolutionDto?> UpdateSolutionAsync(
        int id, ActivitySolutionUpdateRequestDto request, LabelingActor actor, CancellationToken ct = default)
    {
        var row = await db.LabelingActivitySolutions.AsTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null)
            return null;

        var solution = NormalizeSolution(request.Solution);
        if (!graders.Resolve(row.InteractionType).ValidateDefinition(row.PlanJson, solution))
            throw new LabelingValidationException("labeling.solution.invalidSolution");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var changed = !string.Equals(solution, Canonical(row.SolutionJson), StringComparison.Ordinal);
        if (changed)
        {
            // İnsan düzeltmesi: kaynak aynı kalır (onayda Human olur), yeniden onay gerekir.
            row.SolutionJson = solution;
            row.EditedByKeycloakId = actor.KeycloakId;
            row.EditedByName = Truncate(actor.Name, 200);
            row.EditedAt = now;
            row.Status = SolutionStatus.Pending;
            row.ApprovedByKeycloakId = null;
            row.ApprovedByName = null;
            row.ApprovedAt = null;
            row.UpdatedAt = now;
        }
        if (request.Approve)
            Approve(row, actor, now);

        if (changed || request.Approve)
            await db.SaveChangesAsync(ct);
        return ToDto(row, null);
    }

    public async Task<LabelingActivitySolutionDto?> ApproveSolutionAsync(int id, LabelingActor actor, CancellationToken ct = default)
    {
        var row = await db.LabelingActivitySolutions.AsTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null)
            return null;
        Approve(row, actor, timeProvider.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return ToDto(row, null);
    }

    /// <summary>Onay: geçersiz çözüm onaylanamaz; zaten onaylıysa dokunulmaz. Düzeltilmiş çözümün kaynağı Human olur.</summary>
    private void Approve(LabelingActivitySolution row, LabelingActor actor, DateTime now)
    {
        if (row.SolutionJson is null || !graders.Resolve(row.InteractionType).ValidateDefinition(row.PlanJson, row.SolutionJson))
            throw new LabelingValidationException("labeling.solution.cannotApproveInvalid");
        if (row.Status == SolutionStatus.Approved)
            return;
        if (row.EditedAt is not null)
            row.Source = SolutionSource.Human;
        row.Status = SolutionStatus.Approved;
        row.ApprovedByKeycloakId = actor.KeycloakId;
        row.ApprovedByName = Truncate(actor.Name, 200);
        row.ApprovedAt = now;
        row.UpdatedAt = now;
    }

    public async Task<ActivitySolutionListDto> FindSolutionsAsync(
        string? cropSha256, string? interactionType, string? planHash, CancellationToken ct = default)
    {
        if (!LabelingImage.IsValidSha256(cropSha256))
            throw new LabelingValidationException("labeling.invalidHash");
        if (planHash is not null && !LabelingImage.IsValidSha256(planHash))
            throw new LabelingValidationException("labeling.invalidHash");
        string? type = null;
        if (!string.IsNullOrWhiteSpace(interactionType))
        {
            type = ActivityStructureSettings.Canonical(interactionType)
                   ?? throw new LabelingValidationException("labeling.activity.unknownType");
        }

        var query = db.LabelingActivitySolutions.AsNoTracking().Where(s => s.CropSha256 == cropSha256);
        if (type is not null)
            query = query.Where(s => s.InteractionType == type);
        if (planHash is not null)
            query = query.Where(s => s.PlanHash == planHash);
        var rows = await query.OrderByDescending(s => s.UpdatedAt).ThenByDescending(s => s.Id).Take(MaxSolutionsListed)
            .ToListAsync(ct);
        return new ActivitySolutionListDto(rows.Select(r => ToDto(r, null)).ToList());
    }

    // ================================================================== yardımcılar

    private sealed record KeyEntry(LabelingAnswerKeyEntry Entry);

    private async Task<List<KeyEntry>> LoadKeyAsync(ActivitySolutionKeyDto? key, CancellationToken ct)
    {
        var ids = key?.EntryIds;
        if (ids is { Count: > ActivitySolutionRequestDto.MaxKeyEntries })
            throw new LabelingValidationException("labeling.solution.tooManyKeyEntries");
        if (ids is null || ids.Count == 0 || ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Count)
            throw new LabelingValidationException("labeling.solution.keyRequired");

        var rows = await LiveEntries().Where(e => ids.Contains(e.Id)).ToListAsync(ct);
        if (rows.Count != ids.Count)
            throw new LabelingValidationException("labeling.solution.keyEntryNotFound");
        var byId = rows.ToDictionary(r => r.Id);
        var ordered = ids.Select(id => new KeyEntry(byId[id])).ToList();
        if (ordered.Count(k => k.Entry.CropJpeg is { Length: > 0 }) > ClaudeActivitySolutionAi.MaxKeyImages)
            throw new LabelingValidationException("labeling.solution.tooManyKeyImages");
        return ordered;
    }

    /// <summary>Anahtar girdisinin hash'i: madde metinleri + görsel kırpma hash'leri, verilen sırayla (madde id'leri değil).</summary>
    internal static string KeyInputHash(IEnumerable<LabelingAnswerKeyEntry> entries)
    {
        var payload = JsonSerializer.Serialize(entries.Select(e => new object?[]
            { e.PrintedPage, e.ActivityLabel, e.ItemLabel, e.AnswerText, e.CropSha256 }));
        return Sha256(payload);
    }

    private static string KeyInputHash(IEnumerable<KeyEntry> entries) => KeyInputHash(entries.Select(k => k.Entry));

    /// <summary>Plan: JSON nesnesi, tipi isteğin tipi, tipin kayıt doğrulamasından (çözümsüz) geçer; yeniden serileştirilmiş hali.</summary>
    private string NormalizePlan(JsonElement? plan, string type)
    {
        if (plan is not { ValueKind: JsonValueKind.Object } element)
            throw new LabelingValidationException("labeling.solution.invalidPlan");
        var raw = element.GetRawText();
        if (raw.Length > InteractionPlanSolution.MaxPlanLength)
            throw new LabelingValidationException("labeling.solution.invalidPlan");
        string json;
        try
        {
            json = JsonNode.Parse(raw)!.ToJsonString();
        }
        catch (JsonException)
        {
            throw new LabelingValidationException("labeling.solution.invalidPlan");
        }
        if (element.TryGetProperty("type", out var t) is false || t.ValueKind != JsonValueKind.String
            || !string.Equals(t.GetString(), type, StringComparison.Ordinal)
            || !graders.Resolve(type).ValidateDefinition(json, null))
            throw new LabelingValidationException("labeling.solution.invalidPlan");
        return json;
    }

    private static string NormalizeSolution(JsonElement? solution)
    {
        if (solution is not { ValueKind: JsonValueKind.Object } element)
            throw new LabelingValidationException("labeling.solution.invalidSolution");
        var raw = element.GetRawText();
        if (raw.Length > InteractionPlanSolution.MaxSolutionLength)
            throw new LabelingValidationException("labeling.solution.invalidSolution");
        return Canonical(raw)!;
    }

    private static string? Canonical(string? json)
    {
        if (json is null)
            return null;
        try
        {
            return JsonNode.Parse(json)?.ToJsonString();
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static Rectangle ValidateBlock(ActivitySolutionRequestDto request)
    {
        double[] values = [request.X, request.Y, request.Width, request.Height];
        if (!values.All(double.IsFinite) || request.X < 0 || request.Y < 0 || request.Width < 1 || request.Height < 1
            || request.X + request.Width > LabelingImage.MaxEdge || request.Y + request.Height > LabelingImage.MaxEdge)
            throw new LabelingValidationException("labeling.invalidRegion");
        var x = (int)Math.Round(request.X);
        var y = (int)Math.Round(request.Y);
        return Rectangle.FromLTRB(x, y, (int)Math.Round(request.X + request.Width), (int)Math.Round(request.Y + request.Height));
    }

    private LabelingActivitySolutionDto ToDto(LabelingActivitySolution row, string? origin)
    {
        var valid = row.SolutionJson is not null
                    && graders.Resolve(row.InteractionType).ValidateDefinition(row.PlanJson, row.SolutionJson);
        IReadOnlyList<int> keyIds = [];
        if (row.KeyEntryIdsJson is not null)
        {
            try
            {
                keyIds = JsonSerializer.Deserialize<List<int>>(row.KeyEntryIdsJson) ?? [];
            }
            catch (JsonException)
            {
                keyIds = [];
            }
        }

        return new LabelingActivitySolutionDto(
            row.Id > 0 ? row.Id : null,
            row.SolutionJson is null ? null : Parse(row.SolutionJson),
            row.Source.ToString(),
            ToStatus(row.Status),
            row.PromptVersion,
            origin,
            valid,
            valid ? [] : [InvalidSolutionKey],
            row.EstimatedCostUsd,
            null,
            row.CropSha256,
            row.InteractionType,
            row.PlanHash,
            Parse(row.PlanJson),
            row.Mode,
            row.ModelName,
            keyIds,
            row.InputTokens,
            row.OutputTokens,
            row.CreatedByName,
            row.CreatedAt,
            row.EditedByName,
            row.EditedAt,
            row.ApprovedByName,
            row.ApprovedAt);
    }

    internal static string ToStatus(SolutionStatus status) =>
        status == SolutionStatus.Approved ? StatusApproved : StatusPending;

    private static JsonElement? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task EnsureRequestRateAsync(LabelingActor actor, CancellationToken ct)
    {
        var decision = await rateLimitStore.TryAcquireAsync($"labeling-solution-req:{actor.KeycloakId}", 1,
            options.Value.ActivityStructureRequestsPerMinute, RequestRateWindow, ct);
        if (!decision.Allowed)
            throw new LabelingRateLimitException(decision.RetryAfter);
    }

    private LabelingAiModelOption ResolveModel(string? id) =>
        options.Value.Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal))
        ?? throw new LabelingValidationException("labeling.unknownModel");

    /// <summary>En kötü durum bütçesi + 1 saatlik çağrı; çağrı kotası yetmezse ayrılan bütçe iade edilir.</summary>
    private async Task<int> ReserveAsync(LabelingActor actor, decimal estimate, CancellationToken ct)
    {
        var reserved = await LabelingAiQuota.ReserveBudgetAsync(rateLimitStore, actor, options.Value, estimate, ct);
        try
        {
            await LabelingAiQuota.AcquireCallsAsync(rateLimitStore, actor, options.Value, 1, ct);
        }
        catch
        {
            await LabelingAiQuota.SettleAsync(rateLimitStore, actor, reserved, 0m);
            throw;
        }
        return reserved;
    }

    private CancellationTokenSource LiveCallToken()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime?.ApplicationStopping ?? CancellationToken.None);
        cts.CancelAfter(LiveCallTimeout);
        return cts;
    }

    private static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
