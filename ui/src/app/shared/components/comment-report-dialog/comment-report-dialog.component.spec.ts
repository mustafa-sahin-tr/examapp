import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Subject } from 'rxjs';

import { CommentReportDialogComponent, openCommentReportDialog } from './comment-report-dialog.component';
import { translocoTestingModule } from '../../testing/transloco-testing';
import rootTr from '../../../../../public/i18n/tr.json';
import commentsTr from '../../../../../public/i18n/comments/tr.json';

const URL = '/api/exam/worksheet/12/comments/7/report';

describe('CommentReportDialogComponent (issue #305)', () => {
  let fixture: ComponentFixture<CommentReportDialogComponent>;
  let http: HttpTestingController;
  let dialogRef: jasmine.SpyObj<MatDialogRef<CommentReportDialogComponent>>;

  let keydown$: Subject<KeyboardEvent>;
  let backdrop$: Subject<MouseEvent>;
  const el = () => fixture.nativeElement as HTMLElement;
  const q = <T extends HTMLElement = HTMLElement>(id: string) => el().querySelector<T>(`[data-testid="${id}"]`);

  function create(): void {
    keydown$ = new Subject<KeyboardEvent>();
    backdrop$ = new Subject<MouseEvent>();
    dialogRef = jasmine.createSpyObj('MatDialogRef', ['close', 'keydownEvents', 'backdropClick']);
    dialogRef.keydownEvents.and.returnValue(keydown$);
    dialogRef.backdropClick.and.returnValue(backdrop$);
    TestBed.configureTestingModule({
      imports: [
        CommentReportDialogComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { tr: { ...rootTr, comments: commentsTr }, 'comments/tr': commentsTr } }),
      ],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { worksheetId: 12, commentId: 7 } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CommentReportDialogComponent);
    fixture.detectChanges();
  }

  function chooseReason(reason: string): void {
    q(`report-reason-${reason}`)!.querySelector<HTMLInputElement>('input')!.click();
    fixture.detectChanges();
  }

  function typeNote(value: string): void {
    const textarea = q<HTMLTextAreaElement>('report-note')!;
    textarea.value = value;
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('rendersAllReasonsAsRadios_SubmitDisabledUntilChosen', () => {
    create();

    expect(el().querySelectorAll('mat-radio-button').length).toBe(4);
    expect(el().textContent).toContain(commentsTr.reportDialog.reasons.personalInfo);
    expect(q('report-reasons')?.getAttribute('aria-labelledby')).toBeTruthy();
    expect(q<HTMLButtonElement>('report-submit')!.disabled).toBeTrue();

    chooseReason('abuse');
    expect(q<HTMLButtonElement>('report-submit')!.disabled).toBeFalse();
  });

  it('noteCounter_AndTooLongDisablesSubmit', () => {
    create();
    chooseReason('other');

    typeNote('kısa not');
    expect(q('report-note-counter')?.textContent?.trim()).toBe('8/500');

    typeNote('x'.repeat(501));
    expect(q('report-note-error')?.textContent).toContain('500');
    expect(q<HTMLButtonElement>('report-submit')!.disabled).toBeTrue();
  });

  it('submit_PostsReasonAndNote_ClosesWithResult', () => {
    create();
    chooseReason('personalInfo');
    typeNote(' telefon var ');

    q('report-submit')!.click();
    fixture.detectChanges();
    const req = http.expectOne(URL);
    expect(req.request.body).toEqual({ reason: 'personalInfo', note: 'telefon var' });
    expect(q('report-submit')?.getAttribute('aria-busy')).toBe('true');
    req.flush({ success: true, alreadyReported: true, reportedByMe: true });

    expect(dialogRef.close).toHaveBeenCalledWith({ alreadyReported: true, reportedByMe: true });
  });

  it('error_ShowsBackendMessageAndStaysOpen', () => {
    create();
    chooseReason('spam');

    q('report-submit')!.click();
    http
      .expectOne(URL)
      .flush({ message: 'Kendi yorumunu şikayet edemezsin.', errorCode: 'CannotReportOwnComment' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(q('report-error')?.textContent).toContain('Kendi yorumunu şikayet edemezsin.');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('cancel_ClosesWithoutRequest', () => {
    create();

    q('report-cancel')!.click();

    expect(dialogRef.close).toHaveBeenCalledWith();
  });

  it('open_DisableCloseAndFocusFirstTabbable', () => {
    create();
    const dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);

    openCommentReportDialog(dialog, { worksheetId: 12, commentId: 7 });

    const config = dialog.open.calls.mostRecent().args[1]!;
    expect(config.data).toEqual({ worksheetId: 12, commentId: 7 });
    expect(config.disableClose).toBeTrue();
    expect(config.autoFocus).toBe('first-tabbable');
    expect(config.restoreFocus).toBeTrue();
  });

  it('escOrBackdrop_Idle_ClosesWithoutResult; otherKeys_Ignored', () => {
    create();

    keydown$.next(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(dialogRef.close).not.toHaveBeenCalled();
    keydown$.next(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(dialogRef.close).toHaveBeenCalledOnceWith();
    backdrop$.next(new MouseEvent('click'));
    expect(dialogRef.close).toHaveBeenCalledTimes(2);
  });

  it('escOrBackdrop_WhileSubmitting_DoesNotClose', () => {
    create();
    chooseReason('spam');
    q('report-submit')!.click();
    const req = http.expectOne((r) => r.url.endsWith('/report'));

    keydown$.next(new KeyboardEvent('keydown', { key: 'Escape' }));
    backdrop$.next(new MouseEvent('click'));
    expect(dialogRef.close).not.toHaveBeenCalled();

    req.flush({});
  });
});
