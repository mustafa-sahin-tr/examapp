// #545 — Angular 21 control-flow migrasyonu (@if conflict / hata / submitting @else) sonrası davranış sözleşmesi.
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Observable, Subject, of, throwError } from 'rxjs';

import {
  AssignmentPermissionDialogComponent,
  AssignmentPermissionDialogData,
} from './assignment-permission-dialog.component';
import { WorksheetAccessRequestService } from '../../../../services/worksheet-access-request.service';
import { ResponseBase } from '../../../../models/worksheet-access-request.model';
import { translocoTestingModule } from '../../../../shared/testing/transloco-testing';
import rootTr from '../../../../../../public/i18n/tr.json';
import worksheetDetailTr from '../../../../../../public/i18n/worksheet-detail/tr.json';

const T = worksheetDetailTr.permissionDialog;

const OK = { success: true, message: '' } as ResponseBase;

const DATA: AssignmentPermissionDialogData = {
  worksheetId: 42,
  worksheetName: 'Fen Bilimleri Sınavı',
  ownerName: 'Ali Hoca',
};

describe('AssignmentPermissionDialogComponent (#545)', () => {
  let fixture: ComponentFixture<AssignmentPermissionDialogComponent>;
  let service: jasmine.SpyObj<WorksheetAccessRequestService>;
  let dialogRef: jasmine.SpyObj<MatDialogRef<AssignmentPermissionDialogComponent, unknown>>;

  const el = () => fixture.nativeElement as HTMLElement;
  const submitButton = () =>
    Array.from(el().querySelectorAll('mat-dialog-actions button')).pop() as HTMLButtonElement;
  const cancelButton = () => el().querySelector('mat-dialog-actions button') as HTMLButtonElement;
  const alerts = () => Array.from(el().querySelectorAll('.apd-alert'));

  function setup(createResult: () => Observable<ResponseBase>, data: AssignmentPermissionDialogData = DATA): void {
    service = jasmine.createSpyObj<WorksheetAccessRequestService>('WorksheetAccessRequestService', ['createRequest']);
    service.createRequest.and.callFake(createResult);
    dialogRef = jasmine.createSpyObj('MatDialogRef', ['close']);

    TestBed.configureTestingModule({
      imports: [
        AssignmentPermissionDialogComponent,
        NoopAnimationsModule,
        translocoTestingModule({
          langs: { tr: { ...rootTr, 'worksheet-detail': worksheetDetailTr }, 'worksheet-detail/tr': worksheetDetailTr },
        }),
      ],
      providers: [
        { provide: WorksheetAccessRequestService, useValue: service },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: MAT_DIALOG_DATA, useValue: data },
      ],
    });
    fixture = TestBed.createComponent(AssignmentPermissionDialogComponent);
    fixture.detectChanges();
  }

  it('template_OwnerNameGiven_ShowsOwnerAndWorksheetInDescription', () => {
    setup(() => of(OK));

    const text = el().querySelector('.apd-desc')?.textContent ?? '';
    expect(text).toContain('Ali Hoca');
    expect(text).toContain('Fen Bilimleri Sınavı');
  });

  it('template_NoOwnerName_UsesOwnerFallback', () => {
    setup(() => of(OK), { ...DATA, ownerName: null });

    expect(el().querySelector('.apd-desc')?.textContent).toContain(T.ownerFallback);
  });

  it('template_InitialState_NoAlertsAndSubmitEnabled', () => {
    setup(() => of(OK));

    expect(alerts().length).toBe(0);
    expect(submitButton().disabled).toBeFalse();
    expect(submitButton().textContent).toContain(T.submit);
  });

  it('template_NoteTyped_ShowsLengthCounter', () => {
    setup(() => of(OK));

    fixture.componentInstance['noteControl'].setValue('abcd');
    fixture.detectChanges();

    expect(el().querySelector('mat-hint')?.textContent).toContain('4 / 500');
  });

  it('submit_SubmitInFlight_ShowsSpinnerInsteadOfLabel', () => {
    const pending$ = new Subject<ResponseBase>();
    setup(() => pending$.asObservable());

    submitButton().click();
    fixture.detectChanges();

    expect(submitButton().querySelector('mat-spinner')).not.toBeNull();
    expect(submitButton().textContent).not.toContain(T.submit);
    expect(submitButton().disabled).toBeTrue();
  });

  it('submit_Succeeds_ClosesDialogWithSubmittedTrue', () => {
    setup(() => of(OK));
    fixture.componentInstance['noteControl'].setValue('  Lütfen  ');

    submitButton().click();

    expect(service.createRequest).toHaveBeenCalledWith({ worksheetId: 42, note: 'Lütfen' });
    expect(dialogRef.close).toHaveBeenCalledWith({ submitted: true });
  });

  it('submit_ServerReturnsConflict_ShowsConflictAlertAndDisablesSubmit', () => {
    setup(() => throwError(() => new HttpErrorResponse({ status: 409, error: { success: false, message: '' } })));

    submitButton().click();
    fixture.detectChanges();

    const conflict = alerts().find((a) => a.textContent?.includes(T.conflict));
    expect(conflict).toBeDefined();
    expect(alerts().length).toBe(1);
    expect(submitButton().disabled).toBeTrue();
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('submit_ServerErrorWithMessage_ShowsServerMessageAlert', () => {
    setup(() => throwError(() => new HttpErrorResponse({ status: 400, error: { success: false, message: 'Kota doldu' } })));

    submitButton().click();
    fixture.detectChanges();

    const error = alerts().find((a) => a.classList.contains('apd-alert--error'));
    expect(error?.textContent).toContain('Kota doldu');
    expect(alerts().some((a) => a.textContent?.includes(T.conflict))).toBeFalse();
    expect(submitButton().disabled).toBeFalse();
  });

  it('submit_ServerErrorWithoutMessage_ShowsSubmitFailedFallback', () => {
    setup(() => throwError(() => new HttpErrorResponse({ status: 500, error: null })));

    submitButton().click();
    fixture.detectChanges();

    expect(el().querySelector('.apd-alert--error')?.textContent).toContain(T.submitFailed);
  });

  it('submit_NoteOverMaxLength_DoesNotCallService', () => {
    setup(() => of(OK));
    fixture.componentInstance['noteControl'].setValue('a'.repeat(501));
    fixture.detectChanges();

    expect(submitButton().disabled).toBeTrue();
    fixture.componentInstance['submit']();

    expect(service.createRequest).not.toHaveBeenCalled();
  });

  it('cancel_CancelClicked_ClosesDialogWithoutResult', () => {
    setup(() => of(OK));

    cancelButton().click();

    expect(dialogRef.close).toHaveBeenCalledWith();
  });
});
