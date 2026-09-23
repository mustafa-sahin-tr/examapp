import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Subject, of, throwError } from 'rxjs';

import {
  AdminAccountStatusDialogComponent,
  AdminAccountStatusDialogData,
  AdminAccountStatusDialogResult,
} from './admin-account-status-dialog.component';
import { AdminService } from '../../../services/admin.service';
import { AdminAccountStatusResponse } from '../../../models/admin-account-status.model';
import { translocoTestingModule } from '../../testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

describe('AdminAccountStatusDialogComponent (issue #155)', () => {
  const texts = adminTr.accountStatus;

  let fixture: ComponentFixture<AdminAccountStatusDialogComponent>;
  let component: AdminAccountStatusDialogComponent;
  let adminService: jasmine.SpyObj<AdminService>;
  let dialogRef: jasmine.SpyObj<MatDialogRef<AdminAccountStatusDialogComponent, AdminAccountStatusDialogResult | undefined>>;

  function configure(data: Partial<AdminAccountStatusDialogData> = {}): void {
    adminService = jasmine.createSpyObj<AdminService>('AdminService', ['setAccountStatus']);
    adminService.setAccountStatus.and.returnValue(of({ enabled: data.enable ?? false }));
    dialogRef = jasmine.createSpyObj<MatDialogRef<AdminAccountStatusDialogComponent, AdminAccountStatusDialogResult | undefined>>(
      'MatDialogRef',
      ['close'],
    );

    const dialogData: AdminAccountStatusDialogData = {
      target: 'teacher',
      id: 12,
      displayName: 'Ayşe Yılmaz',
      enable: false,
      ...data,
    };

    TestBed.configureTestingModule({
      imports: [AdminAccountStatusDialogComponent, translocoTestingModule({ langs: { 'admin/tr': adminTr } })],
      providers: [
        provideNoopAnimations(),
        { provide: AdminService, useValue: adminService },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: MAT_DIALOG_DATA, useValue: dialogData },
      ],
    });
    fixture = TestBed.createComponent(AdminAccountStatusDialogComponent);
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

  function httpError(status: number, error: unknown = null, headers?: HttpHeaders): HttpErrorResponse {
    return new HttpErrorResponse({ status, error, headers });
  }

  it('disable_ConfirmStep_ShowsSessionsNoteAndSendsNoRequest', () => {
    configure();

    expect(el().textContent).toContain(texts.disable.confirmTitle);
    expect(el().textContent).toContain('Ayşe Yılmaz');
    expect(el().querySelector('[data-testid="sessions-note"]')?.textContent).toContain(texts.disable.confirmSessions);
    expect(button('confirm').textContent).toContain(texts.disable.confirm);
    expect(adminService.setAccountStatus).not.toHaveBeenCalled();
  });

  it('enable_ConfirmStep_HasNoSessionsNote', () => {
    configure({ enable: true });

    expect(el().textContent).toContain(texts.enable.confirmTitle);
    expect(el().querySelector('[data-testid="sessions-note"]')).toBeNull();
    expect(button('confirm').textContent).toContain(texts.enable.confirm);
  });

  it('confirm_Success_ClosesWithServerState', () => {
    configure({ target: 'student', id: 7 });

    click('confirm');

    expect(adminService.setAccountStatus).toHaveBeenCalledOnceWith('student', 7, false);
    expect(dialogRef.close).toHaveBeenCalledOnceWith({ enabled: false });
  });

  it('confirm_WhileSubmitting_IgnoresSecondClickAndDisablesButtons', () => {
    configure();
    const pending = new Subject<AdminAccountStatusResponse>();
    adminService.setAccountStatus.and.returnValue(pending.asObservable());

    click('confirm');
    component.confirm();
    component.cancel();

    expect(adminService.setAccountStatus).toHaveBeenCalledTimes(1);
    expect(button('confirm').disabled).toBeTrue();
    expect(button('cancel').disabled).toBeTrue();
    expect(button('confirm').textContent).toContain(texts.submitting);
    expect(dialogRef.close).not.toHaveBeenCalled();

    pending.next({ enabled: false });
    expect(dialogRef.close).toHaveBeenCalledOnceWith({ enabled: false });
  });

  it('error_ShowsBackendMessageStaysOpenAndAllowsRetry', () => {
    configure();
    const message = 'Yönetici veya servis hesaplarının durumu bu ekrandan değiştirilemez.';
    adminService.setAccountStatus.and.returnValue(throwError(() => httpError(403, { message })));

    click('confirm');

    expect(el().querySelector('[data-testid="status-error"]')?.textContent).toContain(message);
    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(button('confirm').textContent).toContain(texts.retry);

    adminService.setAccountStatus.and.returnValue(of({ enabled: false }));
    click('confirm');
    expect(dialogRef.close).toHaveBeenCalledOnceWith({ enabled: false });
  });

  it('cancel_ClosesWithoutResult', () => {
    configure();

    click('cancel');

    expect(dialogRef.close).toHaveBeenCalledOnceWith();
    expect(adminService.setAccountStatus).not.toHaveBeenCalled();
  });

  it('sessionRevokeFailed_ThenCancel_ClosesWithRefreshSoListReloads', () => {
    configure();
    const message = 'Hesap devre dışı bırakıldı ancak kullanıcının açık oturumları kapatılamadı. Lütfen işlemi tekrar deneyin.';
    adminService.setAccountStatus.and.returnValue(throwError(() => httpError(502, { message })));

    click('confirm');
    expect(el().querySelector('[data-testid="status-error"]')?.textContent).toContain(message);
    click('cancel');

    expect(dialogRef.close).toHaveBeenCalledOnceWith({ refresh: true });
  });

  it('rateLimited_ShowsRetryAfterText', () => {
    configure();
    adminService.setAccountStatus.and.returnValue(
      throwError(() => httpError(429, 'plain', new HttpHeaders({ 'Retry-After': '42' }))),
    );

    click('confirm');

    expect(el().querySelector('[data-testid="status-error"]')?.textContent).toContain(
      texts.errors.rateLimitedSeconds.replace('{{seconds}}', '42'),
    );
  });
});
