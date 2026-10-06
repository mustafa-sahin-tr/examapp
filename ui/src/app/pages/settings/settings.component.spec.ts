import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';

import { SettingsComponent } from './settings.component';
import { AuthService, UserProfile } from '../../services/auth.service';
import { LocaleService } from '../../services/locale.service';
import { LocalePreferenceService } from '../../services/locale-preference.service';
import { KEYCLOAK_ACCOUNT_CONSOLE_PATH } from '../../models/account-settings.model';
import { Teacher } from '../../models/teacher';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import settingsTr from '../../../../public/i18n/settings/tr.json';
import settingsEn from '../../../../public/i18n/settings/en.json';
import accountSecurityTr from '../../../../public/i18n/account-security/tr.json';
import accountSecurityEn from '../../../../public/i18n/account-security/en.json';

/** Issue #417: öğretmen/admin Ayarlar sayfası — rol bazlı bölümler, dil tercihi, hesap bağlantısı. */
describe('SettingsComponent (issue #417)', () => {
  let fixture: ComponentFixture<SettingsComponent>;
  let user: WritableSignal<UserProfile | null>;
  let refreshProfile: jasmine.Spy;
  let persistPreference: jasmine.Spy;

  function profile(role: 'Teacher' | 'Admin', teacher?: Partial<Teacher>): UserProfile {
    return {
      email: 'ayse@hedefokul.local',
      avatar: '',
      fullName: 'Ayşe Yılmaz',
      id: 1,
      keycloakId: 'k',
      profileId: 1,
      role,
      teacher: teacher ? { id: 1, userId: 1, schoolName: '', ...teacher } : undefined,
    };
  }

  function setup(
    roles: string[],
    initial: UserProfile | null,
    refreshed: Observable<UserProfile | null> = of(initial)
  ): HTMLElement {
    user = signal(initial);
    refreshProfile = jasmine.createSpy('refreshProfile').and.callFake(() => refreshed);
    persistPreference = jasmine.createSpy('persistPreference').and.returnValue(of(undefined));
    const authStub: Partial<AuthService> = {
      hasRealmRole: (role: string) => roles.includes(role),
      user,
      refreshProfile,
    };
    TestBed.configureTestingModule({
      imports: [
        SettingsComponent,
        NoopAnimationsModule,
        translocoTestingModule({
          langs: {
            'settings/tr': settingsTr,
            'settings/en': settingsEn,
            'account-security/tr': accountSecurityTr,
            'account-security/en': accountSecurityEn,
          },
        }),
      ],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authStub },
        { provide: LocaleService, useValue: { locale: signal('tr').asReadonly() } },
        { provide: LocalePreferenceService, useValue: { persistPreference } },
      ],
    });
    fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const text = (el: HTMLElement, selector: string) => el.querySelector(selector)?.textContent?.trim() ?? null;

  it('teacherWithSchool_ShowsProfileAndSchoolReadOnly_HidesTutorLinkWhenNotIndependent', () => {
    const el = setup(['Teacher'], profile('Teacher', { schoolName: 'Hedef Okul', schoolId: 7, isIndependentTutor: false }));

    expect(refreshProfile).toHaveBeenCalledTimes(1);
    expect(text(el, '.stg-profile__name')).toBe('Ayşe Yılmaz');
    expect(text(el, '.stg-profile__email')).toBe('ayse@hedefokul.local');
    expect(text(el, '.stg-profile__role')).toBe('Öğretmen');
    expect(text(el, '.stg-teacher__school')).toBe('Hedef Okul');
    expect(el.querySelector('.stg-teacher__tutor-link')).toBeNull();
    // Salt-okunur: profil/okul için düzenlenebilir alan yok.
    expect(el.querySelector('.stg-profile input, .stg-profile textarea')).toBeNull();
  });

  it('independentTeacher_ShowsTutorProfileLink_AndNoSchoolText', () => {
    const el = setup(['Teacher'], profile('Teacher', { schoolName: '', schoolId: null, isIndependentTutor: true }));

    const link = el.querySelector<HTMLAnchorElement>('.stg-teacher__tutor-link');
    expect(link).not.toBeNull();
    expect(link?.getAttribute('href')).toBe('/tutor-profile');
    expect(text(el, '.stg-teacher__school')).toBe(settingsTr.teacher.noSchool);
  });

  it('teacherIndependenceUnknown_ShowsTutorProfileLink_GuardDecides', () => {
    const el = setup(['Teacher'], profile('Teacher', { schoolName: 'Hedef Okul' }));
    expect(el.querySelector('.stg-teacher__tutor-link')).not.toBeNull();
  });

  it('admin_SeesProfileLanguageAndAccountOnly_NoTeacherSection', () => {
    const el = setup(['Admin'], profile('Admin'));

    expect(text(el, '.stg-profile__role')).toBe('Yönetici');
    expect(el.querySelector('.stg-teacher')).toBeNull();
    expect(el.querySelector('.stg-teacher__tutor-link')).toBeNull();
    expect(el.querySelector('.stg-language')).not.toBeNull();
    expect(el.querySelector('.stg-account')).not.toBeNull();
  });

  it('adminTeacher_ShowsAdminRoleAndTeacherSection', () => {
    const el = setup(['Admin', 'Teacher'], profile('Teacher', { schoolName: 'Hedef Okul', isIndependentTutor: false }));
    expect(text(el, '.stg-profile__role')).toBe('Yönetici');
    expect(el.querySelector('.stg-teacher')).not.toBeNull();
  });

  it('accountLink_PointsToKeycloakAccountConsoleOnGatewayOrigin', () => {
    const el = setup(['Admin'], profile('Admin'));
    const link = el.querySelector<HTMLAnchorElement>('.stg-account .acc-card__link');

    expect(KEYCLOAK_ACCOUNT_CONSOLE_PATH).toBe('/realms/exam-realm/account');
    // Göreli yol → uygulamanın (gateway) origin'i; doğrudan Keycloak portu yok.
    expect(link?.getAttribute('href')).toBe('/realms/exam-realm/account');
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(link?.getAttribute('rel')).toContain('noopener');
    expect(link?.textContent).toContain(accountSecurityTr.open);
  });

  it('languageToggle_SelectingOtherLocale_PersistsPreference', () => {
    const el = setup(['Teacher'], profile('Teacher', { schoolName: 'Hedef Okul' }));
    const toggles = Array.from(el.querySelectorAll<HTMLElement>('.stg-language mat-button-toggle'));
    expect(toggles.map((t) => t.textContent?.trim())).toEqual(['Türkçe', 'English']);

    toggles[1].querySelector<HTMLButtonElement>('button')?.click();
    fixture.detectChanges();

    expect(persistPreference).toHaveBeenCalledOnceWith('en');
  });

  it('languageToggle_ActiveLocale_DoesNotCallService', () => {
    setup(['Admin'], profile('Admin'));
    fixture.componentInstance.selectLocale('tr');
    expect(persistPreference).not.toHaveBeenCalled();
  });

  it('languageToggle_CompletesWithoutReload_ReenablesToggle', () => {
    setup(['Admin'], profile('Admin'));
    const pending = new Subject<void>();
    persistPreference.and.returnValue(pending.asObservable());
    const el = fixture.nativeElement as HTMLElement;

    fixture.componentInstance.selectLocale('en');
    fixture.detectChanges();
    expect(persistPreference).toHaveBeenCalledOnceWith('en');
    expect(el.querySelector('.stg-language button[disabled]')).not.toBeNull();

    pending.complete();
    fixture.detectChanges();
    expect(el.querySelector('.stg-language button[disabled]')).toBeNull();
  });

  it('avatar_LoadError_FallsBackToInitials', () => {
    const el = setup(['Admin'], { ...profile('Admin'), avatar: '/broken/avatar.png' });
    const img = el.querySelector<HTMLImageElement>('img.stg-profile__avatar');
    expect(img).not.toBeNull();

    img?.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(el.querySelector('img.stg-profile__avatar')).toBeNull();
    expect(text(el, '.stg-profile__avatar--initials')).toBe('AY');
  });

  it('loading_NoCachedProfile_ShowsSpinnerUntilRefreshResolves', () => {
    const pending = new Subject<UserProfile | null>();
    const el = setup(['Teacher'], null, pending.asObservable());

    expect(el.querySelector('.stg-profile mat-spinner')).not.toBeNull();
    // Dil ve hesap bölümleri profile bağlı değil — yüklenirken de kullanılabilir.
    expect(el.querySelector('.stg-language mat-button-toggle-group')).not.toBeNull();

    const loaded = profile('Teacher', { schoolName: 'Hedef Okul' });
    user.set(loaded);
    pending.next(loaded);
    pending.complete();
    fixture.detectChanges();

    expect(el.querySelector('.stg-profile mat-spinner')).toBeNull();
    expect(text(el, '.stg-profile__name')).toBe('Ayşe Yılmaz');
  });

  it('error_NoCachedProfile_ShowsErrorAndRetryReloads', () => {
    const el = setup(['Teacher'], null, throwError(() => new Error('boom')));

    expect(text(el, '.stg-profile [role="alert"] span')).toBe(settingsTr.profile.loadError);
    el.querySelector<HTMLButtonElement>('.stg-profile .stg-retry')?.click();
    expect(refreshProfile).toHaveBeenCalledTimes(2);
  });

  it('error_WithCachedProfile_ShowsCachedDataWithStaleNotice', () => {
    const el = setup(['Teacher'], profile('Teacher', { schoolName: 'Hedef Okul' }), throwError(() => new Error('boom')));

    expect(el.querySelector('.stg__stale')).not.toBeNull();
    expect(text(el, '.stg-profile__name')).toBe('Ayşe Yılmaz');
  });

  it('empty_RefreshReturnsNoProfile_ShowsEmptyState', () => {
    const el = setup(['Admin'], null, of(null));
    expect(el.querySelector('.stg__state--empty')).not.toBeNull();
  });

  it('i18n_TrAndEnHaveSameKeys', () => {
    const keys = (o: object, p = ''): string[] =>
      Object.entries(o).flatMap(([k, v]) => (v && typeof v === 'object' ? keys(v, `${p}${k}.`) : [`${p}${k}`]));
    expect(keys(settingsEn).sort()).toEqual(keys(settingsTr).sort());
  });
});
