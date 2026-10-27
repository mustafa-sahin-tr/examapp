// Issue #576: eşleştirme oynatıcısı — dokun-dokun bağlama/çözme, klavye, örnek çift, SVG çizgiler, kontrol işaretleri.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionCanvasMatchComponent } from './question-canvas-match.component';
import { QuestionRegion } from '../../../models/draws';
import { translocoTestingModule } from '../../testing/transloco-testing';

const PLAN = {
    version: 1,
    type: 'match',
    left: [
        { id: 'l1', label: 'Deprem' },
        { id: 'l2', box: { x: 0, y: 0, width: 50, height: 50 } },
        { id: 'l3', label: 'Sel' },
    ],
    right: [
        { id: 'r1', label: 'Fay hattı' },
        { id: 'r2', label: 'Heyelan' },
        { id: 'r3', label: 'Taşkın' },
        { id: 'r4', label: 'Çığ' },
    ],
    prelinked: [{ leftId: 'l1', rightId: 'r1' }],
};

function region(): QuestionRegion {
    return { imageUrl: 'https://cdn.example.test/q1.webp', width: 200, height: 100, passage: null } as unknown as QuestionRegion;
}

const frame = () => new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));

describe('QuestionCanvasMatchComponent (#576)', () => {
    let fixture: ComponentFixture<QuestionCanvasMatchComponent>;
    let emitted: string[];

    const el = () => fixture.nativeElement as HTMLElement;
    const cmp = () => fixture.componentInstance;
    const item = (side: 'left' | 'right', id: string) =>
        el().querySelector(`button[data-side="${side}"][data-item="${id}"]`) as HTMLButtonElement;
    const last = () => JSON.parse(emitted.at(-1)!).pairs;
    const hint = () => el().querySelector('[data-testid="mt-hint"]')!.textContent!.trim();

    function setup(inputs: {
        solution?: object | null;
        answer?: object | null;
        feedbackRows?: { id: string; correct: boolean }[] | null;
        showSolution?: boolean;
    } = {}): void {
        TestBed.configureTestingModule({ imports: [QuestionCanvasMatchComponent, translocoTestingModule()] });
        fixture = TestBed.createComponent(QuestionCanvasMatchComponent);
        emitted = [];
        cmp().answerPayloadChange.subscribe((p) => emitted.push(p));
        fixture.componentRef.setInput('questionRegion', region());
        fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(PLAN));
        fixture.componentRef.setInput('interactionSolutionJson', inputs.solution ? JSON.stringify(inputs.solution) : null);
        fixture.componentRef.setInput('answerPayloadJson', inputs.answer ? JSON.stringify(inputs.answer) : null);
        if (inputs.feedbackRows !== undefined) fixture.componentRef.setInput('feedbackRows', inputs.feedbackRows);
        if (inputs.showSolution !== undefined) fixture.componentRef.setInput('showSolution', inputs.showSolution);
        fixture.componentRef.setInput('activityMode', true);
        fixture.detectChanges();
    }

    function tap(element: Element | null, detail = 1) {
        (element as HTMLElement).dispatchEvent(new MouseEvent('click', { bubbles: true, detail }));
        fixture.detectChanges();
    }

    async function settle() {
        fixture.detectChanges();
        await frame();
        fixture.detectChanges();
    }

    it('rendersColumns_CropItem_PrelinkedPairDisabledAndConnected', async () => {
        setup();
        await settle();

        expect(el().querySelectorAll('button[data-side="left"]').length).toBe(3);
        expect(el().querySelectorAll('button[data-side="right"]').length).toBe(4);
        expect(item('left', 'l2').querySelector('[data-testid="image-crop"] img')?.getAttribute('src')).toBe('https://cdn.example.test/q1.webp');
        expect(item('left', 'l1').disabled).toBe(true);
        expect(item('right', 'r1').disabled).toBe(true);
        expect(item('left', 'l1').getAttribute('aria-label')).toBe('Deprem, eşi: Fay hattı, örnek eşleşme');
        const line = el().querySelector('[data-line="l1:r1"]');
        expect(line?.classList).toContain('mt-line--example');
    });

    it('tapLeftThenRight_Links_DrawsLine_TapLinkedUnlinks', async () => {
        setup();
        tap(item('left', 'l3'));
        expect(hint()).toBe('Sel seçildi; sağ sütundan eşini seç.');
        tap(item('right', 'r3'));
        expect(last()).toEqual([{ leftId: 'l3', rightId: 'r3' }]);
        await settle();

        const line = el().querySelector('[data-line="l3:r3"]')!;
        const left = item('left', 'l3').getBoundingClientRect();
        const right = item('right', 'r3').getBoundingClientRect();
        const board = el().querySelector('[data-testid="mt-board"]')!.getBoundingClientRect();
        expect(Number(line.getAttribute('x1'))).toBeCloseTo(left.right - board.left, 0);
        expect(Number(line.getAttribute('x2'))).toBeCloseTo(right.left - board.left, 0);
        expect(Number(line.getAttribute('x2'))).toBeGreaterThan(Number(line.getAttribute('x1')));

        tap(item('left', 'l3'));
        expect(last()).toEqual([]);
        expect(cmp().selected()).toEqual({ side: 'left', id: 'l3' });
        expect(hint()).toContain('eşleşmesi kaldırıldı');
        await settle();
        expect(el().querySelector('[data-line="l3:r3"]')).toBeNull();
    });

    it('rightFirst_AlsoLinks_AndRightUsedOnceMovesToNewLeft', () => {
        setup();
        tap(item('right', 'r2'));
        tap(item('left', 'l2'));
        expect(last()).toEqual([{ leftId: 'l2', rightId: 'r2' }]);
        tap(item('left', 'l3'));
        tap(item('right', 'r2'));
        expect(last()).toEqual([{ leftId: 'l3', rightId: 'r2' }]);
        // Örnek çiftin öğesi seçilemez ve payload'a girmez.
        tap(item('left', 'l1'));
        expect(cmp().selected()).toBeNull();
    });

    it('keyboard_PickMovesFocusToOtherColumn_ArrowsNavigate_EscapeCancels', async () => {
        setup();
        item('left', 'l2').focus();
        item('left', 'l2').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
        await Promise.resolve();
        expect(document.activeElement).toBe(item('left', 'l3'));

        tap(item('left', 'l3'), 0);
        await Promise.resolve();
        // Örnek çiftteki r1 pasif: odak sağ sütunun (karışık sırada) ilk etkin öğesine geçer.
        const rights = Array.from(el().querySelectorAll<HTMLButtonElement>('button[data-side="right"]'));
        const firstEnabled = rights.find((b) => !b.disabled)!;
        expect(document.activeElement).toBe(firstEnabled);
        firstEnabled.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
        await Promise.resolve();
        expect((document.activeElement as HTMLElement).getAttribute('data-side')).toBe('left');

        el().querySelector('.mt-root')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        fixture.detectChanges();
        expect(cmp().selected()).toBeNull();
        expect(hint()).toBe('Seçim kaldırıldı.');
    });

    it('feedback_ColorsLinesAndMarksLefts', async () => {
        setup({
            answer: { version: 1, type: 'match', pairs: [{ leftId: 'l2', rightId: 'r2' }, { leftId: 'l3', rightId: 'r4' }] },
            feedbackRows: [{ id: 'l2', correct: true }, { id: 'l3', correct: false }],
        });
        await settle();

        expect(el().querySelector('[data-line="l2:r2"]')?.classList).toContain('mt-line--correct');
        expect(el().querySelector('[data-line="l3:r4"]')?.classList).toContain('mt-line--wrong');
        expect(item('left', 'l3').classList).toContain('is-wrong');
        expect(item('left', 'l3').getAttribute('aria-label')).toContain('yanlış');
        expect(el().querySelector('[data-mark="l1"]')).toBeNull();
    });

    it('showSolution_ShowsCorrectPairsReadOnly_RestoresOwnAfter', async () => {
        setup({
            solution: { l2: 'r2', l3: 'r3' },
            answer: { version: 1, type: 'match', pairs: [{ leftId: 'l2', rightId: 'r4' }] },
        });
        fixture.componentRef.setInput('showSolution', true);
        await settle();
        expect(el().querySelector('[data-line="l3:r3"]')?.classList).toContain('mt-line--solution');
        expect(el().querySelector('[data-line="l2:r4"]')).toBeNull();
        expect(item('left', 'l2').disabled).toBe(true);

        fixture.componentRef.setInput('showSolution', false);
        await settle();
        expect(el().querySelector('[data-line="l2:r4"]')).not.toBeNull();
    });

    it('restore_DropsPrelinkedAndDuplicates_EchoKeepsScreen_Clear', () => {
        setup({
            answer: {
                version: 1,
                type: 'match',
                pairs: [
                    { leftId: 'l1', rightId: 'r2' },
                    { leftId: 'l2', rightId: 'r1' },
                    { leftId: 'l2', rightId: 'r3' },
                    { leftId: 'l3', rightId: 'r3' },
                ],
            },
        });
        expect({ ...cmp().pairs() }).toEqual({ l2: 'r3' });

        tap(item('left', 'l3'));
        tap(item('right', 'r4'));
        fixture.componentRef.setInput('answerPayloadJson', emitted.at(-1)!);
        fixture.detectChanges();
        expect({ ...cmp().pairs() }).toEqual({ l2: 'r3', l3: 'r4' });

        tap(el().querySelector('.mt-clear'));
        expect(last()).toEqual([]);
    });

    it('rightColumn_IsShuffledStablyBySeed_LeftKeepsOrder (#576 review)', () => {
        setup();
        const many = { ...PLAN, right: Array.from({ length: 10 }, (_, i) => ({ id: `r${i + 1}`, label: `S${i + 1}` })) };
        fixture.componentRef.setInput('shuffleSeed', 101);
        fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(many));
        fixture.detectChanges();
        const ids = (side: string) => Array.from(el().querySelectorAll(`button[data-side="${side}"]`)).map((b) => b.getAttribute('data-item'));
        const right = ids('right');
        expect(right).not.toEqual(many.right.map((r) => r.id));
        expect([...right].sort()).toEqual(many.right.map((r) => r.id).sort());
        expect(ids('left')).toEqual(['l1', 'l2', 'l3']);
        tap(item('left', 'l2'));
        tap(item('right', 'r5'));
        expect(ids('right')).toEqual(right);
    });

    it('disabled_BlocksLinkingAndClear (#576 review)', () => {
        setup({ answer: { version: 1, type: 'match', pairs: [{ leftId: 'l2', rightId: 'r2' }] } });
        tap(item('left', 'l3'));
        fixture.componentRef.setInput('disabled', true);
        fixture.detectChanges();
        expect(cmp().selected()).toBeNull();
        expect(item('left', 'l3').disabled).toBe(true);
        cmp().onItem('right', 'r3');
        cmp().clearAll();
        expect(emitted.length).toBe(0);
    });
});

