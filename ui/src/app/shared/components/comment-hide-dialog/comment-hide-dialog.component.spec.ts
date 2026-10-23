import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
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
    let dialogRef: MockedObject<MatDialogRef<CommentHideDialogComponent>>;

    let keydown$: Subject<KeyboardEvent>;
    let backdrop$: Subject<MouseEvent>;
    const el = () => fixture.nativeElement as HTMLElement;
    const q = <T extends HTMLElement = HTMLElement>(id: string) => el().querySelector<T>(`[data-testid="${id}"]`);

    function create(): void {
        keydown$ = new Subject<KeyboardEvent>();
        backdrop$ = new Subject<MouseEvent>();
        dialogRef = {
            close: vi.fn().mockName("MatDialogRef.close"),
            keydownEvents: vi.fn().mockName("MatDialogRef.keydownEvents"),
            backdropClick: vi.fn().mockName("MatDialogRef.backdropClick")
        } as unknown as MockedObject<MatDialogRef<CommentHideDialogComponent, any>>;
        dialogRef.keydownEvents.mockReturnValue(keydown$);
        dialogRef.backdropClick.mockReturnValue(backdrop$);
        TestBed.configureTestingModule({
            imports: [
                CommentHideDialogComponent,
                NoopAnimationsModule,
                translocoTestingModule({ langs: { tr: { ...rootTr, comments: commentsTr }, 'comments/tr': commentsTr } }),
            ],
            providers: [
                provideHttpClient(withXhr()),
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

        expect(submit().disabled).toBe(true);
        expect(q('hide-reason')?.hasAttribute('required')).toBe(true);
        type('   ');
        expect(submit().disabled).toBe(true);
        q('hide-reason')!.dispatchEvent(new Event('blur'));
        fixture.detectChanges();
        expect(q('hide-reason-required')?.textContent).toContain(commentsTr.hideDialog.required);

        type('kişisel bilgi');
        expect(submit().disabled).toBe(false);
        expect(q('hide-reason-counter')?.textContent?.trim()).toBe('13/500');
    });

    it('tooLong_DisablesSubmit', () => {
        create();

        type('x'.repeat(501));

        expect(q('hide-reason-too-long')).not.toBeNull();
        expect(q<HTMLButtonElement>('hide-submit')!.disabled).toBe(true);
    });

    it('submit_PostsTrimmedReason_ClosesWithServerComment', () => {
        create();
        type('  hakaret  ');

        q('hide-submit')!.click();
        const req = http.expectOne(URL);
        expect(req.request.body).toEqual({ reason: 'hakaret' });
        const updated = { id: 7, isHidden: true, hiddenReason: 'hakaret' };
        req.flush(updated);

        expect(dialogRef.close).toHaveBeenCalledWith(expect.objectContaining(updated));
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
        const dialog = {
            open: vi.fn().mockName("MatDialog.open")
        } as unknown as MockedObject<MatDialog>;

        openCommentHideDialog(dialog, { worksheetId: 12, commentId: 7 });

        const config = vi.mocked(dialog.open).mock.lastCall![1]!;
        expect(config.disableClose).toBe(true);
        expect(config.autoFocus).toBe('first-tabbable');
    });

    it('escOrBackdrop_Idle_ClosesWithoutResult; otherKeys_Ignored', () => {
        create();

        keydown$.next(new KeyboardEvent('keydown', { key: 'Enter' }));
        expect(dialogRef.close).not.toHaveBeenCalled();
        keydown$.next(new KeyboardEvent('keydown', { key: 'Escape' }));
        expect(dialogRef.close).toHaveBeenCalledTimes(1);
        expect(dialogRef.close).toHaveBeenCalledWith();
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
