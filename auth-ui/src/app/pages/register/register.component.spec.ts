import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { RegisterComponent } from './register.component';

describe('RegisterComponent', () => {
  let component: RegisterComponent;
  let fixture: ComponentFixture<RegisterComponent>;
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations()],
    })
    .compileComponents();

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
    expect(component.registerForm.get('role')?.enabled).toBeTrue();
  });

  // Issue #240: register yanıtı id içermez (kayıtlı/yeni e-posta aynı gövde) — her 200'de login'e yönlendirilir.
  it('navigates to login on the generic accepted response without a user id', fakeAsync(() => {
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
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
    tick(1000);

    // Parola navigation state'e (history) konmaz — yalnızca e-posta.
    expect(navigate).toHaveBeenCalledWith(['/login'], { state: { email: 'ayse@test.local' } });
  }));

  it('shows the server-localized message on a 400 without navigating', () => {
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    const open = spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open').and.callThrough();
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

    expect(open).toHaveBeenCalledWith('Geçerli bir e-posta adresi girin.', 'Kapat', jasmine.any(Object));
    expect(component.isLoading).toBeFalse();
    expect(navigate).not.toHaveBeenCalled();
  });
});
