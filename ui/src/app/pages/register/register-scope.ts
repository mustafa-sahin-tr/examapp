/**
 * Kayıt akışının tamamı (hesap formu, sihirbaz, rol bazlı tamamlama sayfaları) tek bir Transloco
 * scope'unu paylaşır: `public/i18n/register/<lang>.json` (issue #183). Sayfalar ayrı route'larda
 * olduğu için her biri kendi `provideTranslocoScope`'unu verir; scope adı burada tek yerde durur.
 */
export const REGISTER_SCOPE = 'register';
