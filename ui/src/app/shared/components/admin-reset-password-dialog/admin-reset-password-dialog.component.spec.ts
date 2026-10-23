import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Clipboard } from '@angular/cdk/clipboard';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Subject, of, throwError } from 'rxjs';

import { AdminResetPasswordDialogComponent, AdminResetPasswordDialogData, } from './admin-reset-password-dialog.component';
import { AdminService } from '../../../services/admin.service';
import { AdminPasswordResetResponse } from '../../../models/admin-password-reset.model';
import { translocoTestingModule } from '../../testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

describe('AdminResetPasswordDialogComponent (issue #156)', () => {
    /** Sahte geçici değer (16 karakter, backend biçimine benzer). */
    const PW = 'Xy7#kP2m-example';
    const errors = adminTr.passwordReset.errors;

    let fixture: ComponentFixture<AdminResetPasswordDialogComponent>;
    let component: AdminResetPasswordDialogComponent;
    let adminService: MockedObject<AdminService>;
    let dialogRef: MockedObject<MatDialogRef<AdminResetPasswordDialogComponent, void>>;
    let clipboard: MockedObject<Clipboard>;

    function ok(): AdminPasswordResetResponse {
        return { temporaryPassword: PW };
    }

    function configure(data: Partial<AdminResetPasswordDialogData> = {}): void {
        adminService = {
            resetPassword: vi.fn().mockName("AdminService.resetPassword")
        } as unknown as MockedObject<AdminService>;
        adminService.resetPassword.mockReturnValue(of(ok()));
        dialogRef = {
            close: vi.fn().mockName("MatDialogRef.close")
        } as unknown as MockedObject<MatDialogRef<AdminResetPasswordDialogComponent, void>>;
        clipboard = {
            copy: vi.fn().mockName("Clipboard.copy")
        } as unknown as MockedObject<Clipboard>;
        clipboard.copy.mockReturnValue(true);

        const dialogData: AdminResetPasswordDialogData = { target: 'teacher', id: 12, displayName: 'Ayşe Yılmaz', ...data };

        TestBed.configureTestingModule({
            imports: [AdminResetPasswordDialogComponent, translocoTestingModule({ langs: { 'admin/tr': adminTr } })],
            providers: [
                provideNoopAnimations(),
                { provide: AdminService, useValue: adminService },
                { provide: MatDialogRef, useValue: dialogRef },
                { provide: Clipboard, useValue: clipboard },
                { provide: MAT_DIALOG_DATA, useValue: dialogData },
            ],
        });
        fixture = TestBed.createComponent(AdminResetPasswordDialogComponent);
        component = fixture.componentInstance;
        fixture.detectChanges();
    }

    function el(): HTMLElement {
        return fixture.nativeElement as HTMLElement;
    }

    function button(testId: string): HTMLButtonElement | null {
        return el().querySelector(`button[data-testid="${testId}"]`);
    }

    function click(testId: string): void {
        button(testId)!.click();
        fixture.detectChanges();
    }

    function shownPassword(): string | null {
        return el().querySelector('[data-testid="temporary-password"]')?.textContent?.trim() ?? null;
    }

    function httpError(status: number, error: unknown = null, headers?: HttpHeaders): HttpErrorResponse {
        return new HttpErrorResponse({ status, error, headers });
    }

    function failWith(err: HttpErrorResponse): string {
        adminService.resetPassword.mockReturnValue(throwError(() => err));
        click('confirm');
        return el().querySelector('[data-testid="reset-error"] span')?.textContent?.trim() ?? '';
    }

    // ── Onay ────────────────────────────────────────────────────────────────

    it('confirmStep_ShowsTargetSessionsAndTemporaryPasswordInfo_WithoutRequest', () => {
        configure();

        const content = el().textContent ?? '';
        expect(content).toContain(adminTr.passwordReset.confirmTitle);
        expect(content).toContain('Ayşe Yılmaz');
        expect(content).toContain(adminTr.passwordReset.confirmSessions);
        expect(content).toContain(adminTr.passwordReset.confirmTemporary);
        expect(adminService.resetPassword).not.toHaveBeenCalled();
    });

    it('cancel_ClosesWithoutRequest', () => {
        configure();

        click('cancel');

        expect(adminService.resetPassword).not.toHaveBeenCalled();
        expect(dialogRef.close).toHaveBeenCalledTimes(1);
        expect(dialogRef.close).toHaveBeenCalledWith();
    });

    // ── Başarı ──────────────────────────────────────────────────────────────

    it('confirm_Success_ShowsPasswordMonospaceWithCopyAndOnceWarning', () => {
        configure({ target: 'student', id: 7 });

        click('confirm');

        expect(adminService.resetPassword).toHaveBeenCalledTimes(1);

        expect(adminService.resetPassword).toHaveBeenCalledWith('student', 7);
        expect(shownPassword()).toBe(PW);
        expect(el().querySelector('[data-testid="temporary-password"]')?.tagName).toBe('CODE');
        expect(button('copy-password')).not.toBeNull();
        expect(el().textContent).toContain(adminTr.passwordReset.onceWarning);
        expect(button('confirm')).toBeNull();
        expect(dialogRef.close).not.toHaveBeenCalled();
        // Geçici şifre (permanent yok/false): veli notu yok, geçici başlık/etiket.
        expect(el().querySelector('[data-testid="permanent-note"]')).toBeNull();
        expect(el().textContent).toContain(adminTr.passwordReset.resultTitle);
        expect(el().textContent).toContain(adminTr.passwordReset.passwordLabel);
    });

    it('confirm_PermanentFalse_KeepsTemporaryTexts (issue #438)', () => {
        configure({ target: 'student', id: 7 });
        adminService.resetPassword.mockReturnValue(of({ ...ok(), permanent: false }));

        click('confirm');

        expect(el().querySelector('[data-testid="permanent-note"]')).toBeNull();
        expect(el().textContent).toContain(adminTr.passwordReset.onceWarning);
    });

    it('confirm_PermanentTrue_ShowsParentManagedNoteInsteadOfTemporaryTexts (issue #438)', () => {
        configure({ target: 'student', id: 7 });
        adminService.resetPassword.mockReturnValue(of({ ...ok(), permanent: true }));

        click('confirm');

        expect(shownPassword()).toBe(PW);
        const content = el().textContent ?? '';
        expect(el().querySelector('[data-testid="permanent-note"]')?.textContent).toContain(adminTr.passwordReset.permanentNote);
        expect(content).toContain(adminTr.passwordReset.resultTitlePermanent);
        expect(content).toContain(adminTr.passwordReset.passwordLabelPermanent);
        expect(content).toContain(adminTr.passwordReset.onceWarningPermanent);
        expect(content).not.toContain(adminTr.passwordReset.resultTitle);
        expect(content).not.toContain(adminTr.passwordReset.onceWarning);
    });

    it('close_AfterPermanentResult_ClearsPermanentFlag (issue #438)', () => {
        configure({ target: 'student', id: 7 });
        adminService.resetPassword.mockReturnValue(of({ ...ok(), permanent: true }));
        click('confirm');

        click('close');

        expect(component.permanent()).toBe(false);
        expect(component.password()).toBeNull();
    });

    it('copy_UsesClipboardAndShowsFeedback', () => {
        configure();
        click('confirm');

        click('copy-password');

        expect(clipboard.copy).toHaveBeenCalledTimes(1);

        expect(clipboard.copy).toHaveBeenCalledWith(PW);
        expect(button('copy-password')?.textContent).toContain(adminTr.passwordReset.copied);
    });

    it('copy_Failure_ShowsManualCopyHint', () => {
        configure();
        clipboard.copy.mockReturnValue(false);
        click('confirm');

        click('copy-password');

        expect(el().textContent).toContain(adminTr.passwordReset.copyFailed);
    });

    // ── Yükleniyor / çift tıklama ───────────────────────────────────────────

    it('confirm_WhileSubmitting_DisablesButtonsAndIgnoresSecondTrigger', () => {
        configure();
        const pending = new Subject<AdminPasswordResetResponse>();
        adminService.resetPassword.mockReturnValue(pending.asObservable());

        click('confirm');
        component.confirm(); // disabled butonu atlayan ikinci tetikleme
        component.cancel(); // istek sürerken kapanmaz

        expect(adminService.resetPassword).toHaveBeenCalledTimes(1);
        expect(button('confirm')?.disabled).toBe(true);
        expect(button('cancel')?.disabled).toBe(true);
        expect(el().textContent).toContain(adminTr.passwordReset.submitting);
        expect(dialogRef.close).not.toHaveBeenCalled();

        pending.next(ok());
        pending.complete();
        fixture.detectChanges();
        expect(shownPassword()).toBe(PW);
    });

    // ── Hatalar ─────────────────────────────────────────────────────────────

    it('error403_WithMessage_ShowsBackendMessage', () => {
        configure();
        expect(failWith(httpError(403, { message: 'Kendi hesabınızın şifresini sıfırlayamazsınız.' }))).toBe('Kendi hesabınızın şifresini sıfırlayamazsınız.');
    });

    it('error403_WithoutBody_ShowsForbiddenText', () => {
        configure();
        expect(failWith(httpError(403))).toBe(errors.forbidden);
    });

    it('error404_ShowsBackendMessageOrNotFoundText', () => {
        configure();
        expect(failWith(httpError(404, { message: 'Öğretmen bulunamadı.' }))).toBe('Öğretmen bulunamadı.');
        expect(failWith(httpError(404))).toBe(errors.notFound);
    });

    it('error502_ShowsBackendMessageOrUpstreamText', () => {
        configure();
        expect(failWith(httpError(502, { message: 'Kimlik servisi hata verdi.' }))).toBe('Kimlik servisi hata verdi.');
        // Şifre değişti ama oturumlar kapatılamadı (SessionRevokeFailed) — ayrı backend mesajı aynen gösterilir.
        expect(failWith(httpError(502, { message: 'Şifre değiştirildi ancak kullanıcının açık oturumları kapatılamadı.' })))
            .toBe('Şifre değiştirildi ancak kullanıcının açık oturumları kapatılamadı.');
        expect(failWith(httpError(502))).toBe(errors.upstream);
    });

    it('error429_PlainTextBody_ShowsFriendlyTextWithRetryAfter', () => {
        configure();
        expect(failWith(httpError(429, 'Too many requests', new HttpHeaders({ 'Retry-After': '42' })))).toBe(errors.rateLimitedSeconds.replace('{{seconds}}', '42'));
        expect(failWith(httpError(429, 'Too many requests'))).toBe(errors.rateLimited);
    });

    it('error500_ShowsGenericTextAndIgnoresBody', () => {
        configure();
        expect(failWith(httpError(500, { message: 'internal detail' }))).toBe(errors.generic);
    });

    it('error_KeepsDialogOpenAndAllowsRetry', () => {
        configure();
        failWith(httpError(502));

        expect(dialogRef.close).not.toHaveBeenCalled();
        expect(button('confirm')?.disabled).toBe(false);
        expect(button('confirm')?.textContent).toContain(adminTr.passwordReset.retry);

        adminService.resetPassword.mockReturnValue(of(ok()));
        click('confirm');
        expect(adminService.resetPassword).toHaveBeenCalledTimes(2);
        expect(el().querySelector('[data-testid="reset-error"]')).toBeNull();
        expect(shownPassword()).toBe(PW);
    });

    it('emptyPasswordInResponse_TreatedAsError', () => {
        configure();
        adminService.resetPassword.mockReturnValue(of({ temporaryPassword: '' }));

        click('confirm');

        expect(el().querySelector('[data-testid="reset-error"] span')?.textContent?.trim()).toBe(errors.generic);
        expect(shownPassword()).toBeNull();
    });

    // ── Şifre saklanmıyor ───────────────────────────────────────────────────

    it('close_ClearsPasswordStateAndReturnsNoResult', () => {
        configure();
        click('confirm');
        expect(component.password()).toBe(PW);

        click('close');

        expect(component.password()).toBeNull();
        expect(dialogRef.close).toHaveBeenCalledTimes(1);
        expect(dialogRef.close).toHaveBeenCalledWith();
        expect(el().textContent).not.toContain(PW);
    });

    it('destroy_ClearsPassword', () => {
        configure();
        click('confirm');

        fixture.destroy();

        expect(component.password()).toBeNull();
    });

    it('success_DoesNotWritePasswordToStorageConsoleOrHistory', () => {
        configure();
        const logSpies = (['log', 'info', 'warn', 'error', 'debug'] as const).map((m) => vi.spyOn(console, m).mockReturnValue(undefined));
        const localSet = vi.spyOn(localStorage, 'setItem');
        const sessionSet = vi.spyOn(sessionStorage, 'setItem');

        click('confirm');
        click('copy-password');
        click('close');

        const written = [
            ...vi.mocked(localSet).mock.calls,
            ...vi.mocked(sessionSet).mock.calls,
            ...logSpies.flatMap((s) => vi.mocked(s).mock.calls),
        ];
        expect(JSON.stringify(written)).not.toContain(PW);
        expect(JSON.stringify({ ...localStorage })).not.toContain(PW);
        expect(JSON.stringify({ ...sessionStorage })).not.toContain(PW);
        expect(JSON.stringify(history.state ?? null)).not.toContain(PW);
    });
});
