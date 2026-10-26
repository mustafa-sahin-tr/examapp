// #545 — Angular 21 control-flow migrasyonu (@if görsel/pasaj/plan, @for bölge ve parça listeleri) sonrası davranış sözleşmesi.
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { QuestionCanvasDragDropLabelingComponent } from './question-canvas-dragdrop-labeling.component';
import { QuestionRegion } from '../../../models/draws';
import { translocoTestingModule } from '../../testing/transloco-testing';

const PLAN = {
    version: 1,
    type: 'dragDropLabeling',
    dropZones: [
        { id: 'z1', x: 0, y: 0, width: 50, height: 20 },
        { id: 'z2', x: 60, y: 0, width: 50, height: 20 },
    ],
    draggables: [
        { id: 'd1', label: 'Kalp' },
        { id: 'd2', label: 'Beyin' },
    ],
};

// issue #570: çözüm plandan ayrı girdi (`interactionSolutionJson`).
const SOLUTION = {
    placements: [
        { draggableId: 'd1', dropZoneId: 'z1' },
        { draggableId: 'd2', dropZoneId: 'z2' },
    ],
};

function region(overrides: Record<string, unknown> = {}): QuestionRegion {
    return {
        imageUrl: 'https://cdn.example.test/q1.webp',
        width: 200,
        height: 100,
        passage: null,
        ...overrides,
    } as unknown as QuestionRegion;
}

