import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Subject } from 'rxjs';

import { CommentHideDialogComponent, openCommentHideDialog } from './comment-hide-dialog.component';
import { translocoTestingModule } from '../../testing/transloco-testing';
import rootTr from '../../../../../public/i18n/tr.json';
import commentsTr from '../../../../../public/i18n/comments/tr.json';

const URL = '/api/exam/worksheet/12/comments/7/hide';

describe('CommentHideDialogComponent (issue #305)', () => {
  let fixture: ComponentFixture<CommentHideDialogComponent>;
  let http: HttpTestingController;
  let dialogRef: jasmine.SpyObj<MatDialogRef<CommentHideDialogComponent>>;

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
        CommentHideDialogComponent,
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
    fixture = TestBed.createComponent(CommentHideDialogComponent);
    fixture.detectChanges();
  }

  function type(value: string): void {
    const textarea = q<HTMLTextAreaElement>('hide-reason')!;
    textarea.value = value;
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('reasonRequired_BlankOrWhitespaceDisablesSubmit_ShowsRequiredAfterBlur', () => {
    create();
    const submit = () => q<HTMLButtonElement>('hide-submit')!;

    expect(submit().disabled).toBeTrue();
    expect(q('hide-reason')?.hasAttribute('required')).toBeTrue();
    type('   ');
    expect(submit().disabled).toBeTrue();
    q('hide-reason')!.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    expect(q('hide-reason-required')?.textContent).toContain(commentsTr.hideDialog.required);

    type('kişisel bilgi');
    expect(submit().disabled).toBeFalse();
    expect(q('hide-reason-counter')?.textContent?.trim()).toBe('13/500');
  });

  it('tooLong_DisablesSubmit', () => {
    create();

    type('x'.repeat(501));

    expect(q('hide-reason-too-long')).not.toBeNull();
    expect(q<HTMLButtonElement>('hide-submit')!.disabled).toBeTrue();
  });

  it('submit_PostsTrimmedReason_ClosesWithServerComment', () => {
    create();
    type('  hakaret  ');

    q('hide-submit')!.click();
    const req = http.expectOne(URL);
    expect(req.request.body).toEqual({ reason: 'hakaret' });
    const updated = { id: 7, isHidden: true, hiddenReason: 'hakaret' };
    req.flush(updated);

    expect(dialogRef.close).toHaveBeenCalledWith(jasmine.objectContaining(updated));
  });

  it('error_ShowsMessageAndStaysOpen', () => {
    create();
    type('neden');

    q('hide-submit')!.click();
    http.expectOne(URL).flush({ errorCode: 'NotModerator' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(q('hide-error')?.textContent).toContain(commentsTr.errors.codes.NotModerator);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('open_DisableClose', () => {
    create();
    const dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);

    openCommentHideDialog(dialog, { worksheetId: 12, commentId: 7 });

    const config = dialog.open.calls.mostRecent().args[1]!;
    expect(config.disableClose).toBeTrue();
    expect(config.autoFocus).toBe('first-tabbable');
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
    type('neden');
    q('hide-submit')!.click();
    const req = http.expectOne((r) => r.url.endsWith('/hide'));

    keydown$.next(new KeyboardEvent('keydown', { key: 'Escape' }));
    backdrop$.next(new MouseEvent('click'));
    expect(dialogRef.close).not.toHaveBeenCalled();

    req.flush({});
  });
});
