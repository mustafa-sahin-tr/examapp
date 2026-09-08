import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { PracticeSolveComponent } from './practice-solve.component';
import { PracticeService } from '../../services/practice.service';
import { StudentService } from '../../services/student.service';
import { SubjectService } from '../../services/subject.service';
import { PracticeAnswerResult, PracticeSession, PracticeSessionReview } from '../../models/practice';
import { StudentProfile } from '../../models/student-profile';
import { Question } from '../../models/question';
import { Subject } from '../../models/subject';
import { Paged } from '../../models/test-instance';

describe('PracticeSolveComponent', () => {
  let fixture: ComponentFixture<PracticeSolveComponent>;
  let component: PracticeSolveComponent;
  let practiceService: jasmine.SpyObj<PracticeService>;
  let studentService: jasmine.SpyObj<StudentService>;
  let subjectService: jasmine.SpyObj<SubjectService>;
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
      imports: [PracticeSolveComponent],
      providers: [
        { provide: PracticeService, useValue: practiceService },
        { provide: StudentService, useValue: studentService },
        { provide: SubjectService, useValue: subjectService },
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
    ]);
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
});
