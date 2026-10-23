import type { MockedObject } from "vitest";
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { TranslocoTestingModule } from '@jsverse/transloco';

import { AppLocale, DEFAULT_LOCALE, SUPPORTED_LOCALES, SUPPORTED_LOCALE_CODES } from '../../../models/locale';
import { LocaleService } from '../../../services/locale.service';
import { LocalePreferenceService } from '../../../services/locale-preference.service';
import { LanguageSwitcherComponent } from './language-switcher.component';
import trTranslations from '../../../../../public/i18n/tr.json';

const translocoTesting = TranslocoTestingModule.forRoot({
    langs: { tr: trTranslations },
    translocoConfig: {
        availableLangs: [...SUPPORTED_LOCALE_CODES],
        defaultLang: DEFAULT_LOCALE,
    },
    preloadLangs: true,
});

describe('LanguageSwitcherComponent', () => {
    let component: LanguageSwitcherComponent;
    let fixture: ComponentFixture<LanguageSwitcherComponent>;
    let localeServiceSpy: MockedObject<LocaleService>;
    let localePreferenceSpy: MockedObject<LocalePreferenceService>;
    let localeSignal: ReturnType<typeof signal<AppLocale>>;

    beforeEach(async () => {
        localeSignal = signal<AppLocale>('tr');
        localeServiceSpy = {
            setLocale: vi.fn().mockName("LocaleService.setLocale")
        } as unknown as MockedObject<LocaleService>;
        localePreferenceSpy = {
            persistPreference: vi.fn().mockName("LocalePreferenceService.persistPreference")
        } as unknown as MockedObject<LocalePreferenceService>;
        localePreferenceSpy.persistPreference.mockReturnValue(of(undefined));
        Object.defineProperty(localeServiceSpy, 'locale', { value: localeSignal.asReadonly() });
        Object.defineProperty(localeServiceSpy, 'localeDefinition', {
            value: signal(SUPPORTED_LOCALES[0]).asReadonly(),
        });

        await TestBed.configureTestingModule({
            imports: [LanguageSwitcherComponent, translocoTesting],
            providers: [
                { provide: LocaleService, useValue: localeServiceSpy },
                { provide: LocalePreferenceService, useValue: localePreferenceSpy },
            ],
        }).compileComponents();

        fixture = TestBed.createComponent(LanguageSwitcherComponent);
        component = fixture.componentInstance;
        fixture.detectChanges();
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('locales_Always_ExposesEverySupportedLocale', () => {
        expect(component.locales.map((locale) => locale.code)).toEqual([...SUPPORTED_LOCALE_CODES]);
    });

    it('activeLocaleLabel_ActiveLocaleIsTurkish_ReturnsUppercaseCode', () => {
        expect(component.activeLocaleLabel()).toBe('TR');
    });

    it('isActive_CodeMatchesActiveLocale_ReturnsTrue', () => {
        expect(component.isActive('tr')).toBe(true);
        expect(component.isActive('en')).toBe(false);
    });

    it('selectLocale_Called_PersistsPreferenceToProfile', () => {
        component.selectLocale('en');

        expect(localePreferenceSpy.persistPreference).toHaveBeenCalledTimes(1);

        expect(localePreferenceSpy.persistPreference).toHaveBeenCalledWith('en');
        // Dili uygulamak LocalePreferenceService'in isi; komponent dogrudan setLocale cagirmaz.
        expect(localeServiceSpy.setLocale).not.toHaveBeenCalled();
    });

    it('trigger_Rendered_UsesTranslatedTooltipAndAriaLabel', () => {
        const trigger = fixture.nativeElement.querySelector('.language-switcher-trigger') as HTMLElement;

        expect(trigger.getAttribute('aria-label')).toBe(trTranslations.layout.language.switch);
    });

    it('menu_Opened_ListsEverySupportedLocaleAndMarksActiveOne', () => {
        const trigger = fixture.nativeElement.querySelector('.language-switcher-trigger') as HTMLElement;
        trigger.click();
        fixture.detectChanges();

        const items = Array.from(document.querySelectorAll<HTMLElement>('.mat-mdc-menu-panel button[mat-menu-item]'));

        expect(items.length).toBe(SUPPORTED_LOCALES.length);
        expect(items[0].getAttribute('aria-current')).toBe('true');
        expect(items[1].getAttribute('aria-current')).toBeNull();
    });

    it('menuItem_Clicked_SetsSelectedLocale', () => {
        const trigger = fixture.nativeElement.querySelector('.language-switcher-trigger') as HTMLElement;
        trigger.click();
        fixture.detectChanges();

        const items = Array.from(document.querySelectorAll<HTMLElement>('.mat-mdc-menu-panel button[mat-menu-item]'));
        items[1].click();

        expect(localePreferenceSpy.persistPreference).toHaveBeenCalledTimes(1);

        expect(localePreferenceSpy.persistPreference).toHaveBeenCalledWith('en');
    });
});
