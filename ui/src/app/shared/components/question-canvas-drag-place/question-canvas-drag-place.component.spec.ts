// Issue #576: sürükle-yerleştir oynatıcısı — dokun-dokun, klavye, kapasite, tekrar kullanım, sıra modu, cdk bırakma,
// kontrol işaretleri (yanlış kart ✗ + havuza geri al, eksik hedef "t:"), çözüm gösterimi ve yankı koruması.
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CdkDragDrop } from '@angular/cdk/drag-drop';

import { DragPlaceDragData, QuestionCanvasDragPlaceComponent } from './question-canvas-drag-place.component';
import { QuestionRegion } from '../../../models/draws';
import { translocoTestingModule } from '../../testing/transloco-testing';

const PLACE_PLAN = {
    version: 1,
    type: 'dragPlace',
    mode: 'place',
    reuse: false,
    targets: [
        { id: 't1', label: 'Erime', box: { x: 10, y: 10, width: 40, height: 20 }, capacity: 1 },
        { id: 't2', label: 'Geri dönüşüm', capacity: 2 },
        { id: 't3', label: 'Çöp', capacity: 3 },
    ],
    cards: [
        { id: 'c1', label: 'Buz' },
        { id: 'c2', label: 'Kâğıt' },
        { id: 'c3', box: { x: 100, y: 50, width: 50, height: 25 } },
        { id: 'c4', label: 'Cam' },
    ],
};

const ORDER_PLAN = {
    version: 1,
    type: 'dragPlace',
    mode: 'order',
    reuse: false,
    targets: [{ id: 't1', capacity: 1 }, { id: 't2', capacity: 1 }, { id: 't3', capacity: 1 }],
    cards: [{ id: 'c1', label: 'Tohum' }, { id: 'c2', label: 'Fide' }, { id: 'c3', label: 'Ağaç' }],
};

function region(): QuestionRegion {
    return { imageUrl: 'https://cdn.example.test/q1.webp', width: 200, height: 100, passage: null } as unknown as QuestionRegion;
}

