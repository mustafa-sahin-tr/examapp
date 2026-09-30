import { ApplicationRef } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, flush } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { CommentComposerComponent } from './comment-composer.component';
import { translocoTestingModule } from '../../testing/transloco-testing';
import rootTr from '../../../../../public/i18n/tr.json';
import commentsTr from '../../../../../public/i18n/comments/tr.json';

describe('CommentComposerComponent (issue #105)', () => {
  let fixture: ComponentFixture<CommentComposerComponent>;
  let submitted: string[];

  const el = () => fixture.nativeElement as HTMLElement;
  const textarea = () => el().querySelector<HTMLTextAreaElement>('[data-testid="comment-textarea"]')!;
  const send = () => el().querySelector<HTMLButtonElement>('[data-testid="comment-send"]')!;
  const counter = () => el().querySelector<HTMLElement>('[data-testid="comment-counter"]')!;

  function create(inputs: Record<string, unknown> = {}): void {
    TestBed.configureTestingModule({
      imports: [
        CommentComposerComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { tr: { ...rootTr, comments: commentsTr }, 'comments/tr': commentsTr } }),
      ],
    });
    fixture = TestBed.createComponent(CommentComposerComponent);
    fixture.componentRef.setInput('placeholder', 'Yaz');
    fixture.componentRef.setInput('ariaLabel', 'Yorum veya soru yaz');
    Object.entries(inputs).forEach(([k, v]) => fixture.componentRef.setInput(k, v));
    submitted = [];
    fixture.componentInstance.submitted.subscribe((b) => submitted.push(b));
    fixture.detectChanges();
  }

  function type(value: string): void {
    textarea().value = value;
    textarea().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('counter_TracksLengthAndDescribesTextarea', () => {
    create();
    expect(counter().textContent?.trim()).toBe('0/2000');

    type('Merhaba');

    expect(counter().textContent?.trim()).toBe('7/2000');
    expect(fixture.componentInstance.draft()).toBe('Merhaba');
    expect(textarea().getAttribute('aria-label')).toBe('Yorum veya soru yaz');
    expect(textarea().getAttribute('aria-describedby')).toBeTruthy();
  });

  it('overLimit_AriaInvalidErrorHintAndSendDisabled', () => {
    create();

    type('x'.repeat(2001));

    expect(textarea().getAttribute('aria-invalid')).toBe('true');
    expect(el().textContent).toContain('En fazla 2000 karakter');
    expect(send().disabled).toBeTrue();

    type('x'.repeat(2000));
    expect(textarea().getAttribute('aria-invalid')).toBe('false');
    expect(send().disabled).toBeFalse();
  });

  it('whitespaceOnly_CannotSend', () => {
    create();

    type('   \n ');

    expect(send().disabled).toBeTrue();
    send().click();
    expect(submitted).toEqual([]);
  });

  it('ctrlOrCmdEnter_SubmitsTrimmed; plainEnter_DoesNot', () => {
    create();
    type('  soru  ');

    textarea().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(submitted).toEqual([]);

    textarea().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true }));
    textarea().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', metaKey: true }));
    expect(submitted).toEqual(['soru', 'soru']);
  });

  it('autofocus_FocusesTextareaAfterRender', fakeAsync(() => {
    const focusSpy = spyOn(HTMLTextAreaElement.prototype, 'focus').and.callThrough();
    create({ autofocus: true });
    fixture.detectChanges();
    TestBed.inject(ApplicationRef).tick();
    flush();

    expect(focusSpy).toHaveBeenCalled();
    expect(focusSpy.calls.mostRecent().object).toBe(textarea());
  }));

  it('noAutofocus_DoesNotSteal', fakeAsync(() => {
    const focusSpy = spyOn(HTMLTextAreaElement.prototype, 'focus').and.callThrough();
    create();
    fixture.detectChanges();
    TestBed.inject(ApplicationRef).tick();
    flush();

    expect(focusSpy).not.toHaveBeenCalled();
  }));

  it('cancel_OnlyWhenShowCancel', () => {
    create({ showCancel: true });
    let cancelled = 0;
    fixture.componentInstance.cancelled.subscribe(() => cancelled++);

    (el().querySelector('.cc__cancel') as HTMLButtonElement).click();
    expect(cancelled).toBe(1);
  });

  // ---- Issue #305: PII uyarısı -------------------------------------------------------------------------------

  const q = (id: string) => el().querySelector<HTMLElement>(`[data-testid="${id}"]`);

  it('pii_NoPattern_NoWarning', () => {
    create();
    type('Bu soruyu anlamadım');

    expect(q('pii-warning')).toBeNull();
    send().click();
    expect(submitted).toEqual(['Bu soruyu anlamadım']);
  });

  it('pii_PhoneTyped_ShowsInlineWarningBeforeSend_AndDescribesTextarea', () => {
    create();
    type('Numaram 0532 123 45 67');

    expect(q('pii-warning')?.textContent).toContain(commentsTr.composer.piiWarning);
    expect(q('pii-warning-region')?.getAttribute('aria-live')).toBe('polite');
    const regionId = q('pii-warning-region')!.id;
    expect(textarea().getAttribute('aria-describedby')).toContain(regionId);
    expect(q('pii-send-anyway')).toBeNull();
    expect(submitted).toEqual([]);
  });

  it('pii_FirstSendAsksConfirm_SendAnywayEmits', fakeAsync(() => {
    const focusSpy = spyOn(HTMLButtonElement.prototype, 'focus').and.callThrough();
    create();
    type('mailim ali@example.com');

    send().click();
    fixture.detectChanges();
    flush();
    expect(submitted).toEqual([]);
    expect(q('pii-confirm-hint')?.textContent).toContain(commentsTr.composer.piiConfirmHint);
    const anyway = q('pii-send-anyway') as HTMLButtonElement;
    expect(anyway.textContent).toContain(commentsTr.composer.sendAnyway);
    TestBed.inject(ApplicationRef).tick();
    expect(focusSpy).toHaveBeenCalled();
    expect(focusSpy.calls.mostRecent().object).toBe(anyway);

    anyway.click();
    fixture.detectChanges();
    expect(submitted).toEqual(['mailim ali@example.com']);
  }));

  it('pii_SecondSendAlsoConfirms; editingResetsConfirm', () => {
    create();
    type('0532 123 45 67');
    send().click();
    fixture.detectChanges();
    expect(q('pii-send-anyway')).not.toBeNull();

    type('0532 123 45 68');
    expect(q('pii-send-anyway')).toBeNull();
    send().click();
    fixture.detectChanges();
    expect(submitted).toEqual([]);

    send().click();
    expect(submitted).toEqual(['0532 123 45 68']);
  });

  it('pii_CtrlEnterFollowsSameConfirmFlow', () => {
    create();
    type('ara 05321234567');
    const enter = () =>
      textarea().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true, cancelable: true }));

    enter();
    fixture.detectChanges();
    expect(submitted).toEqual([]);
    expect(q('pii-send-anyway')).not.toBeNull();
    enter();
    expect(submitted).toEqual(['ara 05321234567']);
  });
});
