import type { Mock, MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialogRef } from '@angular/material/dialog';
import { Subject, of, throwError } from 'rxjs';

import { LabelingPromptDialogComponent } from './labeling-prompt-dialog.component';
import { LabelingPrompt, LabelingPromptState } from '../../../models/labeling';
import { LabelingService } from '../../../services/labeling.service';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import labelingTr from '../../../../../public/i18n/labeling/tr.json';

function prompt(overrides: Partial<LabelingPrompt> = {}): LabelingPrompt {
    return {
        version: 2,
        questionInstruction: 'Soru {width}x{height}',
        optionInstruction: 'Şık {width}x{height}',
        questionPadding: 6,
        optionPadding: 4,
        expandQuestionToOptions: true,
        createdByName: 'Eğitmen',
        createdAt: '2026-10-22T00:00:00Z',
        ...overrides,
    };
}

function state(overrides: Partial<LabelingPromptState> = {}): LabelingPromptState {
    return {
        active: prompt(),
        defaults: prompt({ version: 0, createdByName: null, createdAt: null, questionInstruction: 'DQ {width}x{height}' }),
        history: [prompt(), prompt({ version: 1, questionInstruction: 'Eski {width}x{height}', createdByName: null })],
        ...overrides,
    };
}

describe('LabelingPromptDialogComponent', () => {
    let fixture: ComponentFixture<LabelingPromptDialogComponent>;
    let component: LabelingPromptDialogComponent;
    let api: MockedObject<LabelingService>;
    let dialogRef: {
        close: Mock;
        disableClose: boolean;
    };

    function create(getPrompt = of(state())): void {
        api = {
            getPrompt: vi.fn().mockName("LabelingService.getPrompt"),
            savePrompt: vi.fn().mockName("LabelingService.savePrompt")
        } as unknown as MockedObject<LabelingService>;
        api.getPrompt.mockReturnValue(getPrompt);
        dialogRef = { close: vi.fn().mockName('close'), disableClose: false };
        TestBed.configureTestingModule({
            imports: [LabelingPromptDialogComponent, translocoTestingModule({ langs: { 'labeling/tr': labelingTr } })],
            providers: [
                provideNoopAnimations(),
                { provide: LabelingService, useValue: api },
                { provide: MatDialogRef, useValue: dialogRef },
            ],
        });
        fixture = TestBed.createComponent(LabelingPromptDialogComponent);
        component = fixture.componentInstance;
        fixture.detectChanges();
    }

    describe('yükleme', () => {
        it('init_LoadsPromptAndFillsFormFromActiveVersion', () => {
            create();

            expect(api.getPrompt).toHaveBeenCalledTimes(1);
            expect(component.loading()).toBe(false);
            expect(component.loadFailed()).toBe(false);
            expect(component.state()?.active.version).toBe(2);
            expect(component.form.getRawValue()).toEqual({
                questionInstruction: 'Soru {width}x{height}',
                optionInstruction: 'Şık {width}x{height}',
                questionPadding: 6,
                optionPadding: 4,
                expandQuestionToOptions: true,
            });
        });

        it('load_ServerFails_ShowsLoadFailedAndRetryRecovers', () => {
            vi.spyOn(console, 'error').mockReturnValue(undefined);
            create(throwError(() => new Error('down')));

            expect(component.loadFailed()).toBe(true);
            expect(component.loading()).toBe(false);
            expect(component.state()).toBeNull();

            api.getPrompt.mockReturnValue(of(state()));
            component.load();

            expect(component.loadFailed()).toBe(false);
            expect(component.state()?.active.version).toBe(2);
        });

        it('load_PendingRequest_ShowsLoadingUntilResponse', () => {
            const pending = new Subject<LabelingPromptState>();
            create(pending.asObservable());

            expect(component.loading()).toBe(true);
            pending.next(state());
            pending.complete();

            expect(component.loading()).toBe(false);
        });
    });

    describe('forma doldurma', () => {
        beforeEach(() => create());

        it('loadVersion_KnownHistoryVersion_FillsFormFromThatVersion', () => {
            component.loadVersion(1);

            expect(component.form.controls.questionInstruction.value).toBe('Eski {width}x{height}');
        });

        it('loadVersion_UnknownVersion_LeavesFormUnchanged', () => {
            const before = component.form.getRawValue();

            component.loadVersion(99);

            expect(component.form.getRawValue()).toEqual(before);
        });

        it('resetToDefaults_FillsFormFromCodeDefaults', () => {
            component.resetToDefaults();

            expect(component.form.controls.questionInstruction.value).toBe('DQ {width}x{height}');
        });

        it('fill_MarksFormDirty', () => {
            component.form.markAsPristine();

            component.fill(prompt({ version: 5 }));

            expect(component.form.dirty).toBe(true);
        });
    });

    describe('doğrulama', () => {
        beforeEach(() => create());

        it('form_EmptyQuestionInstruction_IsInvalid', () => {
            component.form.controls.questionInstruction.setValue('');

            expect(component.form.controls.questionInstruction.hasError('required')).toBe(true);
            expect(component.form.valid).toBe(false);
        });

        it('form_InstructionOver4000Characters_IsInvalid', () => {
            component.form.controls.optionInstruction.setValue('x'.repeat(4001));

            expect(component.form.controls.optionInstruction.hasError('maxlength')).toBe(true);
        });

        it('form_InstructionExactly4000Characters_IsValid', () => {
            component.form.controls.optionInstruction.setValue('x'.repeat(4000));

            expect(component.form.controls.optionInstruction.valid).toBe(true);
        });

        it('form_PaddingOutsideZeroToFifty_IsInvalid', () => {
            component.form.controls.questionPadding.setValue(-1);
            expect(component.form.controls.questionPadding.invalid).toBe(true);

            component.form.controls.questionPadding.setValue(51);
            expect(component.form.controls.questionPadding.invalid).toBe(true);
        });

        it('form_PaddingAtBoundaries_IsValid', () => {
            component.form.controls.optionPadding.setValue(0);
            expect(component.form.controls.optionPadding.valid).toBe(true);

            component.form.controls.optionPadding.setValue(50);
            expect(component.form.controls.optionPadding.valid).toBe(true);
        });

        it('missingPlaceholders_BothPlaceholdersPresent_IsFalse', () => {
            expect(component.missingPlaceholders('a {width} b {height}')).toBe(false);
        });

        it('missingPlaceholders_HeightMissing_IsTrue', () => {
            expect(component.missingPlaceholders('only {width}')).toBe(true);
        });

        it('missingPlaceholders_EmptyText_IsTrue', () => {
            expect(component.missingPlaceholders('')).toBe(true);
        });

        it('template_PlaceholderMissing_ShowsWarningWithoutBlockingSave', () => {
            component.form.controls.questionInstruction.setValue('no placeholders');
            fixture.detectChanges();

            expect(fixture.nativeElement.querySelector('.lp-warn')).not.toBeNull();
            expect(component.form.valid).toBe(true);
        });
    });

    describe('kaydetme', () => {
        beforeEach(() => create());

        it('save_InvalidForm_DoesNotCallApi', () => {
            component.form.controls.questionInstruction.setValue('');

            component.save();

            expect(api.savePrompt).not.toHaveBeenCalled();
            expect(dialogRef.close).not.toHaveBeenCalled();
        });

        it('save_Success_SendsRawFormAndClosesWithNewState', () => {
            const saved = state({ active: prompt({ version: 3 }) });
            api.savePrompt.mockReturnValue(of(saved));

            component.save();

            expect(api.savePrompt).toHaveBeenCalledTimes(1);

            expect(api.savePrompt).toHaveBeenCalledWith({
                questionInstruction: 'Soru {width}x{height}',
                optionInstruction: 'Şık {width}x{height}',
                questionPadding: 6,
                optionPadding: 4,
                expandQuestionToOptions: true,
            });
            expect(dialogRef.close).toHaveBeenCalledTimes(1);
            expect(dialogRef.close).toHaveBeenCalledWith(saved);
            expect(component.saving()).toBe(false);
        });

        it('save_InFlight_DisablesDialogCloseUntilResponse', () => {
            const pending = new Subject<LabelingPromptState>();
            api.savePrompt.mockReturnValue(pending.asObservable());

            component.save();

            expect(component.saving()).toBe(true);
            expect(dialogRef.disableClose).toBe(true);
            expect(dialogRef.close).not.toHaveBeenCalled();

            pending.next(state());
            pending.complete();

            expect(dialogRef.close).toHaveBeenCalledTimes(1);
        });

        it('save_DoubleClickWhileSaving_SendsOnlyOnce', () => {
            const pending = new Subject<LabelingPromptState>();
            api.savePrompt.mockReturnValue(pending.asObservable());

            component.save();
            component.save();

            expect(api.savePrompt).toHaveBeenCalledTimes(1);
        });

        it('save_ServerErrorWithMessage_ShowsMessageStaysOpenAndReEnablesClose', () => {
            api.savePrompt.mockReturnValue(throwError(() => ({ error: { message: 'Sunucu mesajı' } })));

            component.save();

            expect(component.saveError()).toBe('Sunucu mesajı');
            expect(component.saving()).toBe(false);
            expect(dialogRef.close).not.toHaveBeenCalled();
            expect(dialogRef.disableClose).toBe(false);
        });

        it('save_ServerErrorWithoutBody_ShowsEmptyErrorSoTemplateUsesFallback', () => {
            api.savePrompt.mockReturnValue(throwError(() => ({ status: 500 })));

            component.save();

            expect(component.saveError()).toBe('');
            expect(dialogRef.close).not.toHaveBeenCalled();
        });

        it('save_RetryAfterError_ClearsPreviousError', () => {
            api.savePrompt.mockReturnValue(throwError(() => ({ error: { message: 'x' } })));
            component.save();
            api.savePrompt.mockReturnValue(of(state()));

            component.save();

            expect(component.saveError()).toBeNull();
            expect(dialogRef.close).toHaveBeenCalledTimes(1);
        });
    });

    describe('kapatma', () => {
        beforeEach(() => create());

        it('cancel_Idle_ClosesWithoutResult', () => {
            component.cancel();

            expect(dialogRef.close).toHaveBeenCalledTimes(1);

            expect(dialogRef.close).toHaveBeenCalledWith();
        });

        it('cancel_WhileSaving_IsIgnored', () => {
            api.savePrompt.mockReturnValue(new Subject<LabelingPromptState>().asObservable());
            component.save();

            component.cancel();

            expect(dialogRef.close).not.toHaveBeenCalled();
        });
    });
});
