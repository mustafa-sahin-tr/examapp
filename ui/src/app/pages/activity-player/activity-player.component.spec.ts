import type { Mock } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';

import { ACTIVITY_SUMMARY_REFETCH_MS, ActivityPlayerComponent } from './activity-player.component';
import { ActivityService } from '../../services/activity.service';
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
    expect(steps[0].getAttribute('aria-label')).toBe('Madde a: cevaplandı');
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
    expect(q('check-summary')?.textContent).toContain('2 / 3 doğru');
    const steps = Array.from(el().querySelectorAll('.apf__step'));
    expect(steps[0].getAttribute('aria-label')).toBe('Madde a: yanlış');

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
    expect(steps[0].getAttribute('aria-label')).toBe('Madde a: doğru');
    expect(q('check-summary')?.textContent).toContain('2 / 3 doğru');
    expect((q('reveal') as HTMLButtonElement).disabled).toBe(false);
  });

  it('completedInstance_OpensSummaryDirectly', async () => {
    await create(instance({ status: TestStatus.Completed, checkCount: 1 }));

    expect(q('activity-summary')).not.toBeNull();
    expect(el().querySelector('app-activity-player-frame')).toBeNull();
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

    expect(q('check-summary')?.textContent).toContain('1 / 2 doğru');
    expect(q('fix-wrong')).not.toBeNull();
    expect(el().textContent).toContain('Kontrolden sonra cevabını değiştirdin');
  });

  it('s6_FinishedWithoutRowData_InfoLineFromInstanceFirstCheck', async () => {
    // Atamadaki sayılar kullanılmaz (bağımlılık kaldırıldı); kaynak oturum yanıtı.
    testServiceAssignments = [{ worksheetId: 9, kind: 1, isCompleted: true, firstCheckCorrectCount: 1, firstCheckTotalCount: 1 }];
    await create(instance({ status: TestStatus.Completed, checkCount: 2, firstCheckCorrectCount: 7, firstCheckTotalCount: 10 }));

    expect(q('summary-info')?.textContent?.trim()).toBe('7 / 10 doğru');
  });

  it('s6_InProgressIgnoresFirstCheckCounts', async () => {
    await create(instance({ checkCount: 1, firstCheckCorrectCount: 7, firstCheckTotalCount: 10 }));

    expect(q('check-summary')).toBeNull();
  });

  it('s9_ItemAriaUsesStepLetter', async () => {
    await create();

    expect(q('item-content')?.getAttribute('aria-label')).toBe('Madde b içeriği');
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
      expect(fixture.componentInstance.blankItems()).toContain('a');
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
      expect(fixture.componentInstance.blankItems()).not.toContain('a');

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
      expect(fixture.componentInstance.blankItems()).toContain('a');
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
      expect(fixture.componentInstance.blankItems()).not.toContain('a');

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
      const rows = rowTexts();
      expect(rows[0]).toContain('Kuşlar uçar.: Y');
      expect(rows[1]).toContain('Satır 2: D');
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

      const rows = fixture.componentInstance.currentRows();
      expect(rows.map((r) => r.solution)).toEqual(['D', 'Y']);
      expect(rows[1].label).toBe('Satır 2: boş');
      expect(el().querySelector('[data-cell="r1:c1"]')?.getAttribute('aria-pressed')).toBe('true');
      expect(el().querySelector('[data-cell="r1:c2"]')?.getAttribute('aria-pressed')).toBe('false');
    });
  });
});
