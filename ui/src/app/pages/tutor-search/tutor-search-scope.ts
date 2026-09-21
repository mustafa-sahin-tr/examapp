/**
 * Öğretmen arama listesi ve tekil herkese açık profil aynı Transloco scope'unu paylaşır:
 * `public/i18n/tutor-search/<lang>.json` (issue #183). İki sayfa da ayrı route olduğu için
 * her biri kendi `provideTranslocoScope`'unu verir.
 */
export const TUTOR_SEARCH_SCOPE = 'tutor-search';
