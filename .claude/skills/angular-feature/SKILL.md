---
name: angular-feature
description: ui/ altına yeni sayfa, komponent veya API servisi ekleme prosedürü — routing, signal state, Material ve SCSS token kullanımı. Frontend'e yeni ekran, form veya liste eklenecek her durumda bu adımları izle.
---

# Yeni Angular özelliği

## Sıra

1. **Model.** `ui/src/app/models/<domain>.model.ts` — backend DTO'suyla birebir eşleşen interface.
   Tipi tahmin etme, `api/ExamApp.Api/Models/` altındaki karşılığını oku.
2. **Servis.** `ui/src/app/services/<domain>.service.ts`
   - `inject(HttpClient)`, `providedIn: 'root'`.
   - Base URL gateway (`localhost:5678`), `environment` dosyasından okunur — hardcode etme.
3. **Sayfa.** `ui/src/app/pages/<ad>/` içinde `.ts` + `.html` + `.scss`.
   - `standalone: true`, DI için `inject()`.
   - State `signal()`, türetilmiş değerler `computed()`.
   - HTTP sonucu `toSignal()` ile signal'a çevrilir.
4. **Route.** Uygulamanın route dosyasına lazy `loadComponent` ile ekle. Korumalı sayfaysa mevcut guard'ı uygula.
5. **Material.** Kullandığın modülü komponentin `imports: []` dizisine ekle — fazlasını değil.

## Üç durumu her zaman ele al

```ts
readonly loading = signal(false);
readonly error   = signal<string | null>(null);
readonly items   = signal<Item[]>([]);
```

Şablonda: yükleniyor → spinner, hata → mesaj + tekrar dene, boş liste → boş durum metni.
Bu üçü yoksa özellik bitmiş sayılmaz.

## Stil

Renk ve tipografi yalnızca CSS custom property üzerinden:
`--primaryColor`, `--secondaryColor`, `--main-bg-color`, `--main-foreground-color`,
`--ms-action-background`, `--ms-danger-background`, `--mainFontFamily`, `--paragraphFontFamily`.

Hex kodu, `!important` ve `::ng-deep` yazma. Global stil gerekiyorsa `src/styles.scss`.

## Doğrulama

```bash
cd ui && npx tsc --noEmit
```
