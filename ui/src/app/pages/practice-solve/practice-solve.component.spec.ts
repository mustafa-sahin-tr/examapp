import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';

import { PracticeSolveComponent } from './practice-solve.component';
import { PracticeService } from '../../services/practice.service';
import { StudentService } from '../../services/student.service';
import { SubjectService } from '../../services/subject.service';
import { PracticeAnswerResult, PracticeSession } from '../../models/practice';
import { StudentProfile } from '../../models/student-profile';
import { Question } from '../../models/question';
import { Subject } from '../../models/subject';

describe('PracticeSolveComponent', () => {
  let fixture: ComponentFixture<PracticeSolveComponent>;
  let component: PracticeSolveComponent;
  let practiceService: jasmine.SpyObj<PracticeService>;
  let studentService: jasmine.SpyObj<StudentService>;
  let subjectService: jasmine.SpyObj<SubjectService>;
  let router: Router;

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
      'toQuestionRegion',
    ]);
    studentService = jasmine.createSpyObj<StudentService>('StudentService', ['getProfile']);
    subjectService = jasmine.createSpyObj<SubjectService>('SubjectService', ['getSubjectsByGrade', 'getTopicsBySubjectAndGrade']);

    studentService.getProfile.and.returnValue(of(profile));
    subjectService.getSubjectsByGrade.and.returnValue(of(subjects));
    practiceService.toQuestionRegion.and.callFake((q: Question, correctAnswerId?: number | null) => ({
      id: q.id,
      name: 'Soru',
      x: q.x,
      y: q.y,
      width: q.width,
      height: q.height,
      passageId: '',
      answers: [],
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
});
