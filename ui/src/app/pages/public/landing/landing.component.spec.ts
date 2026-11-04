import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Translation } from '@jsverse/transloco';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import rootTr from '../../../../../public/i18n/tr.json';
import landingEn from '../../../../../public/i18n/landing/en.json';
import landingTr from '../../../../../public/i18n/landing/tr.json';
import { LandingComponent } from './landing.component';
import { LANDING_STATS, LANDING_TESTIMONIALS } from './landing-content';

function keysOf(dictionary: Translation, prefix = ''): string[] {
  return Object.entries(dictionary).flatMap(([key, value]) =>
    value && typeof value === 'object' ? keysOf(value as Translation, `${prefix}${key}.`) : [`${prefix}${key}`],
  );
}

describe('LandingComponent (#395)', () => {
  let fixture: ComponentFixture<LandingComponent>;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        LandingComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { tr: rootTr, 'landing/tr': landingTr, 'landing/en': landingEn } }),
      ],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(LandingComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => fixture.destroy());

  it('dictionaries_HaveTheSameKeys', () => {
    expect(keysOf(landingEn).sort()).toEqual(keysOf(landingTr).sort());
  });

  it('rendersOneH1_AndAllSections', () => {
    expect(el.querySelectorAll('h1').length).toBe(1);
    for (const id of ['anasayfa', 'kimler-icin', 'ozellikler', 'ogretmenler', 'ogrenciler', 'motivasyon', 'ozel-ders', 'nasil-calisir', 'dersler', 'yorumlar', 'sss']) {
      expect(el.querySelector(`section[id="${id}"]`), id).not.toBeNull();
    }
  });

  it('makesNoBackendRequest', () => {
    TestBed.inject(HttpTestingController).expectNone(() => true);
  });

  it('stats_ComeFromConstant_WithSampleNote', () => {
    expect(el.querySelectorAll('.stat').length).toBe(LANDING_STATS.length);
    expect(el.querySelector('[data-testid="landing-stats-sample"]')?.textContent).toContain('Örnek veriler');
  });

  it('testimonials_ComeFromConstant_WithDisclaimer', () => {
    expect(el.querySelectorAll('.quote').length).toBe(LANDING_TESTIMONIALS.length);
    expect(el.querySelector('[data-testid="landing-testimonials-disclaimer"]')?.textContent).toContain('Temsili kullanıcı yorumlarıdır.');
  });

  it('links_OfferNoParentRegistration_AndPointToWorkingTargets', () => {
    const hrefs = Array.from(el.querySelectorAll('a')).map((a) => a.getAttribute('href') ?? '');
    expect(hrefs.some((href) => href.includes('intent=parent') || href.includes('role=parent'))).toBe(false);
    expect(hrefs).toContain('/app/login?intent=student');
    expect(hrefs).toContain('/app/login?intent=teacher');
    expect(hrefs).toContain('/app/login');
    expect(hrefs).toContain('/terms');
    expect(hrefs).toContain('/privacy-policy');
    expect(hrefs).toContain('mailto:admin@hedefokul.com');
    for (const href of hrefs.filter((h) => h.startsWith('/welcome#'))) {
      expect(el.querySelector(`[id="${href.split('#')[1]}"]`), href).not.toBeNull();
    }
  });

  it('removedContent_IsNotOnThePage', () => {
    const text = el.textContent ?? '';
    for (const word of ['99 ₺', '499 ₺', 'Bülten', 'Blog', '3-8. sınıf', 'Beden Eğitimi']) {
      expect(text, word).not.toContain(word);
    }
  });

  it('writesStructuredData_AndRemovesItOnDestroy', () => {
    const script = document.getElementById('landing-json-ld');
    expect(script?.textContent).toContain('admin@hedefokul.com');
    expect(script?.textContent).not.toContain('sameAs');
    fixture.destroy();
    expect(document.getElementById('landing-json-ld')).toBeNull();
  });

  it('mobileMenu_OpensClosesAndClosesOnEscape', () => {
    const toggle = el.querySelector('.menu-btn') as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="landing-mobile-menu"]')).not.toBeNull();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="landing-mobile-menu"]')).toBeNull();
  });
});
