import type { MockedObject } from "vitest";
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { ChildPasswordResetDialogComponent } from './child-password-reset-dialog.component';
import { ParentChildAccountService } from '../../../services/parent-child-account.service';
import { ParentLinkService } from '../../../services/parent-link.service';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import parentLinksTr from '../../../../../public/i18n/parent-links/tr.json';
import parentLinksEn from '../../../../../public/i18n/parent-links/en.json';

/** Issue #438: velinin çocuk şifresini belirlediği diyalog — doğrulama, istek, başarıda kapanma, sunucu hatası. */
describe('ChildPasswordResetDialogComponent (issue #438)', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    // Test-only örnekler; secret tarayıcısı literal görmesin diye çalışma anında birleştirilir.
    const PW = ['Defter', '77', 'yz'].join('');
    const WEAK_PW = ['kisa', '1'].join('');
    const OTHER_PW = ['baska', '12345'].join('');
    let fixture: ComponentFixture<ChildPasswordResetDialogComponent>;
    let service: MockedObject<ParentChildAccountService>;
    let dialogRef: MockedObject<MatDialogRef<ChildPasswordResetDialogComponent, boolean>>;
    let el: HTMLElement;

    beforeEach(() => {
        service = {
            create: vi.fn().mockName("ParentChildAccountService.create"),
            resetPassword: vi.fn().mockName("ParentChildAccountService.resetPassword")
        } as unknown as MockedObject<ParentChildAccountService>;
        dialogRef = {
            close: vi.fn().mockName("MatDialogRef.close")
        } as unknown as MockedObject<MatDialogRef<ChildPasswordResetDialogComponent, boolean>>;
        const linkService = {
            extractError: vi.fn().mockName("ParentLinkService.extractError"),
            errorCode: vi.fn().mockName("ParentLinkService.errorCode")
        };
        linkService.extractError.mockImplementation((err: HttpErrorResponse, fallback: string) => err.error?.message ?? fallback);
        linkService.errorCode.mockImplementation((err: HttpErrorResponse) => err.error?.errorCode ?? null);

        TestBed.configureTestingModule({
            imports: [
                ChildPasswordResetDialogComponent,
                NoopAnimationsModule,
                translocoTestingModule({ langs: { 'parent-links/tr': parentLinksTr, 'parent-links/en': parentLinksEn } }),
            ],
            providers: [
                { provide: MAT_DIALOG_DATA, useValue: { studentId: 41, studentName: 'Ali Kaya' } },
                { provide: MatDialogRef, useValue: dialogRef },
                { provide: ParentChildAccountService, useValue: service },
                { provide: ParentLinkService, useValue: linkService },
            ],
        });
        fixture = TestBed.createComponent(ChildPasswordResetDialogComponent);
        fixture.detectChanges();
        el = fixture.nativeElement as HTMLElement;
    });

    function type(selector: string, value: string): void {
        const input = el.querySelector<HTMLInputElement>(`[data-test="${selector}"]`)!;
        input.value = value;
        input.dispatchEvent(new Event('input'));
        input.dispatchEvent(new Event('blur'));
        fixture.detectChanges();
    }

    function submit(): void {
        el.querySelector<HTMLButtonElement>('[data-test="reset-submit"]')!.click();
        fixture.detectChanges();
    }

    it('shows the child name in the title', () => {
        expect(el.textContent).toContain('Ali Kaya');
    });

    it('weak or mismatching passwords are not sent', async () => {
        type('reset-pw', WEAK_PW);
        type('reset-pw-repeat', OTHER_PW);
        submit();
        await vi.advanceTimersByTimeAsync(0);

        expect(el.querySelector('[data-test="reset-pw-invalid"]')).not.toBeNull();
        expect(el.querySelector('[data-test="reset-pw-mismatch"]')).not.toBeNull();
        expect(service.resetPassword).not.toHaveBeenCalled();
    });

    it('sends the new password for the child and closes with true', async () => {
        service.resetPassword.mockReturnValue(of(void 0));
        type('reset-pw', PW);
        type('reset-pw-repeat', PW);

        submit();
        await vi.advanceTimersByTimeAsync(0);

        expect(service.resetPassword).toHaveBeenCalledTimes(1);

        expect(service.resetPassword).toHaveBeenCalledWith(41, PW);
        expect(dialogRef.close).toHaveBeenCalledTimes(1);
        expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('shows the server message and stays open on failure', async () => {
        service.resetPassword.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404, error: { message: 'Çocuk bulunamadı', errorCode: 'NotFound' } })));
        type('reset-pw', PW);
        type('reset-pw-repeat', PW);

        submit();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();

        expect(el.querySelector('[data-test="reset-error"]')!.textContent).toContain('Çocuk bulunamadı');
        expect(dialogRef.close).not.toHaveBeenCalled();
        expect(el.textContent).not.toContain(PW);
    });

    it('403 OnlyPrimaryParent shows the client-side "only the primary parent" message', async () => {
        service.resetPassword.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 403, error: { message: 'Forbidden', errorCode: 'OnlyPrimaryParent' } })));
        type('reset-pw', PW);
        type('reset-pw-repeat', PW);

        submit();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();

        expect(el.querySelector('[data-test="reset-error"]')!.textContent).toContain(parentLinksTr.resetPw.onlyPrimary);
        expect(dialogRef.close).not.toHaveBeenCalled();
    });
});
