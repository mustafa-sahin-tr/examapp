import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { RegisterComponent } from './register.component';

describe('RegisterComponent', () => {
  beforeEach(() => {
    vi.useFakeTimers({ advanceTimeDelta: 1, shouldAdvanceTime: true });
  });
  afterEach(() => {
    vi.useRealTimers();
  });
  let component: RegisterComponent;
  let fixture: ComponentFixture<RegisterComponent>;
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [provideHttpClient(withXhr()), provideHttpClientTesting(), provideNoopAnimations()],
    }).compileComponents();

    fixture = TestBed.createComponent(RegisterComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    fixture.detectChanges();
  });

  afterEach(() => httpMock.verify());

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  // Issue #240: /api/auth/roles artık yalnızca Admin'e açık; anonim kayıt formu onu çağırmamalı.
  it('offers only the fixed app roles without calling /api/auth/roles', () => {
    httpMock.expectNone('/api/auth/roles');
    expect(component.roles.map((r) => r.value)).toEqual(['Student', 'Teacher', 'Parent']);
    expect(component.registerForm.get('role')?.enabled).toBe(true);
  });

  // Issue #850: kayıt rolle başlar; form rol seçilip "Devam"a basılınca o rolle açılır.
  it('starts with the role step and opens the form for the chosen role', () => {
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('form')).toBeNull();
    const cont = el.querySelector('[data-testid="role-continue"]') as HTMLButtonElement;
    expect(cont.disabled).toBe(true);
    expect(el.querySelector('.auth-callout')).toBeNull();

    (el.querySelector('[data-role="Student"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(component.registerForm.value.role).toBe('Student');
    expect(el.querySelector('[data-role="Student"]')?.getAttribute('aria-checked')).toBe('true');
    expect(el.querySelector('.auth-callout')).not.toBeNull();
    expect(cont.disabled).toBe(false);

    cont.click();
    fixture.detectChanges();
    expect(el.querySelector('form')).not.toBeNull();
    expect(el.querySelector('h1')?.textContent).toContain('Öğrenci hesabı oluştur');

    (el.querySelector('[data-testid="role-change"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('form')).toBeNull();
    expect(component.selectedRole()).toBe('Student');
  });

  it('meets the password rule while typing (#850)', () => {
    const el = fixture.nativeElement as HTMLElement;
    component.selectRole('Teacher');
    component.continueToForm();
    fixture.detectChanges();
    const rule = () => el.querySelector('[data-testid="password-rule"]') as HTMLElement;
    expect(rule().classList).not.toContain('is-met');
    component.registerForm.controls['password'].setValue('abc123');
    fixture.detectChanges();
    expect(rule().classList).toContain('is-met');
    expect(rule().textContent).toContain('✓');
  });

  // Issue #240: register yanıtı id içermez (kayıtlı/yeni e-posta aynı gövde) — her 200'de login'e yönlendirilir.
  it('navigates to login on the generic accepted response without a user id', async () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.registerForm.setValue({
      firstName: 'Ayşe',
      lastName: 'Yılmaz',
      email: 'ayse@test.local',
      role: 'Student',
      password: 'pw1234',
      confirmPassword: 'pw1234',
    });

    component.onSubmit();
    const req = httpMock.expectOne('/api/auth/register');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.role).toBe('Student');
    req.flush({ message: 'Kayıt talebiniz alındı.' });
    await vi.advanceTimersByTimeAsync(1000);

    // Parola navigation state'e (history) konmaz — yalnızca e-posta.
    expect(navigate).toHaveBeenCalledWith(['/login'], { state: { email: 'ayse@test.local' } });
  });

  it('shows the server-localized message on a 400 without navigating', () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const open = vi.spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open');
    component.registerForm.patchValue({
      firstName: 'Ayşe',
      lastName: 'Yılmaz',
      email: 'ayse@localhost',
      role: 'Student',
    });
    component.registerForm.get('password')?.setValue('pw1234');
    component.registerForm.get('confirmPassword')?.setValue('pw1234');

    component.onSubmit();
    httpMock
      .expectOne('/api/auth/register')
      .flush({ message: 'Geçerli bir e-posta adresi girin.' }, { status: 400, statusText: 'Bad Request' });

    expect(open).toHaveBeenCalledWith('Geçerli bir e-posta adresi girin.', 'Kapat', expect.any(Object));
    expect(component.isLoading).toBe(false);
    expect(navigate).not.toHaveBeenCalled();
  });
});
