import type { Mock, MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialogRef } from '@angular/material/dialog';
import { Subject, of, throwError } from 'rxjs';

import { LabelingPromptDialogComponent } from './labeling-prompt-dialog.component';
import {
    LabelingActivityPromptType,
    LabelingActivityPromptsState,
    LabelingPrompt,
    LabelingPromptState,
} from '../../../models/labeling';
import { LabelingPromptKind } from '../../../models/labeling';
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

function activityType(type: LabelingPromptKind, version = 1): LabelingActivityPromptType {
    const active = { version, instruction: `${type} v${version}`, createdByName: 'Eğitmen', createdAt: '2026-10-26T00:00:00Z' };
    return {
        interactionType: type,
        active,
        defaults: { version: 0, instruction: `${type} varsayılan`, createdByName: null, createdAt: null },
        history: version > 0 ? [active, { ...active, version: version - 1, instruction: `${type} eski` }] : [],
    };
}

function activityState(): LabelingActivityPromptsState {
    return {
        types: [
            activityType('matrixChoice', 2),
            activityType('slotFill', 0),
            activityType('dragPlace', 1),
            activityType('match', 1),
            activityType('answerKey', 1),
            activityType('solveFromKey', 0),
            activityType('solveAi', 3),
        ],
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

    function create(getPrompt = of(state()), getActivityPrompts = of(activityState())): void {
        api = {
            getPrompt: vi.fn().mockName("LabelingService.getPrompt"),
            savePrompt: vi.fn().mockName("LabelingService.savePrompt"),
            getActivityPrompts: vi.fn().mockName("LabelingService.getActivityPrompts"),
            saveActivityPrompt: vi.fn().mockName("LabelingService.saveActivityPrompt")
        } as unknown as MockedObject<LabelingService>;
        api.getPrompt.mockReturnValue(getPrompt);
        api.getActivityPrompts.mockReturnValue(getActivityPrompts);
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

    describe('etkinlik sekmeleri (#579)', () => {
        function tabLabels(): string[] {
            return Array.from(fixture.nativeElement.querySelectorAll('.mdc-tab__text-label') as NodeListOf<HTMLElement>).map((el) =>
                el.textContent!.trim()
            );
        }

        it('template_ShowsPageTabAndOneTabPerActivityType', () => {
            create();

            expect(tabLabels()).toEqual([
                labelingTr.prompt.tabs.page,
                labelingTr.prompt.tabs.matrixChoice,
                labelingTr.prompt.tabs.slotFill,
                labelingTr.prompt.tabs.dragPlace,
                labelingTr.prompt.tabs.match,
                labelingTr.prompt.tabs.answerKey,
                labelingTr.prompt.tabs.solveFromKey,
                labelingTr.prompt.tabs.solveAi,
            ]);
        });

        it('init_LoadsActivityPromptsAndFillsEachTypeFromActiveVersion', () => {
            create();

            expect(api.getActivityPrompts).toHaveBeenCalledTimes(1);
            expect(component.activityLoading()).toBe(false);
            expect(component.activityForms.matrixChoice.value).toBe('matrixChoice v2');
            expect(component.activityForms.slotFill.value).toBe('slotFill v0');
            expect(component.activityState('match')?.active.version).toBe(1);
        });

        it('activityLoadFails_PageTabStillWorks_RetryRecovers', () => {
            vi.spyOn(console, 'error').mockReturnValue(undefined);
            create(of(state()), throwError(() => new Error('down')));

            expect(component.activityLoadFailed()).toBe(true);
            expect(component.loadFailed()).toBe(false);
            expect(component.canSaveSelected()).toBe(true);

            component.onTabChange(1);
            expect(component.canSaveSelected()).toBe(false);

            api.getActivityPrompts.mockReturnValue(of(activityState()));
            component.loadActivity();
            expect(component.activityLoadFailed()).toBe(false);
            expect(component.canSaveSelected()).toBe(true);
        });

        it('loadActivityVersionAndDefaults_FillOnlyThatType', () => {
            create();

            component.loadActivityVersion('matrixChoice', 1);
            component.resetActivityToDefaults('dragPlace');

            expect(component.activityForms.matrixChoice.value).toBe('matrixChoice eski');
            expect(component.activityForms.dragPlace.value).toBe('dragPlace varsayılan');
            expect(component.activityForms.match.value).toBe('match v1');
        });

        it('saveSelected_OnActivityTab_SavesOnlyThatTypeAndClosesWithActivityResult', () => {
            create();
            const saved = activityType('slotFill', 1).active;
            api.saveActivityPrompt.mockReturnValue(of(saved));
            component.onTabChange(2);
            component.activityForms.slotFill.setValue('Yeni kutu talimatı');

            component.saveSelected();

            expect(api.saveActivityPrompt).toHaveBeenCalledTimes(1);
            expect(api.saveActivityPrompt).toHaveBeenCalledWith('slotFill', { instruction: 'Yeni kutu talimatı' });
            expect(api.savePrompt).not.toHaveBeenCalled();
            expect(dialogRef.close).toHaveBeenCalledWith({ kind: 'activity', interactionType: 'slotFill', prompt: saved });
        });

        it('saveSelected_OnPageTab_SavesPagePrompt', () => {
            create();
            api.savePrompt.mockReturnValue(of(state()));

            component.saveSelected();

            expect(api.savePrompt).toHaveBeenCalledTimes(1);
            expect(api.saveActivityPrompt).not.toHaveBeenCalled();
        });

        it('saveActivity_EmptyInstruction_DoesNotCallApi', () => {
            create();
            component.activityForms.match.setValue('');

            component.saveActivity('match');

            expect(api.saveActivityPrompt).not.toHaveBeenCalled();
        });

        it('saveActivity_ServerError_ShowsMessageStaysOpen_TabChangeClearsIt', () => {
            create();
            api.saveActivityPrompt.mockReturnValue(throwError(() => ({ error: { message: 'Çok uzun' } })));
            component.onTabChange(4);

            component.saveSelected();

            expect(component.saveError()).toBe('Çok uzun');
            expect(dialogRef.close).not.toHaveBeenCalled();
            expect(dialogRef.disableClose).toBe(false);
            component.onTabChange(0);
            expect(component.saveError()).toBeNull();
        });
    
        it('saveSelected_OtherTabHasUnsavedChanges_WarnsAndSavesOnlyAfterConfirm', () => {
            create();
            api.saveActivityPrompt.mockReturnValue(of(activityType('match', 2).active));
            component.activityForms.matrixChoice.setValue('Kaydedilmemiş matris');
            component.form.controls.questionInstruction.setValue('Kaydedilmemiş sayfa');
            component.onTabChange(4);
            component.activityForms.match.setValue('Yeni eşleştirme');

            component.saveSelected();

            expect(api.saveActivityPrompt).not.toHaveBeenCalled();
            expect(component.unsavedOthers()).toEqual(['page', 'matrixChoice']);
            fixture.detectChanges();
            const warning = fixture.nativeElement.querySelector('.lp-unsaved') as HTMLElement;
            expect(warning.textContent).toContain(labelingTr.prompt.tabs.matrixChoice);
            expect(warning.textContent).toContain(labelingTr.prompt.tabs.page);

            (warning.querySelector('.lp-save-anyway') as HTMLButtonElement).click();

            expect(api.saveActivityPrompt).toHaveBeenCalledWith('match', { instruction: 'Yeni eşleştirme' });
            expect(component.unsavedOthers()).toEqual([]);
        });

        it('saveSelected_OnlyCurrentTabChanged_SavesWithoutWarning', () => {
            create();
            api.saveActivityPrompt.mockReturnValue(of(activityType('match', 2).active));
            component.onTabChange(4);
            component.activityForms.match.setValue('Yeni');

            component.saveSelected();

            expect(api.saveActivityPrompt).toHaveBeenCalledTimes(1);
        });

        it('saveActivity_SameVersionReturned_ClosesAsUnchanged', () => {
            create();
            api.saveActivityPrompt.mockReturnValue(of(activityType('matrixChoice', 2).active));

            component.saveActivity('matrixChoice');

            expect(dialogRef.close).toHaveBeenCalledWith({ kind: 'unchanged', version: 2 });
        });

        it('savePage_SameVersionReturned_ClosesAsUnchanged', () => {
            create();
            api.savePrompt.mockReturnValue(of(state()));

            component.save();

            expect(dialogRef.close).toHaveBeenCalledWith({ kind: 'unchanged', version: 2 });
        });
    });

    describe('çözüm talimatı sekmeleri (#580)', () => {
        it('init_FillsThreeSolutionKindsFromActiveVersion', () => {
            create();

            expect(component.activityForms.answerKey.value).toBe('answerKey v1');
            expect(component.activityForms.solveFromKey.value).toBe('solveFromKey v0');
            expect(component.activityForms.solveAi.value).toBe('solveAi v3');
        });

        it('solutionTab_ShowsVersionAndOwnHint', () => {
            create();
            component.onTabChange(5);
            fixture.detectChanges();

            const tab = fixture.nativeElement.querySelector('.lp-activity[data-type="answerKey"]') as HTMLElement;
            expect(tab.textContent).toContain('Etkin: v1');
            expect(tab.textContent).toContain(labelingTr.prompt.solution.hints.answerKey);
            expect(tab.textContent).not.toContain(labelingTr.prompt.activity.hint);
        });

        it('saveSelected_OnSolveAiTab_SavesThatKindAndClosesWithItsResult', () => {
            create();
            const saved = activityType('solveAi', 4).active;
            api.saveActivityPrompt.mockReturnValue(of(saved));
            component.onTabChange(7);
            component.activityForms.solveAi.setValue('Yeni AI çözüm talimatı');

            component.saveSelected();

            expect(api.saveActivityPrompt).toHaveBeenCalledWith('solveAi', { instruction: 'Yeni AI çözüm talimatı' });
            expect(dialogRef.close).toHaveBeenCalledWith({ kind: 'activity', interactionType: 'solveAi', prompt: saved });
        });

        it('loadVersionAndDefaults_OnAnswerKeyTab_FillOnlyThatKind', () => {
            create();

            component.loadActivityVersion('answerKey', 0);
            component.resetActivityToDefaults('solveFromKey');

            expect(component.activityForms.answerKey.value).toBe('answerKey eski');
            expect(component.activityForms.solveFromKey.value).toBe('solveFromKey varsayılan');
            expect(component.activityForms.solveAi.value).toBe('solveAi v3');
        });

        it('saveSolutionKind_SameVersion_ClosesAsUnchanged', () => {
            create();
            api.saveActivityPrompt.mockReturnValue(of(activityType('solveAi', 3).active));
            component.onTabChange(7);

            component.saveSelected();

            expect(dialogRef.close).toHaveBeenCalledWith({ kind: 'unchanged', version: 3 });
        });

        it('serverOmitsSolutionKind_TabShowsMissingAndSaveDisabled', () => {
            create(of(state()), of({ types: activityState().types.filter((t) => t.interactionType !== 'solveFromKey') }));
            component.onTabChange(6);
            fixture.detectChanges();

            expect(component.canSaveSelected()).toBe(false);
            const tab = fixture.nativeElement.querySelector('.lp-activity[data-type="solveFromKey"]') as HTMLElement;
            expect(tab.textContent).toContain(labelingTr.prompt.activity.missing);
        });
    });
});
