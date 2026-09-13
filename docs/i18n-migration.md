# Sayfa i18n taşıma rehberi (`ui/`)

Bir sayfanın Türkçe metinlerini Transloco çeviri anahtarlarına taşımak için izlenecek adımlar.
Altyapı issue #180'de kuruldu, scope deseni #182'de (landing sayfası) uygulandı. Referans olarak
`src/app/pages/public/landing/` (scope'lu) ve `src/app/pages/dashboard/` (kök sözlük) sayfalarına bak.

## 1. Scope oluştur

Her sayfa/alan **kendi scope'unu** kullanır; böylece paralel çalışan taşımalar tek bir `tr.json`
üzerinde çakışmaz.

1. `ui/public/i18n/<scope>/tr.json` ve `ui/public/i18n/<scope>/en.json` dosyalarını oluştur.
   Scope adı kebab-case, `^[a-z][a-z0-9-]*$` kalıbına uymalı (loader bunu doğruluyor).
2. Sayfanın **route ile yüklenen kök komponentine** provider'ı ekle:

```ts
import { provideTranslocoScope } from '@jsverse/transloco';

@Component({
  // ...
  providers: [provideTranslocoScope('landing')],
})
export class LandingComponent {}
```

Alt komponentler scope'u element injector ağacından devralır — kendi provider'larını vermeleri
**gerekmez**. Tek istisna: aynı alt komponent scope'lu sayfa dışında (örn. ayrı bir lazy route'ta)
da kullanılıyorsa, orada kendi `provideTranslocoScope`'unu vermelidir.

Kök `public/i18n/{tr,en}.json` yalnızca gerçekten paylaşılan anahtarlar içindir: `common.*`
(loading/retry/error), `layout.*` (kabuk, dil seçici). Sayfaya özel bir anahtarı oraya koyma.

## 2. Anahtar adlandırma

`<scope>.<bölüm>.<anahtar>` — bölüm ve anahtar **camelCase**:

```
landing.pricing.teacher.cta
landing.faq.membership.question
landing.footer.contact.emailLabel
```

