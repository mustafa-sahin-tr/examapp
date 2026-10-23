import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Subject, of, throwError } from 'rxjs';
import { AdminTeacherSuspensionDialogComponent, AdminTeacherSuspensionDialogData, AdminTeacherSuspensionDialogResult, } from './admin-teacher-suspension-dialog.component';
import { AdminService } from '../../../services/admin.service';
import { AdminTeacherSuspensionResponse } from '../../../models/admin-teacher-suspension.model';
import { translocoTestingModule } from '../../testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

describe('AdminTeacherSuspensionDialogComponent (issue #289)', () => {
    const texts = adminTr.teacherSuspension;
    const REASON = 'Şikâyet inceleniyor';
    const SUSPENDED: AdminTeacherSuspensionResponse = {
        teacherId: 12,
        accountApproved: false,
        accountSuspended: true,
        accountApprovedAt: null,
        accountSuspendedAt: '2026-09-29T19:03:20Z',
    };
    const RESTORED: AdminTeacherSuspensionResponse = {
        teacherId: 12,
        accountApproved: true,
        accountSuspended: false,
        accountApprovedAt: '2026-09-30T08:00:00Z',
        accountSuspendedAt: null,
    };

    let fixture: ComponentFixture<AdminTeacherSuspensionDialogComponent>;
    let component: AdminTeacherSuspensionDialogComponent;
    let adminService: MockedObject<AdminService>;
    let dialogRef: MockedObject<MatDialogRef<AdminTeacherSuspensionDialogComponent, AdminTeacherSuspensionDialogResult | undefined>>;

    function configure(data: Partial<AdminTeacherSuspensionDialogData> = {}): void {
        adminService = {
            suspendTeacher: vi.fn().mockName("AdminService.suspendTeacher"),
            unsuspendTeacher: vi.fn().mockName("AdminService.unsuspendTeacher")
        } as unknown as MockedObject<AdminService>;
        adminService.suspendTeacher.mockReturnValue(of(SUSPENDED));
        adminService.unsuspendTeacher.mockReturnValue(of(RESTORED));
        dialogRef = {
            close: vi.fn().mockName("MatDialogRef.close")
        } as unknown as MockedObject<MatDialogRef<AdminTeacherSuspensionDialogComponent, AdminTeacherSuspensionDialogResult | undefined>>;
        const dialogData: AdminTeacherSuspensionDialogData = {
            teacherId: 12,
            displayName: 'Ayşe Yılmaz',
            mode: 'suspend',
            ...data,
        };

        TestBed.configureTestingModule({
            imports: [AdminTeacherSuspensionDialogComponent, translocoTestingModule({ langs: { 'admin/tr': adminTr } })],
            providers: [
                provideNoopAnimations(),
                { provide: AdminService, useValue: adminService },
                { provide: MatDialogRef, useValue: dialogRef },
                { provide: MAT_DIALOG_DATA, useValue: dialogData },
            ],
        });
        fixture = TestBed.createComponent(AdminTeacherSuspensionDialogComponent);
        component = fixture.componentInstance;
        fixture.detectChanges();
    }

    function el(): HTMLElement {
        return fixture.nativeElement as HTMLElement;
    }

    function button(testId: string): HTMLButtonElement {
        return el().querySelector(`button[data-testid="${testId}"]`)!;
    }

    function click(testId: string): void {
        button(testId).click();
        fixture.detectChanges();
    }

    function typeReason(value: string): void {
        const textarea = el().querySelector('textarea[data-testid="reason"]') as HTMLTextAreaElement;
        textarea.value = value;
        textarea.dispatchEvent(new Event('input'));
        fixture.detectChanges();
    }

    function errorText(): string {
        return el().querySelector('[data-testid="suspension-error"]')?.textContent ?? '';
    }

    function httpError(status: number, error: unknown = null, headers?: HttpHeaders): HttpErrorResponse {
        return new HttpErrorResponse({ status, error, headers });
    }

    // ── Askıya al ────────────────────────────────────────────────────────────

    it('suspend_ConfirmStep_ShowsReasonFieldAndSendsNoRequest', () => {
        configure();

        expect(el().textContent).toContain(texts.suspend.confirmTitle);
        expect(el().textContent).toContain('Ayşe Yılmaz');
        expect(el().querySelector('[data-testid="login-note"]')?.textContent).toContain(texts.suspend.confirmLogin);
        expect(el().querySelector('textarea')?.getAttribute('maxlength')).toBe('500');
        expect(el().querySelector('[data-testid="reason-counter"]')?.textContent?.trim()).toBe('0 / 500');
        expect(button('confirm').textContent).toContain(texts.suspend.confirm);
        expect(adminService.suspendTeacher).not.toHaveBeenCalled();
    });

    it('suspend_EmptyOrBlankReason_ConfirmDisabledAndNoRequest', () => {
        configure();
        expect(button('confirm').disabled).toBe(true);

        typeReason('    ');
        expect(button('confirm').disabled).toBe(true);
        component.confirm(); // buton atlanıp çağrılsa da istek atılmaz
        fixture.detectChanges();

        expect(adminService.suspendTeacher).not.toHaveBeenCalled();
        expect(el().textContent).toContain(texts.suspend.required);

        typeReason(REASON);
        expect(button('confirm').disabled).toBe(false);
    });

    it('suspend_Success_SendsTrimmedReasonAndClosesWithResponseAndReason', () => {
        configure();
        typeReason(`  ${REASON}  `);
        click('confirm');

        expect(adminService.suspendTeacher).toHaveBeenCalledTimes(1);

        expect(adminService.suspendTeacher).toHaveBeenCalledWith(12, REASON);
        expect(adminService.unsuspendTeacher).not.toHaveBeenCalled();
        expect(dialogRef.close).toHaveBeenCalledTimes(1);
        expect(dialogRef.close).toHaveBeenCalledWith({ response: SUSPENDED, reason: REASON });
    });

    it('suspend_WhileSubmitting_IgnoresSecondClickAndDisablesButtons', () => {
        configure();
        const pending = new Subject<AdminTeacherSuspensionResponse>();
        adminService.suspendTeacher.mockReturnValue(pending.asObservable());
        typeReason(REASON);

        click('confirm');
        component.confirm();
        component.cancel();

        expect(adminService.suspendTeacher).toHaveBeenCalledTimes(1);
        expect(button('confirm').disabled).toBe(true);
        expect(button('cancel').disabled).toBe(true);
        expect(button('confirm').textContent).toContain(texts.submitting);
        expect(dialogRef.close).not.toHaveBeenCalled();

        pending.next(SUSPENDED);
        expect(dialogRef.close).toHaveBeenCalledTimes(1);
        expect(dialogRef.close).toHaveBeenCalledWith({ response: SUSPENDED, reason: REASON });
    });

    it('suspend_400_ShowsBackendMessageAndRetryText', () => {
        configure();
        const message = 'Askıya alma nedeni zorunludur.';
        adminService.suspendTeacher.mockReturnValue(throwError(() => httpError(400, { message, errorCode: 'SuspensionReasonRequired' })));
        typeReason(REASON);
        click('confirm');

        expect(errorText()).toContain(message);
        expect(button('confirm').textContent).toContain(texts.retry);
        expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('suspend_409_ShowsBackendMessage_RetrySucceeds', () => {
        configure();
        const message = 'Öğretmen hesabı zaten askıda.';
        adminService.suspendTeacher.mockReturnValue(throwError(() => httpError(409, { message, errorCode: 'TeacherAlreadySuspended' })));
        typeReason(REASON);
        click('confirm');

        expect(errorText()).toContain(message);
        expect(button('confirm').textContent).toContain(texts.retry);

        adminService.suspendTeacher.mockReturnValue(of(SUSPENDED));
        click('confirm');
        expect(dialogRef.close).toHaveBeenCalledTimes(1);
        expect(dialogRef.close).toHaveBeenCalledWith({ response: SUSPENDED, reason: REASON });
    });

    it('suspend_409WithoutBody_FallsBackToGenericText', () => {
        configure();
        adminService.suspendTeacher.mockReturnValue(throwError(() => httpError(409)));
        typeReason(REASON);
        click('confirm');

        expect(errorText()).toContain(texts.errors.generic);
    });

    it('suspend_ErrorThenCancel_ClosesWithRefresh', () => {
        configure();
        adminService.suspendTeacher.mockReturnValue(throwError(() => httpError(409, { message: 'Eşzamanlı değişiklik.', errorCode: 'ConcurrentChange' })));
        typeReason(REASON);
        click('confirm');
        click('cancel');

        expect(dialogRef.close).toHaveBeenCalledTimes(1);

        expect(dialogRef.close).toHaveBeenCalledWith({ refresh: true });
    });

    it('suspend_RateLimited_ShowsRetryAfterText', () => {
        configure();
        adminService.suspendTeacher.mockReturnValue(throwError(() => httpError(429, 'plain', new HttpHeaders({ 'Retry-After': '42' }))));
        typeReason(REASON);
        click('confirm');

        expect(errorText()).toContain(texts.errors.rateLimitedSeconds.replace('{{seconds}}', '42'));
    });

    it('cancel_WithoutError_ClosesWithoutResult', () => {
        configure();
        click('cancel');

        expect(dialogRef.close).toHaveBeenCalledTimes(1);

        expect(dialogRef.close).toHaveBeenCalledWith();
        expect(adminService.suspendTeacher).not.toHaveBeenCalled();
    });

    // ── Askıyı kaldır ────────────────────────────────────────────────────────

    it('unsuspend_ConfirmStep_HasNoReasonFieldAndConfirmEnabled', () => {
        configure({ mode: 'unsuspend' });

        expect(el().textContent).toContain(texts.unsuspend.confirmTitle);
        expect(el().querySelector('textarea')).toBeNull();
        expect(el().querySelector('[data-testid="login-note"]')).toBeNull();
        expect(button('confirm').disabled).toBe(false);
        expect(button('confirm').textContent).toContain(texts.unsuspend.confirm);
    });

    it('unsuspend_Success_ClosesWithResponseAndNullReason', () => {
        configure({ mode: 'unsuspend' });
        click('confirm');

        expect(adminService.unsuspendTeacher).toHaveBeenCalledTimes(1);

        expect(adminService.unsuspendTeacher).toHaveBeenCalledWith(12);
        expect(adminService.suspendTeacher).not.toHaveBeenCalled();
        expect(dialogRef.close).toHaveBeenCalledTimes(1);
        expect(dialogRef.close).toHaveBeenCalledWith({ response: RESTORED, reason: null });
    });

    it('unsuspend_DoubleClick_SendsSingleRequest', () => {
        configure({ mode: 'unsuspend' });
        adminService.unsuspendTeacher.mockReturnValue(new Subject<AdminTeacherSuspensionResponse>().asObservable());

        click('confirm');
        component.confirm();

        expect(adminService.unsuspendTeacher).toHaveBeenCalledTimes(1);
    });

    it('unsuspend_409_ShowsBackendMessage_ThenCancelClosesWithRefresh', () => {
        configure({ mode: 'unsuspend' });
        const message = 'Öğretmen hesabı askıda değil.';
        adminService.unsuspendTeacher.mockReturnValue(throwError(() => httpError(409, { message, errorCode: 'TeacherNotSuspended' })));
        click('confirm');

        expect(errorText()).toContain(message);
        expect(button('confirm').textContent).toContain(texts.retry);
        click('cancel');
        expect(dialogRef.close).toHaveBeenCalledTimes(1);
        expect(dialogRef.close).toHaveBeenCalledWith({ refresh: true });
    });
});
