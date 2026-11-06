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
import { LinkBankQuestionsResult, QuestionBankItem } from '../../../../models/question-bank';
import { QuestionBankPanelComponent } from '../../../../shared/components/question-bank-panel/question-bank-panel.component';
import { ClassificationSelection } from '../../../../shared/components/classification-selector/classification-selector.component';
import { translocoTestingModule } from '../../../../shared/testing/transloco-testing';
import rootTr from '../../../../../../public/i18n/tr.json';
import worksheetDetailTr from '../../../../../../public/i18n/worksheet-detail/tr.json';
import questionBankTr from '../../../../../../public/i18n/question-bank/tr.json';

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
    @Input()
    layout: 'cards' | 'list' = 'cards';
    @Input()
    initialAnswerFilter = 'any';
    @Output()
    selectionChange = new EventEmitter<number[]>();
    @Output()
    selectedItemsChange = new EventEmitter<QuestionBankItem[]>();
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
                    langs: { tr: { ...rootTr, 'worksheet-detail': worksheetDetailTr, 'question-bank': questionBankTr }, 'worksheet-detail/tr': worksheetDetailTr },
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

    // Issue #863: liste düzeni, "yalnız cevabı olanlar" ve seçim çubuğu.
    it('opens the bank as a list with answered questions first and shows a selection tray', () => {
        existing$.next([]);
        fixture.detectChanges();
        expect(panel()!.layout).toBe('list');
        expect(panel()!.initialAnswerFilter).toBe('yes');

        const item = (id: number, difficultyLevel: number) => ({ id, difficultyLevel, imageUrl: null } as unknown as QuestionBankItem);
        panel()!.selectionChange.emit([1, 2, 3]);
        panel()!.selectedItemsChange.emit([item(1, 2), item(2, 3), item(3, 5)]);
        fixture.detectChanges();

        const tray = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="bank-tray"]')!;
        expect(tray.textContent).toContain('3 soru seçildi · 2 Kolay, 1 Orta');
        expect(tray.querySelectorAll('[data-testid="bank-tray-remove"]').length).toBe(3);
        expect(submitButton().textContent).toContain('3 soruyu teste ekle');
    });
});