- Bölüm adı genelde alt komponent/section adıdır (`hero`, `faq`, `footer`).
- Anahtar **anlamı** anlatır, metni değil: `cta`, `emptyState`, `deleteConfirm` — `buttonText2` değil.
- Aynı metin iki yerde geçiyorsa tek anahtar kullan (örn. ders adları `landing.categories.*`
  hem kategori kartlarında hem footer'da kullanılıyor).
- Marka adı, e-posta, telefon, kişi adı gibi sabitler **çevrilmez**, şablonda düz metin kalır.
- Dekoratif görsellerin `alt="image"` gibi sahte metinleri anahtar olmaz; `alt=""` yapılır.

## 3. Şablon desenleri

### `*transloco` (varsayılan)

Bir komponentte 1'den fazla anahtar varsa yapısal direktifi kullan — pipe'a göre daha az iş yapar,
`prefix` ile anahtarlar kısalır:

```html
<div class="faq-area" *transloco="let t; prefix: 'landing'">
  <h2>{{ t('faq.titleLead') }}</h2>
  <input [placeholder]="t('footer.emailPlaceholder')" />
  <button [attr.aria-label]="t('common.backToTop')">…</button>
</div>
```

Komponentin `imports: []` dizisine `TranslocoDirective` eklenir.

### `| transloco` pipe

Tek bir anahtar için yeterli: `{{ 'landing.hero.cta' | transloco }}` (`TranslocoPipe` import edilir).
Pipe'ta anahtar **tam** yazılır, `prefix` yoktur.

### TypeScript içinden

Snackbar, `confirm()`, `Title`/`Meta`, grafik eksen etiketleri gibi şablon dışı metinler:

```ts
private readonly transloco = inject(TranslocoService);

// Scope'lu sayfada: önce scope'u yükleyen selectTranslate, sonrasında senkron translate.
this.transloco
  .selectTranslate<string>('meta.title', {}, 'landing')   // key scope'a göreli
  .pipe(takeUntilDestroyed(this.destroyRef))
  .subscribe((title) => {
    this.titleService.setTitle(title);
    this.metaService.updateTag({ name: 'description', content: this.transloco.translate('landing.meta.description') });
  });
```

`translate()` senkrondur ve **tam anahtar** ister (`landing.meta.description`); ancak sözlük
yüklendikten sonra çağrılmalıdır. `translate<string>()` dönüşü `string | undefined` tiplidir,
`?? ''` ile daralt.

### Parametre

```json
{ "welcome": { "title": "Hoş geldin, {{name}}" } }
```

```html
{{ t('welcome.title', { name: user.name }) }}
```

### Diziler / dinamik listeler

Sabit sayıda öğe varsa **ayrı anahtar** kullan (grep'lenebilir, eksik anahtar tespit edilir).
Uzunluğu çeviriye göre değişen listelerde JSON dizisi + `selectTranslateObject` kullanılabilir;
`translateObject` senkron karşılığıdır.

### Çoğul

Transloco'da yerleşik ICU plural yoktur. İki biçim yeterliyse anahtar ikiye ayrılır
(`item.one` / `item.other`) ve komponentte seçilir; daha karmaşık durumlarda
Angular `i18nPlural` pipe'ı ya da sayıyı parametre olarak veren tek bir cümle tercih edilir.

### Tarih, sayı, para

Çeviri dosyasına biçimlenmiş metin **yazma**. Angular pipe'larını kullan:
`{{ value | date:'longDate' }}`, `{{ n | number }}`. `LOCALE_ID` ve `MAT_DATE_LOCALE`
`LocaleService` üzerinden aktif dile bağlanmıştır (`app.config.ts`).
Intl API'sini elle çağırıyorsan dili sabit yazma:

```ts
private get intlLocale(): string {
  return this.localeService.localeDefinition().angularLocale; // 'tr' | 'en-US'
}
```

## 4. Test

`TranslocoTestingModule` ile **gerçek** sözlüğü yükle — sahte çeviri kullanma; böylece anahtar
bozulduğunda test kırılır. Desen: `src/app/pages/dashboard/dashboard.component.spec.ts`.

```ts
import trTranslations from '../../../../public/i18n/tr.json';
import landingTr from '../../../../public/i18n/landing/tr.json';

const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { tr: trTranslations, 'landing/tr': landingTr },   // scope'lu sözlük böyle verilir
  translocoConfig: { availableLangs: [...SUPPORTED_LOCALE_CODES], defaultLang: DEFAULT_LOCALE },
  preloadLangs: true,
});
```

`ui/`'da üç eski spec derlemeyi kırdığı için hedefli test çalıştırırken geçici bir
`tsconfig.spec.tmp.json` gerekir (bkz. `.claude/agent-memory/angular-dev/project_broken_specs.md`).

## 5. Kontrol listesi

- [ ] Şablondaki tüm metin düğümleri
- [ ] `alt`, `aria-label`, `title`, `placeholder`, `matTooltip` öznitelikleri
- [ ] `@if/@else` boş durum, hata ve yükleniyor metinleri
- [ ] TS içindeki sabit listeler (menü, sekme, filtre, grafik serisi adları)
- [ ] `MatSnackBar`, `confirm()`, `alert()`, dialog başlık/buton metinleri
- [ ] `Title` / `Meta` etiketleri (SSR'da prerender edilen sayfalarda önemli)
- [ ] Sabit `'tr-TR'` verilen `toLocaleDateString` / `toLocaleString` / `Intl.*` çağrıları
- [ ] `tr.json` ve `en.json` anahtar setleri **birebir** aynı
- [ ] Marka adı / e-posta / telefon çevrilmemiş
- [ ] `ng build` temiz; prerender edilen HTML'de ham anahtar (`<scope>.` ile başlayan string) yok

## 6. SSR / prerender

`TranslocoHttpLoader` tarayıcıda `/i18n/<path>.json` çeker; SSR'da HTTP sunucusu olmadığı için aynı
JSON'ları build'e gömülü olarak `import()` eder. Scope'lu yol (`landing/tr`) için şablonlu dinamik
import kullanılır, yani **yeni bir scope eklerken loader'da kod değişmez** — sadece JSON dosyalarını
oluştur. Prerender her zaman `DEFAULT_LOCALE` (tr) üretir; `en` tercihi olan kullanıcı hydration
tamamlanana kadar TR görür (bilinçli kabul edilmiş sınır, bkz. issue #180).

## 7. Yeni dil ekleme

Tek dokunulacak yer `src/app/models/locale.ts` içindeki `SUPPORTED_LOCALES`:

1. `public/i18n/<code>.json` ve her scope için `public/i18n/<scope>/<code>.json` dosyalarını
   mevcut sözlüklerle **aynı anahtar setiyle** oluştur.
2. `SUPPORTED_LOCALES`'e bir satır ekle: `code`, `label` (kendi dilinde), `angularLocale`,
   `angularLocaleData`, `dateFnsLocale`, `serverDictionary`.

`availableLangs`, `registerLocaleData`, `LOCALE_ID`, `MAT_DATE_LOCALE` ve dil seçici menüsü bu
listeden türer; ikinci bir dil listesi tutma.