describe('QuestionCanvasDragDropLabelingComponent (#545)', () => {
    let fixture: ComponentFixture<QuestionCanvasDragDropLabelingComponent>;

    const el = () => fixture.nativeElement as HTMLElement;
    const zones = () => Array.from(el().querySelectorAll('.dd-zone')) as HTMLElement[];
    const poolChips = () => Array.from(el().querySelectorAll('.dd-pool__list .dd-chip')).map((c) => c.textContent?.trim());

    function setup(inputs: {
        region?: QuestionRegion;
        plan?: string | null;
        answer?: string | null;
        solution?: string | null;
        practiceFeedback?: boolean;
        hidePassage?: boolean;
    }): void {
        TestBed.configureTestingModule({
            imports: [QuestionCanvasDragDropLabelingComponent, NoopAnimationsModule, translocoTestingModule()],
        });
        fixture = TestBed.createComponent(QuestionCanvasDragDropLabelingComponent);
        fixture.componentRef.setInput('questionRegion', inputs.region ?? region());
        fixture.componentRef.setInput('interactionPlanJson', inputs.plan === undefined ? JSON.stringify(PLAN) : inputs.plan);
        fixture.componentRef.setInput('answerPayloadJson', inputs.answer ?? null);
        fixture.componentRef.setInput('interactionSolutionJson', inputs.solution === undefined ? JSON.stringify(SOLUTION) : inputs.solution);
        fixture.componentRef.setInput('practiceFeedback', inputs.practiceFeedback ?? false);
        fixture.componentRef.setInput('hidePassage', inputs.hidePassage ?? false);
        fixture.detectChanges();
    }

    it('template_ImageAndPlan_RendersZonesWithSlotsAndPoolChips', () => {
        setup({});

        expect(el().querySelector('.dd-image')).not.toBeNull();
        expect(zones().length).toBe(2);
        expect(el().querySelectorAll('.dd-zone__slot').length).toBe(2);
        expect(poolChips()).toEqual(['Kalp', 'Beyin']);
    });

    it('template_ZoneStyle_ScalesPlanCoordinatesToRegionSize', () => {
        setup({});

        const z2 = el().querySelector('#zone-z2') as HTMLElement;
        expect(z2.style.left).toBe('30%');
        expect(z2.style.width).toBe('25%');
        expect(z2.style.height).toBe('20%');
    });

    it('template_NoImageUrl_RendersNoImageOrOverlay', () => {
        setup({ region: region({ imageUrl: null }) });

        expect(el().querySelector('.dd-image')).toBeNull();
        expect(el().querySelector('.dd-overlay')).toBeNull();
    });

    it('template_InvalidPlanJson_RendersNoOverlayAndEmptyPool', () => {
        setup({ plan: '{"type":"other"}' });

        expect(el().querySelector('.dd-image')).not.toBeNull();
        expect(el().querySelector('.dd-overlay')).toBeNull();
        expect(poolChips()).toEqual([]);
    });

    it('template_SavedPlacement_FillsZoneAndRemovesItemFromPool', () => {
        setup({
            answer: JSON.stringify({ version: 1, type: 'dragDropLabeling', placements: [{ dropZoneId: 'z1', draggableId: 'd1' }] }),
        });

        const z1 = el().querySelector('#zone-z1') as HTMLElement;
        expect(z1.querySelector('.dd-zone__slot')).toBeNull();
        expect(z1.querySelector('.dd-chip')?.textContent?.trim()).toBe('Kalp');
        expect(el().querySelector('#zone-z2 .dd-zone__slot')).not.toBeNull();
        expect(poolChips()).toEqual(['Beyin']);
    });

    it('template_PracticeFeedback_MarksCorrectZoneAndLeavesEmptyZoneUnmarked', () => {
        setup({
            practiceFeedback: true,
            answer: JSON.stringify({ version: 1, type: 'dragDropLabeling', placements: [{ dropZoneId: 'z1', draggableId: 'd1' }] }),
        });

        expect(el().querySelector('#zone-z1')?.classList.contains('is-correct')).toBe(true);
        expect(el().querySelector('#zone-z2')?.classList.contains('is-correct')).toBe(false);
        expect(el().querySelector('#zone-z2')?.classList.contains('is-incorrect')).toBe(false);
    });

    const SWAPPED = JSON.stringify({
        version: 1,
        type: 'dragDropLabeling',
        placements: [
            { dropZoneId: 'z1', draggableId: 'd2' },
            { dropZoneId: 'z2', draggableId: 'd1' },
        ],
    });
    const marked = () => zones().filter((z) => z.classList.contains('is-correct') || z.classList.contains('is-incorrect'));

    it('template_PracticeFeedbackWithSolution_MarksWrongPlacementsIncorrect', () => {
        setup({ practiceFeedback: true, answer: SWAPPED });

        expect(el().querySelector('#zone-z1')?.classList.contains('is-incorrect')).toBe(true);
        expect(el().querySelector('#zone-z2')?.classList.contains('is-incorrect')).toBe(true);
    });

    // issue #570: istemci tarafı kontrol yalnız örnek sorularda; çözüm gelse bile practiceFeedback kapalıysa işaret yok.
    it('template_NoPracticeFeedback_NoMarksEvenWithSolution', () => {
        setup({ practiceFeedback: false, answer: SWAPPED });

        expect(marked()).toEqual([]);
    });

    it('template_PracticeFeedbackWithoutSolution_NoMarks', () => {
        setup({ practiceFeedback: true, solution: null, answer: SWAPPED });

        expect(marked()).toEqual([]);
    });

    // Plan içinde eski `solution` kalsa bile kaynak değildir (yalnız ayrı girdi).
    it('template_LegacySolutionInsidePlan_IsIgnored', () => {
        setup({ practiceFeedback: true, solution: null, plan: JSON.stringify({ ...PLAN, solution: SOLUTION }), answer: SWAPPED });

        expect(marked()).toEqual([]);
    });

    it('template_MalformedSolutionJson_NoMarksAndNoError', () => {
        setup({ practiceFeedback: true, solution: '{not json', answer: SWAPPED });

        expect(marked()).toEqual([]);
        expect(zones().length).toBe(2);
    });

    it('template_PassageWithTitle_ShowsPassageUnlessHidden', () => {
        const withPassage = region({ passage: { title: 'Pasaj 1', imageUrl: 'https://cdn.example.test/p1.webp' } });

        setup({ region: withPassage });
        expect(el().querySelector('.dd-passage__title')?.textContent?.trim()).toBe('Pasaj 1');
        expect(el().querySelector('.dd-passage__image')).not.toBeNull();
    });

    it('template_PassageHiddenByInput_DoesNotRenderPassage', () => {
        const withPassage = region({ passage: { title: 'Pasaj 1', imageUrl: 'https://cdn.example.test/p1.webp' } });

        setup({ region: withPassage, hidePassage: true });

        expect(el().querySelector('.dd-passage')).toBeNull();
    });

    // "Temizle" girdi olarak gelen eski cevabı yeniden uygulamamalı (#545 sırasında bulundu).
    it('clearAll_AfterPlacement_ResetsPoolAndEmitsEmptyPayload', () => {
        setup({
            answer: JSON.stringify({ version: 1, type: 'dragDropLabeling', placements: [{ dropZoneId: 'z1', draggableId: 'd1' }] }),
        });
        const emitted: string[] = [];
        fixture.componentInstance.answerPayloadChange.subscribe((p: string) => emitted.push(p));

        fixture.componentInstance.clearAll();
        fixture.detectChanges();

        expect(poolChips()).toEqual(['Kalp', 'Beyin']);
        expect(el().querySelectorAll('.dd-zone__slot').length).toBe(2);
        expect(JSON.parse(emitted[0])).toEqual({ version: 1, type: 'dragDropLabeling', placements: [] });
    });
});
