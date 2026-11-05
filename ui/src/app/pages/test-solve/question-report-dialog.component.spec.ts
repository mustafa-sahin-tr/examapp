import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NEVER, Observable, of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { QuestionReportCreated } from '../../models/question-report';
import { QuestionReportDialogComponent, QuestionReportDialogData } from './question-report-dialog.component';

function setup(submit: QuestionReportDialogData['submit']) {
  const close = vi.fn();
  const data: QuestionReportDialogData = {
    labels: {
      title: 'Title',
      message: 'Message',
      reasonLabel: 'Reason',
      reason: (reason) => reason,
      noteLabel: 'Note',
      send: 'Send',
      cancel: 'Cancel',
      error: 'Failed',
    },
    submit,
  };
  TestBed.configureTestingModule({
    providers: [
      { provide: MAT_DIALOG_DATA, useValue: data },
      { provide: MatDialogRef, useValue: { close } },
    ],
  });
  const fixture = TestBed.createComponent(QuestionReportDialogComponent);
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  const send = () => root.querySelector<HTMLButtonElement>('.report__send')!;
  const chooseFirstReason = () => {
    root.querySelector<HTMLInputElement>('mat-radio-button input')!.click();
    fixture.detectChanges();
  };
  const typeNote = (value: string) => {
    const textarea = root.querySelector('textarea')!;
    textarea.value = value;
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  return { fixture, root, close, send, chooseFirstReason, typeNote };
}

describe('QuestionReportDialogComponent (#781)', () => {
  it('asks for a reason before sending', () => {
    const { send, chooseFirstReason, fixture } = setup(() => NEVER);

    expect(send().disabled).toBe(true);
    chooseFirstReason();
    fixture.detectChanges();
    expect(send().disabled).toBe(false);
  });

  it('closes with the result only after the save succeeds', () => {
    const submit = vi.fn((): Observable<QuestionReportCreated> => of({ alreadyReported: false }));
    const { send, chooseFirstReason, typeNote, close } = setup(submit);

    chooseFirstReason();
    typeNote('  blurry  ');
    send().click();

    expect(submit).toHaveBeenCalledWith('wrongAnswer', 'blurry');
    expect(close).toHaveBeenCalledWith({ alreadyReported: false });
  });

  it('does not close while the save is running and blocks a second send', () => {
    const submit = vi.fn((): Observable<QuestionReportCreated> => NEVER);
    const { send, chooseFirstReason, close, fixture } = setup(submit);

    chooseFirstReason();
    send().click();
    fixture.detectChanges();

    expect(close).not.toHaveBeenCalled();
    expect(send().disabled).toBe(true);
  });

  it('stays open with the note kept when the save fails', () => {
    const { send, chooseFirstReason, typeNote, close, root, fixture } = setup(() => throwError(() => new Error('down')));

    chooseFirstReason();
    typeNote('blurry');
    send().click();
    fixture.detectChanges();

    expect(close).not.toHaveBeenCalled();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Failed');
    expect(root.querySelector('textarea')!.value).toBe('blurry');
    expect(send().disabled).toBe(false);
  });

  it('sends no note when only spaces were typed', () => {
    const submit = vi.fn((): Observable<QuestionReportCreated> => of({ alreadyReported: true }));
    const { send, chooseFirstReason, typeNote } = setup(submit);

    chooseFirstReason();
    typeNote('   ');
    send().click();

    expect(submit).toHaveBeenCalledWith('wrongAnswer', null);
  });
});
