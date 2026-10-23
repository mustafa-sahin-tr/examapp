import type { MockedObject } from 'vitest';
// #545 — Angular 21 control-flow migrasyonu (@if adım ikonları) sonrası davranış sözleşmesi.
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { LogoutComponent } from './logout.component';
import { AuthService } from '../../services/auth.service';

describe('LogoutComponent (#545)', () => {
  beforeEach(() => {
    vi.useFakeTimers({ advanceTimeDelta: 1, shouldAdvanceTime: true });
  });
  afterEach(() => {
    vi.useRealTimers();
  });
  let fixture: ComponentFixture<LogoutComponent>;
  let auth: MockedObject<AuthService>;
  let router: Router;

  const el = () => fixture.nativeElement as HTMLElement;
  const stepIcons = () =>
    Array.from(el().querySelectorAll('.ms-step-circle mat-icon')).map((i) => i.textContent?.trim());

  function setup(
    user: {
      fullName: string;
    } | null,
    logout$ = of(undefined)
  ): void {
    auth = {
      getUser: vi.fn().mockName('AuthService.getUser'),
      logout: vi.fn().mockName('AuthService.logout'),
    } as unknown as MockedObject<AuthService>;
    auth.getUser.mockReturnValue(user);
    auth.logout.mockReturnValue(logout$);

    TestBed.configureTestingModule({
      imports: [LogoutComponent],
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(LogoutComponent);
  }

  it('template_UserWithFullName_GreetsByName', () => {
    setup({ fullName: 'Ayşe Yılmaz' });
    fixture.detectChanges();

    expect(el().querySelector('h2')?.textContent).toContain('Ayşe Yılmaz');
  });

  it('ngOnInit_NoUser_GreetsWithGenericName', () => {
    setup(null);
    fixture.detectChanges();

    expect(el().querySelector('h2')?.textContent).toContain('Kullanıcı');
  });

  it('template_InitialStep_ShowsFirstMessageAndLogoutIconOnlyForFirstStep', async () => {
    setup({ fullName: 'Ayşe' });
    fixture.detectChanges();

    expect(el().querySelector('.ms-loading-text h3')?.textContent).toContain('Oturum sonlandırılıyor...');
    expect(stepIcons()).toEqual(['logout', 'cleaning_services', 'exit_to_app']);
    expect(el().querySelectorAll('.ms-step-active').length).toBe(1);
    await vi.runAllTimersAsync();
  });

  it('template_AfterFirstDelay_CompletesFirstStepWithCheckIcon', async () => {
    setup({ fullName: 'Ayşe' });
    fixture.detectChanges();

    await vi.advanceTimersByTimeAsync(200);
    fixture.detectChanges();

    expect(el().querySelector('.ms-loading-text h3')?.textContent).toContain(
      'Veriler güvenli bir şekilde temizleniyor...'
    );
    expect(stepIcons()[0]).toBe('check');
    expect(stepIcons()[1]).toBe('cleaning_services');
    expect(el().querySelector('.ms-step')?.classList.contains('ms-step-completed')).toBe(true);
    await vi.runAllTimersAsync();
  });

  it('template_LogoutSucceeds_ShowsLastStepAndNavigatesToLogin', async () => {
    setup({ fullName: 'Ayşe' });
    fixture.detectChanges();

    await vi.advanceTimersByTimeAsync(200);
    await vi.advanceTimersByTimeAsync(200);
    fixture.detectChanges();
    expect(stepIcons()).toEqual(['check', 'check', 'exit_to_app']);
    expect(el().querySelector('.ms-loading-text h3')?.textContent).toContain('Çıkış yapılıyor...');

    await vi.advanceTimersByTimeAsync(200);
    await vi.runAllTimersAsync();

    expect(auth.logout).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('performLogout_LogoutErrors_StillNavigatesToLogin', async () => {
    vi.spyOn(console, 'error').mockReturnValue(undefined);
    setup(
      { fullName: 'Ayşe' },
      throwError(() => new Error('network'))
    );
    fixture.detectChanges();

    await vi.advanceTimersByTimeAsync(600);
    await vi.runAllTimersAsync();

    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });
});
