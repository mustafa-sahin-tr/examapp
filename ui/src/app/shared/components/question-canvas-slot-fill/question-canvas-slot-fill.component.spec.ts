// Issue #575: cevap kutusu oynatıcısı — her giriş türü, kesir bileşeni, palet, payload, geri bildirim, çözüm gösterimi.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionCanvasSlotFillComponent, SLOT_FILL_SAVE_DEBOUNCE_MS } from './question-canvas-slot-fill.component';
import { QuestionRegion } from '../../../models/draws';
import { translocoTestingModule } from '../../testing/transloco-testing';

const PLAN = {
    version: 1,
    type: 'slotFill',
    slots: [
        { id: 's1', box: { x: 10, y: 10, width: 40, height: 20 }, input: { kind: 'number', entry: 'plain', unit: 'cm' } },
        { id: 's2', label: 'Başkent', box: { x: 60, y: 10, width: 60, height: 20 }, input: { kind: 'text' } },
        {
            id: 's3',
            box: { x: 10, y: 40, width: 60, height: 20 },
            input: { kind: 'select', options: [{ id: 'o1', label: 'artar' }, { id: 'o2', label: 'azalır' }] },
        },
        { id: 's4', box: { x: 80, y: 40, width: 60, height: 20 }, input: { kind: 'palette', symbols: ['∠', '⊥'] } },
        { id: 's5', box: { x: 10, y: 70, width: 40, height: 28 }, input: { kind: 'number', entry: 'fraction' } },
        { id: 's6', box: { x: 60, y: 70, width: 60, height: 28 }, input: { kind: 'number', entry: 'mixed' } },
    ],
};

const SOLUTION = {
    s1: { value: '5', equivalence: true, requireSimplest: false, units: ['cm'], unitMode: 'optional' },
    s2: { accept: ['Ankara'], optionalSuffixes: [] },
    s3: { optionIds: ['o2'] },
    s4: { accept: ['∠ABC'] },
    s5: { value: '3/4', equivalence: true, requireSimplest: false, units: [], unitMode: 'none' },
    s6: { value: '2 3/5', equivalence: true, requireSimplest: false, units: [], unitMode: 'none' },
};

function region(): QuestionRegion {
    return { imageUrl: 'https://cdn.example.test/q1.webp', width: 200, height: 100, passage: null } as unknown as QuestionRegion;
}

