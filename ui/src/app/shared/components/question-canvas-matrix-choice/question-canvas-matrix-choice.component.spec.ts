// Issue #574: matris seçim oynatıcısı — liste / hücre modu, tekli / çoklu seçim, payload, geri bildirim, çözüm gösterimi.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionCanvasMatrixChoiceComponent } from './question-canvas-matrix-choice.component';
import { QuestionRegion } from '../../../models/draws';
import { translocoTestingModule } from '../../testing/transloco-testing';
import tr from '../../../../../public/i18n/tr.json';

const LIST_PLAN = {
    version: 1,
    type: 'matrixChoice',
    multiPerRow: false,
    columns: [
        { id: 'c1', label: 'D' },
        { id: 'c2', label: 'Y' },
    ],
    rows: [
        { id: 'r1', label: 'Kuşlar uçar.', box: { x: 0, y: 0, width: 100, height: 20 } },
        { id: 'r2', box: { x: 0, y: 30, width: 100, height: 20 } },
    ],
};

const CELL_PLAN = {
    ...LIST_PLAN,
    rows: LIST_PLAN.rows.map((r, i) => ({
        ...r,
        cells: {
            c1: { x: 120, y: i * 30, width: 20, height: 20 },
            c2: { x: 150, y: i * 30, width: 20, height: 20 },
        },
    })),
};

const SOLUTION = { r1: ['c1'], r2: ['c2'] };

function region(): QuestionRegion {
    return { imageUrl: 'https://cdn.example.test/q1.webp', width: 200, height: 100, passage: null } as unknown as QuestionRegion;
}

