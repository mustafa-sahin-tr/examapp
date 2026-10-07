import { TestBed } from '@angular/core/testing';
import { Translation, TranslocoService } from '@jsverse/transloco';

import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { PUBLIC_PAGES_SCOPE } from './public-page-meta';
import rootEn from '../../../../public/i18n/en.json';
import rootTr from '../../../../public/i18n/tr.json';
import landingEn from '../../../../public/i18n/landing/en.json';
import landingTr from '../../../../public/i18n/landing/tr.json';
import publicPagesEn from '../../../../public/i18n/public-pages/en.json';
import publicPagesTr from '../../../../public/i18n/public-pages/tr.json';

const BRAND = 'Hedef Okul';

/** Sözlükteki tüm metin değerleri (iç içe nesneler düzleştirilerek). */
function stringValues(dictionary: Translation): string[] {
  return Object.values(dictionary).flatMap((value) =>
    typeof value === 'string' ? [value] : value && typeof value === 'object' ? stringValues(value as Translation) : []
  );
}

/**
 * Issue #409: marka adı kök sözlükte tek bir anahtardan (`brand.name`) gelir. Diğer metinler adı
 * sabit yazmaz, Transloco anahtar referansı `{{ brand.name }}` ile kullanır.
 */
describe('Brand name i18n key (issue #409)', () => {
  let transloco: TranslocoService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        translocoTestingModule({
          langs: {
            'landing/tr': landingTr,
            'landing/en': landingEn,
            [`${PUBLIC_PAGES_SCOPE}/tr`]: publicPagesTr,
            [`${PUBLIC_PAGES_SCOPE}/en`]: publicPagesEn,
          },
        }),
      ],
    });
    transloco = TestBed.inject(TranslocoService);
  });

  it('brandName_IsHedefOkul_InTurkishAndEnglish', () => {
    expect(rootTr.brand.name).toBe(BRAND);
    expect(rootEn.brand.name).toBe(BRAND);
    expect(transloco.translate('brand.name', {}, 'tr')).toBe(BRAND);
    expect(transloco.translate('brand.name', {}, 'en')).toBe(BRAND);
  });

  it('landingTexts_ResolveBrandReference', () => {
    expect(transloco.translate('landing.about.sub', {}, 'tr')).toBe(`${BRAND} Hakkında`);
    expect(transloco.translate('landing.news.title', {}, 'tr')).toBe(`${BRAND}'dan Haberler`);
    expect(transloco.translate('landing.about.sub', {}, 'en')).toBe(`About ${BRAND}`);
    expect(transloco.translate('landing.meta.title', {}, 'en')).toBe(`${BRAND} | Online Exam and Worksheet Platform`);
  });

  it('legalPageTitles_ResolveBrandReference', () => {
    expect(transloco.translate(`${PUBLIC_PAGES_SCOPE}.terms.meta.title`, {}, 'tr')).toBe(`Kullanım Şartları | ${BRAND}`);
    expect(transloco.translate(`${PUBLIC_PAGES_SCOPE}.privacyPolicy.meta.title`, {}, 'en')).toBe(
      `Privacy Policy | ${BRAND}`
    );
  });

  it('dictionaries_DoNotHardcodeBrandOrOldName', () => {
    const dictionaries: Record<string, Translation> = {
      'tr (brand hariç)': { ...rootTr, brand: {} },
      'en (brand hariç)': { ...rootEn, brand: {} },
      'landing/tr': landingTr,
      'landing/en': landingEn,
      'public-pages/tr': publicPagesTr,
      'public-pages/en': publicPagesEn,
    };

    for (const [name, dictionary] of Object.entries(dictionaries)) {
      for (const value of stringValues(dictionary)) {
        expect(value).withContext(name).not.toContain(BRAND);
        expect(value).withContext(name).not.toContain('ExamApp');
      }
    }
  });
});
