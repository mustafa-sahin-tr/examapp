import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoService, TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';

import { NavbarComponent } from './navbar.component';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../../../models/locale';
import { LocalePreferenceService } from '../../../../services/locale-preference.service';
import landingEn from '../../../../../../public/i18n/landing/en.json';
import landingTr from '../../../../../../public/i18n/landing/tr.json';
import rootEn from '../../../../../../public/i18n/en.json';
import rootTr from '../../../../../../public/i18n/tr.json';

/**
 * Navbar hem 'landing' scope'unu hem de kok sozlugu (dil secici) kullanir; her ikisi de gercek
 * JSON dosyalarindan yuklenir (issue #182). Boylece TR ve EN sozlukleri arasindaki bir uyumsuzluk
 * testi kirar.
 */
const translocoTesting = TranslocoTestingModule.forRoot({
  langs: {
    tr: rootTr,
    en: rootEn,
    'landing/tr': landingTr,
    'landing/en': landingEn,
  },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
    reRenderOnLangChange: true,
  },
  preloadLangs: true,
});

describe('NavbarComponent', () => {
  let component: NavbarComponent;
  let fixture: ComponentFixture<NavbarComponent>;

  function host(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function navLinkTexts(): string[] {
    return Array.from(host().querySelectorAll<HTMLElement>('.navbar-nav .nav-link')).map((link) =>
      (link.textContent ?? '').trim()
    );
  }

  beforeEach(async () => {
    const localePreferenceSpy = jasmine.createSpyObj<LocalePreferenceService>('LocalePreferenceService', [
      'persistPreference',
    ]);
    localePreferenceSpy.persistPreference.and.returnValue(of(undefined));

    await TestBed.configureTestingModule({
      imports: [NavbarComponent, translocoTesting],
      providers: [provideRouter([]), { provide: LocalePreferenceService, useValue: localePreferenceSpy }],
    }).compileComponents();

    fixture = TestBed.createComponent(NavbarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('navLinks_DefaultLocale_RenderTurkishLabels', () => {
    expect(navLinkTexts()).toContain(landingTr.nav.home);
    expect(navLinkTexts()).toContain(landingTr.nav.howItWorks);
    expect(navLinkTexts()).toContain(landingTr.nav.login);
  });

  it('navLinks_LocaleSwitchedToEnglish_RenderEnglishLabels', () => {
    TestBed.inject(TranslocoService).setActiveLang('en');
    fixture.detectChanges();

    expect(navLinkTexts()).toContain(landingEn.nav.home);
    expect(navLinkTexts()).toContain(landingEn.nav.howItWorks);
    expect(navLinkTexts()).toContain(landingEn.nav.login);
  });

  it('toggler_Rendered_UsesTranslatedAriaLabel', () => {
    const toggler = host().querySelector('.navbar-toggler') as HTMLElement;

    expect(toggler.getAttribute('aria-label')).toBe(landingTr.nav.toggleMenu);
  });

  it('languageSwitcher_Rendered_IsPartOfTheLandingNavbar', () => {
    expect(host().querySelector('app-language-switcher')).not.toBeNull();
  });
});
