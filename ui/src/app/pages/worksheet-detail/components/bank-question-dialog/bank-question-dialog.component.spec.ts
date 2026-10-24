import type { MockedObject } from "vitest";
import { Component, EventEmitter, Input, Output, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { By } from '@angular/platform-browser';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Subject, of, throwError } from 'rxjs';

import { BankQuestionDialogComponent, BankQuestionDialogData } from './bank-question-dialog.component';
import { QuestionService } from '../../../../services/question.service';
import { Question } from '../../../../models/question';
import { LinkBankQuestionsResult } from '../../../../models/question-bank';
import { QuestionBankPanelComponent } from '../../../../shared/components/question-bank-panel/question-bank-panel.component';
import { ClassificationSelection } from '../../../../shared/components/classification-selector/classification-selector.component';
import { translocoTestingModule } from '../../../../shared/testing/transloco-testing';
import rootTr from '../../../../../../public/i18n/tr.json';
import worksheetDetailTr from '../../../../../../public/i18n/worksheet-detail/tr.json';

/** Gerçek panel kendi HTTP aramasını yapar; dialog testinde yalnız girdileri ve seçim çıktısı taklit edilir. */
@Component({ selector: 'app-question-bank-panel', standalone: true, changeDetection: ChangeDetectionStrategy.Eager,
    template: '' })
class QuestionBankPanelStub {
    @Input()
    selectable = false;
    @Input()
    disabledIds: number[] = [];
    @Input()
    filterLayout: 'chips' | 'selects' = 'chips';
    @Input()
    initialClassification: ClassificationSelection | null = null;
    @Output()
    selectionChange = new EventEmitter<number[]>();
}

describe('BankQuestionDialogComponent (issue #472)', () => {
    let fixture: ComponentFixture<BankQuestionDialogComponent>;
    let questions: MockedObject<QuestionService>;
    let dialogRef: MockedObject<MatDialogRef<BankQuestionDialogComponent>>;
    let existing$: Subject<Question[]>;
    let dialogData: BankQuestionDialogData;

    beforeEach(() => {
        existing$ = new Subject<Question[]>();
        dialogData = { testId: 12 };
        questions = {
            getAll: vi.fn().mockName("QuestionService.getAll"),
            linkBankQuestions: vi.fn().mockName("QuestionService.linkBankQuestions")
        } as unknown as MockedObject<QuestionService>;
        questions.getAll.mockReturnValue(existing$);
        dialogRef = {
            close: vi.fn().mockName("MatDialogRef.close")
        } as unknown as MockedObject<MatDialogRef<BankQuestionDialogComponent, any>>;

        TestBed.configureTestingModule({
            imports: [
                BankQuestionDialogComponent,
                translocoTestingModule({
                    langs: { tr: { ...rootTr, 'worksheet-detail': worksheetDetailTr }, 'worksheet-detail/tr': worksheetDetailTr },
                }),
            ],
            providers: [
                provideNoopAnimations(),
                { provide: MAT_DIALOG_DATA, useValue: dialogData },
                { provide: MatDialogRef, useValue: dialogRef },
                { provide: QuestionService, useValue: questions },
            ],
        });
        TestBed.overrideComponent(BankQuestionDialogComponent, {
            remove: { imports: [QuestionBankPanelComponent] },
            add: { imports: [QuestionBankPanelStub] },
        });
        fixture = TestBed.createComponent(BankQuestionDialogComponent);
        fixture.detectChanges();
    });

    const panel = () => fixture.debugElement.query(By.directive(QuestionBankPanelStub))?.componentInstance as QuestionBankPanelStub | undefined;
    const submitButton = () => fixture.debugElement.query(By.css('[data-testid="bank-dialog-submit"]')).nativeElement as HTMLButtonElement;
    const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

    function loadExisting(ids: number[]): void {
        existing$.next(ids.map((id) => ({ id }) as Question));
        fixture.detectChanges();
    }

    function select(ids: number[]): void {
        panel()!.selectionChange.emit(ids);
        fixture.detectChanges();
    }

    it('loadsTestQuestions_AndPassesThemAsDisabledIdsInSelectionMode', () => {
        expect(questions.getAll).toHaveBeenCalledWith(12);
        expect(panel()).toBeUndefined(); // yüklenene kadar seçim yapılamaz

        loadExisting([3, 7]);

        expect(panel()!.selectable).toBe(true);
        expect(panel()!.disabledIds).toEqual([3, 7]);
    });

    it('panel_UsesSelectsFilterLayoutInDialog', () => {
        loadExisting([]);

        expect(panel()!.filterLayout).toBe('selects');
    });

    it('panel_NoInitialClassification_PassesNull', () => {
        loadExisting([]);

        expect(panel()!.initialClassification).toBeNull();
    });

    it('panel_InitialClassificationInData_IsPassedToPanel', () => {
        const initial: ClassificationSelection = { gradeId: 5, subjectId: 10, topicId: 100, subtopicIds: [1000] };
        dialogData.initialClassification = initial;

        loadExisting([]);

        expect(panel()!.initialClassification).toEqual(initial);
    });

    it('submit_IsDisabledUntilSomethingIsSelected', () => {
        loadExisting([]);
        expect(submitButton().disabled).toBe(true);

        select([5]);

        expect(submitButton().disabled).toBe(false);
        expect(text()).toContain('1 soru seçildi');
    });

    it('submit_LinksSelectedQuestionsAndClosesWithServerResult', () => {
        const result: LinkBankQuestionsResult = { addedQuestionIds: [5, 9], alreadyInTestQuestionIds: [] };
        questions.linkBankQuestions.mockReturnValue(of(result));
        loadExisting([3]);
        select([5, 9]);

        submitButton().click();

        expect(questions.linkBankQuestions).toHaveBeenCalledTimes(1);

        expect(questions.linkBankQuestions).toHaveBeenCalledWith(12, [5, 9]);
        expect(dialogRef.close).toHaveBeenCalledTimes(1);
        expect(dialogRef.close).toHaveBeenCalledWith(result);
    });

    it('submit_404_ShowsNotFoundMessageAndKeepsDialogOpen', () => {
        questions.linkBankQuestions.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
        loadExisting([]);
        select([5]);

        submitButton().click();
        fixture.detectChanges();

        expect(dialogRef.close).not.toHaveBeenCalled();
        expect(text()).toContain(worksheetDetailTr.bankDialog.notFound);
        expect(submitButton().disabled).toBe(false);
    });

    it('submit_OtherError_ShowsGenericFailure', () => {
        questions.linkBankQuestions.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
        loadExisting([]);
        select([5]);

        submitButton().click();
        fixture.detectChanges();

        expect(text()).toContain(worksheetDetailTr.bankDialog.submitFailed);
    });

    it('loadError_ShowsMessageAndNoPanel', () => {
        existing$.error(new HttpErrorResponse({ status: 500 }));
        fixture.detectChanges();

        expect(panel()).toBeUndefined();
        expect(text()).toContain(worksheetDetailTr.bankDialog.loadFailed);
    });

    it('cancel_ClosesWithoutResult', () => {
        loadExisting([]);
        const cancel = fixture.debugElement
            .queryAll(By.css('mat-dialog-actions button'))
            .find((b) => (b.nativeElement as HTMLElement).textContent?.includes(worksheetDetailTr.bankDialog.cancel))!;

        (cancel.nativeElement as HTMLButtonElement).click();

        expect(dialogRef.close).toHaveBeenCalledTimes(1);

        expect(dialogRef.close).toHaveBeenCalledWith();
        expect(questions.linkBankQuestions).not.toHaveBeenCalled();
    });
});
