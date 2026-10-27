import type { Mock, MockedObject } from 'vitest';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { Subject, of, throwError } from 'rxjs';

import { CanvasActivityDrafts } from './canvas-activity-drafts';
import { ImageSelectorComponent } from '../image-selector/image-selector.component';
import { LabelingActivityStructureResult } from '../../models/labeling';
import { LabelingService } from '../../services/labeling.service';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import questionTr from '../../../../public/i18n/question/tr.json';

const translocoTesting = TranslocoTestingModule.forRoot({
    langs: { 'question/tr': questionTr },
    translocoConfig: {
        availableLangs: [...SUPPORTED_LOCALE_CODES],
        defaultLang: DEFAULT_LOCALE,
        scopes: { keepCasing: true },
    },
    preloadLangs: true,
});

const PAGE = `data:image/png;base64,${btoa('page')}`;

interface Block {
    name: string;
    kind?: string;
    type?: string;
}

/** Sırayla: MCQ, A (matris), B (kutu), C (sürükle), tipsiz etkinlik D. */
const MIXED: Block[] = [
    { name: 'Soru 1' },
    { name: 'Soru 2', kind: 'activity', type: 'matrixChoice' },
    { name: 'Soru 3', kind: 'activity', type: 'slotFill' },
    { name: 'Soru 4', kind: 'activity', type: 'dragPlace' },
    { name: 'Soru 5', kind: 'activity' },
];

function result(key: string, interactionType: string, overrides: Partial<LabelingActivityStructureResult> = {}) {
    return {
        key,
        interactionType,
        status: 'ok',
        origin: 'cached',
        plan: { version: 1, type: interactionType },
        instruction: null,
        valid: true,
        validationErrors: [],
        promptVersion: 2,
        inputTokens: 100,
        outputTokens: 50,
        estimatedCostUsd: 0.0012,
        error: null,
        ...overrides,
    } as LabelingActivityStructureResult;
}

/** image-selector'ın taslak akışının okuduğu yüzey. */
function makeSelector() {
    const kinds: Record<string, string> = {};
    const types: Record<string, string> = {};
    return {
        kinds,
        types,
        regions: signal<Array<{ name: string; x: number; y: number; width: number; height: number }>>([]),
        detectionRevision: signal(0),
        inProgress: signal(false),
        imageData: signal<string | null>(PAGE),
        getBlockKind: (name: string) => kinds[name] ?? 'mcq_question',
        getInteractionType: (name: string) => types[name] ?? 'mcq',
        applyInteractionDraft: vi.fn().mockName('applyInteractionDraft').mockReturnValue('applied'),
    };
}

async function settle(): Promise<void> {
    for (let i = 0; i < 5; i++) {
        TestBed.tick();
        await new Promise<void>((resolve) => setTimeout(resolve, 0));
    }
}

