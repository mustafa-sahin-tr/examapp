# Mesaj sözlüğü (issue #184)

Client'a giden **tüm** hata/başarı metinleri bu klasördeki JSON dosyalarında tutulur.
Kod içinde sabit Türkçe metin bırakılmaz. Log mesajları çevrilmez — onlar Türkçe kalabilir.

resx yerine JSON seçildi: yeni bir dil eklemek veya bir metni düzeltmek yalnızca çeviri
dosyasını değiştirmeyi gerektirsin, kod/derleme gerekmesin. UI de (Transloco) JSON kullanıyor.

## Dosya adlandırma

```
Resources/<alan>.<dil>.json      →  questions.tr.json, questions.en.json, common.tr.json
```

- `<alan>` genelde controller/domain adı (`questions`, `exam`, `booking`…). **Her alan kendi
  dosyasında** olduğu için paralel çalışan geliştiriciler aynı dosyada çakışmaz.
- `<dil>` `ExamApp.Foundation.Localization.SupportedLocales.All` içinden bir kod: `tr`, `en`.
- Alt klasör kullanılabilir (`Resources/Admin/teacher.tr.json`); tarama özyinelemelidir.
- Açılışta **tüm dosyalar dil başına tek sözlükte birleştirilir**.

## Anahtar kuralı

- Biçim: `<alan>.<anahtar>`, gerekiyorsa bir seviye daha gruplama:
  `questions.classification.invalidDifficulty`.
- Her parça **camelCase**: `questions.imageNotFoundInStorage`.
- JSON'da iç içe obje yazılır, nokta ile düzleştirilir:
  ```json
  { "questions": { "classification": { "invalidData": "Geçersiz sınıflandırma verisi." } } }
  ```
  → `questions.classification.invalidData`
- **Aynı anahtar iki dosyada olamaz.** Olursa uygulama açılışta `InvalidOperationException`
  ile durur (hangi dosyanın kazandığı belirsiz kalmasın diye bilinçli fail-fast).
- Genel, alana özgü olmayan metinler `common.*.json` içindedir
  (`common.invalidData`, `common.notFound`, `common.forbidden`, `common.unexpectedError`).
  Alanına özgü bir metni common'a taşıma; kendi dosyanı aç.
- **Her anahtar hem `tr` hem `en` dosyasına eklenir.** `en` eksikse `tr`'ye düşülür
  (fallback zinciri `en-US` → `en` → `tr`), yani hata almazsın ama kullanıcı Türkçe görür.

## Controller'da kullanım

> **Not:** Mevcut testler bazı controller ve servisleri `new XController(...)` ile doğrudan kurduğu
> için localizer ctor parametresi **opsiyonel** tutulur:
> `IStringLocalizer<Messages>? localizer = null` → `_localizer = localizer ?? FallbackMessageLocalizer.Instance`.
> Fallback varsayılan dile (tr) kilitlidir; DI üzerinden gelen gerçek localizer istek kültürünü kullanır.

```csharp
using ExamApp.Foundation.Localization;
using Microsoft.Extensions.Localization;

public class QuestionsController : BaseController
{
    private readonly IStringLocalizer<Messages> _localizer;

    public QuestionsController(..., IStringLocalizer<Messages> localizer) => _localizer = localizer;

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var q = await _questionQuery.GetQuestionById(id);
        if (q is null)
        {
            // .Value ŞART: LocalizedString'i doğrudan anonim objeye koyarsan JSON'a
            // {"name":..,"value":..} olarak serialize olur ve yanıt şekli bozulur.
            return NotFound(new { message = _localizer["questions.notFound"].Value });
        }

        return Ok(q);
    }
}
```

Her yerde tek marker tip kullanılır: **`IStringLocalizer<Messages>`**
(`ExamApp.Foundation.Localization.Messages`). Controller başına ayrı bir tip yoktur —
sözlük zaten birleşik olduğu için gereksiz.

## Servis katmanında kullanım

`ResponseBaseDto.Message` gibi client'a ulaşan metinler servis içinde üretiliyorsa localizer
servise de enjekte edilir. Parametre **opsiyonel** bırakılır ki DI kullanmayan birim testler
(`new QuestionService(ctx, ...)`) derlenmeye devam etsin:

```csharp
public QuestionClassificationService(AppDbContext context, IStringLocalizer<Messages>? localizer = null)
{
    _context = context;
    _localizer = localizer ?? FallbackMessageLocalizer.Instance; // varsayılan dile (tr) kilitli
}
```

`Message` bir `string` olduğu için `Message = _localizer["..."]` yeterlidir
(`LocalizedString` → `string` implicit dönüşüm var).

## Parametreli mesaj

`string.Format` sözdizimi kullanılır, sıralı `{0}`, `{1}`:

```json
{ "questions": { "classification": { "invalidSource": "Geçersiz sınıflandırma kaynağı: {0}. 'Human' veya 'AI' beklenmektedir." } } }
```

```csharp
Message = _localizer["questions.classification.invalidSource", sourceStr];
```

Yer tutucu sırası tüm dillerde **aynı anlamı** taşımalı; İngilizce çeviride cümleyi
yeniden kurabilirsin ama `{0}`'ın neyi temsil ettiği değişmemeli.

## DataAnnotations (model validation)

`AddDataAnnotationsLocalization` bu sözlüğe bağlı. Bir DTO'yu taşırken `ErrorMessage`
alanına düz metin değil **anahtar** yazılır:

```csharp
[Required(ErrorMessage = "admin.teacherApplication.rejectReasonRequired")]
```

Henüz taşınmamış DTO'larda `ErrorMessage` düz Türkçe metindir; anahtar bulunamayınca metnin
kendisi döndüğü için davranış değişmez (yalnızca bir kez uyarı log'u yazılır).

## Yeni dil ekleme

1. `ExamApp.Foundation.Localization.SupportedLocales.All` ve `CultureNames` içine dil kodunu ekle
   (UI'daki `ui/src/app/models/locale.ts` elle senkron tutulur).
2. Her alan için `<alan>.<yenidil>.json` dosyasını ekle. Eksik anahtarlar `tr`'ye düşer.
3. Kod değişikliği gerekmez, uygulamanın yeniden başlatılması yeterlidir.

## Bilinen sınırlar

- Sözlük **yalnızca uygulama açılışında** okunur; dosya değişince otomatik yeniden yükleme yok
  (Development'ta bile). Değişikliği görmek için uygulamayı yeniden başlat.
- Anahtar bulunamazsa yanıt boş kalmaz: anahtarın kendisi döner, `ResourceNotFound = true` olur
  ve anahtar başına bir kez `LogWarning` yazılır.

## Nerede duruyor

| Ne | Nerede |
|---|---|
| Localizer altyapısı | `api/ExamApp.Foundation/Localization/` (`JsonResourceStore`, `JsonStringLocalizer`, `JsonStringLocalizerFactory`, `AddJsonLocalization`) |
| Marker tip | `ExamApp.Foundation.Localization.Messages` |
| DI kaydı | `api/ExamApp.Api/Program.cs` → `AddJsonLocalization(options => options.ResourcesPath = "Resources")` |
| İstek kültürü çözümlemesi | `api/ExamApp.Api/Program.cs` → `RequestLocalization` (#181): Accept-Language → kullanıcının `PreferredLocale`'i → `tr-TR` |