describe('QuestionCanvasMatrixChoiceComponent (#574)', () => {
    let fixture: ComponentFixture<QuestionCanvasMatrixChoiceComponent>;
    let emitted: string[];
    const m = tr.shared.questionCanvas.matrix;

    const el = () => fixture.nativeElement as HTMLElement;
    const cell = (rowId: string, colId: string) => el().querySelector(`[data-cell="${rowId}:${colId}"]`) as HTMLButtonElement;
    const lastPayload = () => JSON.parse(emitted.at(-1)!);

    function setup(inputs: {
        plan?: object | string | null;
        solution?: object | null;
        answer?: object | null;
        feedbackRows?: { id: string; correct: boolean }[] | null;
        showSolution?: boolean;
        practiceFeedback?: boolean;
    } = {}): void {
        TestBed.configureTestingModule({ imports: [QuestionCanvasMatrixChoiceComponent, translocoTestingModule()] });
        fixture = TestBed.createComponent(QuestionCanvasMatrixChoiceComponent);
        emitted = [];
        fixture.componentInstance.answerPayloadChange.subscribe((p) => emitted.push(p));
        const plan = inputs.plan === undefined ? LIST_PLAN : inputs.plan;
        fixture.componentRef.setInput('questionRegion', region());
        fixture.componentRef.setInput('interactionPlanJson', typeof plan === 'string' || plan === null ? plan : JSON.stringify(plan));
        fixture.componentRef.setInput('interactionSolutionJson', inputs.solution ? JSON.stringify(inputs.solution) : null);
        fixture.componentRef.setInput('answerPayloadJson', inputs.answer ? JSON.stringify(inputs.answer) : null);
        if (inputs.feedbackRows !== undefined) fixture.componentRef.setInput('feedbackRows', inputs.feedbackRows);
        if (inputs.showSolution !== undefined) fixture.componentRef.setInput('showSolution', inputs.showSolution);
        if (inputs.practiceFeedback !== undefined) fixture.componentRef.setInput('practiceFeedback', inputs.practiceFeedback);
        fixture.componentRef.setInput('activityMode', true);
        fixture.detectChanges();
    }

    it('listMode_NoCells_RendersRowListWithLabelsFallbackAndRowBoxes', () => {
        setup();

        expect(el().querySelector('[data-testid="mc-list"]')).not.toBeNull();
        const labels = Array.from(el().querySelectorAll('.mc-list__text')).map((x) => x.textContent?.trim());
        expect(labels).toEqual(['Kuşlar uçar.', 'Satır 2']);
        expect(el().querySelectorAll('.mc-row-box').length).toBe(2);
        expect(el().querySelectorAll('.mc-cell').length).toBe(0);
        expect(cell('r1', 'c1').textContent?.trim()).toBe('D');
    });

    it('textOnly_NoImageNoBoxes_RendersListOnly_ShuffleRowsIsSeededAndStable', () => {
        // Issue #590: görselsiz madde — görsel yok, satırlar kutusuz; shuffleRows tohumla aynı sırayı verir.
        const rows = Array.from({ length: 6 }, (_, i) => ({ id: `r${i + 1}`, label: `İfade ${i + 1}` }));
        const plan = { ...LIST_PLAN, shuffleRows: true, rows };
        const order = (seed: string) => {
            TestBed.resetTestingModule();
            TestBed.configureTestingModule({ imports: [QuestionCanvasMatrixChoiceComponent, translocoTestingModule()] });
            fixture = TestBed.createComponent(QuestionCanvasMatrixChoiceComponent);
            emitted = [];
            fixture.componentInstance.answerPayloadChange.subscribe((p) => emitted.push(p));
            fixture.componentRef.setInput('questionRegion', { imageUrl: '', width: 0, height: 0, passage: null } as unknown as QuestionRegion);
            fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(plan));
            fixture.componentRef.setInput('shuffleSeed', seed);
            fixture.detectChanges();
            return Array.from(el().querySelectorAll('.mc-list__row')).map((x) => x.getAttribute('data-row'));
        };

        const first = order('item-7');
        expect(el().querySelector('.mc-stage')).toBeNull();
        expect([...first].sort()).toEqual(rows.map((r) => r.id).sort());
        expect(first).not.toEqual(rows.map((r) => r.id));
        expect(order('item-7')).toEqual(first);

        cell('r3', 'c1').click();
        expect(lastPayload().selections).toEqual({ r3: ['c1'] });
    });

    it('cellMode_AllCells_RendersTappableOverlayAndNoList', () => {
        setup({ plan: CELL_PLAN });

        expect(el().querySelector('[data-testid="mc-list"]')).toBeNull();
        expect(el().querySelectorAll('.mc-cell').length).toBe(4);
        const c2 = cell('r1', 'c2');
        expect(c2.style.left).toBe('75%');
        expect(c2.style.width).toBe('10%');
        expect(c2.getAttribute('aria-label')).toBe('Kuşlar uçar. — Y');
    });

    it('cellMode_PartialCells_FallsBackToList', () => {
        const partial = { ...CELL_PLAN, rows: [CELL_PLAN.rows[0], LIST_PLAN.rows[1]] };
        setup({ plan: partial });

        expect(el().querySelector('[data-testid="mc-list"]')).not.toBeNull();
        expect(el().querySelectorAll('.mc-cell').length).toBe(0);
    });

    it('singleChoice_Click_EmitsPayloadReplacesAndClears', () => {
        setup({ plan: CELL_PLAN });

        cell('r1', 'c1').click();
        fixture.detectChanges();
        expect(lastPayload()).toEqual({ version: 1, type: 'matrixChoice', selections: { r1: ['c1'] } });
        expect(cell('r1', 'c1').getAttribute('aria-pressed')).toBe('true');

        cell('r1', 'c2').click();
        fixture.detectChanges();
        expect(lastPayload().selections).toEqual({ r1: ['c2'] });
        expect(cell('r1', 'c1').getAttribute('aria-pressed')).toBe('false');

        cell('r1', 'c2').click();
        fixture.detectChanges();
        expect(lastPayload().selections).toEqual({});
    });

    it('multiChoice_Click_TogglesAndKeepsColumnOrder', () => {
        setup({ plan: { ...LIST_PLAN, multiPerRow: true } });

        cell('r1', 'c2').click();
        cell('r1', 'c1').click();
        fixture.detectChanges();
        expect(lastPayload().selections).toEqual({ r1: ['c1', 'c2'] });

        cell('r1', 'c2').click();
        fixture.detectChanges();
        expect(lastPayload().selections).toEqual({ r1: ['c1'] });
        expect(el().querySelector('.mc-hint')?.textContent?.trim()).toBe('Kuşlar uçar.: Y seçimi kaldırıldı.');
    });

    it('answerPayload_RestoresSelectionsAndIgnoresUnknownIds', () => {
        setup({ answer: { version: 1, type: 'matrixChoice', selections: { r2: ['c1'], rX: ['c1'], r1: ['cX'] } } });

        expect(cell('r2', 'c1').getAttribute('aria-pressed')).toBe('true');
        expect(cell('r1', 'c1').getAttribute('aria-pressed')).toBe('false');
        expect(fixture.componentInstance.selections()).toEqual({ r2: ['c1'] });
    });

    it('feedbackRows_MarksRowsCorrectAndWrongWithText', () => {
        setup({ feedbackRows: [{ id: 'r1', correct: true }, { id: 'r2', correct: false }] });

        const rows = Array.from(el().querySelectorAll('.mc-list__row')) as HTMLElement[];
        expect(rows[0].classList.contains('is-correct')).toBe(true);
        expect(rows[1].classList.contains('is-wrong')).toBe(true);
        expect(rows[1].querySelector('.mc-list__mark')?.textContent).toContain(m.markWrong);
        expect(el().querySelectorAll('.mc-mark').length).toBe(2);
    });

    it('showSolution_DisplaysSolutionReadOnlyAndRestoresOwnAnswer', () => {
        setup({ solution: SOLUTION, answer: { version: 1, type: 'matrixChoice', selections: { r1: ['c2'] } } });
        fixture.componentRef.setInput('showSolution', true);
        fixture.detectChanges();

        expect(cell('r1', 'c1').getAttribute('aria-pressed')).toBe('true');
        expect(cell('r1', 'c1').classList.contains('is-solution')).toBe(true);
        expect(cell('r2', 'c2').getAttribute('aria-pressed')).toBe('true');
        expect(cell('r1', 'c1').disabled).toBe(true);
        expect(el().querySelector('.mc-hint')?.textContent?.trim()).toBe(m.solutionShownHint);

        fixture.componentRef.setInput('showSolution', false);
        fixture.detectChanges();
        expect(cell('r1', 'c2').getAttribute('aria-pressed')).toBe('true');
        expect(cell('r1', 'c1').getAttribute('aria-pressed')).toBe('false');
    });

    it('noSolutionInput_ShowSolutionSelectsNothingAndPracticeMarksNothing', () => {
        setup({ practiceFeedback: true, answer: { version: 1, type: 'matrixChoice', selections: { r1: ['c1'] } } });
        expect(el().querySelectorAll('.mc-list__mark').length).toBe(0);

        fixture.componentRef.setInput('showSolution', true);
        fixture.detectChanges();
        expect(el().querySelectorAll('[aria-pressed="true"]').length).toBe(0);
    });

    it('practiceFeedback_WithSolution_MarksOnlyAnsweredRows', () => {
        setup({ practiceFeedback: true, solution: SOLUTION, answer: { version: 1, type: 'matrixChoice', selections: { r1: ['c2'] } } });

        const rows = Array.from(el().querySelectorAll('.mc-list__row')) as HTMLElement[];
        expect(rows[0].classList.contains('is-wrong')).toBe(true);
        expect(rows[1].classList.contains('is-wrong')).toBe(false);
        expect(rows[1].classList.contains('is-correct')).toBe(false);
    });

    it('clearAll_EmitsEmptySelections', () => {
        setup({ answer: { version: 1, type: 'matrixChoice', selections: { r1: ['c1'] } } });

        (el().querySelector('.mc-clear') as HTMLButtonElement).click();

        expect(lastPayload().selections).toEqual({});
    });

    // Güvenlik L2: prototip adlı id'ler oynatıcıyı bozmamalı.
    it('prototypeLikeIds_SelectAndEmitAsPlainKeys', () => {
        const plan = {
            ...LIST_PLAN,
            type: 'MatrixChoice',
            columns: [{ id: 'constructor', label: 'D' }, { id: 'toString', label: 'Y' }],
            rows: [
                { id: '__proto__', box: { x: 0, y: 0, width: 10, height: 10 } },
                { id: 'constructor', box: { x: 0, y: 20, width: 10, height: 10 } },
            ],
        };
        setup({ plan, answer: { version: 1, type: 'matrixChoice', selections: {} } });

        expect(el().querySelectorAll('[aria-pressed="true"]').length).toBe(0);
        expect(fixture.componentInstance.answeredCount()).toBe(0);
        cell('__proto__', 'toString').click();
        cell('constructor', 'constructor').click();
        fixture.detectChanges();

        expect(emitted.at(-1)).toBe('{"version":1,"type":"matrixChoice","selections":{"__proto__":["toString"],"constructor":["constructor"]}}');
        expect(cell('__proto__', 'toString').getAttribute('aria-pressed')).toBe('true');
        expect(cell('__proto__', 'constructor').getAttribute('aria-pressed')).toBe('false');
        expect(fixture.componentInstance.answeredCount()).toBe(2);
    });

    it('nextItem_ResetsAriaLiveHint', () => {
        setup();
        cell('r1', 'c1').click();
        fixture.detectChanges();
        expect(el().querySelector('.mc-hint')?.textContent?.trim()).toBe('Kuşlar uçar.: D seçildi.');

        fixture.componentRef.setInput('questionRegion', region());
        fixture.componentRef.setInput('interactionPlanJson', JSON.stringify({ ...LIST_PLAN, rows: [LIST_PLAN.rows[1]] }));
        fixture.detectChanges();

        expect(el().querySelector('.mc-hint')?.textContent?.trim()).toBe(m.hintSingle);
    });

    it('invalidPlan_RendersNoListOrOverlay', () => {
        setup({ plan: '{"type":"dragDropLabeling"}' });

        expect(el().querySelector('[data-testid="mc-overlay"]')).toBeNull();
        expect(el().querySelector('[data-testid="mc-list"]')).toBeNull();
    });
});
