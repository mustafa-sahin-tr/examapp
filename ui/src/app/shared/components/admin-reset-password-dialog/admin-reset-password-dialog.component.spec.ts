import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Clipboard } from '@angular/cdk/clipboard';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Subject, of, throwError } from 'rxjs';

import {
  AdminResetPasswordDialogComponent,
  AdminResetPasswordDialogData,
} from './admin-reset-password-dialog.component';
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
  let adminService: jasmine.SpyObj<AdminService>;
  let dialogRef: jasmine.SpyObj<MatDialogRef<AdminResetPasswordDialogComponent, void>>;
  let clipboard: jasmine.SpyObj<Clipboard>;

  function ok(): AdminPasswordResetResponse {
    return { temporaryPassword: PW };
  }

  function configure(data: Partial<AdminResetPasswordDialogData> = {}): void {
    adminService = jasmine.createSpyObj<AdminService>('AdminService', ['resetPassword']);
    adminService.resetPassword.and.returnValue(of(ok()));
    dialogRef = jasmine.createSpyObj<MatDialogRef<AdminResetPasswordDialogComponent, void>>('MatDialogRef', ['close']);
    clipboard = jasmine.createSpyObj<Clipboard>('Clipboard', ['copy']);
    clipboard.copy.and.returnValue(true);

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
    adminService.resetPassword.and.returnValue(throwError(() => err));
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
    expect(dialogRef.close).toHaveBeenCalledOnceWith();
  });

  // ── Başarı ──────────────────────────────────────────────────────────────

  it('confirm_Success_ShowsPasswordMonospaceWithCopyAndOnceWarning', () => {
    configure({ target: 'student', id: 7 });

    click('confirm');

    expect(adminService.resetPassword).toHaveBeenCalledOnceWith('student', 7);
    expect(shownPassword()).toBe(PW);
    expect(el().querySelector('[data-testid="temporary-password"]')?.tagName).toBe('CODE');
    expect(button('copy-password')).not.toBeNull();
    expect(el().textContent).toContain(adminTr.passwordReset.onceWarning);
    expect(button('confirm')).toBeNull();
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('copy_UsesClipboardAndShowsFeedback', () => {
    configure();
    click('confirm');

    click('copy-password');

    expect(clipboard.copy).toHaveBeenCalledOnceWith(PW);
    expect(button('copy-password')?.textContent).toContain(adminTr.passwordReset.copied);
  });

  it('copy_Failure_ShowsManualCopyHint', () => {
    configure();
    clipboard.copy.and.returnValue(false);
    click('confirm');

    click('copy-password');

    expect(el().textContent).toContain(adminTr.passwordReset.copyFailed);
  });

  // ── Yükleniyor / çift tıklama ───────────────────────────────────────────

  it('confirm_WhileSubmitting_DisablesButtonsAndIgnoresSecondTrigger', () => {
    configure();
    const pending = new Subject<AdminPasswordResetResponse>();
    adminService.resetPassword.and.returnValue(pending.asObservable());

    click('confirm');
    component.confirm(); // disabled butonu atlayan ikinci tetikleme
    component.cancel(); // istek sürerken kapanmaz

    expect(adminService.resetPassword).toHaveBeenCalledTimes(1);
    expect(button('confirm')?.disabled).toBeTrue();
    expect(button('cancel')?.disabled).toBeTrue();
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
    expect(failWith(httpError(403, { message: 'Kendi hesabınızın şifresini sıfırlayamazsınız.' }))).toBe(
      'Kendi hesabınızın şifresini sıfırlayamazsınız.',
    );
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
    expect(failWith(httpError(429, 'Too many requests', new HttpHeaders({ 'Retry-After': '42' })))).toBe(
      errors.rateLimitedSeconds.replace('{{seconds}}', '42'),
    );
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
    expect(button('confirm')?.disabled).toBeFalse();
    expect(button('confirm')?.textContent).toContain(adminTr.passwordReset.retry);

    adminService.resetPassword.and.returnValue(of(ok()));
    click('confirm');
    expect(adminService.resetPassword).toHaveBeenCalledTimes(2);
    expect(el().querySelector('[data-testid="reset-error"]')).toBeNull();
    expect(shownPassword()).toBe(PW);
  });

  it('emptyPasswordInResponse_TreatedAsError', () => {
    configure();
    adminService.resetPassword.and.returnValue(of({ temporaryPassword: '' }));

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
    expect(dialogRef.close).toHaveBeenCalledOnceWith();
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
    const logSpies = (['log', 'info', 'warn', 'error', 'debug'] as const).map((m) => spyOn(console, m));
    const localSet = spyOn(localStorage, 'setItem').and.callThrough();
    const sessionSet = spyOn(sessionStorage, 'setItem').and.callThrough();

    click('confirm');
    click('copy-password');
    click('close');

    const written = [
      ...localSet.calls.allArgs(),
      ...sessionSet.calls.allArgs(),
      ...logSpies.flatMap((s) => s.calls.allArgs()),
    ];
    expect(JSON.stringify(written)).not.toContain(PW);
    expect(JSON.stringify({ ...localStorage })).not.toContain(PW);
    expect(JSON.stringify({ ...sessionStorage })).not.toContain(PW);
    expect(JSON.stringify(history.state ?? null)).not.toContain(PW);
  });
});
