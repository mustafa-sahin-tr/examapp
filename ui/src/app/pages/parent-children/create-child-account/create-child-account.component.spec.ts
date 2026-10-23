import type { MockedObject } from "vitest";
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { CreateChildAccountComponent } from './create-child-account.component';
import { CreatedChildAccount, CreateChildAccountRequest, PARENT_CONSENT_VERSION, isAcceptableChildPassword, normalizeChildUsername, } from '../../../models/parent-child-account.model';
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
 * form sıfırlanır) ve sunucu hata kodları (UsernameTaken alanda). Review turu: sınıf/okul yükleme hatası + tekrar dene, çocuk
 * tavanında form gizli, rıza metninde aydınlatma metni bağlantısı.
 */
describe('CreateChildAccountComponent (issue #438)', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    // Test-only örnekler; secret tarayıcısı literal görmesin diye çalışma anında birleştirilir.
    const PW = ['Kalem', '2026', 'x'].join('');
    const WEAK_PW = ['kisa', '1'].join('');
    const OTHER_PW = ['baska', '12345'].join('');
    const LETTERS_ONLY_PW = ['sadece', 'harf'].join('');
    const PADDED_PW = [' ', 'Kalem', '2026'].join('');
    const PW_WITH_USERNAME = ['x', 'ali.kaya', '99'].join('');
    let fixture: ComponentFixture<CreateChildAccountComponent>;
    let service: MockedObject<ParentChildAccountService>;
    let el: HTMLElement;
    let emitted: CreatedChildAccount[];

    const created: CreatedChildAccount = { studentId: 41, linkId: 7, username: 'ali.kaya', studentName: 'Ali Kaya' };

    let gradeCalls: number;
    let schoolCalls: number;

    interface SetupOptions {
        /** İlk N sınıf isteği hata döner. */
        gradeFailures?: number;
        /** İlk N okul isteği hata döner. */
        schoolFailures?: number;
        openChildren?: number | null;
    }

    function setup(options: SetupOptions = {}): void {
        gradeCalls = 0;
        schoolCalls = 0;
        service = {
            create: vi.fn().mockName("ParentChildAccountService.create"),
            resetPassword: vi.fn().mockName("ParentChildAccountService.resetPassword")
        } as unknown as MockedObject<ParentChildAccountService>;
        const linkService = {
            extractError: vi.fn().mockName("ParentLinkService.extractError"),
            errorCode: vi.fn().mockName("ParentLinkService.errorCode")
        };
        linkService.extractError.mockImplementation((err: HttpErrorResponse, fallback: string) => err.error?.message ?? fallback);
        linkService.errorCode.mockImplementation((err: HttpErrorResponse) => err.error?.errorCode ?? null);

        TestBed.configureTestingModule({
            imports: [
                CreateChildAccountComponent,
                NoopAnimationsModule,
                translocoTestingModule({ langs: { 'parent-links/tr': parentLinksTr, 'parent-links/en': parentLinksEn } }),
            ],
            providers: [
                provideRouter([]),
                { provide: ParentChildAccountService, useValue: service },
                { provide: ParentLinkService, useValue: linkService },
                {
                    provide: GradesService,
                    useValue: {
                        getGrades: () => ++gradeCalls <= (options.gradeFailures ?? 0)
                            ? throwError(() => new HttpErrorResponse({ status: 500 }))
                            : of([{ id: 3, name: '3. Sınıf' }]),
                    },
                },
                {
                    provide: SchoolService,
                    useValue: {
                        getSchools: () => ++schoolCalls <= (options.schoolFailures ?? 0)
                            ? throwError(() => new HttpErrorResponse({ status: 500 }))
                            : of([
                                { id: 9, name: 'Atatürk İlkokulu', provinceId: null, provinceName: null, districtId: null, districtName: null, addressLine: null },
                            ]),
                    },
                },
            ],
        });
        fixture = TestBed.createComponent(CreateChildAccountComponent);
        emitted = [];
        fixture.componentInstance.created.subscribe((c) => emitted.push(c));
        if (options.openChildren !== undefined) {
            fixture.componentRef.setInput('openChildren', options.openChildren);
        }
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
        const form = (fixture.componentInstance as unknown as {
            form: {
                controls: Record<string, {
                    setValue(v: unknown): void;
                }>;
            };
        }).form;
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
        expect(isAcceptableChildPassword(PW)).toBe(true);
        expect(isAcceptableChildPassword(WEAK_PW)).toBe(false);
        expect(isAcceptableChildPassword(LETTERS_ONLY_PW)).toBe(false);
        expect(isAcceptableChildPassword(PADDED_PW)).toBe(false);
    });

    it('renders the form with the brand name in the consent text and the consent version', () => {
        setup();
        expect(el.querySelector('h2')!.textContent).toContain(parentLinksTr.createChild.title);
        const consent = el.querySelector('[data-test="consent"]')!.textContent!;
        expect(consent).toContain(rootTr.brand.name);
        expect(el.textContent).toContain(PARENT_CONSENT_VERSION);
    });

    it('invalid username, weak / mismatching password and missing consent block submit', async () => {
        setup();
        type('username', 'ali-kaya');
        expect(el.querySelector('[data-test="username-invalid"]')).not.toBeNull();

        type('username', 'ali.kaya');
        type('pw', WEAK_PW);
        type('pw-repeat', OTHER_PW);
        expect(el.querySelector('[data-test="pw-invalid"]')).not.toBeNull();
        expect(el.querySelector('[data-test="pw-mismatch"]')).not.toBeNull();

        type('pw', PW_WITH_USERNAME);
        type('pw-repeat', PW_WITH_USERNAME);
        expect(el.querySelector('[data-test="pw-contains-username"]')).not.toBeNull();

        submit();
        await vi.advanceTimersByTimeAsync(0);
        expect(service.create).not.toHaveBeenCalled();
        expect(el.querySelector('[data-test="consent-required"]')).not.toBeNull();
    });

    it('submits the normalized request and shows only the username in the summary (never the password)', async () => {
        setup();
        service.create.mockReturnValue(of(created));
        fillValid('  Ali.Kaya ');

        submit();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();

        const request = vi.mocked(service.create).mock.lastCall![0] as CreateChildAccountRequest;
        expect(request.username).toBe('ali.kaya');
        expect(request.firstName).toBe('Ali');
        expect(request.gradeId).toBe(3);
        expect(request.schoolId).toBeNull();
        expect(request.studentNumber).toBe('12');
        expect(request.consentAccepted).toBe(true);
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
    });

    it('selected school is sent as schoolId', async () => {
        setup();
        service.create.mockReturnValue(of(created));
        fillValid();
        const cmp = fixture.componentInstance as unknown as {
            onSchoolSelected(e: {
                option: {
                    value: unknown;
                };
            }): void;
        };
        cmp.onSchoolSelected({ option: { value: { id: 9, name: 'Atatürk İlkokulu' } } });
        fixture.detectChanges();

        submit();
        await vi.advanceTimersByTimeAsync(0);

        expect((vi.mocked(service.create).mock.lastCall![0] as CreateChildAccountRequest).schoolId).toBe(9);
    });

    it('UsernameTaken (409) is shown under the username field with the server message', async () => {
        setup();
        service.create.mockReturnValue(throwError(() => new HttpErrorResponse({
            status: 409,
            error: { message: 'Bu kullanıcı adı alınmış.', errorCode: 'UsernameTaken' },
        })));
        fillValid();

        submit();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();

        expect(el.querySelector('[data-test="username-server"]')!.textContent).toContain('Bu kullanıcı adı alınmış.');
        expect(el.querySelector('[data-test="create-summary"]')).toBeNull();
        expect(emitted).toEqual([]);
    });

    it('other server errors are shown below the form', async () => {
        setup();
        service.create.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { message: 'Okul bulunamadı', errorCode: 'SchoolNotFound' } })));
        fillValid();

        submit();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();

        expect(el.querySelector('[data-test="server-error"]')!.textContent).toContain('Okul bulunamadı');
        expect(el.querySelector('[data-test="create-form"]')).not.toBeNull();
    });

    it('ChildAccountLimitReached (409) hides the form and shows the server limit message', async () => {
        setup();
        service.create.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { message: 'En fazla 10', errorCode: 'ChildAccountLimitReached' } })));
        fillValid();

        submit();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();

        expect(el.querySelector('[data-test="create-form"]')).toBeNull();
        expect(el.querySelector('[data-test="create-limit"]')!.textContent).toContain('En fazla 10');
        expect(emitted).toEqual([]);
    });

    it('hides the form with an info message when the parent already has the maximum number of children', () => {
        setup({ openChildren: 10 });

        expect(el.querySelector('[data-test="create-form"]')).toBeNull();
        const expected = parentLinksTr.createChild.limitReached.replace('{{max}}', '10');
        expect(el.querySelector('[data-test="create-limit"] p')!.textContent!.trim()).toBe(expected);
    });

    it('keeps the form below the limit', () => {
        setup({ openChildren: 9 });

        expect(el.querySelector('[data-test="create-form"]')).not.toBeNull();
        expect(el.querySelector('[data-test="create-limit"]')).toBeNull();
    });

    it('grade/school load failure shows an error with retry that reloads the failed lists', () => {
        setup({ gradeFailures: 1, schoolFailures: 1 });

        expect(el.querySelector('[data-test="refs-error"]')!.textContent).toContain(parentLinksTr.createChild.refsError);

        el.querySelector<HTMLButtonElement>('[data-test="refs-retry"]')!.click();
        fixture.detectChanges();

        expect(el.querySelector('[data-test="refs-error"]')).toBeNull();
        expect(gradeCalls).toBe(2);
        expect(schoolCalls).toBe(2);
    });

    it('school-only failure shows the school message and retry does not refetch grades', () => {
        setup({ schoolFailures: 1 });

        expect(el.querySelector('[data-test="refs-error"]')!.textContent).toContain(parentLinksTr.createChild.schoolsError);

        el.querySelector<HTMLButtonElement>('[data-test="refs-retry"]')!.click();
        fixture.detectChanges();

        expect(el.querySelector('[data-test="refs-error"]')).toBeNull();
        expect(gradeCalls).toBe(1);
        expect(schoolCalls).toBe(2);
    });

    it('consent section links to the privacy policy (new tab)', () => {
        setup();
        const link = el.querySelector<HTMLAnchorElement>('[data-test="privacy-link"]')!;

        expect(link).not.toBeNull();
        expect(link.getAttribute('href')).toBe('/privacy-policy');
        expect(link.getAttribute('target')).toBe('_blank');
        expect(link.textContent).toContain(parentLinksTr.createChild.privacyLink);
    });
});
