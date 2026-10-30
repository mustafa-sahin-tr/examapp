import type { Mock } from 'vitest';
import { WritableSignal, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';

import { ACTIVITY_SUMMARY_REFETCH_MS, ActivityPlayerComponent } from './activity-player.component';
import { ActivityService } from '../../services/activity.service';
import { ActivityLauncherService } from '../../services/activity-launcher.service';
import { BadgeService } from '../../services/badge.service';
import { TestService } from '../../services/test.service';
import { TestInstance, TestStatus } from '../../models/test-instance';
import { WorksheetKind } from '../../models/activity';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import rootTr from '../../../../public/i18n/tr.json';
import activityTr from '../../../../public/i18n/activity/tr.json';
import questionTr from '../../../../public/i18n/question/tr.json';

const PLAN = JSON.stringify({
  version: 1,
  type: 'dragDropLabeling',
  dropZones: [
    { id: 'z1', x: 0, y: 0, width: 50, height: 20 },
    { id: 'z2', x: 60, y: 0, width: 50, height: 20 },
  ],
  draggables: [
    { id: 'd1', label: 'cat' },
    { id: 'd2', label: 'dog' },
  ],
});

const PLACED_Z1 = JSON.stringify({ version: 1, type: 'dragDropLabeling', placements: [{ dropZoneId: 'z1', draggableId: 'd2' }] });

function instance(overrides: Partial<TestInstance> = {}, questionOverrides: Record<string, unknown>[] = []): TestInstance {
  const base = [
    {
      id: 101,
      order: 1,
      selectedAnswerId: 0,
      answerPayload: PLACED_Z1,
      timeTaken: 5,
      question: {
        id: 1,
        text: '',
        imageUrl: 'https://cdn.example.test/q1.webp',
        isCanvasQuestion: true,
        interactionType: 'dragDropLabeling',
        interactionPlan: PLAN,
        answers: [],
        x: 0, y: 0, width: 200, height: 100,
        passage: { id: 7, title: 'P', kind: 1, instruction: 'Resimleri eşleştir.' },
      },
    },
    {
      id: 102,
      order: 2,
      selectedAnswerId: 0,
      timeTaken: 0,
      question: {
        id: 2,
        text: 'Which one is a cat?',
        imageUrl: '',
        isCanvasQuestion: false,
        interactionType: 'mcq',
        answers: [
          { id: 11, text: 'A' },
          { id: 12, text: 'B' },
          { id: 13, text: 'C' },
        ],
        x: 0, y: 0, width: 100, height: 100,
      },
    },
  ];
  return {
    id: 42,
    worksheetId: 9,
    testName: 'Unit 4 — Match the pictures',
    status: TestStatus.Started,
    maxDurationSeconds: 0,
    isPracticeTest: false,
    kind: WorksheetKind.Activity,
    checkCount: 0,
    worksheetDescription: 'Sayfa yönergesi',
    testInstanceQuestions: base.map((q, i) => ({ ...q, ...(questionOverrides[i] ?? {}) })),
    ...overrides,
  } as unknown as TestInstance;
}

describe('ActivityPlayerComponent (#573)', () => {
  beforeEach(() => localStorage.setItem('user', JSON.stringify({ id: 77 })));
  afterEach(() => {
    testServiceAssignments = [];
    summaryResponse = { today: { activityCount: 0, activeMinutes: 0, streakDays: 0 }, badges: [] };
    fixture?.destroy();
    sessionStorage.removeItem('activity.activeSeconds:42');
    localStorage.removeItem('user');
    vi.useRealTimers();
  });
  let fixture: ComponentFixture<ActivityPlayerComponent>;
  let testService: Record<string, Mock>;
  let activityService: { check: Mock; getSolutions: Mock };
  let badgeService: { getActivitySummary: Mock };
  let launcher: { retaking: WritableSignal<number | null>; retake: Mock; open: Mock };
  /** `create()` öncesi ayarlanırsa `getActivitySummary` bunu döner. */
  let summaryResponse: unknown = { today: { activityCount: 0, activeMinutes: 0, streakDays: 0 }, badges: [] };
  let router: Router;
  /** `create()` öncesi ayarlanırsa `getActiveAssignments` bunu döner. */
  let testServiceAssignments: unknown[] = [];

  const el = () => fixture.nativeElement as HTMLElement;
  const q = (id: string) => el().querySelector(`[data-testid="${id}"]`) as HTMLElement | null;

  async function create(data: TestInstance | Error = instance()): Promise<void> {
    testService = {
      getCanvasTestWithAnswers: vi.fn().mockReturnValue(data instanceof Error ? throwError(() => data) : of(data)),
      convertTestInstanceToRegions: vi.fn((ti: TestInstance) =>
        TestService.prototype.convertTestInstanceToRegions.call({}, ti)
      ),
      saveAnswer: vi.fn().mockReturnValue(of({ success: true })),
      completeTest: vi.fn().mockReturnValue(of({ success: true })),
      get: vi.fn(),
      getActiveAssignments: vi.fn().mockReturnValue(of(testServiceAssignments)),
      startTest: vi.fn(),
    };
    activityService = { check: vi.fn(), getSolutions: vi.fn() };
    launcher = { retaking: signal<number | null>(null), retake: vi.fn(), open: vi.fn() };
    badgeService = {
      getActivitySummary: vi
        .fn()
        .mockImplementation(() => (summaryResponse instanceof Error ? throwError(() => summaryResponse) : of(summaryResponse))),
    };

    TestBed.configureTestingModule({
      imports: [
        ActivityPlayerComponent,
        NoopAnimationsModule,
        translocoTestingModule({
          langs: {
            tr: { ...rootTr, activity: activityTr, question: questionTr },
            'activity/tr': activityTr,
            'question/tr': questionTr,
          },
        }),
      ],
      providers: [
        provideRouter([]),
        { provide: TestService, useValue: testService },
        { provide: ActivityService, useValue: activityService },
        { provide: BadgeService, useValue: badgeService },
        { provide: ActivityLauncherService, useValue: launcher },
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ testInstanceId: '42' })) } },
      ],
    });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(ActivityPlayerComponent);
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('load_RendersFrameWithItemInstructionAndSteps', async () => {
    await create();

    expect(el().querySelector('.apf__title')?.textContent?.trim()).toBe('Unit 4 — Match the pictures');
    // İlk boş madde (b) açılır.
    expect(q('item-count')?.textContent?.trim()).toBe('Madde 2 / 2');
    const steps = Array.from(el().querySelectorAll('.apf__step'));
    expect(steps[0].getAttribute('aria-label')).toBe('Madde 1: cevaplandı');
    // b'nin kapsam yönergesi yok → sayfa yönergesi (açıklama).
    expect(q('instruction')?.textContent).toContain('Sayfa yönergesi');
    // Yedek yönerge oturum yanıtından gelir; ayrı worksheet isteği yok.
    expect(testService['get']).not.toHaveBeenCalled();
  });

  it('noItemOrWorksheetInstruction_HidesInstructionBox', async () => {
    await create(instance({ worksheetDescription: null }));

    expect(q('instruction')).toBeNull();
  });

  it('itemInstruction_TakesPrecedenceOverWorksheetDescription', async () => {
    await create();
    fixture.componentInstance.goTo(0);
    await settle();

    expect(q('instruction')?.textContent).toContain('Resimleri eşleştir.');
    expect(el().querySelector('app-question-canvas-dragdrop-labeling')).not.toBeNull();
  });

  it('testKindInstance_RedirectsToTestSolve', async () => {
    await create(instance({ kind: WorksheetKind.Test }));

    expect(router.navigate).toHaveBeenCalledWith(['/testsolve', 42], { replaceUrl: true });
  });

  it('loadError_ShowsErrorStateWithRetry', async () => {
    await create(new Error('boom'));

    expect(el().textContent).toContain('Etkinlik yüklenemedi.');
    testService['getCanvasTestWithAnswers'].mockReturnValue(of(instance()));
    (el().querySelector('.ap__state--error button') as HTMLButtonElement).click();
    await settle();
    expect(q('item-count')).not.toBeNull();
  });

  it('mcqSelection_SavesAnswerWithTimeTaken', async () => {
    await create();

    fixture.componentInstance.onChoiceSelected(12, fixture.componentInstance.items()[1]);

    expect(testService['saveAnswer']).toHaveBeenCalledWith(
      expect.objectContaining({ testInstanceId: 42, testQuestionId: 102, selectedAnswerId: 12, timeTaken: expect.any(Number) })
    );
  });

  it('check_SendsAllItems_ShowsSummaryAndRowFeedback_NoScore', async () => {
    await create();
    activityService.check.mockReturnValue(
      of({
        success: true,
        correctCount: 2,
        totalCount: 3,
        questions: [
          { instanceQuestionId: 101, isCorrect: false, correctCount: 1, totalCount: 2, feedback: '{"rows":[{"id":"z1","correct":true},{"id":"z2","correct":false}]}' },
          { instanceQuestionId: 102, isCorrect: true, correctCount: 1, totalCount: 1, feedback: null },
        ],
      })
    );

    await fixture.componentInstance.runCheck();
    await settle();

    expect(activityService.check).toHaveBeenCalledWith(42, [
      { instanceQuestionId: 101, selectedAnswerId: null, answerPayload: PLACED_Z1 },
      { instanceQuestionId: 102, selectedAnswerId: null, answerPayload: null },
    ]);
    expect(q('check-summary')?.textContent).toContain('3 cevap yerinden 2 doğru');
    const steps = Array.from(el().querySelectorAll('.apf__step'));
    expect(steps[0].getAttribute('aria-label')).toBe('Madde 1: yanlış');

    fixture.componentInstance.goTo(0);
    await settle();
    expect(el().querySelector('#zone-z1')?.classList.contains('is-correct')).toBe(true);
    expect(el().querySelector('#zone-z2')?.classList.contains('is-incorrect')).toBe(true);
    const rows = Array.from(el().querySelectorAll('[data-testid="result-row"]')).map((r) => r.textContent ?? '');
    expect(rows[0]).toContain('Hedef 1: dog');
    expect(rows[1]).toContain('Hedef 2: boş');
    expect(el().textContent).not.toMatch(/%|puan|XP/i);
  });

  it('answerChangeAfterCheck_DropsThatItemsResult', async () => {
    await create();
    activityService.check.mockReturnValue(
      of({
        success: true,
        correctCount: 1,
        totalCount: 3,
        questions: [
          { instanceQuestionId: 101, isCorrect: false, correctCount: 1, totalCount: 2, feedback: null },
          { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
        ],
      })
    );
    await fixture.componentInstance.runCheck();

    fixture.componentInstance.onChoiceSelected(11, fixture.componentInstance.items()[1]);
    await settle();

    expect(fixture.componentInstance.results()[102]).toBeUndefined();
    expect(fixture.componentInstance.stale()).toBe(true);
  });

  it('check429_ShowsWaitMessage', async () => {
    await create();
    activityService.check.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 429, error: { errorCode: 'RateLimited', message: 'x' } }))
    );

    await fixture.componentInstance.runCheck();
    await settle();

    expect(q('check-error')?.textContent).toContain('Biraz bekleyip tekrar dene.');
  });

  it('reveal409_ShowsCheckFirstWarning', async () => {
    await create(instance({ checkCount: 1 }));
    activityService.getSolutions.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { errorCode: 'ActivityCheckRequired', message: 'x' } }))
    );

    await fixture.componentInstance.reveal();
    await settle();

    expect(q('check-error')?.textContent).toContain('Doğru cevaplar için önce bir kez kontrol et.');
  });

  it('reveal_ShowsSolutionAndRestoreBringsOwnAnswerBack', async () => {
    await create(instance({ checkCount: 1 }));
    activityService.getSolutions.mockReturnValue(
      of({
        success: true,
        questions: [
          { instanceQuestionId: 101, questionId: 1, correctAnswerId: null, interactionSolution: '{"placements":[{"draggableId":"d1","dropZoneId":"z1"},{"draggableId":"d2","dropZoneId":"z2"}]}' },
          { instanceQuestionId: 102, questionId: 2, correctAnswerId: 11, interactionSolution: null },
        ],
      })
    );
    fixture.componentInstance.goTo(0);

    await fixture.componentInstance.reveal();
    await settle();

    expect(q('revealed-info')).not.toBeNull();
    expect(el().querySelector('#zone-z1 .dd-chip')?.textContent?.trim()).toBe('cat');

    (q('restore') as HTMLButtonElement).click();
    await settle();
    expect(el().querySelector('#zone-z1 .dd-chip')?.textContent?.trim()).toBe('dog');
  });

  it('finish409_ShowsServerMessage', async () => {
    await create(instance({ checkCount: 1 }));
    testService['completeTest'].mockReturnValue(
      throwError(
        () => new HttpErrorResponse({ status: 409, error: { errorCode: 'ActivityCheckRequired', message: 'Önce kontrol et (sunucu).' } })
      )
    );

    await fixture.componentInstance.finish();
    await settle();

    expect(q('check-error')?.textContent).toContain('Önce kontrol et (sunucu).');
    expect(q('activity-summary')).toBeNull();
  });

  it('finishSuccess_ShowsSummaryAndNextActivity', async () => {
    await create(instance({ checkCount: 1 }));
    testService['getActiveAssignments'].mockReturnValue(
      of([
        { worksheetId: 9, kind: 1, isCompleted: false },
        { worksheetId: 15, kind: 1, isCompleted: false, isFinished: false },
      ])
    );

    await fixture.componentInstance.finish();
    await settle();

    expect(q('activity-summary')).not.toBeNull();
    expect(testService['getActiveAssignments']).toHaveBeenCalledWith(WorksheetKind.Activity);
    expect(q('next-activity')).not.toBeNull();
    expect(el().textContent).not.toMatch(/%|puan|XP/i);
  });

  it('reload_RestoresCheckMarksAndSummaryFromInstance', async () => {
    await create(
      instance({ checkCount: 2 }, [
        { isCorrect: true, correctCount: 2, totalCount: 2, feedback: '{"rows":[{"id":"z1","correct":true},{"id":"z2","correct":true}]}' },
        { isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
      ])
    );

    const steps = Array.from(el().querySelectorAll('.apf__step'));
    expect(steps[0].getAttribute('aria-label')).toBe('Madde 1: doğru');
    expect(q('check-summary')?.textContent).toContain('3 cevap yerinden 2 doğru');
    expect((q('reveal') as HTMLButtonElement).disabled).toBe(false);
  });

  it('completedInstance_OpensSummaryDirectly', async () => {
    await create(instance({ status: TestStatus.Completed, checkCount: 1 }));

    expect(q('activity-summary')).not.toBeNull();
    expect(el().querySelector('app-activity-player-frame')).toBeNull();
  });

  it('completedInstance_RetakeConfirmedReloadsSameSessionAsPlayer (#609)', async () => {
    await create(instance({ status: TestStatus.Completed, checkCount: 1 }));

    (q('summary-retake') as HTMLButtonElement).click();
    expect(launcher.retake).toHaveBeenCalledWith(42, 9, expect.any(Function));

    // Sunucu oturumu sıfırladı: aynı rota, gezinme yok — oturum yeniden yüklenir ve oynatıcı açılır.
    testService['getCanvasTestWithAnswers'].mockReturnValue(
      of(instance({ checkCount: 0, firstCompletedAt: '2026-10-27T10:00:00Z' }, [{ answerPayload: null }]))
    );
    const reopen = launcher.retake.mock.calls[0][2] as () => void;
    reopen();
    await settle();

    expect(testService['getCanvasTestWithAnswers']).toHaveBeenCalledTimes(2);
    expect(q('activity-summary')).toBeNull();
    expect(el().querySelector('app-activity-player-frame')).not.toBeNull();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('retakeInFlight_DisablesSummaryButton (#609)', async () => {
    await create(instance({ status: TestStatus.Completed, checkCount: 1 }));
    launcher.retaking.set(42);
    await settle();

    expect((q('summary-retake') as HTMLButtonElement).disabled).toBe(true);
  });

  it('expiredInstance_SummaryWithoutRetake (#609)', async () => {
    await create(instance({ status: TestStatus.Expired, checkCount: 1 }));

    expect(q('activity-summary')).not.toBeNull();
    expect(q('summary-retake')).toBeNull();
    fixture.componentInstance.retake();
    expect(launcher.retake).not.toHaveBeenCalled();
  });

  it('finishSuccess_SummaryOffersRetake (#609)', async () => {
    await create(instance({ checkCount: 1 }));

    await fixture.componentInstance.finish();
    await settle();

    expect(q('summary-retake')).not.toBeNull();
  });

  // ---------------------------------------------------------------------------------------------
  // Review düzeltmeleri (#573)
  // ---------------------------------------------------------------------------------------------

  const saveCalls = () => testService['saveAnswer'].mock.calls.map((c) => c[0] as { testQuestionId: number; selectedAnswerId: number });

  it('critical1_ContentReadOnlyWhileChecking_EditsIgnored', async () => {
    await create();
    const pending = new Subject<unknown>();
    activityService.check.mockReturnValue(pending);

    const run = fixture.componentInstance.runCheck();
    await settle();

    const content = q('item-content')!;
    expect(content.hasAttribute('inert')).toBe(true);
    expect(content.getAttribute('aria-busy')).toBe('true');
    expect(content.classList.contains('is-busy')).toBe(true);
    fixture.componentInstance.onChoiceSelected(12, fixture.componentInstance.items()[1]);
    expect(fixture.componentInstance.answers()[102].selectedAnswerId).toBeNull();
    expect(testService['saveAnswer']).not.toHaveBeenCalled();

    pending.next({ success: true, correctCount: 0, totalCount: 3, questions: [] });
    pending.complete();
    await run;
    await settle();
    expect(q('item-content')!.hasAttribute('inert')).toBe(false);
  });

  it('critical1_ItemChangedDuringCheck_IsRepersistedAfterCheck_AndGetsNoStaleResult', async () => {
    await create();
    const pending = new Subject<unknown>();
    activityService.check.mockReturnValue(pending);

    const run = fixture.componentInstance.runCheck();
    await settle();
    // Kilit öncesi uçuşta kalmış bir değişikliği taklit et (ör. bileşenden geç gelen olay).
    fixture.componentInstance.answers.update((all) => ({ ...all, 102: { selectedAnswerId: 13, answerPayload: null } }));
    pending.next({
      success: true,
      correctCount: 1,
      totalCount: 3,
      questions: [
        { instanceQuestionId: 101, isCorrect: false, correctCount: 1, totalCount: 2, feedback: null },
        { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
      ],
    });
    pending.complete();
    await run;

    expect(saveCalls()).toEqual([expect.objectContaining({ testQuestionId: 102, selectedAnswerId: 13 })]);
    expect(fixture.componentInstance.results()[102]).toBeUndefined();
    expect(fixture.componentInstance.results()[101]).toBeDefined();
  });

  it('w3_SecondChangeWhileSaveInFlight_SendsOnlyLatestAfterFirstResolves', async () => {
    await create();
    const first = new Subject<unknown>();
    testService['saveAnswer'].mockReturnValueOnce(first).mockReturnValue(of({ success: true }));
    const item = fixture.componentInstance.items()[1];

    fixture.componentInstance.onChoiceSelected(11, item);
    fixture.componentInstance.onChoiceSelected(12, item);
    fixture.componentInstance.onChoiceSelected(13, item);
    expect(saveCalls().map((c) => c.selectedAnswerId)).toEqual([11]);

    first.next({ success: true });
    first.complete();
    await settle();

    expect(saveCalls().map((c) => c.selectedAnswerId)).toEqual([11, 13]);
  });

  it('w1_FinishWithFailedSave_ResavesThenCompletes', async () => {
    await create(instance({ checkCount: 1 }));
    testService['saveAnswer'].mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })));
    fixture.componentInstance.onChoiceSelected(12, fixture.componentInstance.items()[1]);
    await settle();
    expect(fixture.componentInstance.saveFailed()).toBe(true);

    await fixture.componentInstance.finish();
    await settle();

    expect(saveCalls().map((c) => c.selectedAnswerId)).toEqual([12, 12]);
    expect(testService['completeTest']).toHaveBeenCalledTimes(1);
    expect(q('activity-summary')).not.toBeNull();
  });

  it('w1w4_FinishWhenResaveStillFails_DoesNotEndTest_SaysAnswersNotSaved', async () => {
    await create(instance({ checkCount: 1 }));
    testService['saveAnswer'].mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    fixture.componentInstance.onChoiceSelected(12, fixture.componentInstance.items()[1]);
    await settle();

    await fixture.componentInstance.finish();
    await settle();

    expect(testService['completeTest']).not.toHaveBeenCalled();
    const error = q('check-error')?.textContent ?? '';
    expect(error).toContain('henüz kaydedilmedi');
    expect(error).not.toContain('Cevapların kayıtlı');
  });

  it('w2_RetryAfterRevealFailure_RetriesReveal', async () => {
    await create(instance({ checkCount: 1 }));
    activityService.getSolutions
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })))
      .mockReturnValue(of({ success: true, questions: [] }));

    await fixture.componentInstance.reveal();
    await settle();
    (q('retry') as HTMLButtonElement).click();
    await settle();

    expect(activityService.getSolutions).toHaveBeenCalledTimes(2);
    expect(activityService.check).not.toHaveBeenCalled();
    expect(fixture.componentInstance.revealed()).toBe(true);
  });

  it('w2_RetryAfterFinishFailure_RetriesFinish', async () => {
    await create(instance({ checkCount: 1 }));
    testService['completeTest']
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500, error: { message: 'Sunucu hatası' } })))
      .mockReturnValue(of({ success: true }));

    await fixture.componentInstance.finish();
    await settle();
    expect(q('check-error')?.textContent).toContain('Sunucu hatası');
    (q('retry') as HTMLButtonElement).click();
    await settle();
    await fixture.whenStable();
    await settle();

    expect(testService['completeTest']).toHaveBeenCalledTimes(2);
    expect(activityService.check).not.toHaveBeenCalled();
  });

  it('s6_ReloadWithSomeRowsChangedAfterCheck_SummaryFromUnchangedRows', async () => {
    await create(
      instance({ checkCount: 1 }, [
        { isCorrect: false, correctCount: 1, totalCount: 2, feedback: null },
        {},
      ])
    );

    expect(q('check-summary')?.textContent).toContain('2 cevap yerinden 1 doğru');
    expect(q('fix-wrong')).not.toBeNull();
    expect(el().textContent).toContain('Kontrolden sonra cevabını değiştirdin');
  });

  it('s6_FinishedWithoutRowData_InfoLineFromInstanceFirstCheck', async () => {
    // Atamadaki sayılar kullanılmaz (bağımlılık kaldırıldı); kaynak oturum yanıtı.
    testServiceAssignments = [{ worksheetId: 9, kind: 1, isCompleted: true, firstCheckCorrectCount: 1, firstCheckTotalCount: 1 }];
    await create(instance({ status: TestStatus.Completed, checkCount: 2, firstCheckCorrectCount: 7, firstCheckTotalCount: 10 }));

    expect(q('summary-info')?.textContent?.trim()).toBe('10 cevap yerinden 7 doğru');
  });

  it('s6_InProgressIgnoresFirstCheckCounts', async () => {
    await create(instance({ checkCount: 1, firstCheckCorrectCount: 7, firstCheckTotalCount: 10 }));

    expect(q('check-summary')).toBeNull();
  });

  it('exit_WaitsForSavesThenGoesToActivityList_StaysWhenSaveFailed', async () => {
    await create(instance());
    await settle();
    vi.mocked(router.navigate).mockClear();

    await fixture.componentInstance.exit();
    expect(router.navigate).toHaveBeenCalledWith(['/tests'], { queryParams: { type: 'activities' } });

    vi.mocked(router.navigate).mockClear();
    fixture.componentInstance.saveFailed.set(true);
    await fixture.componentInstance.exit();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('s9_ItemAriaUsesStepNumber', async () => {
    await create();

    expect(q('item-content')?.getAttribute('aria-label')).toBe('Madde 2 içeriği');
  });

  // ---------------------------------------------------------------------------
  // Issue #572: etkin süre + "Bugün" / rozet kartları
  // ---------------------------------------------------------------------------

  const pointer = () => q('item-content')!.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));

  it('i572_Finish_SendsActiveSeconds_SeededFromTimeTaken_ExcludingIdleGap', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-26T10:00:00Z'));
    await create(instance({ checkCount: 1 })); // timeTaken toplamı 5 sn → başlangıç
    vi.setSystemTime(new Date('2026-10-26T10:00:30Z'));
    pointer(); // +30
    vi.setSystemTime(new Date('2026-10-26T10:05:30Z')); // 5 dk hareketsiz → sayılmaz
    pointer();
    vi.setSystemTime(new Date('2026-10-26T10:05:40Z')); // +10 (bitir tıklaması)

    await fixture.componentInstance.finish();

    expect(testService['completeTest']).toHaveBeenCalledWith(42, { activeSeconds: 45 });
  });

  it('i572_ActiveSeconds_CappedAt1800', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-26T10:00:00Z'));
    await create(instance({ checkCount: 1 }, [{ timeTaken: 1500 }, { timeTaken: 400 }]));
    vi.setSystemTime(new Date('2026-10-26T10:01:00Z'));
    pointer();

    await fixture.componentInstance.finish();

    expect(testService['completeTest']).toHaveBeenCalledWith(42, { activeSeconds: 1800 });
  });

  it('i572_Reload_SeedsFromSessionStorage', async () => {
    sessionStorage.setItem('activity.activeSeconds:42', '120');
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-26T10:00:00Z'));
    await create(instance({ checkCount: 1 }));

    await fixture.componentInstance.finish(); // aynı an → +0

    expect(testService['completeTest']).toHaveBeenCalledWith(42, { activeSeconds: 120 });
    expect(sessionStorage.getItem('activity.activeSeconds:42')).toBeNull();
  });

  it('i572_Finished_ShowsTodayAndBadgeCards_NoPoints', async () => {
    summaryResponse = {
      today: { activityCount: 2, activeMinutes: 14, streakDays: 3 },
      badges: [{ code: 'activity-count-1', name: 'Kâşif', current: 8, target: 10, earned: false }],
    };
    await create(instance({ status: TestStatus.Completed, checkCount: 1 }));

    expect(badgeService.getActivitySummary).toHaveBeenCalledWith(77);
    expect(q('today-card')?.textContent).toContain('2 etkinlik · 14 dk etkin süre · 3 gün üst üste');
    expect(q('badge-card')?.textContent).toContain('Kâşif rozeti: 8/10 etkinlik');
    expect(q('activity-summary')?.textContent).not.toMatch(/%|puan|XP/i);
  });

  it('i572_SummaryFailure_HidesCards_NoToast', async () => {
    summaryResponse = new HttpErrorResponse({ status: 500 });
    await create(instance({ status: TestStatus.Completed, checkCount: 1 }));

    expect(q('activity-summary')).not.toBeNull();
    expect(q('today-card')).toBeNull();
    expect(q('badge-card')).toBeNull();
    expect(document.querySelector('mat-snack-bar-container')).toBeNull();
  });

  it('i572_InProgress_DoesNotFetchSummary', async () => {
    await create();

    expect(badgeService.getActivitySummary).not.toHaveBeenCalled();
  });

  it('i572_AfterFinish_RefetchesSummaryOnceAfterDelay', async () => {
    await create(instance({ checkCount: 1 }));
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    summaryResponse = { today: { activityCount: 0, activeMinutes: 0, streakDays: 0 }, badges: [] };

    await fixture.componentInstance.finish();
    fixture.detectChanges();
    expect(badgeService.getActivitySummary).toHaveBeenCalledTimes(1);
    expect(q('today-card')).toBeNull();

    summaryResponse = { today: { activityCount: 1, activeMinutes: 3, streakDays: 1 }, badges: [] };
    await vi.advanceTimersByTimeAsync(ACTIVITY_SUMMARY_REFETCH_MS);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(20_000);

    // Artış görüldü: izleme durdu (2 sn'deki okumadan sonra başka istek yok).
    expect(badgeService.getActivitySummary).toHaveBeenCalledTimes(2);
    expect(q('today-card')?.textContent).toContain('1 etkinlik · 3 dk etkin süre · 1 gün üst üste');
  });

  // #575 E2E: rozet işlemesi 2 sn'den geç biterse 5 ve 10. saniyede yeniden okunur; artınca durur.
  it('i572_AfterFinish_BacksOffAt2_5_10s_StopsOnIncrease', async () => {
    await create(instance({ checkCount: 1 }));
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    summaryResponse = { today: { activityCount: 1, activeMinutes: 5, streakDays: 1 }, badges: [] };

    await fixture.componentInstance.finish();
    expect(badgeService.getActivitySummary).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1999);
    expect(badgeService.getActivitySummary).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(badgeService.getActivitySummary).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(2999);
    expect(badgeService.getActivitySummary).toHaveBeenCalledTimes(2);

    summaryResponse = { today: { activityCount: 2, activeMinutes: 9, streakDays: 1 }, badges: [] };
    await vi.advanceTimersByTimeAsync(1);
    expect(badgeService.getActivitySummary).toHaveBeenCalledTimes(3);
    await vi.advanceTimersByTimeAsync(30_000);
    expect(badgeService.getActivitySummary).toHaveBeenCalledTimes(3);
    fixture.detectChanges();
    expect(q('today-card')?.textContent).toContain('2 etkinlik');
  });

  it('i572_AfterFinish_NoChange_StopsAfterThreeRefetches', async () => {
    await create(instance({ checkCount: 1 }));
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    summaryResponse = { today: { activityCount: 1, activeMinutes: 5, streakDays: 1 }, badges: [] };

    await fixture.componentInstance.finish();
    await vi.advanceTimersByTimeAsync(60_000);

    expect(badgeService.getActivitySummary).toHaveBeenCalledTimes(4);
  });

  it('i572_AfterFinish_DestroyCancelsPendingRefetch', async () => {
    await create(instance({ checkCount: 1 }));
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });

    await fixture.componentInstance.finish();
    fixture.destroy();
    await vi.advanceTimersByTimeAsync(60_000);

    expect(badgeService.getActivitySummary).toHaveBeenCalledTimes(1);
  });

  // Issue #575: cevap kutusu maddesi — oynatıcı, eksik kutu, kontrol gövdesi, kutu satırları ve çözüm.
  describe('slotFill (#575)', () => {
    const SLOT_PLAN = JSON.stringify({
      version: 1,
      type: 'slotFill',
      slots: [
        { id: 's1', box: { x: 0, y: 0, width: 50, height: 20 }, input: { kind: 'number', entry: 'fraction' } },
        { id: 's2', box: { x: 60, y: 0, width: 50, height: 20 }, input: { kind: 'select', options: [{ id: 'o1', label: 'artar' }, { id: 'o2', label: 'azalır' }] } },
      ],
    });
    const PARTIAL = JSON.stringify({ version: 1, type: 'slotFill', values: { s1: '3/4' } });
    const slotInstance = (overrides: Partial<TestInstance> = {}) =>
      instance(overrides, [{ answerPayload: PARTIAL, question: { ...instance().testInstanceQuestions[0].question, interactionType: 'SlotFill', interactionPlan: SLOT_PLAN } }]);
    const rowTexts = () => Array.from(el().querySelectorAll('[data-testid="result-row"]')).map((r) => r.textContent ?? '');

    it('rendersSlotFillPlayer_AnyEmptySlotIsBlankItem', async () => {
      await create(slotInstance());
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-slot-fill')).not.toBeNull();
      expect(el().querySelector('app-question-canvas-matrix-choice')).toBeNull();
      expect(fixture.componentInstance.steps()[0].state).toBe('answered');
      expect(fixture.componentInstance.blankItems()).toContain('1');
      expect(fixture.componentInstance.blankKind()).toBe('generic');
    });

    it('change_SavesPayloadAndCheckSendsIt_RowsShowValuesAndMarks', async () => {
      await create(slotInstance());
      fixture.componentInstance.goTo(0);
      await settle();
      const item = fixture.componentInstance.items()[0];
      const both = JSON.stringify({ version: 1, type: 'slotFill', values: { s1: '3/4', s2: 'o1' } });
      fixture.componentInstance.onDragDropChange(both, item);
      await settle();
      expect(testService['saveAnswer']).toHaveBeenCalledWith(expect.objectContaining({ testQuestionId: 101, answerPayload: both }));
      expect(fixture.componentInstance.blankItems()).not.toContain('1');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 1,
          totalCount: 3,
          questions: [
            { instanceQuestionId: 101, isCorrect: false, correctCount: 1, totalCount: 2, feedback: '{"rows":[{"id":"s1","correct":true},{"id":"s2","correct":false}]}' },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();

      expect(activityService.check.mock.lastCall![1][0]).toEqual({ instanceQuestionId: 101, selectedAnswerId: null, answerPayload: both });
      const rows = rowTexts();
      expect(rows[0]).toContain('Kutu 1: 3/4');
      expect(rows[1]).toContain('Kutu 2: artar');
      expect(el().querySelectorAll('.sf-slot.is-correct').length).toBe(1);
      expect(el().querySelectorAll('.sf-slot.is-wrong').length).toBe(1);
      expect(el().textContent).not.toMatch(/%|puan|XP/i);
    });

    it('samePlanTwoItems_TypeInA_GoToEmptyB_BackToA_KeepsAValue (#583 review)', async () => {
      const TEXT_PLAN = JSON.stringify({
        version: 1,
        type: 'slotFill',
        slots: [{ id: 's1', box: { x: 0, y: 0, width: 50, height: 20 }, input: { kind: 'text' } }],
      });
      const base = instance().testInstanceQuestions[0].question;
      await create(
        instance({}, [
          { answerPayload: undefined, question: { ...base, id: 1, interactionType: 'slotFill', interactionPlan: TEXT_PLAN } },
          { answerPayload: undefined, question: { ...base, id: 2, interactionType: 'slotFill', interactionPlan: TEXT_PLAN } },
        ])
      );
      const field = () => el().querySelector('[data-slot-input="s1"]') as HTMLInputElement;
      fixture.componentInstance.goTo(0);
      await settle();
      field().value = 'elma';
      field().dispatchEvent(new Event('input'));

      fixture.componentInstance.goTo(1);
      await settle();
      expect(field().value).toBe('');
      fixture.componentInstance.goTo(0);
      await settle();

      expect(field().value).toBe('elma');
      expect(testService['saveAnswer']).toHaveBeenCalledWith(
        expect.objectContaining({ testQuestionId: 101, answerPayload: expect.stringContaining('elma') })
      );
      expect(fixture.componentInstance.answerFor(fixture.componentInstance.items()[1])?.answerPayload ?? null).toBeNull();
    });

    it('check_FlushesTypedButUnsavedSlotValueFirst', async () => {
      await create(slotInstance());
      fixture.componentInstance.goTo(0);
      await settle();
      const input = el().querySelector('[data-slot-input="s1"] [data-part="denominator"]') as HTMLInputElement;
      input.value = '8';
      input.dispatchEvent(new Event('input'));

      await fixture.componentInstance.runCheck();

      const sent = JSON.parse(activityService.check.mock.lastCall![1][0].answerPayload);
      expect(sent.values.s1).toBe('3/8');
    });

    it('reveal_RowsShowAcceptedValuesAndPlayerShowsSolution', async () => {
      await create(slotInstance({ checkCount: 1 }));
      activityService.getSolutions.mockReturnValue(
        of({
          success: true,
          questions: [
            { instanceQuestionId: 101, questionId: 1, correctAnswerId: null, interactionSolution: '{"s1":{"value":"3/4"},"s2":{"optionIds":["o2"]}}' },
            { instanceQuestionId: 102, questionId: 2, correctAnswerId: 11, interactionSolution: null },
          ],
        })
      );
      fixture.componentInstance.goTo(0);

      await fixture.componentInstance.reveal();
      await settle();

      const rows = fixture.componentInstance.currentRows();
      expect(rows.map((r) => r.solution)).toEqual(['3/4', 'azalır']);
      expect(rows[1].label).toBe('Kutu 2: boş');
      expect(el().querySelector('[data-solution="s2"]')?.textContent?.trim()).toBe('azalır');
    });
  });

  describe('textSelect (#582)', () => {
    const box = (x: number) => ({ x, y: 0, width: 20, height: 10 });
    const CORRECT_PLAN = JSON.stringify({
      version: 1,
      type: 'textSelect',
      mode: 'correct',
      style: 'underline',
      tokens: [
        { id: 't1', text: 'Sonun', line: 0, boxes: [box(0)] },
        { id: 't2', text: 'da', line: 0, boxes: [box(30)] },
        { id: 't3', text: 'geldi', line: 0, boxes: [box(60)] },
      ],
    });
    const PUNCT_PLAN = JSON.stringify({
      version: 1,
      type: 'textSelect',
      mode: 'punctuation',
      palette: ['.', '!'],
      tokens: [
        { id: 't1', text: 'Eyvah', line: 0, boxes: [box(0)] },
        { id: 'g1', text: '( )', line: 0, gap: true, boxes: [box(30)] },
        { id: 'g2', text: '( )', line: 0, gap: true, boxes: [box(60)] },
      ],
    });
    const textInstance = (plan: string, answer: string | null, overrides: Partial<TestInstance> = {}) =>
      instance(overrides, [
        { answerPayload: answer, question: { ...instance().testInstanceQuestions[0].question, interactionType: 'textSelect', interactionPlan: plan } },
      ]);
    const rowTexts = () => Array.from(el().querySelectorAll('[data-testid="result-row"]')).map((r) => r.textContent ?? '');
    /** Görsel ya da (dar ekranda varsayılan) metin görünümündeki token düğmesi. */
    const token = (id: string) => el().querySelector(`[data-token="${id}"], [data-word="${id}"]`) as HTMLElement | null;

    it('correct_SelectionWithoutFixIsBlank_CheckSendsPayload_RowsAndPlayerMarksWithCounter', async () => {
      const selected = JSON.stringify({ version: 1, type: 'textSelect', selected: ['t2'], values: {} });
      await create(textInstance(CORRECT_PLAN, selected));
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-text-select')).not.toBeNull();
      expect(fixture.componentInstance.steps()[0].state).toBe('answered');
      expect(fixture.componentInstance.blankItems()).toContain('1');

      const item = fixture.componentInstance.items()[0];
      const fixed = JSON.stringify({ version: 1, type: 'textSelect', selected: ['t2', 't3'], values: { t2: 'Sonunda' } });
      fixture.componentInstance.onDragDropChange(fixed, item);
      await settle();
      expect(testService['saveAnswer']).toHaveBeenCalledWith(expect.objectContaining({ testQuestionId: 101, answerPayload: fixed }));

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 1,
          totalCount: 4,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 1,
              totalCount: 3,
              feedback: '{"rows":[{"id":"t2","correct":true},{"id":"t3","correct":false,"reason":"extra"}],"found":1,"missing":1,"extra":1}',
            },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();

      expect(activityService.check.mock.lastCall![1][0]).toEqual({ instanceQuestionId: 101, selectedAnswerId: null, answerPayload: fixed });
      const rows = rowTexts();
      expect(rows[0]).toContain('«da» → Sonunda');
      expect(rows[1]).toContain('«geldi» (fazla)');
      expect(token('t2')?.classList).toContain('is-correct');
      expect(token('t3')?.classList).toContain('is-extra');
      expect(el().querySelector('[data-testid="ts-counter"]')?.textContent).toBe('Bulunan 1 · Kalan 1 · Fazla 1');
      expect(el().textContent).not.toMatch(/%|puan|XP/i);
    });

    it('correct_MultiWordTargetWithFixOnOneWord_IsNotBlank_TooManyFeedbackReachesPlayer', async () => {
      const answer = JSON.stringify({ version: 1, type: 'textSelect', selected: ['t1', 't2'], values: { t1: 'Sonunda' } });
      await create(textInstance(CORRECT_PLAN, answer));
      fixture.componentInstance.goTo(0);
      await settle();
      expect(fixture.componentInstance.blankItems()).not.toContain('1');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 0,
          totalCount: 1,
          questions: [
            { instanceQuestionId: 101, isCorrect: false, correctCount: 0, totalCount: 5, feedback: '{"rows":[],"found":0,"missing":1,"extra":4,"tooMany":true}' },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();
      expect(fixture.componentInstance.currentTextSelectFeedback()).toEqual({ rows: [], found: 0, missing: 1, extra: 4, tooMany: true });
      expect(q('ts-too-many')).not.toBeNull();
    });

    it('check_FlushesTypedButUnsavedCorrectionFirst', async () => {
      const selected = JSON.stringify({ version: 1, type: 'textSelect', selected: ['t1'], values: {} });
      await create(textInstance(CORRECT_PLAN, selected));
      fixture.componentInstance.goTo(0);
      await settle();
      token('t1')!.click(); // çubuğu açar
      await settle();
      const input = el().querySelector('[data-testid="ts-correction"]') as HTMLInputElement;
      input.value = 'Sonunda';
      input.dispatchEvent(new Event('input'));
      activityService.check.mockReturnValue(of({ success: true, correctCount: 0, totalCount: 0, questions: [] }));

      await fixture.componentInstance.runCheck();

      const sent = JSON.parse(activityService.check.mock.lastCall![1][0].answerPayload);
      expect(sent.values).toEqual({ t1: 'Sonunda' });
    });

    it('punctuation_EmptyGapIsIncomplete_RevealRowsShowAcceptedSymbols', async () => {
      const partial = JSON.stringify({ version: 1, type: 'textSelect', values: { g1: '!' } });
      await create(textInstance(PUNCT_PLAN, partial, { checkCount: 1 }));
      fixture.componentInstance.goTo(0);
      await settle();
      expect(fixture.componentInstance.blankItems()).toContain('1');

      activityService.getSolutions.mockReturnValue(
        of({
          success: true,
          questions: [
            { instanceQuestionId: 101, questionId: 1, correctAnswerId: null, interactionSolution: '{"gaps":{"g1":{"accept":["!"]},"g2":{"accept":["."]}}}' },
            { instanceQuestionId: 102, questionId: 2, correctAnswerId: 11, interactionSolution: null },
          ],
        })
      );
      await fixture.componentInstance.reveal();
      await settle();

      const rows = fixture.componentInstance.currentRows();
      expect(rows.map((r) => [r.label, r.solution])).toEqual([
        ['Boşluk 1: !', '!'],
        ['Boşluk 2: boş', '.'],
      ]);
      expect(token('g2')?.classList).toContain('is-solution');
    });
  });

  // Issue #574: matris seçim maddesi — oynatıcı, kontrol gövdesi, satır sonuçları ve çözüm satırları.
  describe('matrixChoice (#574)', () => {
    const MATRIX_PLAN = JSON.stringify({
      version: 1,
      type: 'matrixChoice',
      multiPerRow: false,
      columns: [{ id: 'c1', label: 'D' }, { id: 'c2', label: 'Y' }],
      rows: [
        { id: 'r1', label: 'Kuşlar uçar.', box: { x: 0, y: 0, width: 100, height: 20 } },
        { id: 'r2', box: { x: 0, y: 30, width: 100, height: 20 } },
      ],
    });
    const ANSWER_R1 = JSON.stringify({ version: 1, type: 'matrixChoice', selections: { r1: ['c2'] } });
    const matrixInstance = (overrides: Partial<TestInstance> = {}) =>
      instance(overrides, [{ answerPayload: ANSWER_R1, question: { ...instance().testInstanceQuestions[0].question, interactionType: 'matrixChoice', interactionPlan: MATRIX_PLAN } }]);
    const rowTexts = () => Array.from(el().querySelectorAll('[data-testid="result-row"]')).map((r) => r.textContent ?? '');

    it('rendersMatrixPlayer_PartialAnswerIsBlankItem', async () => {
      await create(matrixInstance());
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-matrix-choice')).not.toBeNull();
      expect(el().querySelector('app-question-canvas-dragdrop-labeling')).toBeNull();
      expect(fixture.componentInstance.steps()[0].state).toBe('answered');
      expect(fixture.componentInstance.blankItems()).toContain('1');
    });

    it('blankWarning_MatrixUsesNeutralWording_DragDropOnlyKeepsPlacementText', async () => {
      await create(matrixInstance());
      await settle();
      expect(fixture.componentInstance.blankKind()).toBe('generic');
      expect(q('blank-warning')?.textContent).toContain('boş bırakılan yer');

      fixture.destroy();
      TestBed.resetTestingModule();
      await create(instance({}, [{}, { selectedAnswerId: 11 }]));
      await settle();
      expect(fixture.componentInstance.blankKind()).toBe('placement');
      expect(q('blank-warning')?.textContent).toContain('boş yerleştirme');
    });

    it('change_SavesPayloadAndCheckSendsIt_RowsShowSelectionAndMarks', async () => {
      await create(matrixInstance());
      fixture.componentInstance.goTo(0);
      await settle();
      const item = fixture.componentInstance.items()[0];
      const both = JSON.stringify({ version: 1, type: 'matrixChoice', selections: { r1: ['c2'], r2: ['c1'] } });
      fixture.componentInstance.onDragDropChange(both, item);
      await settle();
      expect(testService['saveAnswer']).toHaveBeenCalledWith(expect.objectContaining({ testQuestionId: 101, answerPayload: both }));
      expect(fixture.componentInstance.blankItems()).not.toContain('1');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 1,
          totalCount: 3,
          questions: [
            { instanceQuestionId: 101, isCorrect: false, correctCount: 1, totalCount: 2, feedback: '{"rows":[{"id":"r1","correct":false},{"id":"r2","correct":true}]}' },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();

      expect(activityService.check.mock.lastCall![1][0]).toEqual({ instanceQuestionId: 101, selectedAnswerId: null, answerPayload: both });
      // Liste kipinde satırlar ✓/✗'yi kendi üstünde gösterir; panelde tekrar liste yok.
      expect(rowTexts()).toEqual([]);
      expect(el().querySelectorAll('.mc-list__row.is-wrong').length).toBe(1);
      expect(el().querySelectorAll('.mc-list__row.is-correct').length).toBe(1);
      expect(el().textContent).not.toMatch(/%|puan|XP/i);
    });

    it('reveal_RowsShowSolutionColumnsAndPlayerShowsSolution', async () => {
      await create(matrixInstance({ checkCount: 1 }));
      activityService.getSolutions.mockReturnValue(
        of({
          success: true,
          questions: [
            { instanceQuestionId: 101, questionId: 1, correctAnswerId: null, interactionSolution: '{"r1":["c1"],"r2":["c2"]}' },
            { instanceQuestionId: 102, questionId: 2, correctAnswerId: 11, interactionSolution: null },
          ],
        })
      );
      fixture.componentInstance.goTo(0);

      await fixture.componentInstance.reveal();
      await settle();

      expect(fixture.componentInstance.currentRows()).toEqual([]);
      expect(el().querySelector('[data-cell="r1:c1"]')?.getAttribute('aria-pressed')).toBe('true');
      expect(el().querySelector('[data-cell="r1:c2"]')?.getAttribute('aria-pressed')).toBe('false');
    });

    it('fixWrong_ClearsOnlyWrongRows_KeepsCorrectAndBlank_GoesToFirstWrongItem', async () => {
      const both = JSON.stringify({ version: 1, type: 'matrixChoice', selections: { r1: ['c2'], r2: ['c1'] } });
      await create(
        instance({}, [
          { answerPayload: both, question: { ...instance().testInstanceQuestions[0].question, interactionType: 'matrixChoice', interactionPlan: MATRIX_PLAN } },
        ])
      );
      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 1,
          totalCount: 3,
          questions: [
            { instanceQuestionId: 101, isCorrect: false, correctCount: 1, totalCount: 2, feedback: '{"rows":[{"id":"r1","correct":false},{"id":"r2","correct":true}]}' },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      fixture.componentInstance.goTo(1);
      await settle();

      fixture.componentInstance.fixWrong();
      await settle();

      expect(testService['saveAnswer']).toHaveBeenLastCalledWith(
        expect.objectContaining({ testQuestionId: 101, answerPayload: JSON.stringify({ version: 1, type: 'matrixChoice', selections: { r2: ['c1'] } }) })
      );
      expect(fixture.componentInstance.currentIndex()).toBe(0);
      // MCQ maddesi (102) bu sürümde yalnız gezinilir; cevabına dokunulmaz.
      expect(testService['saveAnswer']).not.toHaveBeenCalledWith(expect.objectContaining({ testQuestionId: 102 }));
    });

    it('cellMode_PanelRowsShowSelectionWithArrowAndSolution', async () => {
      const box = (y: number) => ({ x: 0, y, width: 10, height: 10 });
      const cellPlan = JSON.stringify({
        version: 1,
        type: 'matrixChoice',
        multiPerRow: false,
        columns: [{ id: 'c1', label: 'D' }, { id: 'c2', label: 'Y' }],
        rows: [
          { id: 'r1', label: 'Kuşlar uçar.', cells: { c1: box(0), c2: box(0) } },
          { id: 'r2', cells: { c1: box(20), c2: box(20) } },
        ],
      });
      await create(
        instance({ checkCount: 1 }, [
          { answerPayload: ANSWER_R1, question: { ...instance().testInstanceQuestions[0].question, interactionType: 'matrixChoice', interactionPlan: cellPlan } },
        ])
      );
      activityService.getSolutions.mockReturnValue(
        of({
          success: true,
          questions: [
            { instanceQuestionId: 101, questionId: 1, correctAnswerId: null, interactionSolution: '{"r1":["c1"],"r2":["c2"]}' },
            { instanceQuestionId: 102, questionId: 2, correctAnswerId: 11, interactionSolution: null },
          ],
        })
      );
      fixture.componentInstance.goTo(0);

      await fixture.componentInstance.reveal();
      await settle();

      const rows = fixture.componentInstance.currentRows();
      expect(rows.map((r) => r.label)).toEqual(['Kuşlar uçar. → Y', 'Satır 2 → boş']);
      expect(rows.map((r) => r.solution)).toEqual(['D', 'Y']);
    });
  });

  describe('dragPlace + match (#576)', () => {
    const DP_PLAN = JSON.stringify({
      version: 1,
      type: 'dragPlace',
      mode: 'place',
      reuse: false,
      targets: [{ id: 't1', label: 'Erime', capacity: 2 }, { id: 't2', label: 'Donma', capacity: 1 }],
      cards: [{ id: 'c1', label: 'Buz' }, { id: 'c2', label: 'Mum' }, { id: 'c3', label: 'Su' }],
    });
    const DP_PARTIAL = JSON.stringify({ version: 1, type: 'dragPlace', placements: [{ targetId: 't1', cardId: 'c1' }, { targetId: 't1', cardId: 'c3' }] });
    const MT_PLAN = JSON.stringify({
      version: 1,
      type: 'match',
      left: [{ id: 'l1', label: 'Deprem' }, { id: 'l2', label: 'Sel' }, { id: 'l3', label: 'Çığ' }],
      right: [{ id: 'r1', label: 'Fay' }, { id: 'r2', label: 'Yağmur' }, { id: 'r3', label: 'Kar' }, { id: 'r4', label: 'Rüzgâr' }],
      prelinked: [{ leftId: 'l1', rightId: 'r1' }],
    });
    const MT_PARTIAL = JSON.stringify({ version: 1, type: 'match', pairs: [{ leftId: 'l2', rightId: 'r3' }] });
    const base = () => instance().testInstanceQuestions[0].question;
    const typed = (type: string, plan: string, answer: string, overrides: Partial<TestInstance> = {}) =>
      instance(overrides, [{ answerPayload: answer, question: { ...base(), interactionType: type, interactionPlan: plan } }]);
    const rowTexts = () => Array.from(el().querySelectorAll('[data-testid="result-row"]')).map((r) => r.textContent ?? '');

    it('dragPlace_RendersPlayer_IncompleteOnlyWhenNothingPlacedOrOrderGap_NeutralWording', async () => {
      await create(typed('DragPlace', DP_PLAN, DP_PARTIAL));
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-drag-place')).not.toBeNull();
      expect(el().querySelector('app-question-canvas-dragdrop-labeling')).toBeNull();
      expect(fixture.componentInstance.steps()[0].state).toBe('answered');
      // Boş kalan kova (çeldirici / boş olması doğru olabilir) eksik sayılmaz.
      expect(fixture.componentInstance.blankItems()).not.toContain('1');
      const item = fixture.componentInstance.items()[0];
      fixture.componentInstance.onDragDropChange(JSON.stringify({ version: 1, type: 'dragPlace', placements: [] }), item);
      await settle();
      expect(fixture.componentInstance.blankItems()).toContain('1');
      expect(fixture.componentInstance.blankKind()).toBe('generic');
    });

    it('dragPlace_OrderModeGapIsIncomplete', async () => {
      const order = JSON.stringify({
        version: 1,
        type: 'dragPlace',
        mode: 'order',
        targets: [{ id: 't1', capacity: 1 }, { id: 't2', capacity: 1 }],
        cards: [{ id: 'c1', label: 'A' }, { id: 'c2', label: 'B' }],
      });
      await create(typed('dragPlace', order, JSON.stringify({ version: 1, type: 'dragPlace', placements: [{ targetId: 't1', cardId: 'c2' }] })));
      expect(fixture.componentInstance.blankItems()).toContain('1');
    });

    it('dragPlace_CheckSendsPayload_RowsAggregatePerTarget_IncludingMissingCardRow', async () => {
      await create(typed('dragPlace', DP_PLAN, DP_PARTIAL));
      fixture.componentInstance.goTo(0);
      await settle();
      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 1,
          totalCount: 4,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 1,
              totalCount: 3,
              feedback: '{"rows":[{"id":"t1:c1","correct":true},{"id":"t1:","correct":false},{"id":"t2:","correct":false},{"id":"t1:c3","correct":false}]}',
            },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();

      expect(activityService.check.mock.lastCall![1][0]).toEqual({ instanceQuestionId: 101, selectedAnswerId: null, answerPayload: DP_PARTIAL });
      // Kutusuz hedefler (görselsiz sınıflandırma): kartlar ✓/✗'yi yerinde gösterir, panelde tekrar liste yok.
      expect(fixture.componentInstance.currentRows()).toEqual([]);
      expect(rowTexts()).toEqual([]);
      expect(el().querySelector('[data-placed="t1:c3"]')?.classList).toContain('is-wrong');
      expect(el().querySelector('[data-group="t2"]')?.classList).toContain('is-incomplete');

      // "Yanlışları düzelt": yalnız yanlış yerleşen kart havuza döner, madde değişir: sonucu düşer, yeni cevap kaydedilir.
      fixture.componentInstance.fixWrong();
      await settle();
      expect(testService['saveAnswer']).toHaveBeenLastCalledWith(
        expect.objectContaining({ testQuestionId: 101, answerPayload: JSON.stringify({ version: 1, type: 'dragPlace', placements: [{ targetId: 't1', cardId: 'c1' }] }) })
      );
      expect(fixture.componentInstance.results()[101]).toBeUndefined();
      // Diğer kartların işaretleri kalır; taşınan kart havuzda işaretsiz. Bayat "eksik" işaretleri (t1:, t2:) kalkar.
      expect(el().querySelector('[data-placed="t1:c1"]')?.classList).toContain('is-correct');
      expect(el().querySelector('[data-group="t2"]')?.classList).not.toContain('is-incomplete');
      expect(fixture.componentInstance.retainedRows()[101].map((r) => r.id)).toEqual(['t1:c1', 't1:c3']);
      expect(el().querySelector('[data-testid="dp-pool"] [data-card="c3"]')).not.toBeNull();
      expect(fixture.componentInstance.stale()).toBe(true);
    });

    it('lockedWhileChecking_PlayersGetDisabled', async () => {
      await create(typed('dragPlace', DP_PLAN, DP_PARTIAL));
      fixture.componentInstance.goTo(0);
      fixture.componentInstance.checking.set(true);
      await settle();
      const player = fixture.debugElement.query((d) => d.name === 'app-question-canvas-drag-place').componentInstance;
      expect(player.locked()).toBe(true);
      fixture.componentInstance.checking.set(false);
      await settle();
      expect(player.locked()).toBe(false);
    });

    it('dragPlace_RevealShowsSolutionPerTarget', async () => {
      await create(typed('dragPlace', DP_PLAN, DP_PARTIAL, { checkCount: 1 }));
      activityService.getSolutions.mockReturnValue(
        of({
          success: true,
          questions: [
            { instanceQuestionId: 101, questionId: 1, correctAnswerId: null, interactionSolution: '{"t1":["c1","c2"],"t2":["c3"]}' },
            { instanceQuestionId: 102, questionId: 2, correctAnswerId: 11, interactionSolution: null },
          ],
        })
      );
      fixture.componentInstance.goTo(0);
      await fixture.componentInstance.reveal();
      await settle();

      expect(fixture.componentInstance.currentRows()).toEqual([]);
      expect(el().querySelector('[data-placed="t2:c3"]')).not.toBeNull();
    });

    it('match_RendersPlayer_UnpairedLeftIsIncomplete_RowsSkipExample', async () => {
      await create(typed('Match', MT_PLAN, MT_PARTIAL));
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-match')).not.toBeNull();
      expect(fixture.componentInstance.blankItems()).toContain('1');
      expect(fixture.componentInstance.blankKind()).toBe('generic');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 0,
          totalCount: 3,
          questions: [
            { instanceQuestionId: 101, isCorrect: false, correctCount: 0, totalCount: 2, feedback: '{"rows":[{"id":"l2","correct":false},{"id":"l3","correct":false}]}' },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();
      expect(fixture.componentInstance.currentRows().map((r) => [r.label, r.correct])).toEqual([
        ['Sel: Kar', false],
        ['Çığ: boş', false],
      ]);

      const full = JSON.stringify({ version: 1, type: 'match', pairs: [{ leftId: 'l2', rightId: 'r2' }, { leftId: 'l3', rightId: 'r3' }] });
      fixture.componentInstance.onDragDropChange(full, fixture.componentInstance.items()[0]);
      await settle();
      expect(fixture.componentInstance.blankItems()).not.toContain('1');
    });
  });

  describe('logicGrid (#586)', () => {
    const LG_PLAN = JSON.stringify({
      version: 1,
      type: 'logicGrid',
      subject: { label: 'Çocuk', items: [{ id: 'p1', label: 'Ali' }, { id: 'p2', label: 'Ayşe' }] },
      categories: [{ id: 'c1', label: 'Renk', items: [{ id: 'o1', label: 'Mavi' }, { id: 'o2', label: 'Kırmızı' }] }],
      clues: [],
    });
    const lgCells = (...list: [string, string, string][]) =>
      JSON.stringify({ version: 1, type: 'logicGrid', cells: list.map(([row, col, mark]) => ({ row, col, mark })) });
    const base = () => instance().testInstanceQuestions[0].question;
    const typed = (answer: string) =>
      instance({}, [{ answerPayload: answer, question: { ...base(), interactionType: 'LogicGrid', interactionPlan: LG_PLAN } }]);

    it('rendersPlayer_MissingTickIsIncomplete_PairRowsAndFeedback', async () => {
      await create(typed(lgCells(['p1', 'o2', 'yes'])));
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-logic-grid')).not.toBeNull();
      expect(fixture.componentInstance.blankItems()).toContain('1');

      fixture.componentInstance.onDragDropChange(lgCells(['p1', 'o2', 'yes'], ['p2', 'o2', 'yes']), fixture.componentInstance.items()[0]);
      await settle();
      expect(fixture.componentInstance.blankItems()).not.toContain('1');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 1,
          totalCount: 3,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 1,
              totalCount: 2,
              feedback: '{"rows":[{"id":"p1:c1","correct":true},{"id":"p2:c1","correct":false}]}',
            },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();
      expect(fixture.componentInstance.currentRows().map((r) => [r.label, r.correct])).toEqual([
        ['Ali – Renk: Kırmızı', true],
        ['Ayşe – Renk: Kırmızı', false],
      ]);
      expect(fixture.componentInstance.currentLogicGridFeedback()?.rows.length).toBe(2);
    });
  });

  describe('numberLine (#588)', () => {
    const NL_PLAN = JSON.stringify({
      version: 1,
      type: 'numberLine',
      divisions: 4,
      labels: [
        { at: 0, text: '0' },
        { at: 4, text: '1' },
      ],
      cards: [
        { id: 'k1', label: '1/4' },
        { id: 'k2', label: '5/4' },
      ],
    });
    const nlAnswer = (placed: Record<string, number>) => JSON.stringify({ version: 1, type: 'numberLine', placed });
    const base = () => instance().testInstanceQuestions[0].question;
    const typed = (answer: string) =>
      instance({}, [{ answerPayload: answer, question: { ...base(), interactionType: 'NumberLine', interactionPlan: NL_PLAN } }]);

    it('rendersPlayer_BlankIsIncomplete_CardRowsAndFeedback', async () => {
      await create(typed(nlAnswer({})));
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-number-line')).not.toBeNull();
      expect(fixture.componentInstance.blankItems()).toContain('1');

      fixture.componentInstance.onDragDropChange(nlAnswer({ k1: 2, k2: 4 }), fixture.componentInstance.items()[0]);
      await settle();
      expect(fixture.componentInstance.blankItems()).not.toContain('1');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 0,
          totalCount: 3,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 0,
              totalCount: 2,
              feedback: '{"rows":[{"id":"k1","correct":false},{"id":"k2","correct":false}]}',
            },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();
      const rows = fixture.componentInstance.currentRows();
      expect(rows.map((r) => r.correct)).toEqual([false, false]);
      expect(rows[0].label).toContain('1/4');
      expect(rows[0].label).toContain('2/4');
      expect(rows[1].label).toContain('1');
      expect(fixture.componentInstance.currentNumberLineFeedback()?.rows.length).toBe(2);
    });
  });

  describe('clockSet (#634)', () => {
    const CS_PLAN = JSON.stringify({
      version: 1,
      type: 'clockSet',
      clocks: [{ id: 'c0', fixed: '09:20' }, { id: 'c1', label: 'Bitiş' }, { id: 'c2' }],
    });
    const csAnswer = (clocks: object) => JSON.stringify({ version: 1, type: 'clockSet', clocks });
    const base = () => instance().testInstanceQuestions[0].question;
    const typed = (answer: string) =>
      instance({}, [{ answerPayload: answer, question: { ...base(), interactionType: 'ClockSet', interactionPlan: CS_PLAN } }]);

    it('rendersPlayer_UnsetClockIsIncomplete_RowsAndFeedback', async () => {
      await create(typed(csAnswer({ c1: { h: 600, m: 0 } })));
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-clock-set')).not.toBeNull();
      expect(fixture.componentInstance.blankItems()).toContain('1'); // c2 kurulmadı

      fixture.componentInstance.onDragDropChange(csAnswer({ c1: { h: 600, m: 0 }, c2: { h: 180, m: 45 } }), fixture.componentInstance.items()[0]);
      await settle();
      expect(fixture.componentInstance.blankItems()).not.toContain('1');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 1,
          totalCount: 3,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 1,
              totalCount: 2,
              feedback: '{"rows":[{"id":"c1","correct":true},{"id":"c2","correct":false}]}',
            },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();
      const rows = fixture.componentInstance.currentRows();
      expect(rows.map((r) => r.correct)).toEqual([true, false]);
      expect(rows[0].label).toContain('10:00');
      expect(rows[1].label).toContain('2:45'); // akrep 3'te, yelkovan 45 → 2.45 okunur
      expect(fixture.componentInstance.currentClockSetFeedback()).toEqual({ c1: true, c2: false });
    });
  });

  describe('balanceScale (#632)', () => {
    const BS_PLAN = JSON.stringify({
      version: 1,
      type: 'balanceScale',
      unit: 'kg',
      cards: [
        { id: 'k1', value: 5 },
        { id: 'k2', value: 9 },
        { id: 'k3', value: 10 },
        { id: 'k4', value: 14 },
      ],
    });
    const bsAnswer = (left: string[], right: string[]) => JSON.stringify({ version: 1, type: 'balanceScale', left, right });
    const base = () => instance().testInstanceQuestions[0].question;
    const typed = (answer: string) =>
      instance({}, [{ answerPayload: answer, question: { ...base(), interactionType: 'BalanceScale', interactionPlan: BS_PLAN } }]);

    it('rendersPlayer_BlankIsIncomplete_RowAndFeedback', async () => {
      await create(typed(bsAnswer([], [])));
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-balance-scale')).not.toBeNull();
      expect(fixture.componentInstance.blankItems()).toContain('1');

      fixture.componentInstance.onDragDropChange(bsAnswer(['k1', 'k2'], ['k4']), fixture.componentInstance.items()[0]);
      await settle();
      expect(fixture.componentInstance.blankItems()).not.toContain('1');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 0,
          totalCount: 2,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 0,
              totalCount: 1,
              feedback: '{"rows":[{"id":"balance","correct":false}],"tilt":"level","unused":1}',
            },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();
      const rows = fixture.componentInstance.currentRows();
      expect(rows.map((r) => r.correct)).toEqual([false]);
      expect(rows[0].label).toContain('5 kg + 9 kg');
      expect(fixture.componentInstance.currentBalanceScaleFeedback()?.unused).toBe(1);
    });
  });

  describe('gridDraw (#588 dilim c)', () => {
    const GD_PLAN = JSON.stringify({ version: 1, type: 'gridDraw', rows: 10, cols: 16 });
    const gdAnswer = (shapes: object[]) => JSON.stringify({ version: 1, type: 'gridDraw', shapes });
    const base = () => instance().testInstanceQuestions[0].question;
    const typed = (answer: string) =>
      instance({}, [{ answerPayload: answer, question: { ...base(), interactionType: 'GridDraw', interactionPlan: GD_PLAN } }]);

    it('rendersPlayer_BlankIsIncomplete_ShapeRowsAndFeedback', async () => {
      await create(typed(gdAnswer([])));
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-grid-draw')).not.toBeNull();
      expect(fixture.componentInstance.blankItems()).toContain('1');

      const drawn = gdAnswer([
        { row: 0, col: 0, width: 4, height: 3 },
        { row: 5, col: 0, width: 3, height: 3 },
      ]);
      fixture.componentInstance.onDragDropChange(drawn, fixture.componentInstance.items()[0]);
      await settle();
      expect(fixture.componentInstance.blankItems()).not.toContain('1');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 1,
          totalCount: 5,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 1,
              totalCount: 4,
              feedback: '{"shapes":["ok","wrong"],"found":1,"missing":2,"extra":1}',
            },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();
      const rows = fixture.componentInstance.currentRows();
      expect(rows.map((r) => r.correct)).toEqual([true, false]);
      expect(rows[0].label).toContain('4 × 3');
      expect(fixture.componentInstance.currentGridDrawFeedback()?.missing).toBe(2);
    });
  });

  describe('barChart (#588 dilim b)', () => {
    const BC_PLAN = JSON.stringify({
      version: 1,
      type: 'barChart',
      max: 10,
      step: 2,
      unit: 1,
      categories: [
        { id: 'c1', label: 'Elma' },
        { id: 'c2', label: 'Armut' },
        { id: 'c3', label: 'Muz' },
      ],
      fixed: { c1: 6 },
    });
    const bcAnswer = (values: Record<string, number>) => JSON.stringify({ version: 1, type: 'barChart', values });
    const base = () => instance().testInstanceQuestions[0].question;
    const typed = (answer: string) =>
      instance({}, [{ answerPayload: answer, question: { ...base(), interactionType: 'BarChart', interactionPlan: BC_PLAN } }]);

    it('rendersPlayer_BlankIsIncomplete_FreeCategoryRowsAndFeedback', async () => {
      await create(typed(bcAnswer({})));
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-bar-chart')).not.toBeNull();
      expect(fixture.componentInstance.blankItems()).toContain('1');

      fixture.componentInstance.onDragDropChange(bcAnswer({ c2: 4 }), fixture.componentInstance.items()[0]);
      await settle();
      expect(fixture.componentInstance.blankItems()).not.toContain('1');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 1,
          totalCount: 3,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 1,
              totalCount: 2,
              feedback: '{"rows":[{"id":"c2","correct":true},{"id":"c3","correct":false}]}',
            },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();
      const rows = fixture.componentInstance.currentRows();
      expect(rows.map((r) => r.correct)).toEqual([true, false]);
      expect(rows[0].label).toContain('Armut');
      expect(rows[0].label).toContain('4');
      expect(rows[1].label).toContain('Muz');
      expect(fixture.componentInstance.currentBarChartFeedback()?.rows.length).toBe(2);
    });
  });

  describe('regionPaint (#587)', () => {
    const RP_PLAN = JSON.stringify({
      version: 1,
      type: 'regionPaint',
      mode: 'grid',
      rows: 2,
      cols: 2,
      palette: [
        { id: 'p1', color: 'blue', label: '1/2' },
        { id: 'p2', color: 'red', label: '1/4' },
      ],
    });
    const rpGrid = (...rows: string[]) => JSON.stringify({ version: 1, type: 'regionPaint', grid: rows });
    const base = () => instance().testInstanceQuestions[0].question;
    const typed = (answer: string) =>
      instance({}, [{ answerPayload: answer, question: { ...base(), interactionType: 'RegionPaint', interactionPlan: RP_PLAN } }]);

    it('rendersPlayer_BlankIsIncomplete_ColorRowsAndFeedback', async () => {
      await create(typed(rpGrid('..', '..')));
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-region-paint')).not.toBeNull();
      expect(fixture.componentInstance.blankItems()).toContain('1');

      fixture.componentInstance.onDragDropChange(rpGrid('11', '1.'), fixture.componentInstance.items()[0]);
      await settle();
      expect(fixture.componentInstance.blankItems()).not.toContain('1');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 1,
          totalCount: 3,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 0,
              totalCount: 2,
              feedback: '{"rows":[{"id":"p1","correct":false},{"id":"p2","correct":false}]}',
            },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();
      const rows = fixture.componentInstance.currentRows();
      expect(rows.map((r) => r.correct)).toEqual([false, false]);
      expect(rows[0].label).toContain('1/2');
      expect(rows[0].label).toContain('3');
      expect(fixture.componentInstance.currentRegionPaintFeedback()?.rows.length).toBe(2);
    });

    it('regionsMode_BlankIsIncomplete_RowsOnlyForPaintedRegions', async () => {
      const plan = JSON.stringify({
        version: 1,
        type: 'regionPaint',
        mode: 'regions',
        palette: [{ id: 'p1', color: 'blue' }],
        regions: [
          { id: 'r1', points: [[0, 0], [9, 0], [9, 9]], label: '12' },
          { id: 'r2', points: [[20, 0], [29, 0], [29, 9]] },
        ],
      });
      const fills = (f: Record<string, string>) => JSON.stringify({ version: 1, type: 'regionPaint', fills: f });
      await create(instance({}, [{ answerPayload: fills({}), question: { ...base(), interactionType: 'regionPaint', interactionPlan: plan } }]));
      fixture.componentInstance.goTo(0);
      await settle();
      expect(fixture.componentInstance.blankItems()).toContain('1');

      fixture.componentInstance.onDragDropChange(fills({ r2: 'p1' }), fixture.componentInstance.items()[0]);
      await settle();
      expect(fixture.componentInstance.blankItems()).not.toContain('1');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 0,
          totalCount: 3,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 0,
              totalCount: 2,
              feedback: '{"rows":[{"id":"r2","correct":false,"reason":"extra"}],"found":0,"missing":1,"extra":1}',
            },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();
      const rows = fixture.componentInstance.currentRows();
      expect(rows.length).toBe(1);
      expect(rows[0].correct).toBe(false);
      expect(fixture.componentInstance.currentRegionPaintFeedback()?.extra).toBe(1);
    });

    it('numbered_UnpaintedGroupIsIncomplete_RowsPerGroup', async () => {
      const plan = JSON.stringify({
        version: 1,
        type: 'regionPaint',
        mode: 'regions',
        palette: [{ id: 'p1', color: 'blue' }, { id: 'p2', color: 'red' }],
        groups: [{ id: 'g1', label: '1' }, { id: 'g2', label: '2' }],
        regions: [
          { id: 'r1', points: [[0, 0], [9, 0], [9, 9]], group: 'g1' },
          { id: 'r2', points: [[20, 0], [29, 0], [29, 9]], group: 'g2' },
        ],
      });
      const fills = (f: Record<string, string>) => JSON.stringify({ version: 1, type: 'regionPaint', fills: f });
      await create(
        instance({}, [{ answerPayload: fills({ g1: 'p1' }), question: { ...base(), interactionType: 'regionPaint', interactionPlan: plan } }])
      );
      fixture.componentInstance.goTo(0);
      await settle();
      expect(fixture.componentInstance.blankItems()).toContain('1');

      fixture.componentInstance.onDragDropChange(fills({ g1: 'p1', g2: 'p1' }), fixture.componentInstance.items()[0]);
      await settle();
      expect(fixture.componentInstance.blankItems()).not.toContain('1');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 1,
          totalCount: 3,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 1,
              totalCount: 2,
              feedback: '{"rows":[{"id":"g1","correct":true},{"id":"g2","correct":false}]}',
            },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();
      const rows = fixture.componentInstance.currentRows();
      expect(rows.map((r) => r.correct)).toEqual([true, false]);
      expect(rows[1].label).toContain('2');
    });
  });

  describe('wordSearch (#586)', () => {
    const WS_PLAN = JSON.stringify({
      version: 1,
      type: 'wordSearch',
      rows: 4,
      cols: 5,
      letters: ['KALPX', 'EYZİM', 'DTARU', 'İNOKE'],
      words: [
        { id: 'w1', label: 'KALP' },
        { id: 'w2', label: 'KEDİ' },
      ],
    });
    const wsLines = (...lines: [number, number, number, number][]) =>
      JSON.stringify({ version: 1, type: 'wordSearch', lines: lines.map(([a, b, c, d]) => ({ from: [a, b], to: [c, d] })) });
    const base = () => instance().testInstanceQuestions[0].question;
    const typed = (answer: string) =>
      instance({}, [{ answerPayload: answer, question: { ...base(), interactionType: 'WordSearch', interactionPlan: WS_PLAN } }]);

    it('rendersPlayer_FewerLinesThanWordsIsIncomplete_WordRowsAndFeedback', async () => {
      await create(typed(wsLines([0, 0, 0, 3])));
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-word-search')).not.toBeNull();
      expect(fixture.componentInstance.blankItems()).toContain('1');

      fixture.componentInstance.onDragDropChange(wsLines([0, 0, 0, 3], [1, 0, 1, 2]), fixture.componentInstance.items()[0]);
      await settle();
      expect(fixture.componentInstance.blankItems()).not.toContain('1');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 1,
          totalCount: 3,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 1,
              totalCount: 2,
              feedback: '{"rows":[{"id":"w1","correct":true},{"id":"w2","correct":false}],"lines":[true,false]}',
            },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();
      expect(fixture.componentInstance.currentRows().map((r) => [r.label, r.correct])).toEqual([
        ['KALP: bulundu', true],
        ['KEDİ: bulunamadı', false],
      ]);
      expect(fixture.componentInstance.currentWordSearchFeedback()?.lines).toEqual([true, false]);
      expect(el().querySelector('[data-word="w2"]')?.classList).toContain('is-missing');
    });
  });

  describe('crossword (#586)', () => {
    const CW_PLAN = JSON.stringify({
      version: 1,
      type: 'crossword',
      rows: 3,
      cols: 4,
      clues: [
        { id: 'k1', dir: 'across', row: 0, col: 0, length: 4, number: 1, text: 'Organ' },
        { id: 'k2', dir: 'down', row: 0, col: 0, length: 3, number: 1, text: 'Kirli' },
        { id: 'k3', dir: 'across', row: 2, col: 0, length: 3, number: 2, text: 'Raf' },
      ],
    });
    const cwGrid = (...lines: string[]) => JSON.stringify({ version: 1, type: 'crossword', grid: lines });
    const base = () => instance().testInstanceQuestions[0].question;
    const typed = (answer: string) =>
      instance({}, [{ answerPayload: answer, question: { ...base(), interactionType: 'Crossword', interactionPlan: CW_PLAN } }]);

    it('rendersPlayer_EmptyCellsAreIncomplete_ClueRowsAndFeedback', async () => {
      await create(typed(cwGrid('KALP', 'I   ', 'RA  ')));
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-crossword')).not.toBeNull();
      expect(fixture.componentInstance.blankItems()).toContain('1');

      fixture.componentInstance.onDragDropChange(cwGrid('KALP', 'I   ', 'RAF '), fixture.componentInstance.items()[0]);
      await settle();
      expect(fixture.componentInstance.blankItems()).not.toContain('1');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 2,
          totalCount: 4,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 2,
              totalCount: 3,
              feedback: '{"rows":[{"id":"k1","correct":true},{"id":"k2","correct":false},{"id":"k3","correct":true}]}',
            },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();
      expect(fixture.componentInstance.currentRows().map((r) => [r.label, r.correct])).toEqual([
        ['1 → Organ: KALP', true],
        ['1 ↓ Kirli: KIR', false],
        ['2 → Raf: RAF', true],
      ]);
      expect(fixture.componentInstance.currentCrosswordFeedback()?.rows.length).toBe(3);
      expect(el().querySelector('[data-clue="k2"]')?.classList).toContain('is-bad');
    });
  });

  describe('mazeTree (#585)', () => {
    const MZ_PLAN = JSON.stringify({
      version: 1,
      type: 'mazeTree',
      variant: 'tree',
      start: 'n1',
      nodes: [
        { id: 'n1', label: 'Kök' },
        { id: 'n2', label: 'Sol' },
        { id: 'n3', label: 'Sağ' },
        { id: 'x1', label: '1. çıkış', end: true },
        { id: 'x2', label: '2. çıkış', end: true },
        { id: 'x3', label: '3. çıkış', end: true },
        { id: 'x4', label: '4. çıkış', end: true },
      ],
      edges: [
        { id: 'e1', from: 'n1', to: 'n2', label: 'D' },
        { id: 'e2', from: 'n1', to: 'n3', label: 'Y' },
        { id: 'e3', from: 'n2', to: 'x1', label: 'D' },
        { id: 'e4', from: 'n2', to: 'x2', label: 'Y' },
        { id: 'e5', from: 'n3', to: 'x3', label: 'D' },
        { id: 'e6', from: 'n3', to: 'x4', label: 'Y' },
      ],
    });
    const mzPath = (...edges: string[]) => JSON.stringify({ version: 1, type: 'mazeTree', path: edges });
    const base = () => instance().testInstanceQuestions[0].question;
    const typed = (answer: string) =>
      instance({}, [{ answerPayload: answer, question: { ...base(), interactionType: 'MazeTree', interactionPlan: MZ_PLAN } }]);

    it('rendersPlayer_UnfinishedPathIsIncomplete_DeviationRowsAndFeedback', async () => {
      await create(typed(mzPath('e2')));
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-maze-tree')).not.toBeNull();
      expect(fixture.componentInstance.blankItems()).toContain('1');

      fixture.componentInstance.onDragDropChange(mzPath('e2', 'e5'), fixture.componentInstance.items()[0]);
      await settle();
      expect(fixture.componentInstance.blankItems()).not.toContain('1');

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 0,
          totalCount: 3,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 0,
              totalCount: 2,
              feedback: '{"rows":[{"id":"n1","correct":false}],"status":"deviated","at":"n1"}',
            },
            { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();
      expect(fixture.componentInstance.currentRows().map((r) => [r.label, r.correct])).toEqual([['Kök: Y', false]]);
      expect(fixture.componentInstance.currentMazeFeedback()?.at).toBe('n1');
      expect(el().querySelector('[data-node="n1"]')?.classList).toContain('is-deviated');
    });
  });

  describe('mobile result panel collapse (#576 E2E)', () => {
    const checkResponse = () =>
      of({
        success: true,
        correctCount: 0,
        totalCount: 2,
        questions: [
          { instanceQuestionId: 101, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
          { instanceQuestionId: 102, isCorrect: false, correctCount: 0, totalCount: 1, feedback: null },
        ],
      });

    it('narrow_FixWrongCollapses_ExpandReopens_NewCheckReopens', async () => {
      await create();
      fixture.componentInstance.narrowScreen.set(true);
      activityService.check.mockReturnValue(checkResponse());
      await fixture.componentInstance.runCheck();
      await settle();
      expect(q('check-compact')).toBeNull();
      expect(q('collapse')).not.toBeNull();

      (q('fix-wrong') as HTMLButtonElement).click();
      await settle();
      expect(q('check-compact')).not.toBeNull();
      expect(q('check-summary')?.textContent).toContain('2 cevap yerinden 0 doğru');

      (q('expand') as HTMLButtonElement).click();
      await settle();
      expect(q('check-compact')).toBeNull();

      q('item-content')!.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
      await settle();
      expect(q('check-compact')).not.toBeNull();

      await fixture.componentInstance.runCheck();
      await settle();
      expect(q('check-compact')).toBeNull();
    });

    it('answerChangeAfterCheck_Collapses_WideScreenNeverCompact', async () => {
      await create();
      fixture.componentInstance.narrowScreen.set(true);
      activityService.check.mockReturnValue(checkResponse());
      await fixture.componentInstance.runCheck();
      fixture.componentInstance.onChoiceSelected(12, fixture.componentInstance.items()[1]);
      await settle();
      expect(fixture.componentInstance.panelCollapsed()).toBe(true);
      expect(q('check-compact')).not.toBeNull();

      fixture.componentInstance.narrowScreen.set(false);
      await settle();
      expect(q('check-compact')).toBeNull();
      expect(q('collapse')).toBeNull();
    });

    it('beforeAnyCheck_InteractionDoesNotCollapse', async () => {
      await create();
      fixture.componentInstance.narrowScreen.set(true);
      q('item-content')!.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
      await settle();
      expect(fixture.componentInstance.panelCollapsed()).toBe(false);
    });
  });

  describe('openText (#583)', () => {
    const OT_PLAN = JSON.stringify({ version: 1, type: 'openText', minWords: null, maxWords: 50, lines: 4 });
    const otAnswer = (text: string) => JSON.stringify({ version: 1, type: 'openText', text });
    const otQuestion = (extra: Record<string, unknown> = {}) => ({
      answerPayload: otAnswer('bir iki üç'),
      question: { ...instance().testInstanceQuestions[0].question, interactionType: 'openText', interactionPlan: OT_PLAN },
      ...extra,
    });
    const rowTexts = () => Array.from(el().querySelectorAll('[data-testid="result-row"]')).map((r) => r.textContent ?? '');

    it('check_SubmitsText_RowIsPendingNotWrong_StepAnswered_TotalsExcludeOpenText', async () => {
      await create(instance({}, [otQuestion(), { selectedAnswerId: 11 }]));
      fixture.componentInstance.goTo(0);
      await settle();

      expect(el().querySelector('app-question-canvas-open-text')).not.toBeNull();
      expect(q('ot-submit-hint')).not.toBeNull();

      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 1,
          totalCount: 1,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 0,
              totalCount: 0,
              feedback: '{"rows":[{"id":"text","status":"pending"}]}',
              openTextReview: { status: 'pending', evaluation: null, feedback: null, reviewedAt: null },
            },
            { instanceQuestionId: 102, isCorrect: true, correctCount: 1, totalCount: 1, feedback: null },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();

      expect(activityService.check.mock.lastCall![1][0]).toEqual({
        instanceQuestionId: 101,
        selectedAnswerId: null,
        answerPayload: otAnswer('bir iki üç'),
      });
      expect(fixture.componentInstance.steps()[0].state).toBe('answered');
      const row = el().querySelector('[data-testid="result-row"]')!;
      expect(row.getAttribute('data-status')).toBe('pending');
      expect(row.textContent).toContain('Değerlendirme bekliyor');
      expect(row.textContent).toContain('Cevabın: 3 sözcük');
      expect(row.textContent).not.toContain('Yanlış');
      expect(q('ot-pending')?.textContent).toContain('Öğretmen değerlendirmesi bekliyor');
      expect(q('check-summary')?.textContent).toContain('1 cevap yerinden 1 doğru');
      expect(q('fix-wrong')).toBeNull();
      expect(el().textContent).not.toMatch(/%|puan|XP/i);
    });

    it('onlyOpenTextItems_CheckSummaryHidden', async () => {
      const only = instance({}, [otQuestion()]);
      only.testInstanceQuestions = only.testInstanceQuestions.slice(0, 1);
      await create(only);
      activityService.check.mockReturnValue(
        of({
          success: true,
          correctCount: 0,
          totalCount: 0,
          questions: [
            {
              instanceQuestionId: 101,
              isCorrect: false,
              correctCount: 0,
              totalCount: 0,
              feedback: '{"rows":[{"id":"text","status":"pending"}]}',
              openTextReview: { status: 'pending', evaluation: null, feedback: null, reviewedAt: null },
            },
          ],
        })
      );
      await fixture.componentInstance.runCheck();
      await settle();

      expect(fixture.componentInstance.summary()).toBeNull();
      expect(q('check-summary')).toBeNull();
      expect(rowTexts()[0]).toContain('Değerlendirme bekliyor');
      expect(fixture.componentInstance.checkCount()).toBe(1);
    });

    it('reviewed_ShowsBadgeAndFeedback_EditingMakesItOutdated', async () => {
      await create(
        instance({}, [
          otQuestion({
            openTextReview: { status: 'reviewed', evaluation: 'partial', feedback: 'Bir örnek ekle.', reviewedAt: '2026-10-27T10:00:00Z' },
          }),
        ])
      );
      fixture.componentInstance.goTo(0);
      await settle();
      expect(q('ot-evaluation')?.textContent).toContain('Kısmen');
      expect(q('ot-feedback')?.textContent).toContain('Bir örnek ekle.');

      const item = fixture.componentInstance.items()[0];
      fixture.componentInstance.onDragDropChange(otAnswer('bir iki üç dört'), item);
      await settle();

      expect(fixture.componentInstance.currentOpenTextReview()?.status).toBe('outdated');
      expect(q('ot-outdated')).not.toBeNull();
      expect(q('ot-evaluation')?.textContent).toContain('Kısmen');
    });

    it('pendingThenEdited_BecomesUnsentDraft_ShowsSubmitHint', async () => {
      await create(
        instance({}, [otQuestion({ openTextReview: { status: 'pending', evaluation: null, feedback: null, reviewedAt: null } })])
      );
      fixture.componentInstance.goTo(0);
      await settle();
      expect(q('ot-pending')).not.toBeNull();

      fixture.componentInstance.onDragDropChange(otAnswer('yeni metin'), fixture.componentInstance.items()[0]);
      await settle();

      expect(fixture.componentInstance.currentOpenTextReview()).toBeNull();
      expect(q('ot-pending')).toBeNull();
      expect(q('ot-submit-hint')).not.toBeNull();
    });

    it('goTo_FlushesTypedButUnsavedText', async () => {
      await create(instance({}, [otQuestion({ answerPayload: undefined })]));
      fixture.componentInstance.goTo(0);
      await settle();
      expect(fixture.componentInstance.steps()[0].state).toBe('empty');
      expect(fixture.componentInstance.blankItems()).toContain('1');

      const field = q('ot-text') as HTMLTextAreaElement;
      field.value = 'yazdım ama beklemedim';
      field.dispatchEvent(new Event('input'));
      fixture.componentInstance.goTo(1);
      await settle();

      expect(testService['saveAnswer']).toHaveBeenCalledWith(
        expect.objectContaining({ testQuestionId: 101, answerPayload: otAnswer('yazdım ama beklemedim') })
      );
      expect(fixture.componentInstance.steps()[0].state).toBe('answered');
    });

    it('sharedPlayer_TypeInA_GoToEmptyB_BackToA_KeepsAText (review #1)', async () => {
      const both = instance({}, [otQuestion({ answerPayload: undefined }), otQuestion({ answerPayload: undefined })]);
      await create(both);
      fixture.componentInstance.goTo(0);
      await settle();
      const field = () => q('ot-text') as HTMLTextAreaElement;
      field().value = 'A cevabı';
      field().dispatchEvent(new Event('input'));

      fixture.componentInstance.goTo(1);
      await settle();
      expect(field().value).toBe('');
      fixture.componentInstance.goTo(0);
      await settle();

      expect(field().value).toBe('A cevabı');
      expect(fixture.componentInstance.answerFor(fixture.componentInstance.items()[1])?.answerPayload ?? null).toBeNull();
    });

    it('tooLongText_BlocksCheckAndFinish_WithPanelError (review #3)', async () => {
      const plan = JSON.stringify({ version: 1, type: 'openText', minWords: null, maxWords: 2, lines: 2 });
      await create(
        instance({ checkCount: 1 }, [
          otQuestion({ answerPayload: otAnswer('kısa'), question: { ...otQuestion().question, interactionPlan: plan } }),
          { selectedAnswerId: 11 },
        ])
      );
      fixture.componentInstance.goTo(0);
      await settle();
      const field = q('ot-text') as HTMLTextAreaElement;
      field.value = 'bir iki üç dört beş';
      field.dispatchEvent(new Event('input'));
      await settle();

      await fixture.componentInstance.runCheck();
      await settle();
      expect(activityService.check).not.toHaveBeenCalled();
      expect(q('check-error')?.textContent).toContain('Metin çok uzun; kısaltıp tekrar dene.');

      await fixture.componentInstance.finish();
      await settle();
      expect(testService['completeTest']).not.toHaveBeenCalled();
      expect(testService['saveAnswer']).not.toHaveBeenCalled();
    });

    it('reveal_ShowsSampleAnswerInPlayer', async () => {
      await create(instance({ checkCount: 1 }, [otQuestion()]));
      fixture.componentInstance.goTo(0);
      await settle();
      activityService.getSolutions.mockReturnValue(
        of({
          success: true,
          questions: [
            { instanceQuestionId: 101, questionId: 1, correctAnswerId: null, interactionSolution: '{"sampleAnswer":"Kitaptaki örnek."}' },
            { instanceQuestionId: 102, questionId: 2, correctAnswerId: 11, interactionSolution: null },
          ],
        })
      );
      await fixture.componentInstance.reveal();
      await settle();

      expect(q('ot-sample')?.textContent).toContain('Kitaptaki örnek.');
      expect((q('ot-text') as HTMLTextAreaElement).readOnly).toBe(true);
    });

    it('finished_SummaryListsOpenTextReviews_NotCountedInInfo', async () => {
      await create(
        instance(
          { status: TestStatus.Completed, firstCheckCorrectCount: 1, firstCheckTotalCount: 1 },
          [
            otQuestion({
              openTextReview: { status: 'reviewed', evaluation: 'sufficient', feedback: 'Harika <b>iş</b>', reviewedAt: '2026-10-27T10:00:00Z' },
            }),
            { selectedAnswerId: 11 },
          ]
        )
      );

      const item = q('summary-open-text-item');
      expect(item?.textContent).toContain('Madde 1');
      expect(item?.textContent).toContain('Yeterli');
      expect(item?.querySelector('[data-testid="ot-feedback"]')?.textContent).toContain('Harika <b>iş</b>');
      expect(item?.querySelector('b b')).toBeNull();
      expect(q('summary-info')?.textContent).toContain('1 cevap yerinden 1 doğru');
    });

    it('finishWithoutReview_MarksAnsweredOpenTextPending', async () => {
      await create(instance({ checkCount: 1 }, [otQuestion(), { selectedAnswerId: 11 }]));
      await fixture.componentInstance.finish();
      await settle();

      expect(fixture.componentInstance.openTextReviews()[101]?.status).toBe('pending');
      expect(q('summary-open-text-item')?.textContent).toContain('Öğretmen değerlendirmesi bekliyor');
    });
  });
});
