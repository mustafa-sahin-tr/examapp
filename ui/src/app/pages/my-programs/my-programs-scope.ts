/**
 * Program listesi, program detayı ve iki dialog aynı Transloco scope'unu paylaşır:
 * `public/i18n/my-programs/<lang>.json` (issue #183).
 *
 * Dialog komponentleri `MatDialog` ile açıldığı için scope'u sayfanın element injector'ından
 * **devralmaz** — her biri kendi `provideTranslocoScope`'unu verir.
 */
export const MY_PROGRAMS_SCOPE = 'my-programs';
