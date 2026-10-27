---
description: Angular coding conventions for the ui/ frontend — component style, state, DI, and styling
alwaysApply: false
globs:
  - "ui/**/*.ts"
  - "ui/**/*.html"
  - "ui/**/*.scss"
---

## Component style

- All components are **standalone** (`standalone: true`) — never use NgModule declarations.
- Use `inject()` for dependency injection, not constructor parameters.
- Style files are **SCSS** (`.scss`), one per component.

## State management

- Prefer Angular **signals** (`signal()`, `computed()`, `effect()`) over plain class properties.
- Use `toSignal()` to convert observables to signals at the boundary.
- Keep RxJS for async operations (HTTP, debounce), but expose results via signals where possible.

## UI library

- **Angular Material** is the component library — use it for all UI primitives (buttons, inputs, cards, dialogs, icons, menus, etc.).
- Import only the specific Material modules needed in each component's `imports: []` array.
- **Exception — image-overlay players** (issue #575, `question-canvas-slot-fill`): controls positioned inside small boxes on
  top of a question image may use native `<select>` / `<input>` where `mat-select` / `mat-form-field` can't fit the box.
  Style them with the CSS tokens and keep keyboard access + `aria-label`; everywhere else use Material.

## Styling

- Use **CSS custom properties** (defined in `src/styles.scss`) for all colors and design tokens — never hardcode hex values.
- Key variables: `--primaryColor`, `--secondaryColor`, `--main-bg-color`, `--main-foreground-color`, `--ms-action-background`, `--ms-danger-background`, `--mainFontFamily`, `--paragraphFontFamily`.
- Component-specific styles go in the component's `.scss` file; global/shared styles go in `src/styles.scss`.

## File placement

- New reusable components → `ui/src/app/shared/components/<name>/`
- New page components → `ui/src/app/pages/<name>/`
- New services → `ui/src/app/services/`
- New TypeScript models/interfaces → `ui/src/app/models/`

## Unit test (Vitest, #545)

- `ui` ve `auth-ui` testleri **Vitest browser mode**'da (Playwright + Chromium) koşar; Karma/Jasmine kaldırıldı.
  Gerçek tarayıcı: canvas, `getComputedStyle` + global `styles.scss`, native popover testleri jsdom'da çalışmaz.
- Çalıştırma (uygulama dizininden): `CHROME_BIN` sistem Chrome'unu göstermeli (Playwright tarayıcı indirmesi yok) —
  Windows'ta `export CHROME_BIN="C:/Program Files/Google/Chrome/Application/chrome.exe"`, sonra
  `npx ng test --watch=false` ya da tek dosya için `--include src/app/.../x.spec.ts`. CI aynı komutu
  `CHROME_BIN=/usr/bin/google-chrome` ile koşar.
- API: `vi.fn()`, `vi.spyOn()`, `vi.useFakeTimers()` + `await vi.advanceTimersByTimeAsync(ms)`. Zone `fakeAsync`/`tick`
  Vitest'te çalışmaz. **`vi.spyOn` çağrıyı gerçek metoda geçirir** (Jasmine `spyOn` yutardı) — stub istiyorsan
  `.mockReturnValue(...)`/`.mockImplementation(...)` ekle. Kısmi mock: `{ m: vi.fn() } as unknown as MockedObject<T>`.

## Daha derin bilgi

Bu dosya bizim proje-özel kararlarımızı listeler (Material, CSS token, dizin yerleşimi). Angular'ın
kendisine dair güncel resmi rehberlik (Signal Forms, `linkedSignal`, `resource`/`httpResource`,
routing, testing) için `.claude/skills/angular-developer/` (upstream: angular/skills, Angular ekibinin
resmi repo'su) — `angular-dev` agent'ı bu skill'i zaten otomatik yükler. Çakışma olursa **bu dosya
önceliklidir** (örn. upstream skill Tailwind'den bahsediyor, biz Material + CSS custom property
kullanıyoruz).
