# Bildirim mesaj sözlüğü (issue #185)

BadgeService'in ürettiği tüm in-app bildirimlerin (`Notification.Title` / `Notification.Body`)
metinleri bu klasördeki JSON dosyalarında tutulur. Consumer içinde sabit Türkçe metin
**bırakılmaz** — log mesajları hariç (loglar çevrilmez, Türkçe kalır).

Altyapı, `api/ExamApp.Api/Resources/README.md`'deki JSON sözlük mekanizmasıyla aynıdır
(`ExamApp.Foundation.Localization.JsonResourceStore`), ama tüketim şekli farklıdır: consumer'larda
HTTP istek bağlamı yoktur, bu yüzden `IStringLocalizer` (ki `CultureInfo.CurrentUICulture`'a
dayanır) yerine `JsonResourceStore` doğrudan, kültür **açık parametre** olarak kullanılır. Bkz.
`Services/NotificationTextFactory.cs` XML yorumu.

## Dosyalar

```
Resources/notifications.tr.json
Resources/notifications.en.json
```

Tek alan (`notifications`) yeterli — exam API'nin aksine BadgeService'te controller/domain başına
ayrı dosya ayrımına şu an ihtiyaç yok (tüm bildirimler tek yerde, event türü kadar az sayıda).
İhtiyaç doğarsa `Resources/<alan>.<dil>.json` deseni aynen uygulanabilir (tarama özyinelemeli).

## Anahtar kuralı

- Bildirim başlığı/gövdesi: `notifications.<NotificationType>.title` / `notifications.<NotificationType>.body`.
  `<NotificationType>`, consumer'daki `NotificationType`/`ApprovedType`/`RejectedType` const'u ile
  **birebir aynı** string olmalı (`Notification.Type` kolonuyla da aynı değer — DB'de neyi
  filtrelediğini JSON'da da görürsün).
- Varsayılan/yer tutucu adlar (gönderen adı bilinmiyorsa vb.) `notifications.common.*` altında:
  `defaultTeacher`, `defaultStudent`, `defaultApplicant`, `defaultRequester`, `defaultSchool`,
  `unnamedWorksheet`, `worksheetFallback`, `rejectionReasonSuffix`.
- Parametreler `string.Format` sırasıyla (`{0}`, `{1}`, ...), tıpkı exam API sözlüğünde olduğu gibi.
- Her anahtar hem `tr` hem `en` dosyasında bulunur. Eksik anahtar `tr`'ye düşer (aynı fallback
  zinciri: tam kültür → dil kodu → `tr`), consumer asla boş/patlamış bir bildirim üretmez.

## Yeni event handler eklerken izlenecek pattern (issue #185 karar #7)

Yeni bir outbox event'i BadgeService'te bildirime dönüşecekse (bkz. `.claude/skills/outbox-event`
ve memory'deki "Outbox notification template" — `WorksheetAccessRequestService` + Consumer çifti):

1. **Consumer constructor'ına ekle:** `IUserLocaleResolver localeResolver` ve
   `INotificationTextFactory texts` (mevcut `BadgeDbContext db`, `IHubContext hub`, `ILogger logger`
   parametrelerinin yanına).
2. **Idempotency kontrolünden SONRA, notification oluşturmadan ÖNCE** kültürü çöz:
   ```csharp
   var culture = await _localeResolver.ResolveAsync(hedefUserId, hedefKeycloakId, ct);
   ```
   Hedef belirli bir kullanıcı değil (rol bazlı, ör. Admin) ise resolver'ı ÇAĞIRMA — doğrudan
   `CultureInfo.GetCultureInfo(SupportedLocales.DefaultCultureName)` kullan
   (bkz. `TeacherApplicationSubmittedConsumer`, karar #5).
3. **Varsayılan görünen adları** (`e.SomeName` boşsa) `_texts.Resolve("notifications.common.<key>", culture)`
   ile çöz — sabit Türkçe string YAZMA.
4. **Title/Body'yi** `_texts.Build(NotificationType, culture, arg0, arg1, ...)` ile üret; `text.Title` /
   `text.Body`'yi `Notification` satırına ata. Title ve Body FARKLI argüman sayısı kullanabilir —
   ikisine de aynı `args` dizisi geçilir, `string.Format` kullanılmayan indexleri görmezden gelir.
5. **JSON'a** `notifications.<NotificationType>.title` / `.body` anahtarlarını hem `tr` hem `en`
   dosyasına ekle. Yeni bir varsayılan ad gerekiyorsa `notifications.common.*`'a ekle.
6. **Test için mock'lama:** `IUserLocaleResolver` ve `INotificationTextFactory` arayüz oldukları
   için xUnit testinde sahte (fake/stub) implementasyon verilebilir — gerçek DB/JSON dosyası
   gerekmez. `NotificationTextFactory`'nin gerçek halini test etmek istiyorsan `JsonResourceStore.Load`
   ile bu klasörü (`TestFileProvider` + `PhysicalFileProvider`) yükleyip gerçek anahtarları
   doğrulayabilirsin.

## DI kaydı

`Program.cs`:

```csharp
builder.Services.AddJsonLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddScoped<IUserLocaleResolver, UserLocaleResolver>();
builder.Services.AddSingleton<INotificationTextFactory, NotificationTextFactory>();
```

`AddJsonLocalization` `JsonResourceStore`'u singleton olarak da kaydeder — `NotificationTextFactory`
bunun üzerine ince bir sarmalayıcıdır (durumsuz, singleton olabilir).

## Kullanıcının dilini BadgeService nereden bilir

Senkron auth-api çağrısı YOK. auth-api, kullanıcı kayıt olduğunda (varsayılan dille) ve
`PUT /api/auth/me/locale` ile dil değiştiğinde aynı transaction'da outbox'a
`UserPreferredLocaleChangedEvent` yazar. `UserPreferredLocaleChangedConsumer` bunu tüketip
`UserLocalePreference` tablosuna upsert eder (idempotency: `ChangedAtUtc` daha eskiyse no-op).
`IUserLocaleResolver` bu tabloyu okur; kayıt yoksa (henüz event işlenmemiş/hiç değiştirilmemiş)
varsayılan dile (tr) döner — bu, auth-api'deki `User.PreferredLocale` default'uyla aynı olduğu
için mevcut kullanıcılar için backfill gerekmez.

## Bilinen sınırlar

- Sözlük yalnızca uygulama açılışında okunur (exam API ile aynı sınırlama); metni değiştirmek
  servisin yeniden başlatılmasını gerektirir.
- `UserLocalePreference` event'ten önce sorgulanırsa (ör. servis ilk açıldığında, henüz hiç
  `UserPreferredLocaleChangedEvent` işlenmemiş eski bir kullanıcı için) varsayılan dile düşülür;
  bu kasıtlı ve zararsızdır (auth-api default'u da tr).
