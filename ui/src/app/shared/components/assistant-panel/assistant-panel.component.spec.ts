import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { MockedObject } from 'vitest';
import { AssistantCard, AssistantUsage, PanelMessage, SuggestionChip } from '../../../models/assistant';
import { AssistantChatStore } from '../../../services/assistant-chat.store';
import { AssistantPanelService } from '../../../services/assistant-panel.service';
import { AuthService } from '../../../services/auth.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import assistantTr from '../../../../../public/i18n/assistant/tr.json';
import { AssistantPanelComponent, segmentsOf } from './assistant-panel.component';

function message(partial: Partial<PanelMessage>): PanelMessage {
    return {
        id: 1,
        role: 'assistant',
        text: '',
        cards: [],
        confirmStates: {},
        streaming: false,
        messageId: null,
        errorCode: null,
        feedback: null,
        activeTool: null,
        ...partial,
    };
}

describe('AssistantPanelComponent (#507)', () => {
    let fixture: ComponentFixture<AssistantPanelComponent>;
    let store: MockedObject<AssistantChatStore> & {
        messages: ReturnType<typeof signal<PanelMessage[]>>;
        suggestions: ReturnType<typeof signal<SuggestionChip[]>>;
        usage: ReturnType<typeof signal<AssistantUsage | null>>;
        sending: ReturnType<typeof signal<boolean>>;
        navigated: ReturnType<typeof signal<number>>;
    };
    let panel: MockedObject<AssistantPanelService>;

    const el = (selector: string): HTMLElement | null =>
        fixture.debugElement.query(By.css(selector))?.nativeElement ?? null;
    const all = (selector: string): HTMLElement[] =>
        fixture.debugElement.queryAll(By.css(selector)).map((d) => d.nativeElement as HTMLElement);

    function setup(mobile = false): void {
        store = {
            messages: signal<PanelMessage[]>([]),
            suggestions: signal<SuggestionChip[]>([]),
            usage: signal<AssistantUsage | null>(null),
            sending: signal(false),
            navigated: signal(0),
            loadUsage: vi.fn(),
            send: vi.fn().mockResolvedValue(undefined),
            newConversation: vi.fn(),
            confirm: vi.fn(),
            reject: vi.fn(),
            rate: vi.fn(),
        } as unknown as typeof store;
        panel = { markBlockedByParent: vi.fn() } as unknown as MockedObject<AssistantPanelService>;
        TestBed.configureTestingModule({
            imports: [AssistantPanelComponent, translocoTestingModule({ langs: { 'assistant/tr': assistantTr } })],
            providers: [
                provideRouter([]),
                { provide: AssistantChatStore, useValue: store },
                { provide: AssistantPanelService, useValue: panel },
                { provide: AuthService, useValue: { hasRealmRole: (r: string) => r === 'Student' } },
            ],
        });
        fixture = TestBed.createComponent(AssistantPanelComponent);
        fixture.componentRef.setInput('mobile', mobile);
        fixture.detectChanges();
    }

    it('empty_ShowsIntroStartersAndContext_StarterSendsPrompt', () => {
        setup();

        expect(el('[data-testid="assistant-empty"]')?.textContent).toContain('Nasıl yardımcı olabilirim');
        expect(el('[data-testid="assistant-context"]')?.textContent).toContain('Bağlam: Öğrenci');
        const starters = all('[data-testid="assistant-starter"]');
        expect(starters.length).toBe(4);

        starters[0].click();

        expect(store.send).toHaveBeenCalledWith('Zorlandığım konular hangileri?');
        expect(store.loadUsage).toHaveBeenCalled();
    });

    it('enter_Sends_ShiftEnter_DoesNot', () => {
        setup();
        const input = el('[data-testid="assistant-input"]') as HTMLTextAreaElement;
        input.value = 'Merhaba';
        input.dispatchEvent(new Event('input'));
        fixture.detectChanges();

        input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true }));
        expect(store.send).not.toHaveBeenCalled();

        input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
        expect(store.send).toHaveBeenCalledWith('Merhaba');
        expect(fixture.componentInstance.draft()).toBe('');
    });

    it('tooLong_DisablesSend_AndShowsLimit', () => {
        setup();
        const input = el('[data-testid="assistant-input"]') as HTMLTextAreaElement;
        input.value = 'x'.repeat(2001);
        input.dispatchEvent(new Event('input'));
        fixture.detectChanges();

        expect((el('[data-testid="assistant-send"]') as HTMLButtonElement).disabled).toBe(true);
        expect(fixture.nativeElement.textContent).toContain('en çok 2000 karakter');
    });

    it('cards_RenderKpiTopicTableAndConfirm', () => {
        setup();
        const confirm: AssistantCard = {
            kind: 'confirm',
            pendingActionId: 'a1',
            tool: 'start_practice_session',
            preview: { action: 'startPracticeSession', targetCount: 10 },
            expiresAt: '',
        };
        store.messages.set([
            message({ id: 1, role: 'user', text: 'Durumum?' }),
            message({
                id: 2,
                text: 'İşte **özetin**.',
                messageId: 9,
                cards: [
                    { kind: 'kpi', items: [{ key: 'level', value: 4 }, { key: 'accuracyPct', value: 72.5 }] },
                    { kind: 'topicTable', rows: [{ subjectName: 'Matematik', topicName: 'Kesirler', total: 12, accuracyPct: 41.7 }] },
                    confirm,
                ],
                confirmStates: { a1: 'pending' },
            }),
        ]);
        fixture.detectChanges();

        expect(el('strong')?.textContent).toBe('özetin');
        const kpi = el('[data-testid="assistant-card-kpi"]')!.textContent!;
        expect(kpi).toContain('Seviye');
        expect(kpi).toContain('72.5%');
        const table = el('[data-testid="assistant-card-topics"]')!.textContent!;
        expect(table).toContain('Kesirler');
        expect(table).toContain('Matematik');
        expect(table).toContain('42%');
        expect(el('[data-testid="assistant-card-confirm"]')!.textContent).toContain('Pratik oturumu başlat');
        expect(el('[data-testid="assistant-card-confirm"]')!.textContent).toContain('10 soru');

        el('[data-testid="assistant-confirm"]')!.click();
        expect(store.confirm).toHaveBeenCalledWith(2, confirm);
        el('[data-testid="assistant-reject"]')!.click();
        expect(store.reject).toHaveBeenCalledWith(2, confirm);
    });

    it('confirmDone_ShowsStateInsteadOfButtons', () => {
        setup();
        store.messages.set([
            message({
                cards: [{ kind: 'confirm', pendingActionId: 'a1', tool: 't', preview: { action: 'startDailyPractice' }, expiresAt: '' }],
                confirmStates: { a1: 'expired' },
            }),
        ]);
        fixture.detectChanges();

        expect(el('[data-testid="assistant-confirm"]')).toBeNull();
        expect(el('[data-testid="assistant-confirm-state"]')!.textContent).toContain('süresi doldu');
    });

    it('errors_KnownCodeAndUnknownFallback_ParentBlockHidesEntry', () => {
        setup();
        store.messages.set([
            message({ id: 1, errorCode: 'AssistantBudgetExceeded' }),
            message({ id: 2, errorCode: 'SomethingNew' }),
            message({ id: 3, errorCode: 'AssistantDisabledByParent' }),
        ]);
        fixture.detectChanges();

        const errors = all('[data-testid="assistant-error"]').map((e) => e.textContent ?? '');
        expect(errors[0]).toContain('kullanım sınırına');
        expect(errors[1]).toContain('Bir şeyler ters gitti');
        expect(errors[2]).toContain('Velin asistanı kapattı');
        expect(panel.markBlockedByParent).toHaveBeenCalled();
    });

    it('streaming_ShowsToolIndicator_NoFeedbackUntilDone', () => {
        setup();
        store.messages.set([message({ streaming: true, activeTool: 'get_my_profile', messageId: null })]);
        fixture.detectChanges();
        expect(el('[data-testid="assistant-working"]')).toBeTruthy();
        expect(el('[data-testid="assistant-thumb-up"]')).toBeNull();

        store.messages.set([message({ text: 'Bitti', messageId: 3 })]);
        fixture.detectChanges();
        el('[data-testid="assistant-thumb-up"]')!.click();
        expect(store.rate).toHaveBeenCalledWith(1, 'up', null);
    });

    it('chipsAndUsage_Render_ChipSendsPrompt', () => {
        setup();
        store.messages.set([message({ text: 'Tamam' })]);
        store.suggestions.set([{ label: 'Pratik', prompt: 'Pratik başlat' }]);
        store.usage.set({ messagesThisHour: 4, messagesPerHour: 30, spentTodayUsd: 0, dailyBudgetUsd: 0.1, totalBudgetReached: false });
        fixture.detectChanges();

        expect(el('[data-testid="assistant-usage"]')!.textContent).toContain('26');
        el('[data-testid="assistant-chip"]')!.click();
        expect(store.send).toHaveBeenCalledWith('Pratik başlat');
    });

    it('close_And_NewConversation', () => {
        setup();
        const closed = vi.fn();
        fixture.componentInstance.closed.subscribe(closed);
        store.messages.set([message({ text: 'x' })]);
        fixture.detectChanges();

        el('[data-testid="assistant-new"]')!.click();
        el('[data-testid="assistant-close"]')!.click();

        expect(store.newConversation).toHaveBeenCalled();
        expect(closed).toHaveBeenCalled();
    });

    it('mobile_NavigationClosesPanel_DesktopKeepsIt', () => {
        setup(true);
        const closed = vi.fn();
        fixture.componentInstance.closed.subscribe(closed);

        store.navigated.set(1);
        fixture.detectChanges();

        expect(closed).toHaveBeenCalledTimes(1);
    });

    it('segmentsOf_BoldPairsOnly_OpenMarkerStaysPlain', () => {
        expect(segmentsOf('a **b** c')).toEqual([
            { text: 'a ', bold: false },
            { text: 'b', bold: true },
            { text: ' c', bold: false },
        ]);
        expect(segmentsOf('a **b')).toEqual([
            { text: 'a ', bold: false },
            { text: '**b', bold: false },
        ]);
    });
});