describe('CanvasActivityDrafts (#581)', () => {
    let labelingApi: MockedObject<LabelingService>;
    let snackBar: MockedObject<MatSnackBar>;
    let structure: Mock<LabelingService['activityStructure']>;
    let selector: ReturnType<typeof makeSelector>;
    let enabled: ReturnType<typeof signal<boolean>>;
    let drafts: CanvasActivityDrafts;

    function echoOk() {
        structure.mockImplementation((request) =>
            of({ results: request.blocks.map((b) => result(b.key, b.interactionType)) })
        );
    }

    async function detect(blocks: Block[] = MIXED) {
        for (const key of Object.keys(selector.kinds)) delete selector.kinds[key];
        for (const key of Object.keys(selector.types)) delete selector.types[key];
        blocks.forEach((b) => {
            if (b.kind) selector.kinds[b.name] = b.kind;
            if (b.type) selector.types[b.name] = b.type;
        });
        selector.regions.set(blocks.map((b, i) => ({ name: b.name, x: 10.4, y: i * 100, width: 200, height: 80.6 })));
        selector.detectionRevision.update((n) => n + 1);
        await settle();
    }

    async function changeType(name: string, type: string) {
        selector.types[name] = type;
        selector.regions.set([...selector.regions()]);
        await settle();
    }

    beforeEach(() => {
        vi.spyOn(console, 'error').mockReturnValue(undefined);
        structure = vi.fn().mockName('LabelingService.activityStructure');
        labelingApi = { activityStructure: structure } as unknown as MockedObject<LabelingService>;
        snackBar = { open: vi.fn().mockName('MatSnackBar.open') } as unknown as MockedObject<MatSnackBar>;
        snackBar.open.mockReturnValue({ onAction: () => of(undefined) } as never);
        selector = makeSelector();
        enabled = signal(true);

        TestBed.configureTestingModule({
            imports: [translocoTesting],
            providers: [
                CanvasActivityDrafts,
                { provide: LabelingService, useValue: labelingApi },
                { provide: MatSnackBar, useValue: snackBar },
            ],
        });
        drafts = TestBed.inject(CanvasActivityDrafts);
        drafts.debounceMs = 0;
        drafts.attach({
            selector: () => selector as unknown as ImageSelectorComponent,
            enabled: () => enabled(),
            locked: () => false,
            model: () => 'claude-a',
            scope: 'question',
        });
    });

    it('activityContext_TypedBlocks_OneBatchRequestWithRoundedBoxesAndForceFalse', async () => {
        echoOk();
        await detect();

        expect(structure).toHaveBeenCalledTimes(1);
        const request = structure.mock.calls[0][0];
        expect(request).toEqual(expect.objectContaining({ imageBase64: PAGE, model: 'claude-a', force: false }));
        expect(request.blocks).toEqual([
            { key: 'Soru 2', x: 10, y: 100, width: 200, height: 81, interactionType: 'matrixChoice' },
            { key: 'Soru 3', x: 10, y: 200, width: 200, height: 81, interactionType: 'slotFill' },
            { key: 'Soru 4', x: 10, y: 300, width: 200, height: 81, interactionType: 'dragPlace' },
        ]);
        expect(selector.applyInteractionDraft).toHaveBeenCalledWith('Soru 2', 'matrixChoice', { version: 1, type: 'matrixChoice' });
        expect(drafts.draftOf('Soru 2')).toEqual(expect.objectContaining({ status: 'ok', apply: 'applied' }));
        expect(drafts.draftOf('Soru 5')).toBeNull();

        // Aynı bölgeler yeniden yayılınca ikinci istek yok.
        selector.regions.set([...selector.regions()]);
        await settle();
        expect(structure).toHaveBeenCalledTimes(1);
    });

    it('disabled_NoRequest', async () => {
        echoOk();
        enabled.set(false);

        await detect();

        expect(structure).not.toHaveBeenCalled();
    });

    it('analysisInProgress_WaitsThenRequests', async () => {
        echoOk();
        selector.inProgress.set(true);
        await detect();
        expect(structure).not.toHaveBeenCalled();

        selector.inProgress.set(false);
        await settle();

        expect(structure).toHaveBeenCalledTimes(1);
    });

    it('teacherEdits_KeptThenApplyDraftReplacesAfterConfirm', async () => {
        echoOk();
        selector.applyInteractionDraft.mockImplementation((_k: string, _t: string, _p: unknown, options?: { replace?: boolean }) =>
            options?.replace ? 'applied' : 'kept'
        );
        await detect();
        expect(drafts.draftOf('Soru 3')!.apply).toBe('kept');

        drafts.applyDraft('Soru 3');

        expect(selector.applyInteractionDraft).toHaveBeenLastCalledWith('Soru 3', 'slotFill', { version: 1, type: 'slotFill' }, { replace: true });
        expect(drafts.draftOf('Soru 3')!.apply).toBe('applied');
    });

    it('typeChange_ReRequestsOnlyThatBlock', async () => {
        echoOk();
        await detect();

        await changeType('Soru 4', 'match');

        expect(structure).toHaveBeenCalledTimes(2);
        expect(structure.mock.calls[1][0].blocks).toEqual([
            expect.objectContaining({ key: 'Soru 4', interactionType: 'match' }),
        ]);
        expect(drafts.draftOf('Soru 4')).toEqual(expect.objectContaining({ status: 'ok', interactionType: 'match' }));
    });

    it('boxMoved_StaleThenReRequested', async () => {
        echoOk();
        await detect();

        selector.regions.set(selector.regions().map((r) => (r.name === 'Soru 3' ? { ...r, width: 250 } : r)));
        TestBed.tick();
        expect(drafts.draftOf('Soru 3')!.stale).toBe(true);
        await settle();

        expect(structure).toHaveBeenCalledTimes(2);
        expect(structure.mock.calls[1][0].blocks.map((b) => b.key)).toEqual(['Soru 3']);
        expect(drafts.draftOf('Soru 3')).toEqual(expect.objectContaining({ status: 'ok', stale: false }));
    });

    it('error429_MarksErrorWithServerMessage_RetryUsesCache', async () => {
        structure.mockReturnValueOnce(throwError(() => ({ status: 429, error: { message: 'Günlük Claude bütçesi doldu.' } })));
        await detect();

        expect(drafts.draftOf('Soru 2')).toEqual(expect.objectContaining({ status: 'error', error: 'Günlük Claude bütçesi doldu.' }));
        expect(snackBar.open).toHaveBeenCalledWith('Günlük Claude bütçesi doldu.', expect.any(String), expect.any(Object));

        echoOk();
        drafts.retry('Soru 2');

        expect(structure.mock.calls[1][0]).toEqual(expect.objectContaining({ force: false }));
        expect(drafts.draftOf('Soru 2')!.status).toBe('ok');
    });

    it('reExtract_AfterConfirm_ForceTrue', async () => {
        echoOk();
        await detect();

        drafts.reExtract('Soru 2');

        expect(snackBar.open).toHaveBeenCalledWith(questionTr.activityDraft.reExtractConfirm, questionTr.activityDraft.reExtractAction, expect.any(Object));
        expect(structure.mock.calls[1][0]).toEqual(expect.objectContaining({ force: true }));
    });

    it('resetPage_ClearsDraftsAndIgnoresLateResponse', async () => {
        const pending = new Subject<{ results: LabelingActivityStructureResult[] }>();
        structure.mockReturnValue(pending.asObservable());
        await detect();
        expect(drafts.draftOf('Soru 2')!.status).toBe('loading');

        drafts.resetPage();
        pending.next({ results: [result('Soru 2', 'matrixChoice')] });

        expect(drafts.drafts()).toEqual({});
        expect(selector.applyInteractionDraft).not.toHaveBeenCalled();
    });
});
