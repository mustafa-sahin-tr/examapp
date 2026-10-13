import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { CreateChildAccountComponent } from './create-child-account.component';
import {
  CreatedChildAccount,
  CreateChildAccountRequest,
  PARENT_CONSENT_VERSION,
  isAcceptableChildPassword,
  normalizeChildUsername,
} from '../../../models/parent-child-account.model';
import { GradesService } from '../../../services/grades.service';
import { ParentChildAccountService } from '../../../services/parent-child-account.service';
import { ParentLinkService } from '../../../services/parent-link.service';
import { SchoolService } from '../../../services/school.service';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import parentLinksTr from '../../../../../public/i18n/parent-links/tr.json';
import parentLinksEn from '../../../../../public/i18n/parent-links/en.json';
import rootTr from '../../../../../public/i18n/tr.json';

/**
 * Issue #438: velinin "Çocuk hesabı oluştur" formu — istemci doğrulaması (kullanıcı adı biçimi, şifre tabanı / tekrar / kullanıcı
 * adını içermeme, rıza), sunucuya giden gövde (normalize kullanıcı adı, rıza sürümü), başarı özeti (kullanıcı adı var, şifre YOK,
 * form sıfırlanır) ve sunucu hata kodları (UsernameTaken alanda).
 */
describe('CreateChildAccountComponent (issue #438)', () => {
  const PW = 'Kalem2026x'; // test-only example
  let fixture: ComponentFixture<CreateChildAccountComponent>;
  let service: jasmine.SpyObj<ParentChildAccountService>;
  let el: HTMLElement;
  let emitted: CreatedChildAccount[];

  const created: CreatedChildAccount = { studentId: 41, linkId: 7, username: 'ali.kaya', studentName: 'Ali Kaya' };

  function setup(): void {
    service = jasmine.createSpyObj<ParentChildAccountService>('ParentChildAccountService', ['create', 'resetPassword']);
    const linkService = jasmine.createSpyObj<ParentLinkService>('ParentLinkService', ['extractError', 'errorCode']);
    linkService.extractError.and.callFake((err: HttpErrorResponse, fallback: string) => err.error?.message ?? fallback);
    linkService.errorCode.and.callFake((err: HttpErrorResponse) => err.error?.errorCode ?? null);

    TestBed.configureTestingModule({
      imports: [
        CreateChildAccountComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { 'parent-links/tr': parentLinksTr, 'parent-links/en': parentLinksEn } }),
      ],
      providers: [
        { provide: ParentChildAccountService, useValue: service },
        { provide: ParentLinkService, useValue: linkService },
        { provide: GradesService, useValue: { getGrades: () => of([{ id: 3, name: '3. Sınıf' }]) } },
        {
          provide: SchoolService,
          useValue: {
            getSchools: () =>
              of([
                { id: 9, name: 'Atatürk İlkokulu', provinceId: null, provinceName: null, districtId: null, districtName: null, addressLine: null },
              ]),
          },
        },
      ],
    });
    fixture = TestBed.createComponent(CreateChildAccountComponent);
    emitted = [];
    fixture.componentInstance.created.subscribe((c) => emitted.push(c));
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  }

  function type(selector: string, value: string): void {
    const input = el.querySelector<HTMLInputElement>(`[data-test="${selector}"]`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
  }

  /** Geçerli bir form doldurur (sınıf ve rıza form kontrolünden). */
  function fillValid(username = 'Ali.Kaya'): void {
    type('first-name', ' Ali ');
    type('last-name', 'Kaya');
    type('username', username);
    type('pw', PW);
    type('pw-repeat', PW);
    type('student-number', ' 12 ');
    const form = (fixture.componentInstance as unknown as { form: { controls: Record<string, { setValue(v: unknown): void }> } }).form;
    form.controls['gradeId'].setValue(3);
    form.controls['consent'].setValue(true);
    fixture.detectChanges();
  }

  function submit(): void {
    el.querySelector<HTMLButtonElement>('[data-test="create-submit"]')!.click();
    fixture.detectChanges();
  }

  it('model helpers normalize the username and enforce the password floor', () => {
    expect(normalizeChildUsername('  Ali.Kaya ')).toBe('ali.kaya');
    expect(isAcceptableChildPassword(PW)).toBeTrue();
    expect(isAcceptableChildPassword('kisa1')).toBeFalse();
    expect(isAcceptableChildPassword('sadeceharf')).toBeFalse();
    expect(isAcceptableChildPassword(' Kalem2026')).toBeFalse();
  });

  it('renders the form with the brand name in the consent text and the consent version', () => {
    setup();
    expect(el.querySelector('h2')!.textContent).toContain(parentLinksTr.createChild.title);
    const consent = el.querySelector('[data-test="consent"]')!.textContent!;
    expect(consent).toContain(rootTr.brand.name);
    expect(el.textContent).toContain(PARENT_CONSENT_VERSION);
  });

  it('invalid username, weak / mismatching password and missing consent block submit', fakeAsync(() => {
    setup();
    type('username', 'ali-kaya');
    expect(el.querySelector('[data-test="username-invalid"]')).not.toBeNull();

    type('username', 'ali.kaya');
    type('pw', 'kisa1');
    type('pw-repeat', 'baska12345');
    expect(el.querySelector('[data-test="pw-invalid"]')).not.toBeNull();
    expect(el.querySelector('[data-test="pw-mismatch"]')).not.toBeNull();

    type('pw', 'xali.kaya99');
    type('pw-repeat', 'xali.kaya99');
    expect(el.querySelector('[data-test="pw-contains-username"]')).not.toBeNull();

    submit();
    tick();
    expect(service.create).not.toHaveBeenCalled();
    expect(el.querySelector('[data-test="consent-required"]')).not.toBeNull();
  }));

  it('submits the normalized request and shows only the username in the summary (never the password)', fakeAsync(() => {
    setup();
    service.create.and.returnValue(of(created));
    fillValid('  Ali.Kaya ');

    submit();
    tick();
    fixture.detectChanges();

    const request = service.create.calls.mostRecent().args[0] as CreateChildAccountRequest;
    expect(request.username).toBe('ali.kaya');
    expect(request.firstName).toBe('Ali');
    expect(request.gradeId).toBe(3);
    expect(request.schoolId).toBeNull();
    expect(request.studentNumber).toBe('12');
    expect(request.consentAccepted).toBeTrue();
    expect(request.consentVersion).toBe(PARENT_CONSENT_VERSION);

    const summary = el.querySelector('[data-test="create-summary"]')!;
    expect(summary).not.toBeNull();
    expect(el.querySelector('[data-test="summary-username"]')!.textContent!.trim()).toBe('ali.kaya');
    expect(summary.textContent).toContain('Ali Kaya');
    expect(el.textContent).not.toContain(PW);
    expect(el.querySelector('[data-test="create-form"]')).toBeNull();
    expect(emitted).toEqual([created]);

    // "Başka bir çocuk" → boş form (şifre bellekte tutulmadı).
    el.querySelector<HTMLButtonElement>('[data-test="create-another"]')!.click();
    fixture.detectChanges();
    expect(el.querySelector<HTMLInputElement>('[data-test="pw"]')!.value).toBe('');
    expect(el.querySelector<HTMLInputElement>('[data-test="username"]')!.value).toBe('');
  }));

  it('selected school is sent as schoolId', fakeAsync(() => {
    setup();
    service.create.and.returnValue(of(created));
    fillValid();
    const cmp = fixture.componentInstance as unknown as {
      onSchoolSelected(e: { option: { value: unknown } }): void;
    };
    cmp.onSchoolSelected({ option: { value: { id: 9, name: 'Atatürk İlkokulu' } } });
    fixture.detectChanges();

    submit();
    tick();

    expect((service.create.calls.mostRecent().args[0] as CreateChildAccountRequest).schoolId).toBe(9);
  }));

  it('UsernameTaken (409) is shown under the username field with the server message', fakeAsync(() => {
    setup();
    service.create.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { message: 'Bu kullanıcı adı alınmış.', errorCode: 'UsernameTaken' },
          })
      )
    );
    fillValid();

    submit();
    tick();
    fixture.detectChanges();

    expect(el.querySelector('[data-test="username-server"]')!.textContent).toContain('Bu kullanıcı adı alınmış.');
    expect(el.querySelector('[data-test="create-summary"]')).toBeNull();
    expect(emitted).toEqual([]);
  }));

  it('other server errors are shown below the form', fakeAsync(() => {
    setup();
    service.create.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { message: 'En fazla 10', errorCode: 'ChildAccountLimitReached' } }))
    );
    fillValid();

    submit();
    tick();
    fixture.detectChanges();

    expect(el.querySelector('[data-test="server-error"]')!.textContent).toContain('En fazla 10');
  }));
});
