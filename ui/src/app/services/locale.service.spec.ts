import { DOCUMENT } from '@angular/common';
import { Injectable, PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService, TranslocoTestingModule } from '@jsverse/transloco';

import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../models/locale';
import { LOCALE_STORAGE_KEY, LocaleService, readStoredLocale } from './locale.service';

/** `reloadPage` protected olduğu için test alt sınıfı üzerinden spy'lanır. */
@Injectable()
class TestableLocaleService extends LocaleService {
  reloadCallCount = 0;

  protected override reloadPage(): void {
    this.reloadCallCount++;
  }
}

const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { tr: {}, en: {} },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
  },
  preloadLangs: true,
});

describe('LocaleService', () => {
  function configure(platformId: string = 'browser'): void {
    TestBed.configureTestingModule({
      imports: [translocoTesting],
      providers: [
        TestableLocaleService,
        { provide: LocaleService, useExisting: TestableLocaleService },
        { provide: PLATFORM_ID, useValue: platformId },
      ],
    });
  }

  function createService(): TestableLocaleService {
    return TestBed.inject(TestableLocaleService);
  }

  beforeEach(() => {
    localStorage.removeItem(LOCALE_STORAGE_KEY);
  });

  afterEach(() => {
    localStorage.removeItem(LOCALE_STORAGE_KEY);
    TestBed.resetTestingModule();
  });

  it('locale_NoStoredPreference_ReturnsDefaultLocale', () => {
    configure();

    expect(createService().locale()).toBe(DEFAULT_LOCALE);
  });

  it('locale_StoredPreferenceIsSupported_ReturnsStoredLocale', () => {
    localStorage.setItem(LOCALE_STORAGE_KEY, 'en');
    configure();

    expect(createService().locale()).toBe('en');
  });

  it('locale_StoredPreferenceIsUnknown_FallsBackToDefaultLocale', () => {
    localStorage.setItem(LOCALE_STORAGE_KEY, 'klingon');
    configure();

    expect(createService().locale()).toBe(DEFAULT_LOCALE);
  });

  it('localeDefinition_LocaleIsEnglish_ExposesAngularAndDateFnsLocales', () => {
    localStorage.setItem(LOCALE_STORAGE_KEY, 'en');
    configure();

    const definition = createService().localeDefinition();

    expect(definition.code).toBe('en');
    expect(definition.angularLocale).toBe('en-US');
    expect(definition.dateFnsLocale.code).toBe('en-US');
  });

  it('localeDefinition_LocaleIsTurkish_ExposesTurkishAngularAndDateFnsLocales', () => {
    configure();

    const definition = createService().localeDefinition();

    expect(definition.angularLocale).toBe('tr');
    expect(definition.dateFnsLocale.code).toBe('tr');
  });

  it('constructor_BrowserPlatform_SetsHtmlLangAttribute', () => {
    localStorage.setItem(LOCALE_STORAGE_KEY, 'en');
    configure();

    createService();

    const document = TestBed.inject(DOCUMENT);
    expect(document.documentElement.getAttribute('lang')).toBe('en');
  });

  it('setLocale_NewLocale_PersistsUpdatesLangAndReloadsPage', () => {
    configure();
    const service = createService();

    service.setLocale('en');

    expect(service.locale()).toBe('en');
    expect(localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('en');
    expect(TestBed.inject(DOCUMENT).documentElement.getAttribute('lang')).toBe('en');
    expect(service.reloadCallCount).toBe(1);
  });

  it('setLocale_NewLocale_UpdatesTranslocoActiveLang', () => {
    configure();
    const service = createService();
    const transloco = TestBed.inject(TranslocoService);

    service.setLocale('en');

    expect(transloco.getActiveLang()).toBe('en');
  });

  it('setLocale_SameLocale_DoesNotReloadPage', () => {
    configure();
    const service = createService();

    service.setLocale(DEFAULT_LOCALE);

    expect(service.reloadCallCount).toBe(0);
    expect(localStorage.getItem(LOCALE_STORAGE_KEY)).toBeNull();
  });

  it('setLocale_ServerPlatform_KeepsStateButTouchesNeitherStorageNorReload', () => {
    configure('server');
    const service = createService();

    service.setLocale('en');

    expect(service.locale()).toBe('en');
    expect(localStorage.getItem(LOCALE_STORAGE_KEY)).toBeNull();
    expect(service.reloadCallCount).toBe(0);
  });

  it('readStoredLocale_NotBrowser_ReturnsDefaultLocaleWithoutReadingStorage', () => {
    localStorage.setItem(LOCALE_STORAGE_KEY, 'en');

    expect(readStoredLocale(false)).toBe(DEFAULT_LOCALE);
  });
});
