import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { PracticeSolveComponent } from './practice-solve.component';
import { PracticeService } from '../../services/practice.service';
import { StudentService } from '../../services/student.service';
import { SubjectService } from '../../services/subject.service';
import { DailySet, PracticeAnswerResult, PracticeSession, PracticeSessionReview } from '../../models/practice';
import { BadgeService, BadgeProgressResponse } from '../../services/badge.service';
import { StudentProfile } from '../../models/student-profile';
import { Question } from '../../models/question';
import { Subject } from '../../models/subject';
import { Paged } from '../../models/test-instance';

import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import practiceTr from '../../../../public/i18n/practice/tr.json';
// QuestionLiteViewComponent kendi 'question' scope'unu kullanir; sozlugu de verilmeli.
import questionTr from '../../../../public/i18n/question/tr.json';

/**
 * Sayfa cevirileri kendi Transloco scope'undadir (issue #183); testte gercek sozluk verilir,
 * sahte ceviri kullanilmaz - boylece bir anahtar bozulursa test kirilir.
 */
const translocoTesting = translocoTestingModule({ langs: { 'practice/tr': practiceTr, 'question/tr': questionTr } });

describe('PracticeSolveComponent', () => {
  let fixture: ComponentFixture<PracticeSolveComponent>;
  let component: PracticeSolveComponent;
  let practiceService: jasmine.SpyObj<PracticeService>;
  let studentService: jasmine.SpyObj<StudentService>;
  let subjectService: jasmine.SpyObj<SubjectService>;
  let badgeService: jasmine.SpyObj<BadgeService>;
  let router: Router;

  const emptyHistory: Paged<PracticeSession> = { items: [], totalCount: 0, pageNumber: 1, pageSize: 10 };

  const profile: StudentProfile = {
    fullName: 'Ada Lovelace',
    gradeId: 5,
    avatarUrl: '',
    xp: 0,
    level: 1,
    totalQuestionsSolved: 0,
    correctAnswers: 0,
    wrongAnswers: 0,
    testsCompleted: 0,
    totalRewards: 0,
    leaderboardRank: 0,
    badges: [],
    recentTests: [],
  };

  const subjects: Subject[] = [
    { id: 1, name: 'Matematik' },
    { id: 2, name: 'Türkçe' },
  ];

  const session: PracticeSession = {
    id: 55,
    gradeId: 5,
    startTime: '2026-01-01T00:00:00Z',
    endTime: null,
    status: 'Active',
    subjectIds: [],
    topicIds: [],
    answeredCount: 0,
    correctCount: 0,
    skippedCount: 0,
  };

  function buildQuestion(overrides: Partial<Question> = {}): Question {
    return {
      id: 9,
      text: 'soru metni',
      imageUrl: 'q.jpg',
      category: {} as any,
      answers: [
        { id: 900, index: 0, text: 'A şıkkı', imageUrl: 'a.jpg', x: 0, y: 0, width: 1, height: 1, isCanvasQuestion: false },
        { id: 901, index: 1, text: 'B şıkkı', imageUrl: 'b.jpg', x: 0, y: 0, width: 1, height: 1, isCanvasQuestion: false },
      ],
      isExample: false,
      subjectId: 1,
      topicId: 1,
      answerColCount: 2,
      x: 0,
      y: 0,
      width: 100,
      height: 100,
      isCanvasQuestion: false,
      ...overrides,
    };
  }

  function setup(queryParams: Record<string, string> = {}): void {
    TestBed.configureTestingModule({
      imports: [PracticeSolveComponent, translocoTesting],
      providers: [
        { provide: PracticeService, useValue: practiceService },
        { provide: StudentService, useValue: studentService },
        { provide: SubjectService, useValue: subjectService },
        { provide: BadgeService, useValue: badgeService },
        provideRouter([]),
        provideNoopAnimations(),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: convertToParamMap(queryParams) },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PracticeSolveComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
  }

  beforeEach(() => {
    practiceService = jasmine.createSpyObj<PracticeService>('PracticeService', [
      'startSession',
      'getSession',
      'getNextQuestion',
      'submitAnswer',
      'endSession',
      'listSessions',
      'getSessionReview',
      'toQuestionRegion',
      'getDailySet',
      'startDailySet',
    ]);
    badgeService = jasmine.createSpyObj<BadgeService>('BadgeService', ['getUserBadgeProgress']);
    badgeService.getUserBadgeProgress.and.returnValue(throwError(() => new Error('no badge')));
    studentService = jasmine.createSpyObj<StudentService>('StudentService', ['getProfile']);
    subjectService = jasmine.createSpyObj<SubjectService>('SubjectService', ['getSubjectsByGrade', 'getTopicsBySubjectAndGrade']);

    studentService.getProfile.and.returnValue(of(profile));
    subjectService.getSubjectsByGrade.and.returnValue(of(subjects));
    practiceService.listSessions.and.returnValue(of(emptyHistory));
    practiceService.toQuestionRegion.and.callFake((q: Question, correctAnswerId?: number | null) => ({
      id: q.id,
      name: 'Soru',
      x: q.x,
      y: q.y,
      width: q.width,
      height: q.height,
      passageId: '',
      answers: (q.answers ?? []).map((a) => ({
        id: a.id,
        label: a.text,
        x: a.x,
        y: a.y,
        width: a.width,
        height: a.height,
        imageUrl: a.imageUrl,
        isCorrect: correctAnswerId != null && a.id === correctAnswerId,
      })),
      imageId: q.imageUrl,
      imageUrl: q.imageUrl,
      exampleAnswer: null,
      isExample: q.isExample,
    }));
  });

  it('ngOnInit_NoSubjectOrTopicSelected_ShowsDefaultScopeInfoBox', () => {
    setup();
    fixture.detectChanges();

    expect(component.phase()).toBe('setup');
    expect(component.hasScope()).toBeFalse();
    const infoBox = fixture.debugElement.query(By.css('.state-box--info'));
    expect(infoBox).toBeTruthy();
    expect(infoBox.nativeElement.textContent).toContain('Varsayılan kapsam');
  });

  it('loadNext_PoolExhausted_ShowsEmptyState', () => {
    setup();
    practiceService.startSession.and.returnValue(of(session));
    practiceService.getNextQuestion.and.returnValue(
      of({ sessionId: session.id, question: null, poolExhausted: true, answeredCount: 0, correctCount: 0 })
    );

    fixture.detectChanges();
    component.startSession();
    fixture.detectChanges();

    expect(component.phase()).toBe('empty');
    const emptyState = fixture.debugElement.query(By.css('.practice__state--empty'));
    expect(emptyState).toBeTruthy();
  });

  it('submitAnswer_ServerReturnsCorrect_TransitionsToFeedbackPhaseWithCorrectResult', () => {
    setup();
    const question = buildQuestion();
    practiceService.startSession.and.returnValue(of(session));
    practiceService.getNextQuestion.and.returnValue(
      of({ sessionId: session.id, question, poolExhausted: false, answeredCount: 0, correctCount: 0 })
    );
    const result: PracticeAnswerResult = {
      sessionId: session.id,
      questionId: question.id,
      isCorrect: true,
      skipped: false,
      correctAnswerId: 900,
      answeredCount: 1,
      correctCount: 1,
    };
    practiceService.submitAnswer.and.returnValue(of(result));

    fixture.detectChanges();
    component.startSession();
    fixture.detectChanges();

    expect(component.phase()).toBe('question');

    component.selectAnswer(900);
    component.submitAnswer();
    fixture.detectChanges();

    expect(component.phase()).toBe('feedback');
    expect(component.feedbackKind()).toBe('correct');
    expect(component.result()?.isCorrect).toBeTrue();
  });

  it('submitAnswer_ServerReturnsIncorrect_TransitionsToFeedbackPhaseWithWrongResult', () => {
    setup();
    const question = buildQuestion();
    practiceService.startSession.and.returnValue(of(session));
    practiceService.getNextQuestion.and.returnValue(
      of({ sessionId: session.id, question, poolExhausted: false, answeredCount: 0, correctCount: 0 })
    );
    const result: PracticeAnswerResult = {
      sessionId: session.id,
      questionId: question.id,
      isCorrect: false,
      skipped: false,
      correctAnswerId: 901,
      answeredCount: 1,
      correctCount: 0,
    };
    practiceService.submitAnswer.and.returnValue(of(result));

    fixture.detectChanges();
    component.startSession();
    fixture.detectChanges();

    component.selectAnswer(900);
    component.submitAnswer();
    fixture.detectChanges();

    expect(component.phase()).toBe('feedback');
    expect(component.feedbackKind()).toBe('wrong');
  });

  it('skipQuestion_Called_SubmitsSkippedAnswerAndShowsSkippedFeedback', () => {
    setup();
    const question = buildQuestion();
    practiceService.startSession.and.returnValue(of(session));
    practiceService.getNextQuestion.and.returnValue(
      of({ sessionId: session.id, question, poolExhausted: false, answeredCount: 0, correctCount: 0 })
    );
    const result: PracticeAnswerResult = {
      sessionId: session.id,
      questionId: question.id,
      isCorrect: false,
      skipped: true,
      correctAnswerId: 900,
      answeredCount: 1,
      correctCount: 0,
    };
    practiceService.submitAnswer.and.returnValue(of(result));

    fixture.detectChanges();
    component.startSession();
    fixture.detectChanges();

    component.skipQuestion();
    fixture.detectChanges();

    expect(practiceService.submitAnswer).toHaveBeenCalledWith(
      session.id,
      jasmine.objectContaining({ questionId: question.id, selectedAnswerId: null, skipped: true })
    );
    expect(component.phase()).toBe('feedback');
    expect(component.feedbackKind()).toBe('skipped');
    expect(component.skippedCount()).toBe(1);
  });

  it('finishSession_Called_EndsSessionAndTransitionsToEndedSummary', () => {
    setup();
    practiceService.startSession.and.returnValue(of(session));
    practiceService.getNextQuestion.and.returnValue(
      of({ sessionId: session.id, question: null, poolExhausted: true, answeredCount: 3, correctCount: 2 })
    );
    const ended: PracticeSession = { ...session, status: 'Ended', answeredCount: 3, correctCount: 2, skippedCount: 0 };
    practiceService.endSession.and.returnValue(of(ended));

    fixture.detectChanges();
    component.startSession();
    fixture.detectChanges();
    expect(component.phase()).toBe('empty');

    component.finishSession();
    fixture.detectChanges();

    expect(component.phase()).toBe('ended');
    const summary = fixture.debugElement.query(By.css('.practice__state--ended'));
    expect(summary).toBeTruthy();
    expect(summary.nativeElement.textContent).toContain('Oturum tamamlandı');
  });

  // ── Geçmiş oturumlar ──────────────────────────────────────────────────────

  const endedSession: PracticeSession = {
    ...session,
    id: 12,
    endTime: '2026-01-01T00:15:00Z',
    status: 'Ended',
    subjectIds: [1],
    answeredCount: 10,
    correctCount: 7,
    skippedCount: 2,
  };

  // 1: metin sorusu, doğru · 2: canvas, pas · 3: canvas, bekliyor (sunucu doğru şıkkı gizler)
  const review: PracticeSessionReview = {
    session: endedSession,
    questions: [
      {
        question: buildQuestion({ id: 501, text: 'Kesir sorusu', correctAnswerId: 900 }),
        status: 'Answered',
        isSkipped: false,
        isCorrect: true,
        selectedAnswerId: 900,
        timeTaken: 12,
        shownAt: '2026-01-01T00:00:05Z',
        answeredAt: '2026-01-01T00:00:17Z',
      },
      {
        question: buildQuestion({ id: 502, text: '', isCanvasQuestion: true, correctAnswerId: 901 }),
        status: 'Skipped',
        isSkipped: true,
        isCorrect: false,
        selectedAnswerId: null,
        timeTaken: 3,
        shownAt: '2026-01-01T00:00:20Z',
        answeredAt: '2026-01-01T00:00:23Z',
      },
      {
        question: buildQuestion({ id: 503, text: '', isCanvasQuestion: true, correctAnswerId: undefined }),
        status: 'Pending',
        isSkipped: false,
        isCorrect: false,
        selectedAnswerId: null,
        timeTaken: 0,
        shownAt: '2026-01-01T00:00:30Z',
        answeredAt: null,
      },
    ],
  };

  it('ngOnInit_NoPastSessions_ShowsHistoryEmptyStateInSetup', () => {
    setup();
    fixture.detectChanges();

    expect(practiceService.listSessions).toHaveBeenCalledWith(1, 10);
    const empty = fixture.debugElement.query(By.css('.practice__card--history .state-box--empty'));
    expect(empty).toBeTruthy();
    expect(empty.nativeElement.textContent).toContain('Henüz pratik oturumun yok');
  });

  it('ngOnInit_HasPastSessions_RendersRowsWithResolvedSubjectNamesAndAccuracy', () => {
    practiceService.listSessions.and.returnValue(
      of({ items: [endedSession], totalCount: 1, pageNumber: 1, pageSize: 10 })
    );
    setup();
    fixture.detectChanges();

    const rows = fixture.debugElement.queryAll(By.css('.history__row'));
    expect(rows.length).toBe(1);
    const text = rows[0].nativeElement.textContent as string;
    expect(text).toContain('Matematik');
    expect(text).toContain('Tamamlandı');
    // 7 doğru / (10 cevap - 2 pas) = %88
    expect(component.accuracyOf(endedSession)).toBe(88);
    expect(component.wrongCountOf(endedSession)).toBe(1);
    expect(component.durationLabel(endedSession)).toBe('15 dk');
    expect(fixture.debugElement.query(By.css('app-pagination'))).toBeNull();
  });

  it('scopeLabel_NoScope_ReturnsAllSubjects', () => {
    setup();
    fixture.detectChanges();

    expect(component.scopeLabel(session)).toBe('Tüm dersler');
    expect(component.scopeLabel({ ...session, subjectIds: [2], topicIds: [77] })).toBe('Türkçe · 1 konu');
  });

  it('openHistoryItem_EndedSession_LoadsReviewAndShowsQuestions', () => {
    practiceService.listSessions.and.returnValue(
      of({ items: [endedSession], totalCount: 1, pageNumber: 1, pageSize: 10 })
    );
    practiceService.getSessionReview.and.returnValue(of(review));
    setup();
    fixture.detectChanges();

    fixture.debugElement.query(By.css('.history__row')).nativeElement.click();
    fixture.detectChanges();

    expect(component.phase()).toBe('review');
    expect(practiceService.getSessionReview).toHaveBeenCalledWith(12);
    const pills = fixture.debugElement.queryAll(By.css('.review__pill'));
    expect(pills.length).toBe(3);
    expect(pills[0].nativeElement.classList).toContain('review__pill--correct');
    expect(pills[1].nativeElement.classList).toContain('review__pill--skipped');
    expect(pills[2].nativeElement.classList).toContain('review__pill--pending');
    expect(pills[0].nativeElement.classList).toContain('review__pill--current');
    expect(component.reviewWrongCount()).toBe(1);

    // İlk soru metin sorusu: lite view açılır, canvas açılmaz
    expect(component.reviewIndex()).toBe(0);
    expect(fixture.debugElement.query(By.css('app-question-lite-view'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('app-question-canvas-view-v5'))).toBeNull();
  });

  it('reviewEntries_TextQuestion_MapsCorrectAnswerForLiteViewWithoutBuildingRegion', () => {
    practiceService.getSessionReview.and.returnValue(of(review));
    setup();
    fixture.detectChanges();

    component.openReview(12);
    fixture.detectChanges();

    const entry = component.reviewEntries()[0];
    expect(entry.kind).toBe('correct');
    expect(entry.region).toBeNull();
    expect(entry.question.correctAnswer?.id).toBe(900);
    expect(practiceService.toQuestionRegion).not.toHaveBeenCalledWith(jasmine.objectContaining({ id: 501 }), jasmine.anything());
  });

  it('reviewEntries_CanvasQuestion_BuildsRegionWithCorrectChoiceFromNestedQuestion', () => {
    practiceService.getSessionReview.and.returnValue(of(review));
    setup();
    fixture.detectChanges();

    component.openReview(12);
    fixture.detectChanges();

    const entry = component.reviewEntries()[1];
    expect(entry.kind).toBe('skipped');
    expect(practiceService.toQuestionRegion).toHaveBeenCalledWith(jasmine.objectContaining({ id: 502 }), 901);
    expect(entry.region?.id).toBe(502);
    expect(entry.selectedChoice).toBeUndefined();
    expect(entry.correctChoice?.id).toBe(901);
    // v5 referans eşitliği: doğru şık bölgenin kendi nesnesi olmalı
    expect(entry.region?.answers).toContain(entry.correctChoice!);
  });

  it('reviewEntries_PendingQuestion_HasNoCorrectChoiceAndNoCorrectAnswerHighlight', () => {
    practiceService.getSessionReview.and.returnValue(of(review));
    setup();
    fixture.detectChanges();

    component.openReview(12);
    fixture.detectChanges();

    const entry = component.reviewEntries()[2];
    expect(entry.kind).toBe('pending');
    expect(practiceService.toQuestionRegion).toHaveBeenCalledWith(jasmine.objectContaining({ id: 503 }), null);
    expect(entry.correctChoice).toBeUndefined();
    expect(entry.question.correctAnswer).toBeUndefined();
    expect(entry.region?.answers.every((a) => !a.isCorrect)).toBeTrue();
  });

  it('reviewNext_Called_RendersCanvasViewForSecondQuestionAndUpdatesPager', () => {
    practiceService.getSessionReview.and.returnValue(of(review));
    setup();
    fixture.detectChanges();

    component.openReview(12);
    fixture.detectChanges();
    expect(component.reviewHasPrev()).toBeFalse();
    expect(component.reviewHasNext()).toBeTrue();

    component.reviewNext();
    fixture.detectChanges();

    expect(component.reviewIndex()).toBe(1);
    expect(component.reviewEntry()?.row.question.id).toBe(502);
    expect(fixture.debugElement.query(By.css('app-question-lite-view'))).toBeNull();
    expect(fixture.debugElement.query(By.css('app-question-canvas-view-v5'))).toBeTruthy();
    const banner = fixture.debugElement.query(By.css('.feedback__banner'));
    expect(banner.nativeElement.classList).toContain('feedback__banner--skipped');
    expect(banner.nativeElement.textContent).toContain('Soru 2 / 3');

    component.reviewNext();
    fixture.detectChanges();
    expect(component.reviewHasNext()).toBeFalse();
    // Sınır dışına çıkmaz
    component.reviewNext();
    expect(component.reviewIndex()).toBe(2);

    component.reviewPrev();
    expect(component.reviewIndex()).toBe(1);
  });

  it('reviewPending_CanvasQuestionSelected_RendersNoCorrectAnswerHighlightInDom', () => {
    practiceService.getSessionReview.and.returnValue(of(review));
    setup();
    fixture.detectChanges();

    component.openReview(12);
    component.selectReviewQuestion(2);
    fixture.detectChanges();

    expect(component.reviewEntry()?.row.question.id).toBe(503);
    const canvasView = fixture.debugElement.query(By.css('app-question-canvas-view-v5'));
    expect(canvasView).toBeTruthy();
    // Sunucu Pending soru için doğru şıkkı hiç göndermez; component de tahmin etmez —
    // dolayısıyla hiçbir şık kartı 'is-correct' sınıfını taşımamalı (mode='result' olsa bile).
    const answerCards = canvasView.queryAll(By.css('.qcv4-answer-card'));
    expect(answerCards.length).toBeGreaterThan(0);
    for (const card of answerCards) {
      expect(card.nativeElement.classList).not.toContain('is-correct');
    }
  });

  it('selectReviewQuestion_PillClicked_JumpsToThatQuestion', () => {
    practiceService.getSessionReview.and.returnValue(of(review));
    setup();
    fixture.detectChanges();

    component.openReview(12);
    fixture.detectChanges();

    fixture.debugElement.queryAll(By.css('.review__pill'))[2].nativeElement.click();
    fixture.detectChanges();

    expect(component.reviewIndex()).toBe(2);
    expect(fixture.debugElement.query(By.css('.review__pill--current')).nativeElement.textContent.trim()).toBe('3');
    expect(fixture.debugElement.query(By.css('.feedback__banner--pending'))).toBeTruthy();
  });

  it('openHistoryItem_ActiveSession_ResumesSessionInsteadOfReview', () => {
    const active: PracticeSession = { ...session, id: 13, status: 'Active' };
    practiceService.listSessions.and.returnValue(of({ items: [active], totalCount: 1, pageNumber: 1, pageSize: 10 }));
    practiceService.getSession.and.returnValue(of(active));
    practiceService.getNextQuestion.and.returnValue(
      of({ sessionId: 13, question: buildQuestion(), poolExhausted: false, answeredCount: 0, correctCount: 0 })
    );
    setup();
    fixture.detectChanges();

    component.openHistoryItem(active);
    fixture.detectChanges();

    expect(practiceService.getSessionReview).not.toHaveBeenCalled();
    expect(component.phase()).toBe('question');
  });

  it('openReview_CalledAgain_ResetsIndexToFirstQuestion', () => {
    practiceService.getSessionReview.and.returnValue(of(review));
    setup();
    fixture.detectChanges();

    component.openReview(12);
    fixture.detectChanges();
    component.selectReviewQuestion(2);
    component.openReview(12);
    fixture.detectChanges();

    expect(component.reviewIndex()).toBe(0);
  });

  it('closeReview_Called_ReturnsToSetupAndReloadsHistory', () => {
    practiceService.getSessionReview.and.returnValue(of(review));
    setup();
    fixture.detectChanges();
    practiceService.listSessions.calls.reset();

    component.openReview(12);
    fixture.detectChanges();
    component.closeReview();
    fixture.detectChanges();

    expect(component.phase()).toBe('setup');
    expect(component.review()).toBeNull();
    expect(practiceService.listSessions).toHaveBeenCalledWith(1, 10);
  });

  it('onHistoryPageChange_DifferentPage_RequestsThatPage', () => {
    practiceService.listSessions.and.returnValue(
      of({ items: [endedSession], totalCount: 25, pageNumber: 1, pageSize: 10 })
    );
    setup();
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('app-pagination'))).toBeTruthy();
    component.onHistoryPageChange(3);

    expect(practiceService.listSessions).toHaveBeenCalledWith(3, 10);
    expect(component.historyPage()).toBe(3);
  });

  it('getSessionReview_Fails_ShowsReviewErrorWithRetry', () => {
    practiceService.getSessionReview.and.returnValue(throwError(() => new Error('boom')));
    setup();
    fixture.detectChanges();

    component.openReview(99);
    fixture.detectChanges();

    expect(component.phase()).toBe('review');
    expect(component.reviewError()).toBe('Oturum detayı yüklenemedi.');
    expect(fixture.debugElement.query(By.css('.review .state-box--error'))).toBeTruthy();
  });

  // ── Günün soruları (daily modu, issue #99) ──────────────────────────────────
  describe('daily mode', () => {
    function dailySet(overrides: Partial<DailySet> = {}): DailySet {
      return {
        date: '2026-10-05',
        status: 'NotStarted',
        total: 5,
        targetCount: 5,
        answered: 0,
        correct: 0,
        wrong: 0,
        skipped: 0,
        sessionId: null,
        scope: null,
        ...overrides,
      };
    }

    /** Seri `summary.currentActivityStreak`'ten; rozet `currentValue` (en iyi seri, tavanlı) bilerek farklı. */
    function streakResponse(current: number): BadgeProgressResponse {
      return {
        summary: { currentActivityStreak: current } as BadgeProgressResponse['summary'],
        badgeProgress: [
          {
            badgeDefinitionId: 's1',
            name: 'İstikrarlı Öğrenci I',
            description: '',
            iconUrl: '',
            pathKey: 'streak-path',
            pathName: 'İstikrar Yolu',
            pathOrder: 1,
            currentValue: 6,
            targetValue: 7,
            isCompleted: false,
            earnedDateUtc: null,
          },
        ],
        subjectBreakdown: [],
      };
    }

    const reviewWith = (kinds: Array<'correct' | 'wrong' | 'pending'>): PracticeSessionReview => ({
      session,
      questions: kinds.map((kind, i) => ({
        question: buildQuestion({ id: 100 + i }),
        status: kind === 'pending' ? 'Pending' : 'Answered',
        isSkipped: false,
        isCorrect: kind === 'correct',
        selectedAnswerId: null,
        timeTaken: 3,
        shownAt: '2026-10-05T08:00:00Z',
        answeredAt: kind === 'pending' ? null : '2026-10-05T08:00:10Z',
      })),
    });

    const text = (selector: string): string =>
      (fixture.debugElement.query(By.css(selector))?.nativeElement.textContent ?? '').replace(/\s+/g, ' ').trim();

    afterEach(() => localStorage.removeItem('user'));

    it('ngOnInit_DailyNotStarted_SkipsSetupAndOpensFirstQuestionWithStepStrip', () => {
      setup({ daily: '1' });
      practiceService.getDailySet.and.returnValue(of(dailySet()));
      practiceService.startDailySet.and.returnValue(of({ sessionId: session.id, status: 'NotStarted' as const }));
      practiceService.getSession.and.returnValue(of(session));
      practiceService.getNextQuestion.and.returnValue(
        of({ sessionId: session.id, question: buildQuestion(), poolExhausted: false, answeredCount: 0, correctCount: 0 })
      );

      fixture.detectChanges();

      expect(component.dailyMode()).toBeTrue();
      expect(component.phase()).toBe('question');
      expect(studentService.getProfile).not.toHaveBeenCalled();
      expect(practiceService.listSessions).not.toHaveBeenCalled();
      expect(practiceService.getSessionReview).not.toHaveBeenCalled();
      expect(fixture.debugElement.query(By.css('.practice__stats'))).toBeNull();
      expect(text('.daily-bar__step')).toBe('Soru 1 / 5');
      expect(text('app-section-header')).toContain('Günün soruları');

      const items = fixture.debugElement.queryAll(By.css('.daily-steps__item'));
      expect(items.length).toBe(5);
      expect(items[0].attributes['aria-current']).toBe('step');
      expect(items[0].attributes['aria-label']).toBe('1. soru: şu anki soru');
      expect(items[1].attributes['aria-label']).toBe('2. soru: çözülmedi');
      expect(fixture.debugElement.query(By.css('.daily-steps')).attributes['role']).toBe('list');
      expect(fixture.debugElement.query(By.css('.daily-bar__resume'))).toBeNull();
    });

    it('ngOnInit_DailyInProgress_ResumesSameSetWithResumeNoteAndAnsweredSteps', () => {
      setup({ daily: '1' });
      practiceService.getDailySet.and.returnValue(
        of(dailySet({ status: 'InProgress', answered: 2, correct: 1, wrong: 1, sessionId: session.id }))
      );
      practiceService.startDailySet.and.returnValue(of({ sessionId: session.id, status: 'InProgress' as const }));
      practiceService.getSession.and.returnValue(of({ ...session, answeredCount: 2, correctCount: 1 }));
      practiceService.getSessionReview.and.returnValue(of(reviewWith(['wrong', 'correct', 'pending'])));
      practiceService.getNextQuestion.and.returnValue(
        of({ sessionId: session.id, question: buildQuestion(), poolExhausted: false, answeredCount: 2, correctCount: 1 })
      );

      fixture.detectChanges();

      expect(component.phase()).toBe('question');
      expect(component.dailyResults()).toEqual(['wrong', 'correct']);
      expect(text('.daily-bar__step')).toBe('Soru 3 / 5');
      const note = fixture.debugElement.query(By.css('.daily-bar__resume'));
      expect(note.attributes['role']).toBe('status');
      expect(text('.daily-bar__resume')).toContain('Kaldığın yerden devam ediyorsun');

      const items = fixture.debugElement.queryAll(By.css('.daily-steps__item'));
      expect(items[0].classes['daily-steps__item--wrong']).toBeTrue();
      expect(items[0].attributes['aria-label']).toBe('1. soru: yanlış');
      expect(items[1].classes['daily-steps__item--correct']).toBeTrue();
      expect(items[2].attributes['aria-current']).toBe('step');
    });

    it('ngOnInit_DailyCompleted_ShowsAlreadyDoneEndScreenWithoutStartingSession', () => {
      setup({ daily: '1' });
      practiceService.getDailySet.and.returnValue(
        of(dailySet({ status: 'Completed', answered: 5, correct: 4, wrong: 1, sessionId: 77 }))
      );
      practiceService.getSessionReview.and.returnValue(
        of(reviewWith(['correct', 'wrong', 'correct', 'correct', 'correct']))
      );

      fixture.detectChanges();
      fixture.detectChanges();

      expect(practiceService.startDailySet).not.toHaveBeenCalled();
      expect(practiceService.getNextQuestion).not.toHaveBeenCalled();
      expect(component.phase()).toBe('ended');
      expect(component.dailyAlreadyDone()).toBeTrue();
      expect(component.dailyResults()).toEqual(['correct', 'wrong', 'correct', 'correct', 'correct']);
      expect(text('.daily-ended__title')).toBe('Bugünkü setini zaten tamamladın');
      expect(text('.daily-ended')).toContain('Kaçırılan günler birikmez');

      const title = fixture.debugElement.query(By.css('.daily-ended__title')).nativeElement as HTMLElement;
      expect(title.getAttribute('tabindex')).toBe('-1');
      expect(document.activeElement).toBe(title);

      const buttons = fixture.debugElement.queryAll(By.css('.daily-ended .practice__actions button'));
      expect(buttons.map((b) => (b.nativeElement.textContent as string).replace(/\s+/g, ' ').trim())).toEqual([
        'shuffle Serbest pratiğe geç',
        'fact_check Cevapları incele',
      ]);
      buttons[1].nativeElement.click();
      expect(practiceService.getSessionReview).toHaveBeenCalledWith(77);
      expect(component.phase()).toBe('review');

      component.closeReview();
      expect(component.phase()).toBe('ended');
    });

    it('startDaily_StartReturnsCompleted_ShowsAlreadyDoneWithoutNextAndReviewsReturnedSession', () => {
      setup({ daily: '1' });
      // GET henüz NotStarted (oturum açılmış, sessionId dolu) ama start set tamamlandı diyor.
      practiceService.getDailySet.and.returnValue(
        of(dailySet({ answered: 5, correct: 3, wrong: 1, skipped: 1, sessionId: 12 }))
      );
      practiceService.startDailySet.and.returnValue(of({ sessionId: 88, status: 'Completed' }));
      practiceService.getSessionReview.and.returnValue(of(reviewWith(['correct', 'wrong', 'correct', 'correct', 'correct'])));

      fixture.detectChanges();

      expect(practiceService.startDailySet).toHaveBeenCalledTimes(1);
      expect(practiceService.getSession).not.toHaveBeenCalled();
      expect(practiceService.getNextQuestion).not.toHaveBeenCalled();
      expect(practiceService.endSession).not.toHaveBeenCalled();
      expect(component.phase()).toBe('ended');
      expect(component.dailyAlreadyDone()).toBeTrue();
      expect(component.dailySessionId()).toBe(88);
      expect(component.skippedCount()).toBe(1);
      expect(practiceService.getSessionReview).toHaveBeenCalledWith(88);
    });

    it('startDaily_Server409_ShowsEmptyState', () => {
      setup({ daily: '1' });
      practiceService.getDailySet.and.returnValue(of(dailySet()));
      practiceService.startDailySet.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 409, error: { message: 'boş' } }))
      );

      fixture.detectChanges();

      expect(practiceService.getDailySet).toHaveBeenCalledTimes(1);
      expect(component.phase()).toBe('daily');
      expect(component.daily()?.status).toBe('Empty');
      expect(component.dailyLoadError()).toBeNull();
      expect(text('.daily-empty h2')).toBe('Bugün için soru bulunamadı');
    });

    it('ngOnInit_NotStartedWithSessionId_StillCallsStart', () => {
      setup({ daily: '1' });
      practiceService.getDailySet.and.returnValue(of(dailySet({ sessionId: session.id })));
      practiceService.startDailySet.and.returnValue(of({ sessionId: session.id, status: 'NotStarted' }));
      practiceService.getSession.and.returnValue(of(session));
      practiceService.getNextQuestion.and.returnValue(
        of({ sessionId: session.id, question: buildQuestion(), poolExhausted: false, answeredCount: 0, correctCount: 0 })
      );

      fixture.detectChanges();

      expect(practiceService.startDailySet).toHaveBeenCalledTimes(1);
      expect(component.phase()).toBe('question');
      expect(component.dailyResumed()).toBeFalse();
    });

    it('advance_LastQuestionAnswered_EndsSessionShowsDoneScreenWithStreakNote', () => {
      localStorage.setItem('user', JSON.stringify({ id: 16 }));
      badgeService.getUserBadgeProgress.and.returnValue(of(streakResponse(5)));
      setup({ daily: '1' });
      practiceService.getDailySet.and.returnValue(of(dailySet({ total: 1 })));
      practiceService.startDailySet.and.returnValue(of({ sessionId: session.id, status: 'NotStarted' as const }));
      practiceService.getSession.and.returnValue(of(session));
      practiceService.getNextQuestion.and.returnValue(
        of({ sessionId: session.id, question: buildQuestion(), poolExhausted: false, answeredCount: 0, correctCount: 0 })
      );
      practiceService.submitAnswer.and.returnValue(
        of({ sessionId: session.id, questionId: 9, isCorrect: true, skipped: false, correctAnswerId: 900, answeredCount: 1, correctCount: 1 })
      );
      practiceService.endSession.and.returnValue(
        of({ ...session, status: 'Ended', endTime: '2026-10-05T09:00:00Z', answeredCount: 1, correctCount: 1 })
      );

      fixture.detectChanges();
      component.selectAnswer(900);
      component.submitAnswer();
      fixture.detectChanges();

      expect(component.dailyResults()).toEqual(['correct']);
      component.advance();
      fixture.detectChanges();

      expect(practiceService.getNextQuestion).toHaveBeenCalledTimes(1);
      expect(practiceService.endSession).toHaveBeenCalledWith(session.id);
      expect(component.phase()).toBe('ended');
      expect(component.dailyAlreadyDone()).toBeFalse();
      expect(text('.daily-ended__title')).toBe('Bugünkü set tamam!');
      expect(badgeService.getUserBadgeProgress).toHaveBeenCalledWith(16);
      expect(text('.daily-ended__streak')).toBe(
        'local_fire_department Günlük seri: 5 gün. 7 günlük rozete 2 gün kaldı.'
      );
      expect(document.activeElement).toBe(fixture.debugElement.query(By.css('.daily-ended__title')).nativeElement);
    });

    it('finishDaily_SessionAlreadyEnded_ShowsEndScreenWithoutEndSession', () => {
      setup({ daily: '1' });
      practiceService.getDailySet.and.returnValue(
        of(dailySet({ status: 'InProgress', answered: 2, correct: 2, sessionId: session.id }))
      );
      practiceService.startDailySet.and.returnValue(of({ sessionId: session.id, status: 'InProgress' as const }));
      practiceService.getSession.and.returnValue(
        of({ ...session, status: 'Ended', endTime: '2026-10-05T09:00:00Z', answeredCount: 2, correctCount: 2 })
      );
      practiceService.getSessionReview.and.returnValue(of(reviewWith(['correct', 'correct'])));

      fixture.detectChanges();

      expect(practiceService.endSession).not.toHaveBeenCalled();
      expect(practiceService.getNextQuestion).not.toHaveBeenCalled();
      expect(component.phase()).toBe('ended');
      expect(component.dailyAlreadyDone()).toBeFalse();
      expect(component.answeredCount()).toBe(2);
    });

    it('endScreen_StreakOneDayLeft_UsesSingularKey', () => {
      localStorage.setItem('user', JSON.stringify({ id: 16 }));
      badgeService.getUserBadgeProgress.and.returnValue(of(streakResponse(6)));
      setup({ daily: '1' });
      practiceService.getDailySet.and.returnValue(of(dailySet({ status: 'Completed', answered: 5, correct: 5 })));

      fixture.detectChanges();

      expect(text('.daily-ended__streak')).toBe(
        'local_fire_department Günlük seri: 6 gün. 7 günlük rozete 1 gün kaldı.'
      );
    });

    it('endScreen_BrokenStreak_HidesNoteEvenIfBadgeValueHigh', () => {
      localStorage.setItem('user', JSON.stringify({ id: 16 }));
      badgeService.getUserBadgeProgress.and.returnValue(of(streakResponse(0)));
      setup({ daily: '1' });
      practiceService.getDailySet.and.returnValue(of(dailySet({ status: 'Completed', answered: 5, correct: 5 })));

      fixture.detectChanges();

      expect(fixture.debugElement.query(By.css('.daily-ended__streak'))).toBeNull();
    });

    it('endScreen_NoStreakBadge_HidesStreakNote', () => {
      setup({ daily: '1' });
      practiceService.getDailySet.and.returnValue(of(dailySet({ status: 'Completed', answered: 5, correct: 5 })));

      fixture.detectChanges();

      expect(component.phase()).toBe('ended');
      expect(fixture.debugElement.query(By.css('.daily-ended__streak'))).toBeNull();
    });

    it('ngOnInit_DailyEmpty_ShowsEmptyStateWithProfileLink', () => {
      setup({ daily: '1' });
      practiceService.getDailySet.and.returnValue(of(dailySet({ status: 'Empty', total: 0 })));

      fixture.detectChanges();

      expect(component.phase()).toBe('daily');
      expect(practiceService.startDailySet).not.toHaveBeenCalled();
      expect(text('.daily-empty h2')).toBe('Bugün için soru bulunamadı');
      const link = fixture.debugElement.query(By.css('.daily-empty a[href="/student-profile"]'));
      expect(link).toBeTruthy();
    });

    it('ngOnInit_DailyLoadFails_ShowsAlertAndRetryReloads', () => {
      setup({ daily: '1' });
      practiceService.getDailySet.and.returnValues(
        throwError(() => new Error('down')),
        of(dailySet({ status: 'Empty', total: 0 }))
      );

      fixture.detectChanges();

      const alert = fixture.debugElement.query(By.css('.state-box--error'));
      expect(alert.attributes['role']).toBe('alert');
      expect(text('.state-box--error')).toContain('Günün soruları getirilemedi');

      alert.query(By.css('button')).nativeElement.click();
      fixture.detectChanges();

      expect(practiceService.getDailySet).toHaveBeenCalledTimes(2);
      expect(component.dailyLoadError()).toBeNull();
    });

    it('onHeaderBack_DailyQuestion_NavigatesToDashboardWithoutEndingSession', () => {
      setup({ daily: '1' });
      practiceService.getDailySet.and.returnValue(of(dailySet()));
      practiceService.startDailySet.and.returnValue(of({ sessionId: session.id, status: 'NotStarted' as const }));
      practiceService.getSession.and.returnValue(of(session));
      practiceService.getNextQuestion.and.returnValue(
        of({ sessionId: session.id, question: buildQuestion(), poolExhausted: false, answeredCount: 0, correctCount: 0 })
      );
      fixture.detectChanges();

      component.onHeaderBack();

      expect(practiceService.endSession).not.toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);
    });

    it('goFreePractice_FromDailyEnd_SwitchesToSetupAndDropsDailyParam', () => {
      setup({ daily: '1' });
      practiceService.getDailySet.and.returnValue(of(dailySet({ status: 'Completed', answered: 5, correct: 5 })));
      fixture.detectChanges();

      component.goFreePractice();
      fixture.detectChanges();

      expect(component.dailyMode()).toBeFalse();
      expect(component.phase()).toBe('setup');
      expect(studentService.getProfile).toHaveBeenCalled();
      expect(practiceService.listSessions).toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledWith(
        [],
        jasmine.objectContaining({ queryParams: { session: null, daily: null } })
      );
    });

    it('ngOnInit_WithoutDailyParam_KeepsRegularSetupFlow', () => {
      setup();
      fixture.detectChanges();

      expect(component.dailyMode()).toBeFalse();
      expect(practiceService.getDailySet).not.toHaveBeenCalled();
      expect(component.phase()).toBe('setup');
    });
  });
});
