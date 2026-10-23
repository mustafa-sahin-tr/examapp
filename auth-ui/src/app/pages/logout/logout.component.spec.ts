// #545 — Angular 21 control-flow migrasyonu (@if adım ikonları) sonrası davranış sözleşmesi.
import { ComponentFixture, TestBed, fakeAsync, flush, tick } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { LogoutComponent } from './logout.component';
import { AuthService } from '../../services/auth.service';

describe('LogoutComponent (#545)', () => {
  let fixture: ComponentFixture<LogoutComponent>;
  let auth: jasmine.SpyObj<AuthService>;
  let router: Router;

  const el = () => fixture.nativeElement as HTMLElement;
  const stepIcons = () =>
    Array.from(el().querySelectorAll('.ms-step-circle mat-icon')).map((i) => i.textContent?.trim());

  function setup(user: { fullName: string } | null, logout$ = of(undefined)): void {
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['getUser', 'logout']);
    auth.getUser.and.returnValue(user);
    auth.logout.and.returnValue(logout$);

    TestBed.configureTestingModule({
      imports: [LogoutComponent],
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    });
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
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

  it('template_InitialStep_ShowsFirstMessageAndLogoutIconOnlyForFirstStep', fakeAsync(() => {
    setup({ fullName: 'Ayşe' });
    fixture.detectChanges();

    expect(el().querySelector('.ms-loading-text h3')?.textContent).toContain('Oturum sonlandırılıyor...');
    expect(stepIcons()).toEqual(['logout', 'cleaning_services', 'exit_to_app']);
    expect(el().querySelectorAll('.ms-step-active').length).toBe(1);
    flush();
  }));

  it('template_AfterFirstDelay_CompletesFirstStepWithCheckIcon', fakeAsync(() => {
    setup({ fullName: 'Ayşe' });
    fixture.detectChanges();

    tick(200);
    fixture.detectChanges();

    expect(el().querySelector('.ms-loading-text h3')?.textContent).toContain('Veriler güvenli bir şekilde temizleniyor...');
    expect(stepIcons()[0]).toBe('check');
    expect(stepIcons()[1]).toBe('cleaning_services');
    expect(el().querySelector('.ms-step')?.classList.contains('ms-step-completed')).toBeTrue();
    flush();
  }));

  it('template_LogoutSucceeds_ShowsLastStepAndNavigatesToLogin', fakeAsync(() => {
    setup({ fullName: 'Ayşe' });
    fixture.detectChanges();

    tick(200);
    tick(200);
    fixture.detectChanges();
    expect(stepIcons()).toEqual(['check', 'check', 'exit_to_app']);
    expect(el().querySelector('.ms-loading-text h3')?.textContent).toContain('Çıkış yapılıyor...');

    tick(200);
    flush();

    expect(auth.logout).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  }));

  it('performLogout_LogoutErrors_StillNavigatesToLogin', fakeAsync(() => {
    spyOn(console, 'error');
    setup({ fullName: 'Ayşe' }, throwError(() => new Error('network')));
    fixture.detectChanges();

    tick(600);
    flush();

    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  }));
});
