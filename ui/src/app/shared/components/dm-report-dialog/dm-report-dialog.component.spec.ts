import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Subject } from 'rxjs';

import { DmReportDialogComponent, DmReportDialogData, openDmReportDialog } from './dm-report-dialog.component';
import { translocoTestingModule } from '../../testing/transloco-testing';
import rootTr from '../../../../../public/i18n/tr.json';
import dmTr from '../../../../../public/i18n/direct-messages/tr.json';

const URL = '/api/exam/direct-messages/conversations/5/report';

describe('DmReportDialogComponent (issue #106)', () => {
  let fixture: ComponentFixture<DmReportDialogComponent>;
  let http: HttpTestingController;
  let dialogRef: jasmine.SpyObj<MatDialogRef<DmReportDialogComponent>>;
  let keydown$: Subject<KeyboardEvent>;

  const el = () => fixture.nativeElement as HTMLElement;
  const q = <T extends HTMLElement = HTMLElement>(id: string) => el().querySelector<T>(`[data-testid="${id}"]`);

  function create(data: DmReportDialogData = { conversationId: 5, messageId: 12 }): void {
    keydown$ = new Subject<KeyboardEvent>();
    dialogRef = jasmine.createSpyObj('MatDialogRef', ['close', 'keydownEvents', 'backdropClick']);
    dialogRef.keydownEvents.and.returnValue(keydown$);
    dialogRef.backdropClick.and.returnValue(new Subject<MouseEvent>());
    TestBed.configureTestingModule({
      imports: [
        DmReportDialogComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { tr: { ...rootTr, 'direct-messages': dmTr }, 'direct-messages/tr': dmTr } }),
      ],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DmReportDialogComponent);
    fixture.detectChanges();
  }

  function chooseReason(reason: string): void {
    q(`dm-report-reason-${reason}`)!.querySelector<HTMLInputElement>('input')!.click();
    fixture.detectChanges();
  }

  function typeNote(value: string): void {
    const textarea = q<HTMLTextAreaElement>('dm-report-note')!;
    textarea.value = value;
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('fourReasons_SubmitDisabledUntilChosen_MessageTitle', () => {
    create();

    expect(el().querySelectorAll('mat-radio-button').length).toBe(4);
    expect(el().textContent).toContain(dmTr.reportDialog.titleMessage);
    expect(q('dm-report-reasons')?.getAttribute('aria-labelledby')).toBe('dm-report-reason-label');
    expect(q<HTMLButtonElement>('dm-report-submit')!.disabled).toBeTrue();

    chooseReason('abuse');
    expect(q<HTMLButtonElement>('dm-report-submit')!.disabled).toBeFalse();
  });

  it('conversationReport_UsesConversationTitle', () => {
    create({ conversationId: 5 });
    expect(el().textContent).toContain(dmTr.reportDialog.titleConversation);
  });

  it('noteCounter_501CharsDisablesSubmit', () => {
    create();
    chooseReason('other');

    typeNote('not');
    expect(q('dm-report-note-counter')?.textContent?.trim()).toBe('3/500');

    typeNote('x'.repeat(501));
    expect(q('dm-report-note-error')?.textContent).toContain('500');
    expect(q<HTMLButtonElement>('dm-report-submit')!.disabled).toBeTrue();
  });

  it('submit_PostsReasonMessageIdAndNote_ClosesWithResult', () => {
    create();
    chooseReason('personalInfo');
    typeNote(' telefon numarası istedi ');

    q('dm-report-submit')!.click();
    fixture.detectChanges();
    const req = http.expectOne(URL);
    expect(req.request.body).toEqual({ reason: 'personalInfo', messageId: 12, note: 'telefon numarası istedi' });
    expect(q('dm-report-submit')?.getAttribute('aria-busy')).toBe('true');
    req.flush({ success: true, reportId: 3, alreadyReported: true });

    expect(dialogRef.close).toHaveBeenCalledWith({ alreadyReported: true });
  });

  it('error_CannotReportOwnMessage_ShowsLocalTextAndStaysOpen', () => {
    create();
    chooseReason('spam');

    q('dm-report-submit')!.click();
    http.expectOne(URL).flush({ errorCode: 'CannotReportOwnMessage', message: 'x' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(q('dm-report-error')?.textContent).toContain(dmTr.errors.codes.CannotReportOwnMessage);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('escape_WhenIdle_Closes', () => {
    create();
    keydown$.next(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(dialogRef.close).toHaveBeenCalledOnceWith();
  });

  it('open_DisableCloseAndFocusFirstTabbable', () => {
    create();
    const dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);

    openDmReportDialog(dialog, { conversationId: 5 });

    const config = dialog.open.calls.mostRecent().args[1]!;
    expect(config.disableClose).toBeTrue();
    expect(config.autoFocus).toBe('first-tabbable');
    expect(config.restoreFocus).toBeTrue();
  });
});
