import { Component, Input, signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { ComponentFixture, DeferBlockBehavior, DeferBlockState, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { Subject, of, throwError } from 'rxjs';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideNativeDateAdapter } from '@angular/material/core';

import { WorksheetDetailComponent } from './worksheet-detail.component';
import { TestService } from '../../services/test.service';
import { GradesService } from '../../services/grades.service';
import { AuthService, UserProfile } from '../../services/auth.service';
import { Test, TestInstance, TestStartResult } from '../../models/test-instance';
import { WorksheetDetail } from '../../models/worksheet-detail';
import { WorksheetAssignmentDialogData } from './components/assignment-dialog/worksheet-assignment-dialog.component';
import { StudentService } from '../../services/student.service';
import { StudentLookup } from '../../models/student';
import { CommentThreadComponent } from '../../shared/components/comment-thread/comment-thread.component';
import { QuestionCanvasViewComponent } from '../../shared/components/question-canvas-view/question-canvas-view.component';
import { StudyLinkSuggestionsComponent } from '../../shared/components/study-link-suggestions/study-link-suggestions.component';
import { QuestionRegion } from '../../models/draws';

import { TranslocoTestingModule } from '@jsverse/transloco';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import rootTr from '../../../../public/i18n/tr.json';
import worksheetDetailTr from '../../../../public/i18n/worksheet-detail/tr.json';

/** Gercek scope sozlugu yuklenir; anahtar bozulursa test kirilir (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
  // Scope sozlugu hem scope yolu (provideTranslocoScope yukleyicisi) hem de kok 'tr' icine
  // gomulu olarak verilir; sablondaki 'prefix' bicimi ikincisinden cozulur.
  langs: { tr: { ...rootTr, 'worksheet-detail': worksheetDetailTr }, 'worksheet-detail/tr': worksheetDetailTr },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
    // TranslocoTestingModule uygulamanin config'ini almaz; scope oneki kebab kalsin (issue #183).
    scopes: { keepCasing: true },
  },
  preloadLangs: true,
});

describe('WorksheetDetailComponent', () => {
  let component: WorksheetDetailComponent;
  let testService: jasmine.SpyObj<TestService>;
  let router: jasmine.SpyObj<Router>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  beforeEach(() => {
    testService = jasmine.createSpyObj<TestService>('TestService', [
      'getWorksheetDetail',
      'copyWorksheet',
      'startTest',
      'get',
    ]);
    testService.getWorksheetDetail.and.returnValue(of({} as any));

    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    TestBed.configureTestingModule({
      imports: [WorksheetDetailComponent, translocoTesting],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TestService, useValue: testService },
        { provide: Router, useValue: router },
        { provide: MatSnackBar, useValue: snackBar },
        { provide: MatDialog, useValue: jasmine.createSpyObj<MatDialog>('MatDialog', ['open']) },
        { provide: AuthService, useValue: { hasRole: () => false, hasRealmRole: () => false, user: signal(null) } },
        { provide: StudentService, useValue: {} },
        { provide: GradesService, useValue: { getGrades: () => of([]) } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({}), data: {} }, params: of({}), queryParams: of({}) },
        },
      ],
    });

    component = TestBed.createComponent(WorksheetDetailComponent).componentInstance;
    // ngOnInit'i tetiklemeden — sadece kopyalama akışını izole test ediyoruz.
    (component as any)['detail'].set({ worksheet: { id: 5 } } as any);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('study link suggestions (issue #61)', () => {
    it('currentQuestionHelpers_FollowCurrentIndex', () => {
      component.results = { testInstanceQuestions: [{ id: 501 }, { id: 502 }] } as unknown as TestInstance;
      component.questions = [{ status: 'correct' }, { status: 'incorrect' }];

      expect(component['currentTestInstanceQuestionId']()).toBe(501);
      expect(component['isCurrentQuestionWrong']()).toBeFalse();

      component.questionSelected(1);
      expect(component['currentTestInstanceQuestionId']()).toBe(502);
      expect(component['isCurrentQuestionWrong']()).toBeTrue();
    });

    it('currentQuestionHelpers_NoResults_ReturnNullAndFalse', () => {
      expect(component['currentTestInstanceQuestionId']()).toBeNull();
      expect(component['isCurrentQuestionWrong']()).toBeFalse();
    });
  });

  describe('StartTest (issue #383)', () => {
    const startResult = (overrides: Partial<TestStartResult>): TestStartResult => ({
      success: true,
      message: '',
      objectId: 0,
      notFound: false,
      forbidden: false,
      conflict: false,
      instanceId: 0,
      startTime: '2026-10-06T10:00:00Z',
      ...overrides,
    });

    beforeEach(() => {
      component.testId = 5;
    });

    it('StartTest_Success_NavigatesToTestsolveWithWorksheetIdState', () => {
      testService.startTest.and.returnValue(of(startResult({ instanceId: 91 })));

      component.StartTest(5);

      expect(router.navigate).toHaveBeenCalledWith(['/testsolve', 91], { state: { worksheetId: 5 } });
      expect(testService.getWorksheetDetail).not.toHaveBeenCalled();
    });

    it('StartTest_AlreadyCompleted_ShowsLocalizedSnackAndReloadsDetailAndWorksheet', () => {
      const reloaded = { id: 5, instance: { status: 1 } } as unknown as Test;
      testService.startTest.and.returnValue(
        of(startResult({ success: false, instanceId: 91, message: 'Bu test zaten tamamlanmış.' }))
      );
      testService.get.and.returnValue(of(reloaded));

      component.StartTest(5);

      expect(router.navigate).not.toHaveBeenCalled();
      expect(snackBar.open).toHaveBeenCalledWith('Bu test zaten tamamlanmış.', 'Tamam', jasmine.any(Object));
      expect(testService.getWorksheetDetail).toHaveBeenCalledWith(5);
      expect(testService.get).toHaveBeenCalledWith(5);
      expect(component.exam).toBe(reloaded);
    });

    it('StartTest_FailureWithoutInstance_OnlyShowsSnack', () => {
      testService.startTest.and.returnValue(of(startResult({ success: false, message: 'Hata' })));

      component.StartTest(5);

      expect(snackBar.open).toHaveBeenCalledWith('Hata', 'Tamam', jasmine.any(Object));
      expect(testService.getWorksheetDetail).not.toHaveBeenCalled();
      expect(router.navigate).not.toHaveBeenCalled();
    });
  });

  describe('copyWorksheet', () => {
    it('copyWorksheet_Success_SnackBarThenNavigateToNewWorksheetAndResetLoading', () => {
      testService.copyWorksheet.and.returnValue(of({ worksheetId: 777 } as any));

      (component as any)['copyWorksheet']();

      expect(testService.copyWorksheet).toHaveBeenCalledWith(5);
      expect(snackBar.open).toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledWith(['/exam', 777]);
      expect((component as any)['copyLoading']()).toBeFalse();
    });

    it('copyWorksheet_WhilePending_SetsCopyLoadingTrueThenFalseOnComplete', () => {
      const gate = new Subject<any>();
      testService.copyWorksheet.and.returnValue(gate.asObservable());

      (component as any)['copyWorksheet']();
      expect((component as any)['copyLoading']()).toBeTrue();

      gate.next({ worksheetId: 1 });
      gate.complete();
      expect((component as any)['copyLoading']()).toBeFalse();
    });

    it('copyWorksheet_Error_ShowsSnackBarAndResetsLoadingWithoutNavigation', () => {
      testService.copyWorksheet.and.returnValue(throwError(() => ({ error: { message: 'Kopyalama başarısız' } })));

      (component as any)['copyWorksheet']();

      expect(snackBar.open).toHaveBeenCalledWith('Kopyalama başarısız', 'Tamam', jasmine.any(Object));
      expect(router.navigate).not.toHaveBeenCalled();
      expect((component as any)['copyLoading']()).toBeFalse();
    });

    it('copyWorksheet_NoWorksheetId_DoesNothing', () => {
      (component as any)['detail'].set(null);

      (component as any)['copyWorksheet']();

      expect(testService.copyWorksheet).not.toHaveBeenCalled();
    });

    it('copyWorksheet_AlreadyLoading_DoesNotCallServiceAgain', () => {
      (component as any)['copyLoading'].set(true);

      (component as any)['copyWorksheet']();

      expect(testService.copyWorksheet).not.toHaveBeenCalled();
    });
  });
});

describe('WorksheetDetailComponent reminder=edit deep link', () => {
  it('ngOnInit_QueryParamReminderEditWithLoadedStudentDetail_StartsReminderEditing', () => {
    const testService = jasmine.createSpyObj<TestService>('TestService', [
      'getWorksheetDetail',
      'copyWorksheet',
      'get',
    ]);
    testService.getWorksheetDetail.and.returnValue(
      of({
        worksheet: { id: 12, canEdit: true, canAssign: true },
        plannedReminder: {
          scheduledFor: new Date(2026, 9, 1, 9, 0).toISOString(),
          remindBeforeMinutes: 60,
          status: 'Pending',
        },
        attempts: [],
      } as any),
    );
    testService.get.and.returnValue(of(null as any));

    const router = jasmine.createSpyObj<Router>('Router', ['navigate']);

    TestBed.configureTestingModule({
      imports: [WorksheetDetailComponent, NoopAnimationsModule, translocoTesting],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TestService, useValue: testService },
        { provide: Router, useValue: router },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']) },
        { provide: MatDialog, useValue: jasmine.createSpyObj<MatDialog>('MatDialog', ['open']) },
        { provide: AuthService, useValue: { hasRole: () => false, hasRealmRole: () => false, user: signal(null) } },
        { provide: StudentService, useValue: {} },
        { provide: GradesService, useValue: { getGrades: () => of([]) } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap({}), data: {} },
            params: of({ testId: '12' }),
            queryParams: of({ reminder: 'edit' }),
            paramMap: of(convertToParamMap({ testId: '12' })),
            queryParamMap: of(convertToParamMap({ reminder: 'edit' })),
          },
        },
      ],
    });

    const component = TestBed.createComponent(WorksheetDetailComponent).componentInstance;
    component.ngOnInit();

    expect(testService.getWorksheetDetail).toHaveBeenCalledWith(12);
    expect((component as any)['reminderEditing']()).toBeTrue();
    expect((component as any)['showReminderForm']()).toBeTrue();
  });
});

/** Issue #222: bağımsız (okulsuz) öğretmen sınıf bazlı atama yapamaz. */
describe('WorksheetDetailComponent independent tutor assignment', () => {
  const teacherProfile = (schoolId: number | null): UserProfile => ({
    email: 't@x.com',
    avatar: '',
    fullName: 'Öğretmen',
    id: 1,
    keycloakId: 'kc-1',
    profileId: 1,
    role: 'Teacher',
    schoolId,
  });

  function setup(options: {
    role: 'Teacher' | 'Admin';
    user: UserProfile | null;
    realmAdmin?: boolean;
    studentService?: Partial<StudentService>;
  }) {
    const dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    dialog.open.and.returnValue({ afterClosed: () => of(undefined) } as MatDialogRef<unknown>);

    TestBed.configureTestingModule({
      imports: [WorksheetDetailComponent, NoopAnimationsModule, translocoTesting],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TestService, useValue: jasmine.createSpyObj<TestService>('TestService', ['getWorksheetDetail']) },
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']) },
        { provide: AuthService, useValue: { hasRole: (r: string) => r === options.role, hasRealmRole: (r: string) => options.realmAdmin === true && r === 'Admin', user: signal(options.user) } },
        { provide: StudentService, useValue: options.studentService ?? {} },
        { provide: GradesService, useValue: { getGrades: () => of([]) } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap({}), data: {} },
            params: of({}),
            queryParams: of({}),
            paramMap: of(convertToParamMap({})),
            queryParamMap: of(convertToParamMap({})),
          },
        },
      ],
    });
    // Komponent MatDialogModule import ettiği için spy, komponent seviyesinde de verilir.
    TestBed.overrideComponent(WorksheetDetailComponent, { add: { providers: [{ provide: MatDialog, useValue: dialog }] } });

    const fixture = TestBed.createComponent(WorksheetDetailComponent);
    const component = fixture.componentInstance;
    component.exam = { id: 12 } as Test;
    component['detail'].set({
      worksheet: { id: 12, name: 'Deneme', canEdit: true, canAssign: true },
      attempts: [],
      similarWorksheets: [],
    } as unknown as WorksheetDetail);
    // Uygulama rolü Admin iken sayfa öğretmen görünümünü (ve atama butonlarını) hiç render etmez;
    // o durumda yalnızca computed doğrulanır.
    if (options.role === 'Teacher') {
      fixture.detectChanges();
    }
    return { fixture, component, dialog };
  }

  const buttons = (fixture: { nativeElement: HTMLElement }, id: string) =>
    fixture.nativeElement.querySelectorAll(`[data-testid="${id}"]`);

  it('bağımsız öğretmende "Sınıfa ata" butonları render edilmez ve dialog isIndependentTutor=true alır', () => {
    const { fixture, dialog } = setup({ role: 'Teacher', user: teacherProfile(null) });

    expect(buttons(fixture, 'assign-grade-btn').length).toBe(0);
    const studentButtons = buttons(fixture, 'assign-student-btn');
    expect(studentButtons.length).toBeGreaterThan(0);

    (studentButtons[0] as HTMLButtonElement).click();

    expect(dialog.open).toHaveBeenCalled();
    const config = dialog.open.calls.mostRecent().args[1] as { data: WorksheetAssignmentDialogData };
    expect(config.data.isIndependentTutor).toBeTrue();
  });

  it('okullu öğretmende "Sınıfa ata" butonları görünür', () => {
    const { fixture, component } = setup({ role: 'Teacher', user: teacherProfile(3) });

    expect(component['isIndependentTutor']()).toBeFalse();
    expect(buttons(fixture, 'assign-grade-btn').length).toBeGreaterThan(0);
  });

  it('uygulama rolü Admin olan okulsuz kullanıcı bağımsız sayılmaz', () => {
    const { component } = setup({ role: 'Admin', user: { ...teacherProfile(null), role: 'Admin' } });

    expect(component['isIndependentTutor']()).toBeFalse();
  });

  it('realm rolü Admin olan okulsuz öğretmen hesabında "Sınıfa ata" butonları görünür', () => {
    const { fixture, component } = setup({ role: 'Teacher', user: teacherProfile(null), realmAdmin: true });

    expect(component['isIndependentTutor']()).toBeFalse();
    expect(buttons(fixture, 'assign-grade-btn').length).toBeGreaterThan(0);
  });

  /** Issue #223: lookup durumu dialog'a canlı sinyal olarak geçer. */
  it('lookup başarılıysa durum loaded olur ve dialog canlı sinyalleri alır', () => {
    const lookup = new Subject<StudentLookup[]>();
    const { fixture, component, dialog } = setup({
      role: 'Teacher',
      user: teacherProfile(null),
      studentService: { getLookup: () => lookup.asObservable() },
    });

    component['loadStudentLookup']();
    (buttons(fixture, 'assign-student-btn')[0] as HTMLButtonElement).click();
    const data = (dialog.open.calls.mostRecent().args[1] as { data: WorksheetAssignmentDialogData }).data;
    expect(data.studentsStatus!()).toBe('loading');
    expect(data.students()).toEqual([]);

    const ada: StudentLookup = { id: 11, userId: 21, studentNumber: '101', fullName: 'Ada', schoolName: 'Okul', gradeId: 1 };
    lookup.next([ada]);

    expect(component['studentLookupStatus']()).toBe('loaded');
    expect(data.studentsStatus!()).toBe('loaded');
    expect(data.students()).toEqual([ada]);
  });

  it('lookup hata verirse durum error olur ve dialog bunu görür', () => {
    const lookup = new Subject<StudentLookup[]>();
    const { fixture, component, dialog } = setup({
      role: 'Teacher',
      user: teacherProfile(3),
      studentService: { getLookup: () => lookup.asObservable() },
    });

    component['loadStudentLookup']();
    (buttons(fixture, 'assign-student-btn')[0] as HTMLButtonElement).click();
    const data = (dialog.open.calls.mostRecent().args[1] as { data: WorksheetAssignmentDialogData }).data;

    lookup.error(new Error('boom'));

    expect(component['studentLookupStatus']()).toBe('error');
    expect(data.studentsStatus!()).toBe('error');
    expect(data.students()).toEqual([]);
  });

  it('profil henüz yokken (user null) "Sınıfa ata" butonları görünür', () => {
    const { fixture, component } = setup({ role: 'Teacher', user: null });

    expect(component['isIndependentTutor']()).toBeFalse();
    expect(buttons(fixture, 'assign-grade-btn').length).toBeGreaterThan(0);
  });
});

