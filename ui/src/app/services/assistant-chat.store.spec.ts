import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { MockedObject } from 'vitest';
import { of, throwError } from 'rxjs';
import { AssistantError, AssistantStreamEvent, ChatRequest } from '../models/assistant';
import { translocoTestingModule } from '../shared/testing/transloco-testing';
import { AssistantChatStore } from './assistant-chat.store';
import { AssistantService } from './assistant.service';

type Turn = AssistantStreamEvent[] | AssistantError;

describe('AssistantChatStore (#507)', () => {
    let store: AssistantChatStore;
    let api: MockedObject<AssistantService>;
    let router: Router;
    let turns: Turn[];
    let requests: ChatRequest[];

    const confirmCard = {
        kind: 'confirm' as const,
        pendingActionId: 'a1',
        tool: 'start_practice_session',
        preview: { action: 'startPracticeSession', targetCount: 10 },
        expiresAt: '2026-11-23T20:00:00Z',
    };

    beforeEach(() => {
        turns = [];
        requests = [];
        api = {
            chat: vi.fn((request: ChatRequest) => {
                requests.push(request);
                const turn = turns.shift() ?? [];
                return (async function* () {
                    if (turn instanceof AssistantError) throw turn;
                    for (const e of turn) yield e;
                })();
            }),
            usage: vi.fn().mockReturnValue(of({ messagesThisHour: 1, messagesPerHour: 30, spentTodayUsd: 0, dailyBudgetUsd: 0.1, totalBudgetReached: false })),
            confirmAction: vi.fn(),
            rejectAction: vi.fn(),
            feedback: vi.fn().mockReturnValue(of(undefined)),
            refreshAfterExpiry: vi.fn().mockResolvedValue(undefined),
        } as unknown as MockedObject<AssistantService>;
        TestBed.configureTestingModule({
            imports: [translocoTestingModule()],
            providers: [provideRouter([]), { provide: AssistantService, useValue: api }],
        });
        store = TestBed.inject(AssistantChatStore);
        router = TestBed.inject(Router);
        vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    });

    it('send_StreamsTextCardsAndChips_ThenKeepsConversationId', async () => {
        turns.push([
            { event: 'tool_start', data: { tool: 'get_my_weak_topics' } },
            { event: 'card', data: { kind: 'topicTable', rows: [{ topicName: 'Kesirler', total: 12, accuracyPct: 50 }] } },
            { event: 'tool_end', data: { tool: 'get_my_weak_topics', ok: true, errorCode: null, ms: 40 } },
            { event: 'text', data: { delta: 'Kesirler ' } },
            { event: 'text', data: { delta: 'zorlanıyorsun.' } },
            { event: 'ui_action', data: { type: 'suggestions', items: [{ label: 'Pratik', prompt: 'Pratik başlat' }] } },
            { event: 'done', data: { conversationId: 'c1', messageId: 41 } },
        ]);
        turns.push([{ event: 'done', data: { conversationId: 'c1', messageId: 42 } }]);

        await store.send('Zorlandığım konular?');

        const [user, assistant] = store.messages();
        expect(user.text).toBe('Zorlandığım konular?');
        expect(assistant.text).toBe('Kesirler zorlanıyorsun.');
        expect(assistant.cards.length).toBe(1);
        expect(assistant.messageId).toBe(41);
        expect(assistant.streaming).toBe(false);
        expect(assistant.activeTool).toBeNull();
        expect(store.suggestions()).toEqual([{ label: 'Pratik', prompt: 'Pratik başlat' }]);
        expect(store.sending()).toBe(false);
        expect(store.usage()?.messagesThisHour).toBe(1);
        expect(requests[0].conversationId).toBeNull();

        await store.send('Başka?');
        expect(requests[1].conversationId).toBe('c1');
        expect(store.suggestions()).toEqual([]);
    });

    it('send_SendsRouteAndWorksheetContext', async () => {
        await router.navigateByUrl('/');
        Object.defineProperty(router, 'url', { get: () => '/test/77?tab=1' });

        await store.send('Bu test?');

        expect(requests[0].context.route).toBe('/test/77');
        expect(requests[0].context.worksheetId).toBe(77);
        expect(requests[0].context.locale).toBe('tr');
    });

    it('send_EmptyOrTooLong_IsIgnored', async () => {
        await store.send('   ');
        await store.send('x'.repeat(2001));
        expect(api.chat).not.toHaveBeenCalled();
    });

    it('navigateAction_OnlyAppRoutes_AndCountsNavigation', async () => {
        turns.push([
            { event: 'ui_action', data: { type: 'navigate', screen: 'practice', route: '/practice' } },
            { event: 'ui_action', data: { type: 'navigate', screen: 'x', route: 'https://evil.example' } },
        ]);

        await store.send('Pratiğe git');

        expect(router.navigateByUrl).toHaveBeenCalledTimes(1);
        expect(router.navigateByUrl).toHaveBeenCalledWith('/practice');
        expect(store.navigated()).toBe(1);
    });

    it('highlightAction_OpensScreenAndOutlinesElement (issue #515)', async () => {
        vi.useFakeTimers();
        const target = document.createElement('button');
        target.setAttribute('data-assistant-key', 'programs.create');
        target.scrollIntoView = vi.fn();
        document.body.appendChild(target);
        turns.push([
            { event: 'ui_action', data: { type: 'highlight', key: 'programs.create', route: '/programs' } },
            { event: 'ui_action', data: { type: 'highlight', key: '"]x', route: '/programs' } },
        ]);

        await store.send('Program nerede?');
        await vi.advanceTimersByTimeAsync(0);

        expect(router.navigateByUrl).toHaveBeenCalledTimes(1);
        expect(router.navigateByUrl).toHaveBeenCalledWith('/programs');
        expect(target.classList.contains('assistant-highlight')).toBe(true);
        await vi.advanceTimersByTimeAsync(3000);
        expect(target.classList.contains('assistant-highlight')).toBe(false);
        target.remove();
        vi.useRealTimers();
    });

    it('tokenExpired_RefreshesAndResendsSameMessageOnce', async () => {
        turns.push([{ event: 'error', data: { code: 'TokenExpired' } }]);
        turns.push([
            { event: 'text', data: { delta: 'Tamam' } },
            { event: 'done', data: { conversationId: 'c1', messageId: 5 } },
        ]);

        await store.send('Merhaba');

        expect(api.refreshAfterExpiry).toHaveBeenCalledTimes(1);
        expect(requests.map((r) => r.message)).toEqual(['Merhaba', 'Merhaba']);
        const messages = store.messages();
        expect(messages.length).toBe(2);
        expect(messages[1].text).toBe('Tamam');
        expect(messages[1].errorCode).toBeNull();
    });

    it('httpError_ShowsCodeOnAssistantMessage', async () => {
        turns.push(new AssistantError('AssistantDisabledByParent', 403));

        await store.send('Merhaba');

        expect(store.messages()[1].errorCode).toBe('AssistantDisabledByParent');
        expect(store.sending()).toBe(false);
    });

    it('confirm_RunsOnce_AndAppliesNavigate', async () => {
        turns.push([{ event: 'confirm_required', data: confirmCard }]);
        api.confirmAction.mockReturnValue(
            of({ ok: true, data: { sessionId: 9 }, errorCode: null, message: null, cards: null, uiActions: [{ type: 'navigate', screen: 'practiceSession', route: '/practice?session=9' }] })
        );
        await store.send('Pratik başlat');
        const assistant = store.messages()[1];
        expect(assistant.confirmStates['a1']).toBe('pending');

        store.confirm(assistant.id, confirmCard);
        store.confirm(assistant.id, confirmCard);

        expect(api.confirmAction).toHaveBeenCalledTimes(1);
        expect(store.messages()[1].confirmStates['a1']).toBe('confirmed');
        expect(router.navigateByUrl).toHaveBeenCalledWith('/practice?session=9');
    });

    it('confirm_Expired_And_Reject_Decided', async () => {
        turns.push([{ event: 'confirm_required', data: confirmCard }]);
        turns.push([{ event: 'confirm_required', data: { ...confirmCard, pendingActionId: 'a2' } }]);
        await store.send('Bir');
        await store.send('İki');
        const [, first, , second] = store.messages();
        api.confirmAction.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 410 })));
        api.rejectAction.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));

        store.confirm(first.id, confirmCard);
        store.reject(second.id, { ...confirmCard, pendingActionId: 'a2' });

        expect(store.messages()[1].confirmStates['a1']).toBe('expired');
        expect(store.messages()[3].confirmStates['a2']).toBe('decided');
    });

    it('rate_SendsFeedback_AndRevertsOnError', async () => {
        turns.push([{ event: 'done', data: { conversationId: 'c1', messageId: 41 } }]);
        await store.send('Merhaba');
        const assistant = store.messages()[1];

        store.rate(assistant.id, 'down', 'tooLong');
        expect(api.feedback).toHaveBeenCalledWith(41, 'down', 'tooLong');
        expect(store.messages()[1].feedback).toBe('down');

        api.feedback.mockReturnValue(throwError(() => new Error('x')));
        store.rate(assistant.id, 'up');
        expect(store.messages()[1].feedback).toBe('down');
    });

    it('newConversation_ClearsMessagesAndConversation', async () => {
        turns.push([{ event: 'done', data: { conversationId: 'c1', messageId: 1 } }]);
        await store.send('Merhaba');

        store.newConversation();
        await store.send('Yeni');

        expect(store.messages().length).toBe(2);
        expect(requests[1].conversationId).toBeNull();
    });
});