describe('QuestionCanvasDragPlaceComponent (#576)', () => {
    let fixture: ComponentFixture<QuestionCanvasDragPlaceComponent>;
    let emitted: string[];

    const el = () => fixture.nativeElement as HTMLElement;
    const cmp = () => fixture.componentInstance;
    const poolCard = (id: string) => el().querySelector(`[data-testid="dp-pool"] [data-card="${id}"]`) as HTMLElement | null;
    const target = (id: string) => el().querySelector(`[data-target-button][data-for="${id}"]`) as HTMLButtonElement;
    const zoneButton = (id: string) => el().querySelector(`[data-target="${id}"] .dp-zone__button`) as HTMLButtonElement;
    const placed = (t: string, c: string) => el().querySelector(`[data-placed="${t}:${c}"]`) as HTMLElement | null;
    const last = () => JSON.parse(emitted.at(-1)!).placements;
    const hint = () => el().querySelector('[data-testid="dp-hint"]')!.textContent!.trim();

    function setup(plan: object = PLACE_PLAN, inputs: {
        solution?: object | null;
        answer?: object | null;
        feedbackRows?: { id: string; correct: boolean }[] | null;
        showSolution?: boolean;
        practiceFeedback?: boolean;
    } = {}): void {
        TestBed.configureTestingModule({ imports: [QuestionCanvasDragPlaceComponent, translocoTestingModule()] });
        fixture = TestBed.createComponent(QuestionCanvasDragPlaceComponent);
        emitted = [];
        cmp().answerPayloadChange.subscribe((p) => emitted.push(p));
        fixture.componentRef.setInput('questionRegion', region());
        fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(plan));
        fixture.componentRef.setInput('interactionSolutionJson', inputs.solution ? JSON.stringify(inputs.solution) : null);
        fixture.componentRef.setInput('answerPayloadJson', inputs.answer ? JSON.stringify(inputs.answer) : null);
        if (inputs.feedbackRows !== undefined) fixture.componentRef.setInput('feedbackRows', inputs.feedbackRows);
        if (inputs.showSolution !== undefined) fixture.componentRef.setInput('showSolution', inputs.showSolution);
        if (inputs.practiceFeedback !== undefined) fixture.componentRef.setInput('practiceFeedback', inputs.practiceFeedback);
        fixture.componentRef.setInput('activityMode', true);
        fixture.detectChanges();
    }

    function tap(element: Element | null) {
        (element as HTMLElement).click();
        fixture.detectChanges();
    }

    function key(element: Element | null, k: string) {
        (element as HTMLElement).dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true }));
        fixture.detectChanges();
    }

    function drop(data: DragPlaceDragData, to: string, currentIndex = 0) {
        cmp().onDrop({ item: { data }, container: { data: to }, previousIndex: data.index ?? 0, currentIndex } as unknown as CdkDragDrop<string, string, DragPlaceDragData>);
        fixture.detectChanges();
    }

    it('rendersZoneOnImage_BucketsBelow_PoolWithTextAndCropCards', () => {
        setup();

        // Sahte görsel URL'si yüklenmez: görsele gerçek bir boyut verilir (alanlar görselle ölçeklenir).
        const img = el().querySelector('.dp-image') as HTMLImageElement;
        img.style.width = '400px';
        img.style.height = '200px';
        const zone = el().querySelector('[data-target="t1"]') as HTMLElement;
        expect(el().querySelector('[data-testid="dp-overlay"]')?.contains(zone)).toBe(true);
        expect(zone.style.left).toBe('5%');
        expect(zone.style.width).toBe('20%');
        // Görsel üstü hedef de tepside bir grup alır (kartları orada).
        expect(el().querySelectorAll('[data-testid="dp-buckets"] .dp-bucket').length).toBe(3);
        expect(target('t2').textContent).toContain('Geri dönüşüm');
        expect(target('t2').textContent).toContain('0/2');
        expect(poolCard('c1')!.textContent).toContain('Buz');
        // Kırpma kartı aynı soru görselini kaydırarak gösterir (yeniden deneme direktifi aynı URL'de çalışır).
        const crop = poolCard('c3')!.querySelector('[data-testid="image-crop"] img') as HTMLImageElement;
        expect(crop.getAttribute('src')).toBe('https://cdn.example.test/q1.webp');
        expect(crop.style.width).toBe('400%');
        expect(crop.style.left).toBe('-200%');
        expect(crop.style.top).toBe('-200%');
        expect(target('t1').getAttribute('aria-label')).toBe('Erime, 0/1 kart');
    });

    it('tapTap_PlacesCard_TapPlacedCardReturnsItToPool', () => {
        setup();

        tap(poolCard('c1'));
        expect(poolCard('c1')!.getAttribute('aria-pressed')).toBe('true');
        expect(hint()).toContain('Buz seçildi');
        tap(target('t1'));

        expect(last()).toEqual([{ targetId: 't1', cardId: 'c1' }]);
        expect(poolCard('c1')).toBeNull();
        expect(placed('t1', 'c1')).not.toBeNull();
        expect(hint()).toBe('Buz → Erime');

        tap(placed('t1', 'c1'));
        expect(last()).toEqual([]);
        expect(poolCard('c1')).not.toBeNull();
        expect(hint()).toBe('Buz havuza alındı.');
    });

    it('targetFirst_ThenCard_PlacesThere_TapTargetAgainCancels', () => {
        setup();
        tap(target('t2'));
        expect(emitted.length).toBe(0);
        expect(target('t2').getAttribute('aria-pressed')).toBe('true');
        expect(hint()).toContain('Geri dönüşüm seçildi');
        tap(poolCard('c1'));
        expect(last()).toEqual([{ targetId: 't2', cardId: 'c1' }]);
        expect(cmp().pendingTarget()).toBeNull();

        tap(zoneButton('t1'));
        expect(cmp().pendingTarget()).toBe('t1');
        tap(zoneButton('t1'));
        expect(cmp().pendingTarget()).toBeNull();
    });

    it('onImageZone_ShowsOnlyCompactSummary_CardsAndReturnInTray_NothingOverlaps (#576 E2E)', () => {
        const plan = {
            ...PLACE_PLAN,
            targets: [
                { id: 't1', label: 'Erime', box: { x: 10, y: 10, width: 30, height: 12 }, capacity: 3 },
                { id: 't2', label: 'Donma', box: { x: 50, y: 10, width: 30, height: 12 }, capacity: 1 },
            ],
        };
        setup(plan, {
            answer: {
                version: 1,
                type: 'dragPlace',
                placements: [{ targetId: 't1', cardId: 'c1' }, { targetId: 't1', cardId: 'c2' }, { targetId: 't2', cardId: 'c4' }],
            },
            feedbackRows: [{ id: 't1:c1', correct: true }, { id: 't1:c2', correct: false }, { id: 't2:c4', correct: true }],
        });

        // Sahte görsel URL'si yüklenmez: görsele gerçek bir boyut verilir (alanlar görselle ölçeklenir).
        const img = el().querySelector('.dp-image') as HTMLImageElement;
        img.style.width = '400px';
        img.style.height = '200px';
        const zone = el().querySelector('[data-target="t1"]') as HTMLElement;
        // Alan içinde kart / ikinci düğme yok: yalnız alan düğmesi ve etkileşimsiz özet.
        expect(zone.querySelectorAll('.dp-card, .dp-placed, [data-placed], [data-return]').length).toBe(0);
        expect(zone.querySelectorAll('button').length).toBe(1);
        expect(zone.querySelector('[data-zone-summary="t1"]')!.textContent).toContain('2/3');
        expect(zone.querySelector('[data-zone-state]')!.getAttribute('data-zone-state')).toBe('wrong');
        expect(getComputedStyle(zone).overflow).toBe('hidden');
        expect(zone.querySelector('.dp-zone__button')!.getAttribute('tabindex')).toBe('-1');
        const zoneRect = zone.getBoundingClientRect();
        const summaryRect = zone.querySelector('[data-zone-summary="t1"]')!.getBoundingClientRect();
        expect(summaryRect.height).toBeLessThanOrEqual(zoneRect.height + 0.5);
        // Tek kartlık alan kart adını kısaltarak gösterir, tamamı title'da.
        expect(el().querySelector('[data-zone-summary="t2"]')!.textContent).toContain('Cam');
        expect(zoneButton('t2').title).toBe('Donma: Cam');

        // Tepside hedef başına grup; yanlış kartın düğmesi tıklanabilir ve hiçbir öğenin altında değil.
        const group = el().querySelector('[data-group="t1"]')!;
        expect(group.querySelector('.dp-bucket__head')!.textContent).toContain('Erime');
        expect(group.querySelectorAll('[data-placed]').length).toBe(2);
        const ret = el().querySelector('[data-return="t1:c2"]') as HTMLButtonElement;
        const r = ret.getBoundingClientRect();
        expect(document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2)?.closest('[data-return]')).toBe(ret);
        tap(ret);
        expect(last()).toEqual([{ targetId: 't1', cardId: 'c1' }, { targetId: 't2', cardId: 'c4' }]);
    });

    it('keyboard_EnterPicksAndFocusesFirstTarget_ArrowsMove_EnterDrops_EscapeCancels', async () => {
        setup();

        key(poolCard('c2'), 'Enter');
        await Promise.resolve();
        expect(document.activeElement).toBe(target('t1'));
        key(target('t1'), 'ArrowDown');
        await Promise.resolve();
        expect(document.activeElement).toBe(target('t2'));
        tap(target('t2'));
        expect(last()).toEqual([{ targetId: 't2', cardId: 'c2' }]);

        key(poolCard('c4'), ' ');
        expect(cmp().selected()).toEqual({ cardId: 'c4' });
        key(el().querySelector('.dp-root'), 'Escape');
        expect(cmp().selected()).toBeNull();
        expect(hint()).toBe('Seçim kaldırıldı.');
    });

    it('capacity_SingleTargetSwapsOccupantToPool_FullBucketRejects', () => {
        setup();
        tap(poolCard('c1'));
        tap(target('t1'));
        tap(poolCard('c2'));
        tap(target('t1'));
        expect(last()).toEqual([{ targetId: 't1', cardId: 'c2' }]);
        expect(poolCard('c1')).not.toBeNull();

        tap(poolCard('c1'));
        tap(target('t2'));
        tap(poolCard('c4'));
        tap(target('t2'));
        const count = emitted.length;
        tap(poolCard('c3'));
        tap(target('t2'));
        expect(emitted.length).toBe(count);
        expect(hint()).toContain('Geri dönüşüm dolu');
        expect(target('t2').textContent).toContain('2/2');
    });

    it('reuse_CardStaysInPool_AndCanBePlacedInSeveralTargets', () => {
        setup({ ...PLACE_PLAN, reuse: true });
        tap(poolCard('c1'));
        tap(target('t2'));
        tap(poolCard('c1'));
        tap(target('t3'));

        expect(poolCard('c1')).not.toBeNull();
        expect(last()).toEqual([{ targetId: 't2', cardId: 'c1' }, { targetId: 't3', cardId: 'c1' }]);
    });

    it('cdkDrop_FromPoolToTarget_BetweenTargets_AndBackToPool', () => {
        setup();
        drop({ cardId: 'c2', from: 'pool' }, 't2');
        expect(last()).toEqual([{ targetId: 't2', cardId: 'c2' }]);
        drop({ cardId: 'c2', from: 't2' }, 't3');
        expect(last()).toEqual([{ targetId: 't3', cardId: 'c2' }]);
        drop({ cardId: 'c2', from: 't3' }, 'pool');
        expect(last()).toEqual([]);
        expect(cmp().canEnter({ data: { cardId: 'c1', from: 'pool' } } as never, { data: 't1' } as never)).toBe(true);
    });

    it('orderMode_TapAppends_MoveUpDown_Remove_AndDragSorts', () => {
        setup(ORDER_PLAN);
        expect(el().querySelector('[data-testid="dp-sequence"]')).not.toBeNull();
        tap(poolCard('c3'));
        tap(poolCard('c1'));
        expect(last()).toEqual([{ targetId: 't1', cardId: 'c3' }, { targetId: 't2', cardId: 'c1' }]);
        expect(el().querySelectorAll('.dp-seq-empty').length).toBe(1);

        tap(el().querySelector('[data-sequence-index="1"] [data-move="up"]'));
        expect(last()).toEqual([{ targetId: 't1', cardId: 'c1' }, { targetId: 't2', cardId: 'c3' }]);

        drop({ cardId: 'c2', from: 'pool' }, 'sequence', 0);
        expect(last().map((p: { cardId: string }) => p.cardId)).toEqual(['c2', 'c1', 'c3']);
        drop({ cardId: 'c3', from: 'sequence', index: 2 }, 'sequence', 0);
        expect(last().map((p: { cardId: string }) => p.cardId)).toEqual(['c3', 'c2', 'c1']);

        tap(el().querySelector('[data-sequence-index="0"] [data-testid="seq-remove"]'));
        expect(last()).toEqual([{ targetId: 't1', cardId: 'c2' }, { targetId: 't2', cardId: 'c1' }]);
    });

    it('feedback_MarksPairs_WrongCardCanBeReturned_IncompleteTargetFlagged', () => {
        setup(PLACE_PLAN, {
            answer: { version: 1, type: 'dragPlace', placements: [{ targetId: 't1', cardId: 'c1' }, { targetId: 't2', cardId: 'c4' }] },
            feedbackRows: [
                { id: 't1:c1', correct: true },
                { id: 't2:', correct: false },
                { id: 't2:c4', correct: false },
            ],
        });

        expect(placed('t1', 'c1')!.classList).toContain('is-correct');
        expect(el().querySelector('[data-mark="t1:c1"]')?.textContent).toContain('check');
        expect(placed('t2', 'c4')!.classList).toContain('is-wrong');
        expect(el().querySelector('[data-mark="t2:c4"]')?.textContent).toContain('close');
        expect(el().querySelector('[data-group="t2"]')!.classList).toContain('is-incomplete');
        expect(target('t2').getAttribute('aria-label')).toContain('eksik kart var');
        expect(el().querySelector('[data-target="t1"]')!.classList).not.toContain('is-incomplete');
        expect(el().querySelector('[data-target="t1"] [data-zone-state]')!.getAttribute('data-zone-state')).toBe('correct');
        expect(el().querySelector('[data-return="t1:c1"]')).toBeNull();

        tap(el().querySelector('[data-return="t2:c4"]'));
        expect(last()).toEqual([{ targetId: 't1', cardId: 'c1' }]);
        expect(poolCard('c4')).not.toBeNull();
    });

    it('practiceFeedback_MarksAgainstSolution_ShowSolutionIsReadOnly', () => {
        const solution = { t1: ['c1'], t2: ['c2', 'c4'], t3: [] };
        setup(PLACE_PLAN, {
            solution,
            practiceFeedback: true,
            answer: { version: 1, type: 'dragPlace', placements: [{ targetId: 't1', cardId: 'c2' }] },
        });
        expect(placed('t1', 'c2')!.classList).toContain('is-wrong');

        fixture.componentRef.setInput('showSolution', true);
        fixture.detectChanges();
        expect(placed('t1', 'c1')).not.toBeNull();
        expect(placed('t2', 'c4')).not.toBeNull();
        expect(poolCard('c3')).not.toBeNull();
        expect(target('t1').disabled).toBe(true);
        tap(placed('t1', 'c1'));
        expect(emitted.length).toBe(0);
        expect(hint()).toBe('Doğru yerleşim gösteriliyor.');

        fixture.componentRef.setInput('showSolution', false);
        fixture.detectChanges();
        expect(placed('t1', 'c2')).not.toBeNull();
    });

    it('restore_IgnoresUnknownAndOverCapacity_ParentEchoKeepsScreen_ClearEmptiesAll', () => {
        setup(PLACE_PLAN, {
            answer: {
                version: 1,
                type: 'dragPlace',
                placements: [
                    { targetId: 't1', cardId: 'c1' },
                    { targetId: 't1', cardId: 'c2' },
                    { targetId: 'tx', cardId: 'c3' },
                    { targetId: 't2', cardId: 'c1' },
                    { targetId: 'constructor', cardId: 'c4' },
                ],
            },
        });
        expect(cmp().placed()).toEqual(Object.assign(Object.create(null), { t1: ['c1'] }));

        tap(poolCard('c2'));
        tap(target('t2'));
        const payload = emitted.at(-1)!;
        fixture.componentRef.setInput('answerPayloadJson', payload);
        fixture.detectChanges();
        expect(placed('t2', 'c2')).not.toBeNull();

        tap(el().querySelector('.dp-clear'));
        expect(last()).toEqual([]);
        expect(el().querySelectorAll('[data-testid="dp-pool"] .dp-card').length).toBe(4);
    });

    it('reuseSwap_OccupantAlreadyInSource_ReturnsToPoolInsteadOfDuplicate (#576 review)', () => {
        setup({ ...PLACE_PLAN, reuse: true, targets: [{ id: 't1', label: 'A', capacity: 2 }, { id: 't2', label: 'B', capacity: 1 }] }, {
            answer: {
                version: 1,
                type: 'dragPlace',
                placements: [{ targetId: 't1', cardId: 'c1' }, { targetId: 't1', cardId: 'c2' }, { targetId: 't2', cardId: 'c2' }],
            },
        });
        // t1'den c1'i tek kartlık t2'ye taşı: t2'deki c2 t1'de zaten var → t1'e ikinci kez girmez (havuza döner).
        drop({ cardId: 'c1', from: 't1' }, 't2');
        expect(last()).toEqual([{ targetId: 't1', cardId: 'c2' }, { targetId: 't2', cardId: 'c1' }]);
    });

    it('orderMode_KeyboardMoveKeepsFocusOnTheMovedRow (#576 review)', async () => {
        setup(ORDER_PLAN);
        tap(poolCard('c1'));
        tap(poolCard('c2'));
        tap(poolCard('c3'));
        const down = () => el().querySelector('[data-sequence-index="0"] [data-move="down"]') as HTMLButtonElement;
        const li = el().querySelector('[data-card="c1"].dp-seq-item');
        down().focus();
        down().click();
        fixture.detectChanges();
        await fixture.whenStable();

        expect(last().map((p: { cardId: string }) => p.cardId)).toEqual(['c2', 'c1', 'c3']);
        // Aynı <li> (yeniden kurulmadı) ve odak taşınan satırın "aşağı" düğmesinde.
        expect(el().querySelector('[data-card="c1"].dp-seq-item')).toBe(li);
        expect(document.activeElement).toBe(el().querySelector('[data-sequence-index="1"] [data-move="down"]'));
    });

    it('pool_IsShuffledStablyBySeed_PlanOrderNotShown (#576 review)', () => {
        const many = { ...PLACE_PLAN, cards: Array.from({ length: 10 }, (_, i) => ({ id: `c${i + 1}`, label: `K${i + 1}` })) };
        setup(many);
        fixture.componentRef.setInput('shuffleSeed', 101);
        fixture.detectChanges();
        const order = () => Array.from(el().querySelectorAll('[data-testid="dp-pool"] [data-card]')).map((c) => c.getAttribute('data-card'));
        const first = order();
        expect(first).not.toEqual(many.cards.map((c) => c.id));
        expect([...first].sort()).toEqual(many.cards.map((c) => c.id).sort());
        tap(poolCard(first[0]!));
        tap(target('t2'));
        expect(order()).toEqual(first.slice(1));
        fixture.componentRef.setInput('shuffleSeed', 101);
        fixture.detectChanges();
        expect(order()).toEqual(first.slice(1));
    });

    it('disabled_BlocksTapDropAndClear_DropsSelection (#576 review)', () => {
        setup();
        tap(poolCard('c1'));
        fixture.componentRef.setInput('disabled', true);
        fixture.detectChanges();
        expect(cmp().selected()).toBeNull();
        tap(poolCard('c2'));
        tap(target('t2'));
        drop({ cardId: 'c2', from: 'pool' }, 't2');
        expect(emitted.length).toBe(0);
        expect(target('t1').disabled).toBe(true);
        expect(poolCard('c1')!.getAttribute('aria-disabled')).toBe('true');
    });

    it('returnButton_IsSiblingOfTheCardButton_NotNested (#576 review)', () => {
        setup(PLACE_PLAN, {
            answer: { version: 1, type: 'dragPlace', placements: [{ targetId: 't2', cardId: 'c4' }] },
            feedbackRows: [{ id: 't2:c4', correct: false }],
        });
        const button = el().querySelector('[data-return="t2:c4"]')!;
        expect(button.closest('[role="button"]')).toBeNull();
        expect(placed('t2', 'c4')!.querySelector('button')).toBeNull();
    });
});