describe('QuestionCanvasSlotFillComponent (#575)', () => {
    let fixture: ComponentFixture<QuestionCanvasSlotFillComponent>;
    let emitted: string[];

    const el = () => fixture.nativeElement as HTMLElement;
    const field = (slotId: string) => el().querySelector(`[data-slot-input="${slotId}"]`) as HTMLInputElement;
    const slot = (slotId: string) => el().querySelector(`[data-slot="${slotId}"]`) as HTMLElement;
    const lastValues = () => JSON.parse(emitted.at(-1)!).values;

    function setup(inputs: {
        solution?: object | null;
        answer?: object | null;
        feedbackRows?: { id: string; correct: boolean }[] | null;
        showSolution?: boolean;
        practiceFeedback?: boolean;
    } = {}): void {
        TestBed.configureTestingModule({ imports: [QuestionCanvasSlotFillComponent, translocoTestingModule()] });
        fixture = TestBed.createComponent(QuestionCanvasSlotFillComponent);
        emitted = [];
        fixture.componentInstance.answerPayloadChange.subscribe((p) => emitted.push(p));
        fixture.componentRef.setInput('questionRegion', region());
        fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(PLAN));
        fixture.componentRef.setInput('interactionSolutionJson', inputs.solution ? JSON.stringify(inputs.solution) : null);
        fixture.componentRef.setInput('answerPayloadJson', inputs.answer ? JSON.stringify(inputs.answer) : null);
        if (inputs.feedbackRows !== undefined) fixture.componentRef.setInput('feedbackRows', inputs.feedbackRows);
        if (inputs.showSolution !== undefined) fixture.componentRef.setInput('showSolution', inputs.showSolution);
        if (inputs.practiceFeedback !== undefined) fixture.componentRef.setInput('practiceFeedback', inputs.practiceFeedback);
        fixture.componentRef.setInput('activityMode', true);
        fixture.detectChanges();
    }

    function type(input: HTMLInputElement, value: string, commit = true) {
        input.value = value;
        input.dispatchEvent(new Event('input'));
        if (commit) input.dispatchEvent(new Event('change'));
        fixture.detectChanges();
    }

    it('rendersEachKindOverImage_WithBoxPositionsAndAriaLabels', () => {
        setup();

        expect(el().querySelectorAll('.sf-slot').length).toBe(6);
        expect(slot('s1').style.left).toBe('5%');
        expect(slot('s1').style.width).toBe('20%');
        // iOS ondalık klavyesinde / % - yok: metin klavyesi.
        expect(field('s1').getAttribute('inputmode')).toBe('text');
        expect(slot('s6').querySelector('[data-part="whole"]')?.getAttribute('inputmode')).toBe('text');
        expect(field('s1').getAttribute('aria-label')).toBe('Kutu 1');
        expect(slot('s1').querySelector('.sf-unit')?.textContent).toBe('cm');
        expect(field('s2').getAttribute('aria-label')).toBe('Kutu 2: Başkent');
        expect(field('s3').tagName).toBe('SELECT');
        expect(Array.from((field('s3') as unknown as HTMLSelectElement).options).map((o) => o.textContent)).toEqual(['Seç', 'artar', 'azalır']);
        expect(slot('s5').querySelectorAll('input').length).toBe(2);
        expect(slot('s6').querySelectorAll('input').length).toBe(3);
        expect(slot('s5').querySelector('[data-part="numerator"]')?.getAttribute('aria-label')).toBe('Kutu 5, pay');
    });

    afterEach(() => vi.useRealTimers());

    it('typing_SavesAfterDebounce_ChangeSavesImmediately', async () => {
        vi.useFakeTimers();
        setup();

        type(field('s2'), 'Ank', false);
        type(field('s2'), 'Ankara', false);
        expect(emitted.length).toBe(0);
        await vi.advanceTimersByTimeAsync(SLOT_FILL_SAVE_DEBOUNCE_MS);
        expect(emitted.length).toBe(1);
        expect(lastValues()).toEqual({ s2: 'Ankara' });

        type(field('s1'), '5', false);
        field('s1').dispatchEvent(new Event('change'));
        expect(lastValues()).toEqual({ s1: '5', s2: 'Ankara' });
        await vi.advanceTimersByTimeAsync(SLOT_FILL_SAVE_DEBOUNCE_MS);
        expect(emitted.length).toBe(2);
    });

    it('flush_EmitsPendingTypedValue_NoOpWhenNothingPending', () => {
        setup();
        type(field('s2'), 'Ankara', false);

        fixture.componentInstance.flush();
        expect(lastValues()).toEqual({ s2: 'Ankara' });
        fixture.componentInstance.flush();
        expect(emitted.length).toBe(1);
    });

    it('parentEchoOfOwnPayload_KeepsRawTypedText', () => {
        setup();
        fixture.componentInstance.answerPayloadChange.subscribe((p) => fixture.componentRef.setInput('answerPayloadJson', p));
        type(field('s2'), 'İç ', false);
        fixture.componentInstance.flush();
        fixture.detectChanges();

        expect(lastValues()).toEqual({ s2: 'İç' });
        expect(field('s2').value).toBe('İç ');
    });

    it('newItemPlan_CancelsPendingSave', async () => {
        vi.useFakeTimers();
        setup();
        type(field('s2'), 'Ankara', false);
        fixture.componentRef.setInput('interactionPlanJson', JSON.stringify({ ...PLAN, slots: PLAN.slots.slice(0, 1) }));
        await vi.advanceTimersByTimeAsync(SLOT_FILL_SAVE_DEBOUNCE_MS * 2);

        expect(emitted.length).toBe(0);
    });

    it('commitSavesTrimmedPayload_BlankOmitted', () => {
        setup();

        type(field('s1'), ' 0,75 ', false);
        field('s1').dispatchEvent(new Event('change'));
        expect(JSON.parse(emitted.at(-1)!)).toEqual({ version: 1, type: 'slotFill', values: { s1: '0,75' } });

        type(field('s2'), 'Ankara');
        const select = field('s3') as unknown as HTMLSelectElement;
        select.value = 'o2';
        select.dispatchEvent(new Event('change'));
        expect(lastValues()).toEqual({ s1: '0,75', s2: 'Ankara', s3: 'o2' });

        type(field('s1'), '   ');
        expect(lastValues()).toEqual({ s2: 'Ankara', s3: 'o2' });
    });

    it('fractionWidget_ComposesFractionAndMixed', () => {
        setup();
        const part = (slotId: string, name: string) => slot(slotId).querySelector(`[data-part="${name}"]`) as HTMLInputElement;

        type(part('s5', 'numerator'), '3', false);
        expect(emitted.length).toBe(0);
        type(part('s6', 'whole'), '-1');
        expect(lastValues()['s6']).toBe('-1');
        type(part('s5', 'denominator'), '4');
        expect(lastValues()['s5']).toBe('3/4');

        type(part('s6', 'whole'), '2');
        expect(lastValues()['s6']).toBe('2');
        type(part('s6', 'numerator'), '3');
        type(part('s6', 'denominator'), '5');
        expect(lastValues()['s6']).toBe('2 3/5');
    });

    it('restoresSavedAnswer_IntoFieldsAndFractionParts', () => {
        setup({ answer: { version: 1, type: 'slotFill', values: { s1: '5', s3: 'o1', s6: '2 3/5' } } });

        expect(field('s1').value).toBe('5');
        expect((field('s3') as unknown as HTMLSelectElement).value).toBe('o1');
        const parts = Array.from(slot('s6').querySelectorAll('input')).map((i) => i.value);
        expect(parts).toEqual(['2', '3', '5']);
    });

    it('palette_InsertsSymbolAtCaretAndSaves', () => {
        setup();
        const input = field('s4');
        type(input, 'ABC', false);
        input.focus();
        input.dispatchEvent(new Event('focus'));
        input.setSelectionRange(0, 0);
        fixture.detectChanges();

        const button = el().querySelector('[data-symbol="∠"]') as HTMLButtonElement;
        expect(el().querySelector('[data-testid="sf-palette"]')?.textContent).toContain('Kutu 4');
        button.click();
        fixture.detectChanges();

        expect(lastValues()).toEqual({ s4: '∠ABC' });
        expect(input.value).toBe('∠ABC');
    });

    it('serverFeedback_MarksEachSlot', () => {
        setup({ feedbackRows: [{ id: 's1', correct: true }, { id: 's2', correct: false }] });

        expect(slot('s1').classList).toContain('is-correct');
        expect(slot('s2').classList).toContain('is-wrong');
        expect(el().querySelector('[data-mark="s3"]')).toBeNull();
        expect(field('s2').getAttribute('aria-label')).toBe('Kutu 2: Başkent, yanlış');
    });

    it('practiceFeedback_UsesClientNormalizerOnCommittedValues', () => {
        setup({ solution: SOLUTION, practiceFeedback: true });

        type(field('s1'), '5 CM');
        type(field('s2'), 'ankara.');
        type(field('s4'), '∠ abd');
        expect(slot('s1').classList).toContain('is-correct');
        expect(slot('s2').classList).toContain('is-correct');
        expect(slot('s4').classList).toContain('is-wrong');
        expect(el().querySelector('[data-mark="s5"]')).toBeNull();
    });

    it('showSolution_DisplaysAcceptedValues_ReadOnly', () => {
        setup({ solution: SOLUTION, showSolution: true, answer: { version: 1, type: 'slotFill', values: { s1: '9' } } });

        const text = (id: string) => el().querySelector(`[data-solution="${id}"]`)?.textContent?.trim();
        expect(text('s1')).toBe('5 cm');
        expect(text('s2')).toBe('Ankara');
        expect(text('s3')).toBe('azalır');
        expect(text('s4')).toBe('∠ABC');
        expect(text('s6')).toBe('2 3/5');
        expect(el().querySelectorAll('input, select').length).toBe(0);
        expect(el().querySelector('[data-testid="sf-palette"]')).toBeNull();
    });

    it('noSolutionProvided_RendersNoAcceptedValues', () => {
        setup({ showSolution: true });

        const texts = Array.from(el().querySelectorAll('.sf-solution')).map((s) => s.textContent?.trim());
        expect(texts.every((t) => t === '')).toBe(true);
        expect(el().textContent).not.toContain('Ankara');
    });

    it('clear_EmitsEmptyValues', () => {
        setup({ answer: { version: 1, type: 'slotFill', values: { s1: '5' } } });
        (el().querySelector('.sf-clear') as HTMLButtonElement).click();
        fixture.detectChanges();

        expect(lastValues()).toEqual({});
        expect(field('s1').value).toBe('');
    });

    it('prototypeLikeIds_DoNotLeakIntoValues', () => {
        TestBed.configureTestingModule({ imports: [QuestionCanvasSlotFillComponent, translocoTestingModule()] });
        fixture = TestBed.createComponent(QuestionCanvasSlotFillComponent);
        fixture.componentRef.setInput('questionRegion', region());
        fixture.componentRef.setInput(
            'interactionPlanJson',
            JSON.stringify({ version: 1, type: 'SlotFill', slots: [{ id: 'constructor', box: { x: 0, y: 0, width: 10, height: 10 }, input: { kind: 'text' } }] })
        );
        fixture.detectChanges();

        expect(fixture.componentInstance.valueFor('constructor')).toBe('');
        expect(fixture.componentInstance.filledCount()).toBe(0);
    });

    it('payload_IsPrototypeFreeAndReservedIdsIgnored', async () => {
        const { buildSlotFillPayload, emptySlotValues, parseSlotFillValues } = await import('../../../models/slot-fill');
        const values = emptySlotValues();
        values['s1'] = '1';
        expect(JSON.parse(buildSlotFillPayload(values))).toEqual({ version: 1, type: 'slotFill', values: { s1: '1' } });
        const parsed = parseSlotFillValues('{"type":"slotFill","values":{"__proto__":"x","constructor":"y","s1":"1"}}');
        expect(Object.keys(parsed)).toEqual(['s1']);
        expect(Object.getPrototypeOf(parsed)).toBeNull();
    });

    // #575 E2E: tam → pay → payda hızlı yazılırken ebeveynin ESKİ yankısı ("5") araya girer; pay kaybolmamalı.
    it('mixedFraction_RapidSubFieldTypingWithStaleParentEcho_KeepsAllThreeParts', () => {
        setup();
        const part = (slotId: string, name: string) => slot(slotId).querySelector(`[data-part="${name}"]`) as HTMLInputElement;

        type(part('s6', 'whole'), '5');
        const staleEcho = emitted.at(-1)!;
        expect(JSON.parse(staleEcho).values).toEqual({ s6: '5' });
        type(part('s6', 'numerator'), '8', false);
        // Geç gelen yankı: öğrenci payı yazdıktan sonra ebeveyn "5" payload'unu geri verir.
        fixture.componentRef.setInput('answerPayloadJson', staleEcho);
        fixture.detectChanges();
        part('s6', 'numerator').dispatchEvent(new Event('change'));
        type(part('s6', 'denominator'), '12');

        expect(lastValues()['s6']).toBe('5 8/12');
        expect([part('s6', 'whole').value, part('s6', 'numerator').value, part('s6', 'denominator').value]).toEqual(['5', '8', '12']);

        // Kesir: pay → payda, araya payın yankısı girer; payda kaybolmamalı.
        type(part('s5', 'numerator'), '13');
        const numEcho = emitted.at(-1)!;
        type(part('s5', 'denominator'), '6', false);
        fixture.componentRef.setInput('answerPayloadJson', numEcho);
        fixture.detectChanges();
        fixture.componentInstance.flush();
        expect(lastValues()['s5']).toBe('13/6');
        expect(part('s5', 'denominator').value).toBe('6');
    });

    it('externalNewAnswer_StillReplacesValues', () => {
        setup();
        type(field('s1'), '5');
        fixture.componentRef.setInput('answerPayloadJson', JSON.stringify({ version: 1, type: 'slotFill', values: { s1: '7', s6: '1 1/2' } }));
        fixture.detectChanges();

        expect(field('s1').value).toBe('7');
        expect(Array.from(slot('s6').querySelectorAll('input')).map((i) => i.value)).toEqual(['1', '1', '2']);
    });
});
