import { TestBed, ComponentFixture } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
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

    let role = 'Student';

    function setup(mobile = false, asRole = 'Student'): void {
        role = asRole;
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
                { provide: AuthService, useValue: { hasRealmRole: (r: string) => r === role } },
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

    it('parent_GetsParentIntroAndStarters', () => {
        setup(false, 'Parent');

        expect(el('[data-testid="assistant-context"]')?.textContent).toContain('Bağlam: Veli');
        expect(el('[data-testid="assistant-empty"]')?.textContent).toContain('Çocuğunuzun haftasını');
        const starters = all('[data-testid="assistant-starter"]');
        expect(starters.length).toBe(4);

        starters[0].click();

        expect(store.send).toHaveBeenCalledWith('Çocuğum bu hafta nasıl, geciken ödevi var mı, nerede zorlanıyor?');
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

    it('questionsCard_ShowsImagesOrText_SolveTheseSendsPrompt_HardPracticeConfirmTitle', () => {
        setup();
        store.messages.set([
            message({
                id: 3,
                text: 'En çok zorlandığın sorular bunlar.',
                cards: [
                    {
                        kind: 'questions',
                        items: [
                            { questionId: 42, subjectName: 'Matematik', topicName: 'Açılar', missCount: 3, imageUrl: 'https://img/42.jpg' },
                            { questionId: 43, subjectName: 'Matematik', topicName: 'Açılar', missCount: 1, text: 'Bir açının ölçüsü...' },
                            { questionId: 44, subjectName: 'Matematik', topicName: 'Açılar', missCount: 0, correctPercent: 25, text: 'Genel zor' },
                        ],
                    },
                    {
                        kind: 'confirm',
                        pendingActionId: 'h1',
                        tool: 'start_hard_question_practice',
                        preview: { action: 'startHardQuestionPractice', targetCount: 2 },
                        expiresAt: '',
                    },
                ],
                confirmStates: { h1: 'pending' },
            }),
        ]);
        fixture.detectChanges();

        const card = el('[data-testid="assistant-card-questions"]')!;
        expect(card.querySelector('img')!.getAttribute('src')).toBe('https://img/42.jpg');
        expect(card.textContent).toContain('Bir açının ölçüsü');
        expect(card.textContent).toContain('Matematik · Açılar');
        expect(card.textContent).toContain('3 kez yanlış / boş');
        expect(card.textContent).toContain('doğru yapanlar %25');
        expect(el('[data-testid="assistant-card-confirm"]')!.textContent).toContain('Seçilen soruları çöz');

        el('[data-testid="assistant-solve-these"]')!.click();
        expect(store.send).toHaveBeenCalledWith('Bu soruları çözmek istiyorum.');
    });

    it('questionsCard_NotSolvable_HasNoSolveButton_issue536', () => {
        setup();
        store.messages.set([
            message({
                id: 4,
                text: 'Ali en çok bu sorularda zorlandı.',
                cards: [
                    {
                        kind: 'questions',
                        solvable: false,
                        items: [{ questionId: 42, subjectName: 'Matematik', topicName: 'Açılar', missCount: 2, imageUrl: 'https://img/42.jpg' }],
                    },
                ],
            }),
        ]);
        fixture.detectChanges();

        expect(el('[data-testid="assistant-card-questions"]')!.textContent).toContain('2 kez yanlış / boş');
        expect(el('[data-testid="assistant-solve-these"]')).toBeNull();
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

    it('planningConfirm_ShowsPlainNameAndDetails_ApproveLabel (issue #515)', () => {
        setup();
        store.messages.set([
            message({
                cards: [
                    {
                        kind: 'confirm', pendingActionId: 'r1', tool: 'set_worksheet_reminder', expiresAt: '',
                        preview: { action: 'setWorksheetReminder', name: '<untrusted>Kesirler 1</untrusted>', date: '2026-11-27', time: '19:00', remindBeforeMinutes: 30, replacesReminder: true },
                    },
                    {
                        kind: 'confirm', pendingActionId: 's1', tool: 'start_test', expiresAt: '',
                        preview: { action: 'startTest', name: 'Açılar', questionCount: 12, durationMinutes: 20 },
                    },
                ],
                confirmStates: { s1: 'confirmed' },
            }),
        ]);
        fixture.detectChanges();

        const cards = fixture.nativeElement.querySelectorAll('[data-testid="assistant-card-confirm"]') as NodeListOf<HTMLElement>;
        expect(cards[0].textContent).toContain('Test hatırlatması kur');
        expect(cards[0].textContent).toContain('Kesirler 1');
        expect(cards[0].textContent).not.toContain('untrusted');
        expect(cards[0].textContent).toContain('27.11.2026 19:00, 30 dakika önce hatırlatılır');
        expect(cards[0].textContent).toContain('mevcut hatırlatmasının yerine');
        expect(cards[0].querySelector('[data-testid="assistant-confirm"]')!.textContent).toContain('Onayla');
        expect(cards[1].textContent).toContain('Testi başlat');
        expect(cards[1].textContent).toContain('12 soru');
        expect(cards[1].querySelector('[data-testid="assistant-confirm-state"]')!.textContent).toContain('Başlatıldı');
    });

    it('planningCards_ShowEveryChoice_LessonRequestAndDraft (issue #515)', async () => {
        setup();
        const router = TestBed.inject(Router);
        const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
        store.messages.set([
            message({
                cards: [
                    {
                        kind: 'confirm', pendingActionId: 'p1', tool: 'create_study_program', expiresAt: '',
                        preview: { action: 'createStudyProgram', name: '<untrusted>Kış</untrusted>', startDate: '2026-11-23', endDate: '2026-12-23', studyType: 'question', questionsPerDay: 12, subjectsPerDay: 2, restDays: [6, 7], difficultSubjects: ['Matematik'], replacesActiveProgram: true },
                    },
                    {
                        kind: 'confirm', pendingActionId: 'p2', tool: 'schedule_study_pages', expiresAt: '',
                        preview: { action: 'scheduleStudyPages', name: 'Kış', items: [{ studyItemId: 11, title: '<untrusted>Kesir özeti</untrusted>', startDate: '2026-11-24', endDate: '2026-11-25' }] },
                    },
                    {
                        kind: 'confirm', pendingActionId: 'p3', tool: 'request_lesson', expiresAt: '',
                        preview: { action: 'requestLesson', name: '<untrusted>Ali Hoca</untrusted>', date: '2026-11-26', startTime: '17:00:00', endTime: '18:00:00', hourlyRate: 400 },
                    },
                    { kind: 'draft', text: 'Merhaba hocam', route: '/teacher-messages?conversation=12' },
                    { kind: 'draft', text: 'x', route: '/admin' },
                ],
            }),
        ]);
        fixture.detectChanges();

        const cards = fixture.nativeElement.querySelectorAll('[data-testid="assistant-card-confirm"]') as NodeListOf<HTMLElement>;
        expect(cards[0].textContent).toContain('her dersten günde 12 soru');
        expect(cards[0].textContent).toContain('Günde 2 ders');
        expect(cards[0].textContent).toContain('Cumartesi, Pazar');
        expect(cards[0].textContent).toContain('Matematik');
        expect(cards[1].textContent).toContain('Kesir özeti: 24.11.2026 – 25.11.2026');
        expect(cards[1].textContent).not.toContain('untrusted');
        expect(cards[2].textContent).toContain('Ali Hoca');
        expect(cards[2].textContent).toContain('26.11.2026 17:00–18:00');
        expect(cards[2].textContent).toContain('Öğretmen onaylayınca');

        const drafts = fixture.nativeElement.querySelectorAll('[data-testid="assistant-card-draft"]') as NodeListOf<HTMLElement>;
        expect(drafts[0].textContent).toContain('Merhaba hocam');
        expect(drafts[0].textContent).toContain('Gönderilmedi');
        (drafts[0].querySelector('[data-testid="assistant-draft-open"]') as HTMLButtonElement).click();
        expect(navigate).toHaveBeenCalledWith('/teacher-messages?conversation=12');
        expect(drafts[1].querySelector('[data-testid="assistant-draft-open"]')).toBeNull();
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
