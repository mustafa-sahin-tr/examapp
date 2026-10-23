import type { MockedObject } from "vitest";
// #545 — Angular 21 control-flow migrasyonu (*ngIf → @if / @if-else) sonrası davranış sözleşmesi.
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Subject } from 'rxjs';

import { UserThemeSwitcherComponent } from './user-theme-switcher.component';
import { UserThemeService } from '../../services/user-theme.service';
import { ThemeConfigService, WorksheetCardThemeConfig } from '../../services/theme-config.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import rootTr from '../../../../public/i18n/tr.json';

type SavedTheme = {
    themePreset: string;
    themeCustomConfig?: string | null;
} | null;

const T = rootTr.shared.userTheme;

describe('UserThemeSwitcherComponent (#545)', () => {
    let fixture: ComponentFixture<UserThemeSwitcherComponent>;
    let userTheme$: Subject<SavedTheme>;

    const el = () => fixture.nativeElement as HTMLElement;
    const spinner = () => el().querySelector('mat-spinner');
    const formField = () => el().querySelector('mat-form-field');

    function setup(): void {
        userTheme$ = new Subject<SavedTheme>();
        const theme = { borders: true, gradient: false } as unknown as WorksheetCardThemeConfig;
        const themeConfig = {
            getCurrentTheme: vi.fn().mockName("ThemeConfigService.getCurrentTheme"),
            getAvailablePresets: vi.fn().mockName("ThemeConfigService.getAvailablePresets"),
            getPresetConfig: vi.fn().mockName("ThemeConfigService.getPresetConfig"),
            setTheme: vi.fn().mockName("ThemeConfigService.setTheme"),
            setCustomTheme: vi.fn().mockName("ThemeConfigService.setCustomTheme")
        };
        themeConfig.getCurrentTheme.mockReturnValue(theme);
        themeConfig.getAvailablePresets.mockReturnValue(['minimal', 'standard', 'enhanced', 'full'] as never);
        themeConfig.getPresetConfig.mockReturnValue(theme);

        TestBed.configureTestingModule({
            imports: [UserThemeSwitcherComponent, NoopAnimationsModule, translocoTestingModule()],
            providers: [
                { provide: UserThemeService, useValue: { userTheme$: userTheme$.asObservable() } as unknown as UserThemeService },
                { provide: ThemeConfigService, useValue: themeConfig },
            ],
        });
        fixture = TestBed.createComponent(UserThemeSwitcherComponent);
        fixture.detectChanges();
    }

    it('template_BeforeUserThemeArrives_ShowsSpinnerAndNoForm', () => {
        setup();

        expect(fixture.componentInstance.loading).toBe(true);
        expect(spinner()).not.toBeNull();
        expect(el().textContent).toContain(T.loading);
        expect(formField()).toBeNull();
    });

    it('template_NoSavedTheme_HidesSavedStateAndShowsPresetForm', () => {
        setup();

        userTheme$.next(null);
        fixture.detectChanges();

        expect(spinner()).toBeNull();
        expect(formField()).not.toBeNull();
        expect(el().querySelector('.current-theme-info')).toBeNull();
        expect(el().querySelector('.custom-badge')).toBeNull();
    });

    it('template_SavedPresetWithoutCustomConfig_ShowsSavedStateWithoutCustomBadge', () => {
        setup();

        userTheme$.next({ themePreset: 'enhanced', themeCustomConfig: null });
        fixture.detectChanges();

        const info = el().querySelector('.current-theme-info');
        expect(info).not.toBeNull();
        expect(info?.textContent).toContain(T.savedStateTitle);
        expect(info?.textContent).toContain('Enhanced');
        expect(info?.querySelector('.custom-badge')).toBeNull();
        expect(fixture.componentInstance.selectedPreset).toBe('current');
    });

    it('template_SavedPresetWithCustomConfig_ShowsCustomBadge', () => {
        setup();

        userTheme$.next({ themePreset: 'full', themeCustomConfig: '{"borders":true}' });
        fixture.detectChanges();

        const badge = el().querySelector('.current-theme-info .custom-badge');
        expect(badge).not.toBeNull();
        expect(badge?.textContent).toContain(T.customSettings);
    });

    it('onPresetChange_DividerSelected_DoesNotChangeTheme', () => {
        setup();
        userTheme$.next(null);
        fixture.detectChanges();
        const themeConfig = TestBed.inject(ThemeConfigService) as MockedObject<ThemeConfigService>;

        fixture.componentInstance.onPresetChange('divider-1');

        expect(themeConfig.setTheme).not.toHaveBeenCalled();
    });
});
