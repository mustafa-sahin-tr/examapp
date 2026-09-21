import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

import { TranslocoHttpLoader } from './transloco-http.loader';
import landingTr from '../../../public/i18n/landing/tr.json';
import rootTr from '../../../public/i18n/tr.json';

/**
 * Loader'in iki modu ayri ayri dogrulanir: tarayicida HTTP, SSR'da build'e gomulu `import()`.
 * SSR yolu prerender edilen HTML'in gercek metni icermesini sagladigi icin kritiktir (issue #182).
 */
describe('TranslocoHttpLoader', () => {
  function createLoader(platform: 'browser' | 'server'): TranslocoHttpLoader {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PLATFORM_ID, useValue: platform },
        TranslocoHttpLoader,
      ],
    });

    return TestBed.inject(TranslocoHttpLoader);
  }

  describe('tarayici', () => {
    it('getTranslation_RootLang_RequestsRootDictionary', () => {
      const loader = createLoader('browser');
      const http = TestBed.inject(HttpTestingController);

      loader.getTranslation('tr').subscribe();

      const request = http.expectOne('/i18n/tr.json');
      expect(request.request.method).toBe('GET');
      request.flush({});
      http.verify();
    });

    it('getTranslation_ScopedLang_RequestsScopedDictionary', () => {
      const loader = createLoader('browser');
      const http = TestBed.inject(HttpTestingController);

      loader.getTranslation('landing/tr').subscribe();

      const request = http.expectOne('/i18n/landing/tr.json');
      expect(request.request.method).toBe('GET');
      request.flush({});
      http.verify();
    });

    it('getTranslation_UnknownLang_ReturnsEmptyWithoutRequest', async () => {
      const loader = createLoader('browser');
      const http = TestBed.inject(HttpTestingController);

      await expectAsync(firstValueFrom(loader.getTranslation('de'))).toBeResolvedTo({});

      http.verify();
    });
  });

  describe('SSR', () => {
    it('getTranslation_RootLang_LoadsBundledRootDictionary', async () => {
      const loader = createLoader('server');

      const translation = await firstValueFrom(loader.getTranslation('tr'));

      expect(translation).toEqual(rootTr);
    });

    it('getTranslation_ScopedLang_LoadsBundledScopeDictionary', async () => {
      const loader = createLoader('server');

      const translation = await firstValueFrom(loader.getTranslation('landing/tr'));

      expect(translation).toEqual(landingTr);
    });

    it('getTranslation_UnknownScope_ReturnsEmptyDictionary', async () => {
      const loader = createLoader('server');

      await expectAsync(firstValueFrom(loader.getTranslation('no-such-scope/tr'))).toBeResolvedTo({});
    });

    it('getTranslation_ScopeWithIllegalCharacters_ReturnsEmptyDictionaryWithoutImport', async () => {
      const loader = createLoader('server');

      await expectAsync(firstValueFrom(loader.getTranslation('../secret/tr'))).toBeResolvedTo({});
      await expectAsync(firstValueFrom(loader.getTranslation('Landing/tr'))).toBeResolvedTo({});
    });

    it('getTranslation_UnknownLang_ReturnsEmptyDictionary', async () => {
      const loader = createLoader('server');

      await expectAsync(firstValueFrom(loader.getTranslation('landing/de'))).toBeResolvedTo({});
    });
  });
});
