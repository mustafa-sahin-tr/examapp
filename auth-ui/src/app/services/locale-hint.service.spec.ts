import { TestBed } from '@angular/core/testing';

import { LOCALE_STORAGE_KEY, LocaleHintService } from './locale-hint.service';

/**
 * `resolveLoginLocale()` öncelik sırasını doğrular: kayıtlı tercih → tarayıcı dili → 'tr'.
 * `navigator.languages` salt okunur olduğu için `spyOnProperty` ile taklit edilir.
 */
describe('LocaleHintService', () => {
  let service: LocaleHintService;

  function withBrowserLanguages(languages: string[]): void {
    spyOnProperty(navigator, 'languages', 'get').and.returnValue(languages);
    spyOnProperty(navigator, 'language', 'get').and.returnValue(languages[0] ?? '');
  }

  beforeEach(() => {
    localStorage.removeItem(LOCALE_STORAGE_KEY);
    TestBed.configureTestingModule({});
    service = TestBed.inject(LocaleHintService);
  });

  afterEach(() => {
    localStorage.removeItem(LOCALE_STORAGE_KEY);
  });

  it('kayıtlı app-locale değerini tarayıcı diline tercih eder', () => {
    localStorage.setItem(LOCALE_STORAGE_KEY, 'en');
    withBrowserLanguages(['tr-TR']);

    expect(service.resolveLoginLocale()).toBe('en');
  });

  it('kayıt yoksa tarayıcı dillerinden ilk desteklenen ön eki seçer', () => {
    withBrowserLanguages(['de-DE', 'fr', 'en-GB', 'tr']);

    expect(service.resolveLoginLocale()).toBe('en');
  });

  it('kayıt ve eşleşen tarayıcı dili yoksa tr döner', () => {
    withBrowserLanguages(['de-DE', 'fr-FR']);

    expect(service.resolveLoginLocale()).toBe('tr');
  });

  it('geçersiz kayıtlı değeri yok sayıp tarayıcı diline düşer', () => {
    localStorage.setItem(LOCALE_STORAGE_KEY, 'klingon');
    withBrowserLanguages(['en-US']);

    expect(service.resolveLoginLocale()).toBe('en');
  });

  it('rememberPreferredLocale geçerli değeri normalize ederek yazar', () => {
    service.rememberPreferredLocale('EN-us');

    expect(localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('en');
  });

  it('rememberPreferredLocale geçersiz değerde mevcut kaydı bozmaz', () => {
    localStorage.setItem(LOCALE_STORAGE_KEY, 'en');

    service.rememberPreferredLocale('');
    service.rememberPreferredLocale('klingon');
    service.rememberPreferredLocale(null);

    expect(localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('en');
  });
});