/** Soru görüntüleyici stub'ı — thread yerleşimi testinde canvas render'ı gereksiz. */
@Component({ selector: 'app-question-canvas-view', standalone: true, template: '' })
class QuestionCanvasViewStubComponent {
  @Input() questionRegion: unknown;
  @Input() selectedChoice: unknown;
  @Input() correctChoice: unknown;
  @Input() mode: unknown;
}

@Component({ selector: 'app-study-link-suggestions', standalone: true, template: '' })
class StudyLinkSuggestionsStubComponent {
  @Input() testInstanceId: unknown;
  @Input() questionId: unknown;
  @Input() testInstanceQuestionId: unknown;
  @Input() answeredWrong: unknown;
}

/** Issue #105: yorum/soru thread'lerinin yerleşimi ve bildirim derin linki (commentId vurgusu). */
describe('WorksheetDetailComponent comment threads', () => {
  interface SetupOptions {
    role: 'Student' | 'Teacher';
    query?: Record<string, string>;
    completed?: boolean;
    commentsEnabled?: boolean;
  }

  async function setup(options: SetupOptions & { renderDeferred?: boolean }) {
    TestBed.configureTestingModule({
      // Thread'ler @defer içinde: testte açıkça tetiklenir.
      deferBlockBehavior: DeferBlockBehavior.Manual,
      imports: [WorksheetDetailComponent, NoopAnimationsModule, translocoTesting],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNativeDateAdapter(),
        { provide: TestService, useValue: jasmine.createSpyObj<TestService>('TestService', ['getWorksheetDetail']) },
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']) },
        { provide: MatDialog, useValue: jasmine.createSpyObj<MatDialog>('MatDialog', ['open']) },
        {
          provide: AuthService,
          useValue: { hasRole: (r: string) => r === options.role, hasRealmRole: () => false, user: signal(null) },
        },
        { provide: StudentService, useValue: { getLookup: () => of([]) } },
        { provide: GradesService, useValue: { getGrades: () => of([]) } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap({}), data: {} },
            params: of({}),
            queryParams: of(options.query ?? {}),
            paramMap: of(convertToParamMap({})),
            queryParamMap: of(convertToParamMap(options.query ?? {})),
          },
        },
      ],
    });
    TestBed.overrideComponent(WorksheetDetailComponent, {
      remove: { imports: [QuestionCanvasViewComponent, StudyLinkSuggestionsComponent] },
      add: { imports: [QuestionCanvasViewStubComponent, StudyLinkSuggestionsStubComponent] },
    });

    const fixture = TestBed.createComponent(WorksheetDetailComponent);
    const component = fixture.componentInstance;
    component.exam = { id: 12 } as Test;
    component.ngOnInit();
    component['detail'].set({
      worksheet: {
        id: 12,
        name: 'Kesirler',
        canEdit: true,
        canAssign: true,
        commentsEnabled: options.commentsEnabled ?? true,
      },
      attempts: [],
      similarWorksheets: [],
      topicBreakdown: [],
      outcomes: [],
      stats: { solverCount: 0, averageScorePercent: null },
      completedResult: options.completed
        ? {
            instanceId: 900,
            scorePercent: 50,
            correctCount: 1,
            wrongCount: 1,
            emptyCount: 0,
            durationSeconds: 60,
            topicSuccess: [],
            rank: null,
          }
        : null,
    } as unknown as WorksheetDetail);
    if (options.completed) {
      component.regions.set([
        { id: 33, answers: [] },
        { id: 34, answers: [] },
      ] as unknown as QuestionRegion[]);
      component.questions = [{ status: 'correct' }, { status: 'incorrect' }];
    }
    fixture.detectChanges();
    fixture.detectChanges();
    if (options.renderDeferred !== false) {
      await renderDeferred(fixture);
    }
    return { fixture, component };
  }

  /** Tüm @defer bloklarını tamamlanmış duruma getirir. */
  async function renderDeferred(fixture: ComponentFixture<WorksheetDetailComponent>): Promise<void> {
    for (const block of await fixture.getDeferBlocks()) {
      await block.render(DeferBlockState.Complete);
    }
    fixture.detectChanges();
  }

  const threads = (fixture: ComponentFixture<WorksheetDetailComponent>) =>
    fixture.debugElement
      .queryAll(By.directive(CommentThreadComponent))
      .map((d) => d.componentInstance as CommentThreadComponent);
  const byTestId = (fixture: ComponentFixture<WorksheetDetailComponent>, id: string) =>
    (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(`[data-testid="${id}"]`);

  it('completedStudent_RendersWorksheetThreadAndSelectedQuestionThread', async () => {
    const { fixture, component } = await setup({ role: 'Student', completed: true });

    expect(byTestId(fixture, 'worksheet-comments')).not.toBeNull();
    expect(byTestId(fixture, 'question-comments')).not.toBeNull();
    const all = threads(fixture);
    expect(all.length).toBe(2);
    const questionThread = all.find((t) => t.questionId() !== null)!;
    expect(questionThread.worksheetId()).toBe(12);
    expect(questionThread.questionId()).toBe(33);
    const worksheetThread = all.find((t) => t.questionId() === null)!;
    expect(worksheetThread.viewerIsTeacher()).toBeFalse();
    expect(worksheetThread.studentsLockedNotice()).toBeFalse();

    // Soru değişince aynı thread yeni soruya geçer (yeniden yükler).
    component.questionSelected(1);
    fixture.detectChanges();
    expect(threads(fixture).find((t) => t.questionId() !== null)!.questionId()).toBe(34);
  });

  it('teacher_WorksheetThreadOnlyWithTeacherFlags', async () => {
    const { fixture } = await setup({ role: 'Teacher', commentsEnabled: false });

    expect(byTestId(fixture, 'question-comments')).toBeNull();
    const all = threads(fixture);
    expect(all.length).toBe(1);
    expect(all[0].viewerIsTeacher()).toBeTrue();
    expect(all[0].studentsLockedNotice()).toBeTrue();
    expect(all[0].showEditSettings()).toBeTrue();
  });

  it('startView_StudentStillGetsWorksheetThread', async () => {
    const { fixture } = await setup({ role: 'Student' });

    expect(byTestId(fixture, 'worksheet-comments')).not.toBeNull();
    expect(threads(fixture).length).toBe(1);
  });

  it('deepLink_WorksheetLevelComment_HighlightsInWorksheetThread', async () => {
    const { fixture, component } = await setup({ role: 'Teacher', query: { commentId: '57', rootCommentId: '56' } });

    expect(component['commentLink']()).toEqual({ worksheetId: 0, commentId: 57, questionId: null, rootCommentId: 56 });
    const [worksheetThread] = threads(fixture);
    expect(worksheetThread.highlightCommentId()).toBe(57);
    expect(worksheetThread.highlightRootId()).toBe(56);
    expect(byTestId(fixture, 'linked-question-comments')).toBeNull();
  });

  it('deepLink_QuestionCommentForTeacher_RendersSeparateQuestionThreadCard', async () => {
    const { fixture } = await setup({ role: 'Teacher', query: { commentId: '57', questionId: '34' } });

    expect(byTestId(fixture, 'linked-question-comments')).not.toBeNull();
    const questionThread = threads(fixture).find((t) => t.questionId() === 34)!;
    expect(questionThread.highlightCommentId()).toBe(57);
    expect(questionThread.viewerIsTeacher()).toBeTrue();
    const worksheetThread = threads(fixture).find((t) => t.questionId() === null)!;
    expect(worksheetThread.highlightCommentId()).toBeNull();
  });

  // Issue #309: kart başlığındaki soru sırası thread yanıtından gelir (URL'den değil).
  it('linkedQuestionCard_TitleUsesQuestionOrderFromThreadThenFallsBack', async () => {
    const { fixture } = await setup({ role: 'Teacher', query: { commentId: '57', questionId: '34', questionOrder: '9' } });
    const title = () => byTestId(fixture, 'linked-question-title')!.textContent!.replace(/\s+/g, ' ').trim();

    // Thread yanıtı gelmeden: genel başlık (URL'deki questionOrder yok sayılır).
    expect(title()).toBe(worksheetDetailTr.comments.linkedQuestionTitle);

    const questionThread = threads(fixture).find((t) => t.questionId() === 34)!;
    questionThread.questionOrderChange.emit(3);
    fixture.detectChanges();
    expect(title()).toBe('Soru 3 hakkındaki yorumlar');

    questionThread.questionOrderChange.emit(null);
    fixture.detectChanges();
    expect(title()).toBe(worksheetDetailTr.comments.linkedQuestionTitle);
  });

  it('linkedQuestionCard_LinkMovesToAnotherQuestion_OldOrderNotShown', async () => {
    const { fixture, component } = await setup({ role: 'Teacher', query: { commentId: '57', questionId: '34' } });
    const title = () => byTestId(fixture, 'linked-question-title')!.textContent!.replace(/\s+/g, ' ').trim();

    threads(fixture).find((t) => t.questionId() === 34)!.questionOrderChange.emit(3);
    fixture.detectChanges();
    expect(title()).toBe('Soru 3 hakkındaki yorumlar');

    // Code review D4: derin link başka soruya geçince eski sıra başlıkta kalmaz.
    component['commentLink'].set({ worksheetId: 0, commentId: 60, questionId: 35, rootCommentId: null });
    fixture.detectChanges();
    expect(title()).toBe(worksheetDetailTr.comments.linkedQuestionTitle);

    // Aynı soruya geri dönülünce (thread o soru için yeniden yayınlamadan) önceki doğru sıra geçerlidir.
    component['commentLink'].set({ worksheetId: 0, commentId: 61, questionId: 34, rootCommentId: null });
    fixture.detectChanges();
    expect(title()).toBe('Soru 3 hakkındaki yorumlar');
  });

  it('teacherInsights_HardestQuestionsUseDisplayNumberWithOrderFallback', async () => {
    const { fixture, component } = await setup({ role: 'Teacher' });
    component['detail'].update((d) => ({
      ...d!,
      teacherInsights: {
        hardestQuestions: [
          { questionId: 1, order: 0, number: 1, text: null, subtopicName: 'Kesir', answeredCount: 4, correctPercent: 25 },
          { questionId: 2, order: 7, text: null, subtopicName: null, answeredCount: 2, correctPercent: 50 },
        ],
        difficultyDistribution: { easy: 0, medium: 0, hard: 0 },
        classifiedCount: 0,
        totalQuestionCount: 2,
        unclassifiedCount: 2,
      },
    }));
    fixture.detectChanges();

    const rows = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[data-testid="hardest-question"]')
    );
    expect(rows.length).toBe(2);
    expect(rows[0].querySelector('[data-testid="hardest-question-number"]')!.textContent!.trim()).toBe('1');
    expect(rows[0].textContent).toContain('Soru 1');
    // Eski sunucu (number yok): ham order'a düşülür.
    expect(rows[1].querySelector('[data-testid="hardest-question-number"]')!.textContent!.trim()).toBe('7');
  });

  it('teacher_WorksheetThreadShowsStudentsSummaryAndEditSettingsEvenWhenCommentsEnabled', async () => {
    const { fixture } = await setup({ role: 'Teacher', commentsEnabled: true });

    const [worksheetThread] = threads(fixture);
    expect(worksheetThread.showStudentsSummary()).toBeTrue();
    expect(worksheetThread.studentsLockedNotice()).toBeFalse();
    expect(worksheetThread.showEditSettings()).toBeTrue();
  });

  it('student_WorksheetThreadHasNoStudentsSummary', async () => {
    const { fixture } = await setup({ role: 'Student' });

    expect(threads(fixture)[0].showStudentsSummary()).toBeFalse();
    expect(threads(fixture)[0].showEditSettings()).toBeFalse();
  });

  it('deepLink_QuestionCommentForCompletedStudent_SelectsQuestionInReview', async () => {
    const { fixture, component } = await setup({
      role: 'Student',
      completed: true,
      query: { commentId: '70', questionId: '34' },
    });
    fixture.detectChanges();

    expect(component.currentIndex()).toBe(1);
    expect(byTestId(fixture, 'linked-question-comments')).toBeNull();
    const questionThread = threads(fixture).find((t) => t.questionId() !== null)!;
    expect(questionThread.questionId()).toBe(34);
    expect(questionThread.highlightCommentId()).toBe(70);
  });

  it('beforeDeferTrigger_ShowsPlaceholderNotThread', async () => {
    const { fixture } = await setup({ role: 'Student', completed: true, renderDeferred: false });

    expect(threads(fixture).length).toBe(0);
    expect(fixture.nativeElement.querySelectorAll('[data-testid="comments-placeholder"]').length).toBe(2);
  });

  it('deepLink_InvalidCommentId_Ignored', async () => {
    const { component } = await setup({ role: 'Student', query: { commentId: 'abc', questionId: '34' } });

    expect(component['commentLink']()).toBeNull();
  });
});
