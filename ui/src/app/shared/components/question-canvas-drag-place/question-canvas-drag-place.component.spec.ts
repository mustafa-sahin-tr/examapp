// Issue #576: sürükle-yerleştir oynatıcısı — dokun-dokun, klavye, kapasite, tekrar kullanım, sıra modu, cdk bırakma,
// kontrol işaretleri (yanlış kart ✗ + havuza geri al, eksik hedef "t:"), çözüm gösterimi ve yankı koruması.
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CdkDrag, CdkDragDrop } from '@angular/cdk/drag-drop';
import { By } from '@angular/platform-browser';

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

    function drop(data: DragPlaceDragData, to: string) {
        cmp().onDrop({ item: { data }, container: { data: to }, previousIndex: 0, currentIndex: 0 } as unknown as CdkDragDrop<string, string, DragPlaceDragData>);
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

    it('placedCardDrag_IsRegisteredWithItsTargetList_NotFreeDrag (gezinti: sürüklenen kart kayboluyordu)', () => {
        setup();
        drop({ cardId: 'c2', from: 'pool' }, 't2');
        drop({ cardId: 'c4', from: 'pool' }, 't3');
        const containers = fixture.debugElement
            .queryAll(By.css('.dp-placed'))
            .map((d) => d.injector.get(CdkDrag).dropContainer?.data);
        // Kart kendi hedefinin listesine bağlı: CDK bırakmada onDrop'u çağırır ve kartı yerine oturtur (serbest sürükleme değil).
        expect(containers).toEqual(['t2', 't3']);
    });

    it('orderMode_SlotsKeepGaps_TapFillsFirstFree_SlotFirstPlacesThere_DropAndMoveSwap', () => {
        setup(ORDER_PLAN);
        const slots = () => cmp().slots();
        expect(el().querySelectorAll('.dp-slot').length).toBe(3);

        // Dokununca ilk boş yuvaya.
        tap(poolCard('c3'));
        tap(poolCard('c1'));
        expect(last()).toEqual([{ targetId: 't1', cardId: 'c3' }, { targetId: 't2', cardId: 'c1' }]);
        expect(el().querySelectorAll('.dp-slot.is-empty').length).toBe(1);

        // Yukarı: komşuyla yer değiştirir.
        tap(el().querySelector('[data-sequence-index="1"] [data-move="up"]'));
        expect(slots()).toEqual(['c1', 'c3', null]);

        // Havuzdan istenen yuvaya sürükle; yuvadan dolu yuvaya sürükle = yer değiştir.
        drop({ cardId: 'c2', from: 'pool' }, 't3');
        expect(slots()).toEqual(['c1', 'c3', 'c2']);
        drop({ cardId: 'c2', from: 't3' }, 't1');
        expect(slots()).toEqual(['c2', 'c3', 'c1']);

        // Havuza almak boşluk bırakır (diğerleri kaymaz).
        tap(el().querySelector('[data-sequence-index="0"] [data-testid="seq-remove"]'));
        expect(slots()).toEqual([null, 'c3', 'c1']);
        expect(last()).toEqual([{ targetId: 't2', cardId: 'c3' }, { targetId: 't3', cardId: 'c1' }]);
    });

    it('orderMode_PickEmptySlotThenCard_PutsCardStraightIntoThatPosition', () => {
        setup(ORDER_PLAN);
        tap(target('t3'));
        expect(el().querySelector('[data-sequence-index="2"]')?.classList).toContain('is-pending');
        tap(poolCard('c1'));
        expect(cmp().slots()).toEqual([null, null, 'c1']);

        // Önce boş yuva, sonra başka yuvadaki kart: kart oraya taşınır.
        tap(target('t1'));
        tap(el().querySelector('[data-card="c1"].dp-seq-item'));
        expect(cmp().slots()).toEqual(['c1', null, null]);
    });

    it('orderMode_PlacedCardDrag_IsRegisteredWithItsSlot', () => {
        setup(ORDER_PLAN, { answer: { version: 1, type: 'dragPlace', placements: [{ targetId: 't2', cardId: 'c2' }] } });
        const slotCard = fixture.debugElement.query(By.css('.dp-seq-item'));
        expect(slotCard.injector.get(CdkDrag).dropContainer?.data).toBe('t2');
    });

    it('orderMode_StaleIncompleteNotShownOnFilledSlot_EmptySlotFlagged', () => {
        setup(ORDER_PLAN, {
            answer: { version: 1, type: 'dragPlace', placements: [{ targetId: 't1', cardId: 'c2' }] },
            feedbackRows: [
                { id: 't1:c2', correct: false },
                { id: 't1:', correct: false },
                { id: 't2:', correct: false },
            ],
        });
        // Dolu yuvada yanlış kart ✗; "eksik" yalnız boş yuvada.
        expect(el().querySelector('[data-sequence-index="0"] .dp-seq-item')?.classList).toContain('is-wrong');
        expect(el().querySelector('[data-incomplete="t1"]')).toBeNull();
        expect(el().querySelector('[data-incomplete="t2"]')).not.toBeNull();
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

    it('orderMode_KeyboardMoveKeepsFocusOnTheMovedCard (#576 review)', async () => {
        setup(ORDER_PLAN);
        tap(poolCard('c1'));
        tap(poolCard('c2'));
        tap(poolCard('c3'));
        const down = () => el().querySelector('[data-sequence-index="0"] [data-move="down"]') as HTMLButtonElement;
        down().focus();
        down().click();
        fixture.detectChanges();
        await fixture.whenStable();

        expect(last().map((p: { cardId: string }) => p.cardId)).toEqual(['c2', 'c1', 'c3']);
        // Odak taşınan kartın yeni yuvasındaki "aşağı" düğmesinde.
        expect(document.activeElement).toBe(el().querySelector('[data-sequence-index="1"] [data-move="down"]'));
        expect(el().querySelector('[data-sequence-index="1"] [data-card="c1"]')).not.toBeNull();
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


// Issue #590 (dilim b): boşluklu metin + kelime havuzu.
describe('QuestionCanvasDragPlaceComponent boşluklu metin (#590)', () => {
    let fixture: ComponentFixture<QuestionCanvasDragPlaceComponent>;
    let emitted: string[];
    const el = () => fixture.nativeElement as HTMLElement;
    const poolCard = (id: string) => el().querySelector(`[data-testid="dp-pool"] [data-card="${id}"]`) as HTMLElement | null;
    const blank = (id: string) => el().querySelector(`[data-target-button][data-for="${id}"]`) as HTMLButtonElement | null;

    const CLOZE_PLAN = {
        version: 1,
        type: 'dragPlace',
        mode: 'place',
        reuse: false,
        layout: { kind: 'text', text: 'Su [[t1]] °C\'de donar, [[t2]] °C\'de kaynar.' },
        targets: [{ id: 't1', capacity: 1 }, { id: 't2', capacity: 1 }],
        cards: [{ id: 'k1', label: '0' }, { id: 'k2', label: '100' }, { id: 'k3', label: '50' }],
    };

    function setup(extra: { feedbackRows?: object[]; answer?: object } = {}): void {
        TestBed.configureTestingModule({ imports: [QuestionCanvasDragPlaceComponent, translocoTestingModule()] });
        fixture = TestBed.createComponent(QuestionCanvasDragPlaceComponent);
        emitted = [];
        fixture.componentInstance.answerPayloadChange.subscribe((p) => emitted.push(p));
        fixture.componentRef.setInput('questionRegion', { imageUrl: '', width: 0, height: 0, passage: null } as unknown as QuestionRegion);
        fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(CLOZE_PLAN));
        if (extra.answer) fixture.componentRef.setInput('answerPayloadJson', JSON.stringify(extra.answer));
        if (extra.feedbackRows) fixture.componentRef.setInput('feedbackRows', extra.feedbackRows);
        fixture.detectChanges();
    }

    it('placed word in a blank is registered with the blank list', () => {
        setup({ answer: { version: 1, type: 'dragPlace', placements: [{ targetId: 't2', cardId: 'k2' }] } });
        const placed = fixture.debugElement.query(By.css('.dp-placed'));
        expect(placed.injector.get(CdkDrag).dropContainer?.data).toBe('t2');
    });

    it('renders blanks inside the text instead of buckets', () => {
        setup();
        expect(el().querySelector('[data-testid="dp-cloze"]')!.textContent).toContain('°C\'de donar');
        expect(el().querySelector('[data-testid="dp-buckets"]')).toBeNull();
        expect(blank('t1')!.textContent).toContain('1');
        expect(blank('t2')!.getAttribute('aria-label')).toBeTruthy();
    });

    it('tap card then blank places the word inline; tapping the placed word returns it', () => {
        setup();
        poolCard('k2')!.click();
        fixture.detectChanges();
        blank('t2')!.click();
        fixture.detectChanges();

        expect(JSON.parse(emitted.at(-1)!).placements).toEqual([{ targetId: 't2', cardId: 'k2' }]);
        expect(blank('t2')).toBeNull();
        const placedWord = el().querySelector('[data-placed="t2:k2"]') as HTMLElement;
        expect(placedWord.closest('[data-testid="dp-cloze"]')).not.toBeNull();
        expect(poolCard('k2')).toBeNull();

        placedWord.click();
        fixture.detectChanges();
        expect(poolCard('k2')).not.toBeNull();
    });

    it('feedback marks the inline word', () => {
        setup({
            answer: { version: 1, type: 'dragPlace', placements: [{ targetId: 't1', cardId: 'k3' }] },
            feedbackRows: [{ id: 't1:k3', correct: false }],
        });
        expect(el().querySelector('[data-placed="t1:k3"]')!.classList).toContain('is-wrong');
    });
});
