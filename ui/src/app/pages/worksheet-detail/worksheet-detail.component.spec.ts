import type { MockedObject } from "vitest";
import { Component, Input, signal, ChangeDetectionStrategy } from '@angular/core';
import { By } from '@angular/platform-browser';
import { ComponentFixture, DeferBlockBehavior, DeferBlockState, TestBed } from '@angular/core/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { BehaviorSubject, NEVER, Observable, Subject, map, of, throwError } from 'rxjs';
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
import { BankQuestionDialogComponent, BankQuestionDialogData } from './components/bank-question-dialog/bank-question-dialog.component';
import { LinkBankQuestionsResult } from '../../models/question-bank';

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
    let testService: MockedObject<TestService>;
    let router: MockedObject<Router>;
    let snackBar: MockedObject<MatSnackBar>;

    beforeEach(() => {
        testService = {
            getWorksheetDetail: vi.fn().mockName("TestService.getWorksheetDetail"),
            copyWorksheet: vi.fn().mockName("TestService.copyWorksheet"),
            startTest: vi.fn().mockName("TestService.startTest"),
            get: vi.fn().mockName("TestService.get")
        } as unknown as MockedObject<TestService>;
        testService.getWorksheetDetail.mockReturnValue(of({} as any));

        router = {
            navigate: vi.fn().mockName("Router.navigate")
        } as unknown as MockedObject<Router>;
        snackBar = {
            open: vi.fn().mockName("MatSnackBar.open")
        } as unknown as MockedObject<MatSnackBar>;

        TestBed.configureTestingModule({
            imports: [WorksheetDetailComponent, translocoTesting],
            providers: [
                provideHttpClient(withXhr()),
                provideHttpClientTesting(),
                { provide: TestService, useValue: testService },
                { provide: Router, useValue: router },
                { provide: MatSnackBar, useValue: snackBar },
                { provide: MatDialog, useValue: {
                        open: vi.fn().mockName("MatDialog.open")
                    } },
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
            expect(component['isCurrentQuestionWrong']()).toBe(false);

            component.questionSelected(1);
            expect(component['currentTestInstanceQuestionId']()).toBe(502);
            expect(component['isCurrentQuestionWrong']()).toBe(true);
        });

        it('currentQuestionHelpers_NoResults_ReturnNullAndFalse', () => {
            expect(component['currentTestInstanceQuestionId']()).toBeNull();
            expect(component['isCurrentQuestionWrong']()).toBe(false);
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
            testService.startTest.mockReturnValue(of(startResult({ instanceId: 91 })));

            component.StartTest(5);

            expect(router.navigate).toHaveBeenCalledWith(['/testsolve', 91], { state: { worksheetId: 5 } });
            expect(testService.getWorksheetDetail).not.toHaveBeenCalled();
        });

        it('StartTest_AlreadyCompleted_ShowsLocalizedSnackAndReloadsDetailAndWorksheet', () => {
            const reloaded = { id: 5, instance: { status: 1 } } as unknown as Test;
            testService.startTest.mockReturnValue(of(startResult({ success: false, instanceId: 91, message: 'Bu test zaten tamamlanmış.' })));
            testService.get.mockReturnValue(of(reloaded));

            component.StartTest(5);

            expect(router.navigate).not.toHaveBeenCalled();
            expect(snackBar.open).toHaveBeenCalledWith('Bu test zaten tamamlanmış.', 'Tamam', expect.any(Object));
            expect(testService.getWorksheetDetail).toHaveBeenCalledWith(5);
            expect(testService.get).toHaveBeenCalledWith(5);
            expect(component.exam).toBe(reloaded);
        });

        it('StartTest_FailureWithoutInstance_OnlyShowsSnack', () => {
            testService.startTest.mockReturnValue(of(startResult({ success: false, message: 'Hata' })));

            component.StartTest(5);

            expect(snackBar.open).toHaveBeenCalledWith('Hata', 'Tamam', expect.any(Object));
            expect(testService.getWorksheetDetail).not.toHaveBeenCalled();
            expect(router.navigate).not.toHaveBeenCalled();
        });
    });

    describe('copyWorksheet', () => {
        it('copyWorksheet_Success_SnackBarThenNavigateToNewWorksheetAndResetLoading', () => {
            testService.copyWorksheet.mockReturnValue(of({ worksheetId: 777 } as any));

            (component as any)['copyWorksheet']();

            expect(testService.copyWorksheet).toHaveBeenCalledWith(5);
            expect(snackBar.open).toHaveBeenCalled();
            expect(router.navigate).toHaveBeenCalledWith(['/exam', 777]);
            expect((component as any)['copyLoading']()).toBe(false);
        });

        it('copyWorksheet_WhilePending_SetsCopyLoadingTrueThenFalseOnComplete', () => {
            const gate = new Subject<any>();
            testService.copyWorksheet.mockReturnValue(gate.asObservable());

            (component as any)['copyWorksheet']();
            expect((component as any)['copyLoading']()).toBe(true);

            gate.next({ worksheetId: 1 });
            gate.complete();
            expect((component as any)['copyLoading']()).toBe(false);
        });

        it('copyWorksheet_Error_ShowsSnackBarAndResetsLoadingWithoutNavigation', () => {
            testService.copyWorksheet.mockReturnValue(throwError(() => ({ error: { message: 'Kopyalama başarısız' } })));

            (component as any)['copyWorksheet']();

            expect(snackBar.open).toHaveBeenCalledWith('Kopyalama başarısız', 'Tamam', expect.any(Object));
            expect(router.navigate).not.toHaveBeenCalled();
            expect((component as any)['copyLoading']()).toBe(false);
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
        const testService = {
            getWorksheetDetail: vi.fn().mockName("TestService.getWorksheetDetail"),
            copyWorksheet: vi.fn().mockName("TestService.copyWorksheet"),
            get: vi.fn().mockName("TestService.get")
        };
        testService.getWorksheetDetail.mockReturnValue(of({
            worksheet: { id: 12, canEdit: true, canAssign: true },
            plannedReminder: {
                scheduledFor: new Date(2026, 9, 1, 9, 0).toISOString(),
                remindBeforeMinutes: 60,
                status: 'Pending',
            },
            attempts: [],
        } as any));
        testService.get.mockReturnValue(of(null as any));

        const router = {
            navigate: vi.fn().mockName("Router.navigate")
        };

        TestBed.configureTestingModule({
            imports: [WorksheetDetailComponent, NoopAnimationsModule, translocoTesting],
            providers: [
                provideHttpClient(withXhr()),
                provideHttpClientTesting(),
                { provide: TestService, useValue: testService },
                { provide: Router, useValue: router },
                { provide: MatSnackBar, useValue: {
                        open: vi.fn().mockName("MatSnackBar.open")
                    } },
                { provide: MatDialog, useValue: {
                        open: vi.fn().mockName("MatDialog.open")
                    } },
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
        expect((component as any)['reminderEditing']()).toBe(true);
        expect((component as any)['showReminderForm']()).toBe(true);
    });
});

/** Issue #377: Testlerim'deki "Ata" `?assign=student` ile gelir; detay hazır olunca atama diyaloğu açılır. */
describe('WorksheetDetailComponent assign deep link', () => {
    type Query = Record<string, string>;

    function setup(options: {
        role: 'Teacher' | 'Student';
        canAssign?: boolean;
        query?: Query;
        gradesError?: boolean;
        /** Verilirse paramMap/queryParamMap bu subject'lerden gelir (komponentin rotalar arası yeniden kullanımı). */
        params$?: BehaviorSubject<string>;
        query$?: BehaviorSubject<Query>;
        /** testId → `get` yanıtı; verilmezse `{ id }` hemen döner. */
        examFor?: (id: number) => Observable<Test>;
    }) {
        const testService = {
            getWorksheetDetail: vi.fn().mockName("TestService.getWorksheetDetail"),
            get: vi.fn().mockName("TestService.get"),
            getWorksheetAssignmentsForTeacher: vi.fn().mockName("TestService.getWorksheetAssignmentsForTeacher"),
            assignWorksheet: vi.fn().mockName("TestService.assignWorksheet")
        };
        testService.getWorksheetDetail.mockImplementation((id: number) => of({
            worksheet: { id, name: 'Deneme', canEdit: true, canAssign: options.canAssign ?? true },
            attempts: [],
            similarWorksheets: [],
        } as unknown as WorksheetDetail));
        testService.get.mockImplementation((id: number) => options.examFor?.(id) ?? of({ id, gradeId: 5 } as Test));
        testService.getWorksheetAssignmentsForTeacher.mockReturnValue(NEVER);

        const router = {
            navigate: vi.fn().mockName("Router.navigate")
        };
        router.navigate.mockResolvedValue(true);
        const dialog = {
            open: vi.fn().mockName("MatDialog.open")
        } as unknown as MockedObject<MatDialog>;
        dialog.open.mockReturnValue({ afterClosed: () => of(undefined) } as MatDialogRef<unknown>);
        const snackBar = {
            open: vi.fn().mockName("MatSnackBar.open")
        };
        const grades = [{ id: 5, name: '5. Sınıf' }];
        const query = options.query ?? {};
        const route = {
            snapshot: { paramMap: convertToParamMap({}), data: {} },
            params: of({}),
            queryParams: of(query),
            paramMap: options.params$
                ? options.params$.pipe(map((testId) => convertToParamMap({ testId })))
                : of(convertToParamMap({ testId: '12' })),
            queryParamMap: options.query$
                ? options.query$.pipe(map((q) => convertToParamMap(q)))
                : of(convertToParamMap(query)),
        };

        TestBed.configureTestingModule({
            imports: [WorksheetDetailComponent, NoopAnimationsModule, translocoTesting],
            providers: [
                provideHttpClient(withXhr()),
                provideHttpClientTesting(),
                { provide: TestService, useValue: testService },
                { provide: Router, useValue: router },
                { provide: MatSnackBar, useValue: snackBar },
                {
                    provide: AuthService,
                    useValue: { hasRole: (r: string) => r === options.role, hasRealmRole: () => false, user: signal(null) },
                },
                { provide: StudentService, useValue: { getLookup: () => of([]) } },
                {
                    provide: GradesService,
                    useValue: { getGrades: () => (options.gradesError ? throwError(() => new Error('boom')) : of(grades)) },
                },
                { provide: ActivatedRoute, useValue: route },
            ],
        });
        TestBed.overrideComponent(WorksheetDetailComponent, {
            add: { providers: [{ provide: MatDialog, useValue: dialog }, { provide: MatSnackBar, useValue: snackBar }] },
        });

        const component = TestBed.createComponent(WorksheetDetailComponent).componentInstance;
        return { component, dialog, router, grades, snackBar, route, testService };
    }

    /** paramMap aboneliği `await lastValueFrom(get)` içerir; mikro görevler boşalsın. */
    const settle = () => new Promise<void>((resolve) => setTimeout(resolve));
    const dialogData = (dialog: MockedObject<MatDialog>) => (vi.mocked(dialog.open).mock.lastCall![1] as {
        data: WorksheetAssignmentDialogData;
    }).data;

    it('ngOnInit_AssignStudentQueryAndCanAssign_OpensAssignmentDialogWithStudentScopeAndClearsParam', async () => {
        const { component, dialog, router, grades, route } = setup({ role: 'Teacher', query: { assign: 'student' } });

        component.ngOnInit();
        await settle();

        expect(dialog.open).toHaveBeenCalledTimes(1);
        const data = dialogData(dialog);
        expect(data.worksheetId).toBe(12);
        expect(data.scope).toBe('student');
        expect(data.grades).toEqual(grades as never);
        expect(router.navigate).toHaveBeenCalledWith([], {
            relativeTo: route as unknown as ActivatedRoute,
            queryParams: { assign: null },
            queryParamsHandling: 'merge',
            replaceUrl: true,
        });
    });

    it('ngOnInit_AssignWithOtherQueryParams_ClearsOnlyAssignAndMergesTheRest', async () => {
        const { component, router } = setup({
            role: 'Teacher',
            query: { assign: 'student', commentId: '7', tab: 'x' },
        });

        component.ngOnInit();
        await settle();

        const clearCalls = vi.mocked(router.navigate).mock.calls.filter((c) => {
            const extras = c[1] as {
                queryParams?: Record<string, unknown>;
            } | undefined;
            return extras?.queryParams !== undefined && 'assign' in extras.queryParams;
        });
        expect(clearCalls.length).toBe(1);
        const extras = clearCalls[0][1] as {
            queryParams: Record<string, unknown>;
            queryParamsHandling: string;
        };
        // Yalnız `assign` düşürülür; 'merge' sayesinde commentId/tab URL'de kalır.
        expect(extras.queryParams).toEqual({ assign: null });
        expect(extras.queryParamsHandling).toBe('merge');
    });

    // Issue #580: onaylanmamış çözümlü etkinlik atanamaz; sunucunun yerelleştirilmiş mesajı gösterilir.
    it('assignment_PendingSolutionApproval400_ShowsServerMessage', async () => {
        const { component, dialog, snackBar, testService } = setup({ role: 'Teacher', query: { assign: 'student' } });
        const message = 'Bu etkinlikte çözümü henüz onaylanmamış maddeler var; onaylanmadan öğrenciye atanamaz.';
        dialog.open.mockReturnValue({ afterClosed: () => of({ request: { worksheetId: 12 } }) } as MatDialogRef<unknown>);
        testService.assignWorksheet.mockReturnValue(throwError(() => ({ status: 400, error: { success: false, message } })));

        component.ngOnInit();
        await settle();

        expect(testService.assignWorksheet).toHaveBeenCalledWith({ worksheetId: 12 });
        expect(snackBar.open).toHaveBeenCalledWith(message, expect.any(String), expect.any(Object));
    });

    it('ngOnInit_AssignQueryButCannotAssign_DoesNotOpenDialogAndShowsInfoSnackbar', async () => {
        const { component, dialog, snackBar } = setup({ role: 'Teacher', query: { assign: 'student' }, canAssign: false });

        component.ngOnInit();
        await settle();

        expect(dialog.open).not.toHaveBeenCalled();
        expect(snackBar.open).toHaveBeenCalledWith(worksheetDetailTr.snackbar.assignNotAllowed, worksheetDetailTr.snackbar.dismiss, expect.any(Object));
    });

    it('ngOnInit_GradesRequestFails_StillOpensDialogWithEmptyGrades', async () => {
        const { component, dialog } = setup({ role: 'Teacher', query: { assign: 'student' }, gradesError: true });

        component.ngOnInit();
        await settle();

        expect(dialog.open).toHaveBeenCalledTimes(1);
        expect(dialogData(dialog).grades).toEqual([]);
        expect(dialogData(dialog).scope).toBe('student');
    });

    it('ngOnInit_ReusedForAnotherWorksheet_OpensDialogOnlyForCurrentWorksheetOnceItsExamLoads', async () => {
        const params$ = new BehaviorSubject<string>('12');
        const query$ = new BehaviorSubject<Query>({});
        const exam34 = new Subject<Test>();
        const { component, dialog } = setup({
            role: 'Teacher',
            params$,
            query$,
            examFor: (id) => (id === 34 ? exam34.asObservable() : of({ id, gradeId: 5 } as Test)),
        });

        component.ngOnInit();
        await settle();
        expect(dialog.open).not.toHaveBeenCalled();

        // /test/12 → /test/34?assign=student: 34'ün sınavı henüz gelmedi; önceki (12) sınavla açılmamalı.
        params$.next('34');
        query$.next({ assign: 'student' });
        await settle();
        expect(dialog.open).not.toHaveBeenCalled();

        exam34.next({ id: 34, gradeId: 5 } as Test);
        exam34.complete();
        await settle();

        expect(dialog.open).toHaveBeenCalledTimes(1);
        expect(dialogData(dialog).worksheetId).toBe(34);
    });

    it('ngOnInit_AssignQueryForStudent_DoesNotOpenDialog', async () => {
        const { component, dialog, snackBar } = setup({ role: 'Student', query: { assign: 'student' } });

        component.ngOnInit();
        await settle();

        expect(dialog.open).not.toHaveBeenCalled();
        expect(snackBar.open).not.toHaveBeenCalled();
    });

    it('ngOnInit_NoAssignQuery_DoesNotOpenDialog', async () => {
        const { component, dialog } = setup({ role: 'Teacher' });

        component.ngOnInit();
        await settle();

        expect(dialog.open).not.toHaveBeenCalled();
    });

    it('ngOnInit_UnknownAssignValue_IsIgnored', async () => {
        const { component, dialog } = setup({ role: 'Teacher', query: { assign: 'everyone' } });

        component.ngOnInit();
        await settle();

        expect(dialog.open).not.toHaveBeenCalled();
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
        const dialog = {
            open: vi.fn().mockName("MatDialog.open")
        } as unknown as MockedObject<MatDialog>;
        dialog.open.mockReturnValue({ afterClosed: () => of(undefined) } as MatDialogRef<unknown>);

        TestBed.configureTestingModule({
            imports: [WorksheetDetailComponent, NoopAnimationsModule, translocoTesting],
            providers: [
                provideHttpClient(withXhr()),
                provideHttpClientTesting(),
                { provide: TestService, useValue: {
                        getWorksheetDetail: vi.fn().mockName("TestService.getWorksheetDetail")
                    } },
                { provide: Router, useValue: {
                        navigate: vi.fn().mockName("Router.navigate")
                    } },
                { provide: MatSnackBar, useValue: {
                        open: vi.fn().mockName("MatSnackBar.open")
                    } },
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

    const buttons = (fixture: {
        nativeElement: HTMLElement;
    }, id: string) => fixture.nativeElement.querySelectorAll(`[data-testid="${id}"]`);

    it('bağımsız öğretmende "Sınıfa ata" butonları render edilmez ve dialog isIndependentTutor=true alır', () => {
        const { fixture, dialog } = setup({ role: 'Teacher', user: teacherProfile(null) });

        expect(buttons(fixture, 'assign-grade-btn').length).toBe(0);
        const studentButtons = buttons(fixture, 'assign-student-btn');
        expect(studentButtons.length).toBeGreaterThan(0);

        (studentButtons[0] as HTMLButtonElement).click();

        expect(dialog.open).toHaveBeenCalled();
        const config = vi.mocked(dialog.open).mock.lastCall![1] as {
            data: WorksheetAssignmentDialogData;
        };
        expect(config.data.isIndependentTutor).toBe(true);
    });

    it('okullu öğretmende "Sınıfa ata" butonları görünür', () => {
        const { fixture, component } = setup({ role: 'Teacher', user: teacherProfile(3) });

        expect(component['isIndependentTutor']()).toBe(false);
        expect(buttons(fixture, 'assign-grade-btn').length).toBeGreaterThan(0);
    });

    it('uygulama rolü Admin olan okulsuz kullanıcı bağımsız sayılmaz', () => {
        const { component } = setup({ role: 'Admin', user: { ...teacherProfile(null), role: 'Admin' } });

        expect(component['isIndependentTutor']()).toBe(false);
    });

    it('realm rolü Admin olan okulsuz öğretmen hesabında "Sınıfa ata" butonları görünür', () => {
        const { fixture, component } = setup({ role: 'Teacher', user: teacherProfile(null), realmAdmin: true });

        expect(component['isIndependentTutor']()).toBe(false);
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
        const data = (vi.mocked(dialog.open).mock.lastCall![1] as {
            data: WorksheetAssignmentDialogData;
        }).data;
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
        const data = (vi.mocked(dialog.open).mock.lastCall![1] as {
            data: WorksheetAssignmentDialogData;
        }).data;

        lookup.error(new Error('boom'));

        expect(component['studentLookupStatus']()).toBe('error');
        expect(data.studentsStatus!()).toBe('error');
        expect(data.students()).toEqual([]);
    });

    it('profil henüz yokken (user null) "Sınıfa ata" butonları görünür', () => {
        const { fixture, component } = setup({ role: 'Teacher', user: null });

        expect(component['isIndependentTutor']()).toBe(false);
        expect(buttons(fixture, 'assign-grade-btn').length).toBeGreaterThan(0);
    });
});

/** Soru görüntüleyici stub'ı — thread yerleşimi testinde canvas render'ı gereksiz. */
@Component({ selector: 'app-question-canvas-view', standalone: true, changeDetection: ChangeDetectionStrategy.Eager,
    template: '' })
class QuestionCanvasViewStubComponent {
    @Input()
    questionRegion: unknown;
    @Input()
    selectedChoice: unknown;
    @Input()
    correctChoice: unknown;
    @Input()
    mode: unknown;
}

@Component({ selector: 'app-study-link-suggestions', standalone: true, changeDetection: ChangeDetectionStrategy.Eager,
    template: '' })
class StudyLinkSuggestionsStubComponent {
    @Input()
    testInstanceId: unknown;
    @Input()
    questionId: unknown;
    @Input()
    testInstanceQuestionId: unknown;
    @Input()
    answeredWrong: unknown;
}

/** Issue #105: yorum/soru thread'lerinin yerleşimi ve bildirim derin linki (commentId vurgusu). */
describe('WorksheetDetailComponent comment threads', () => {
    interface SetupOptions {
        role: 'Student' | 'Teacher';
        query?: Record<string, string>;
        completed?: boolean;
        commentsEnabled?: boolean;
    }

    async function setup(options: SetupOptions & {
        renderDeferred?: boolean;
    }) {
        TestBed.configureTestingModule({
            // Thread'ler @defer içinde: testte açıkça tetiklenir.
            deferBlockBehavior: DeferBlockBehavior.Manual,
            imports: [WorksheetDetailComponent, NoopAnimationsModule, translocoTesting],
            providers: [
                provideHttpClient(withXhr()),
                provideHttpClientTesting(),
                provideNativeDateAdapter(),
                { provide: TestService, useValue: {
                        getWorksheetDetail: vi.fn().mockName("TestService.getWorksheetDetail")
                    } },
                { provide: Router, useValue: {
                        navigate: vi.fn().mockName("Router.navigate")
                    } },
                { provide: MatSnackBar, useValue: {
                        open: vi.fn().mockName("MatSnackBar.open")
                    } },
                { provide: MatDialog, useValue: {
                        open: vi.fn().mockName("MatDialog.open")
                    } },
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

    const threads = (fixture: ComponentFixture<WorksheetDetailComponent>) => fixture.debugElement
        .queryAll(By.directive(CommentThreadComponent))
        .map((d) => d.componentInstance as CommentThreadComponent);
    const byTestId = (fixture: ComponentFixture<WorksheetDetailComponent>, id: string) => (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(`[data-testid="${id}"]`);

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
        expect(worksheetThread.viewerIsTeacher()).toBe(false);
        expect(worksheetThread.studentsLockedNotice()).toBe(false);

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
        expect(all[0].viewerIsTeacher()).toBe(true);
        expect(all[0].studentsLockedNotice()).toBe(true);
        expect(all[0].showEditSettings()).toBe(true);
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
        expect(questionThread.viewerIsTeacher()).toBe(true);
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

        const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[data-testid="hardest-question"]'));
        expect(rows.length).toBe(2);
        expect(rows[0].querySelector('[data-testid="hardest-question-number"]')!.textContent!.trim()).toBe('1');
        expect(rows[0].textContent).toContain('Soru 1');
        // Eski sunucu (number yok): ham order'a düşülür.
        expect(rows[1].querySelector('[data-testid="hardest-question-number"]')!.textContent!.trim()).toBe('7');
    });

    it('teacher_WorksheetThreadShowsStudentsSummaryAndEditSettingsEvenWhenCommentsEnabled', async () => {
        const { fixture } = await setup({ role: 'Teacher', commentsEnabled: true });

        const [worksheetThread] = threads(fixture);
        expect(worksheetThread.showStudentsSummary()).toBe(true);
        expect(worksheetThread.studentsLockedNotice()).toBe(false);
        expect(worksheetThread.showEditSettings()).toBe(true);
    });

    it('student_WorksheetThreadHasNoStudentsSummary', async () => {
        const { fixture } = await setup({ role: 'Student' });

        expect(threads(fixture)[0].showStudentsSummary()).toBe(false);
        expect(threads(fixture)[0].showEditSettings()).toBe(false);
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

describe('WorksheetDetailComponent add from bank (issue #472)', () => {
    function setup(options: {
        canEdit?: boolean;
        result?: LinkBankQuestionsResult;
        worksheet?: Partial<Test>;
    } = {}) {
        const testService = {
            getWorksheetDetail: vi.fn().mockName("TestService.getWorksheetDetail"),
            get: vi.fn().mockName("TestService.get"),
            getWorksheetAssignmentsForTeacher: vi.fn().mockName("TestService.getWorksheetAssignmentsForTeacher")
        };
        testService.getWorksheetDetail.mockImplementation((id: number) => of({
            worksheet: { id, name: 'Deneme', canEdit: options.canEdit ?? true, canAssign: true, isOwner: true, ...options.worksheet },
            attempts: [],
            similarWorksheets: [],
        } as unknown as WorksheetDetail));
        testService.get.mockImplementation((id: number) => of({ id, gradeId: 5 } as Test));
        testService.getWorksheetAssignmentsForTeacher.mockReturnValue(NEVER);

        const dialog = {
            open: vi.fn().mockName("MatDialog.open")
        } as unknown as MockedObject<MatDialog>;
        dialog.open.mockReturnValue({ afterClosed: () => of(options.result) } as MatDialogRef<unknown>);
        const snackBar = {
            open: vi.fn().mockName("MatSnackBar.open")
        };
        const router = {
            navigate: vi.fn().mockName("Router.navigate")
        };
        router.navigate.mockResolvedValue(true);

        TestBed.configureTestingModule({
            imports: [WorksheetDetailComponent, NoopAnimationsModule, translocoTesting],
            providers: [
                provideHttpClient(withXhr()),
                provideHttpClientTesting(),
                { provide: TestService, useValue: testService },
                { provide: Router, useValue: router },
                { provide: MatSnackBar, useValue: snackBar },
                {
                    provide: AuthService,
                    useValue: { hasRole: (r: string) => r === 'Teacher', hasRealmRole: () => false, user: signal(null) },
                },
                { provide: StudentService, useValue: { getLookup: () => of([]) } },
                { provide: GradesService, useValue: { getGrades: () => of([]) } },
                {
                    provide: ActivatedRoute,
                    useValue: {
                        snapshot: { paramMap: convertToParamMap({}), data: {} },
                        params: of({}),
                        queryParams: of({}),
                        paramMap: of(convertToParamMap({ testId: '12' })),
                        queryParamMap: of(convertToParamMap({})),
                    },
                },
            ],
        });
        TestBed.overrideComponent(WorksheetDetailComponent, {
            add: { providers: [{ provide: MatDialog, useValue: dialog }, { provide: MatSnackBar, useValue: snackBar }] },
        });

        const fixture = TestBed.createComponent(WorksheetDetailComponent);
        return { fixture, component: fixture.componentInstance, dialog, snackBar, testService };
    }

    const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

    it('openBankQuestionDialog_OpensBankDialogWithTestId', async () => {
        const { component, dialog } = setup();
        component.ngOnInit();
        await settle();

        component['openBankQuestionDialog']();

        expect(dialog.open).toHaveBeenCalledTimes(1);
        expect(vi.mocked(dialog.open).mock.lastCall![0]).toBe(BankQuestionDialogComponent);
        expect((vi.mocked(dialog.open).mock.lastCall![1] as {
            data: {
                testId: number;
            };
        }).data).toEqual({ testId: 12 });
    });

    it('openBankQuestionDialog_WorksheetClassified_PassesInitialClassification', async () => {
        const { component, dialog } = setup({ worksheet: { gradeId: 5, subjectId: 10, topicId: 100, subTopicId: 1000 } });
        component.ngOnInit();
        await settle();

        component['openBankQuestionDialog']();

        const data = (vi.mocked(dialog.open).mock.lastCall![1] as { data: BankQuestionDialogData }).data;
        expect(data).toEqual({
            testId: 12,
            initialClassification: { gradeId: 5, subjectId: 10, topicId: 100, subtopicIds: [1000] },
        });
    });

    it('openBankQuestionDialog_PartialClassification_FillsMissingWithNull', async () => {
        const { component, dialog } = setup({ worksheet: { subjectId: 10 } });
        component.ngOnInit();
        await settle();

        component['openBankQuestionDialog']();

        const data = (vi.mocked(dialog.open).mock.lastCall![1] as { data: BankQuestionDialogData }).data;
        expect(data.initialClassification).toEqual({ gradeId: null, subjectId: 10, topicId: null, subtopicIds: [] });
    });

    it('openBankQuestionDialog_WorksheetUnclassified_PassesNoInitialClassification', async () => {
        const { component, dialog } = setup();
        component.ngOnInit();
        await settle();

        component['openBankQuestionDialog']();

        const data = (vi.mocked(dialog.open).mock.lastCall![1] as { data: BankQuestionDialogData }).data;
        expect(data.initialClassification).toBeUndefined();
    });

    it('openBankQuestionDialog_AddedQuestions_ShowsCountAndReloadsDetail', async () => {
        const { component, snackBar, testService } = setup({
            result: { addedQuestionIds: [4, 5], alreadyInTestQuestionIds: [7] },
        });
        component.ngOnInit();
        await settle();
        const detailCalls = vi.mocked(testService.getWorksheetDetail).mock.calls.length;

        component['openBankQuestionDialog']();

        expect(snackBar.open).toHaveBeenCalledWith('2 soru teste eklendi.', expect.any(String), expect.any(Object));
        expect(vi.mocked(testService.getWorksheetDetail).mock.calls.length).toBe(detailCalls + 1);
    });

    it('openBankQuestionDialog_NothingAdded_ShowsAlreadyInTestAndDoesNotReload', async () => {
        const { component, snackBar, testService } = setup({
            result: { addedQuestionIds: [], alreadyInTestQuestionIds: [7] },
        });
        component.ngOnInit();
        await settle();
        const detailCalls = vi.mocked(testService.getWorksheetDetail).mock.calls.length;

        component['openBankQuestionDialog']();

        expect(snackBar.open).toHaveBeenCalledWith(worksheetDetailTr.snackbar.bankAlreadyInTest, expect.any(String), expect.any(Object));
        expect(vi.mocked(testService.getWorksheetDetail).mock.calls.length).toBe(detailCalls);
    });

    it('openBankQuestionDialog_Cancelled_DoesNothing', async () => {
        const { component, snackBar, testService } = setup();
        component.ngOnInit();
        await settle();
        const detailCalls = vi.mocked(testService.getWorksheetDetail).mock.calls.length;

        component['openBankQuestionDialog']();

        expect(snackBar.open).not.toHaveBeenCalled();
        expect(vi.mocked(testService.getWorksheetDetail).mock.calls.length).toBe(detailCalls);
    });

    it('addFromBankButton_RenderedOnlyWhenTeacherCanEdit', async () => {
        const editable = setup();
        editable.component.ngOnInit();
        await settle();
        editable.fixture.detectChanges();
        expect(editable.fixture.debugElement.query(By.css('[data-testid="add-from-bank-btn"]'))).not.toBeNull();
    });

    it('addFromBankButton_HiddenWhenWorksheetIsNotEditable', async () => {
        const readOnly = setup({ canEdit: false });
        readOnly.component.ngOnInit();
        await settle();
        readOnly.fixture.detectChanges();
        expect(readOnly.fixture.debugElement.query(By.css('[data-testid="add-from-bank-btn"]'))).toBeNull();
    });
});

// Issue #573: etkinlik sayfası — öğrenci ve öğretmen ekranlarında puan/yüzde/sıralama yok; öğretmende "Çalışma takibi".
describe('WorksheetDetailComponent activity page (#573)', () => {
    function setup(role: 'Student' | 'Teacher', instanceStatus: number | null = null) {
        const testService = {
            getWorksheetDetail: vi.fn().mockReturnValue(NEVER),
            startTest: vi.fn(),
            get: vi.fn().mockReturnValue(NEVER),
        };
        const router = { navigate: vi.fn() };
        TestBed.configureTestingModule({
            imports: [WorksheetDetailComponent, NoopAnimationsModule, translocoTesting],
            providers: [
                provideHttpClient(withXhr()),
                provideHttpClientTesting(),
                provideNativeDateAdapter(),
                { provide: TestService, useValue: testService },
                { provide: Router, useValue: router },
                { provide: MatSnackBar, useValue: { open: vi.fn() } },
                { provide: MatDialog, useValue: { open: vi.fn() } },
                { provide: AuthService, useValue: { hasRole: (r: string) => r === role, hasRealmRole: () => false, user: signal(null) } },
                { provide: StudentService, useValue: {} },
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

        const fixture = TestBed.createComponent(WorksheetDetailComponent);
        const component = fixture.componentInstance;
        component.testId = 12;
        component.exam = {
            id: 12,
            name: 'Hayvanlar',
            kind: 1,
            instance: instanceStatus === null ? undefined : { status: instanceStatus },
        } as unknown as Test;
        component['detail'].set({
            worksheet: { id: 12, name: 'Hayvanlar', kind: 1, questionCount: 4, maxDurationSeconds: 0, canEdit: true, canAssign: true, description: 'Resimleri eşleştir.' },
            stats: { solverCount: 3, averageScorePercent: 75 },
            topicBreakdown: [],
            outcomes: [],
            rewardBadgeText: null,
            sampleQuestion: null,
            attempts: [{ instanceId: 1, completedDate: null, durationSeconds: 60, correctCount: 0, totalCount: 4, scorePercent: 0 }],
            improvementPoints: null,
            similarWorksheets: [{ id: 2, name: 'Benzer', questionCount: 4, isPracticeTest: false, averageScorePercent: 50 }],
            teacherInsights: {
                hardestQuestions: [{ questionId: 1, order: 1, text: 'S', subtopicName: null, answeredCount: 3, correctPercent: 10 }],
                difficultyDistribution: { easy: 1, medium: 0, hard: 0 },
                classifiedCount: 1,
                totalQuestionCount: 1,
                unclassifiedCount: 0,
            },
            completedResult: {
                instanceId: 1,
                scorePercent: 0,
                correctCount: 0,
                wrongCount: 0,
                emptyCount: 4,
                durationSeconds: 60,
                topicSuccess: [],
                rank: { position: 1, totalStudents: 3, classAveragePercent: 75 },
            },
        } as unknown as WorksheetDetail);
        return { fixture, component, testService, router };
    }

    const text = (fixture: ComponentFixture<WorksheetDetailComponent>) => (fixture.nativeElement as HTMLElement).textContent ?? '';

    it('student_NeverShowsScoreResultView_EvenWithCompletedResult', () => {
        const { fixture, component } = setup('Student', 1);
        fixture.detectChanges();

        expect(component['view']()).toBe('start');
        expect(fixture.nativeElement.querySelector('.result-hero')).toBeNull();
        expect(text(fixture)).not.toMatch(/%|puan|sıralama/i);
        expect(text(fixture)).toContain('Etkinlik sayfası');
        expect(fixture.nativeElement.querySelector('[data-testid="activity-done"]')).not.toBeNull();
        // Tekrar çözüm #572'ye ertelendi: tamamlanan etkinlikte başlat/yeniden çöz düğmesi yok.
        expect(fixture.nativeElement.querySelector('[data-testid="activity-resolve"]')).toBeNull();
        expect(fixture.nativeElement.querySelector('[data-testid="start-cta"]')).toBeNull();
        expect(text(fixture)).not.toContain('Yeniden çöz');
    });

    it('student_StartNavigatesToActivityPlayer', () => {
        const { fixture, component, testService, router } = setup('Student');
        fixture.detectChanges();
        testService.startTest.mockReturnValue(of({ success: true, instanceId: 77 }));

        component.StartTest(12);

        expect(router.navigate).toHaveBeenCalledWith(['/activity', 77], { state: { worksheetId: 12 } });
    });

    it('teacher_ShowsTrackingTable_NoClassAverageOrHardestQuestions', () => {
        const { fixture, component } = setup('Teacher');
        component['assignmentPanelState'].set({
            loading: false,
            assignments: [
                {
                    assignmentId: 1,
                    targetType: 'Grade',
                    targetName: '3-A',
                    isActive: true,
                    startAt: '2026-10-20T08:00:00Z',
                    studentCount: 1,
                    completedCount: 1,
                    inProgressCount: 0,
                    notStartedCount: 0,
                    scheduledCount: 0,
                    expiredCount: 0,
                    students: [
                        { studentId: 1, userId: 1, studentNumber: '101', status: 'Completed', firstCheckCorrectCount: 7, firstCheckTotalCount: 10 },
                    ],
                },
            ],
            summary: { totalAssignments: 1, activeAssignments: 1, upcomingAssignments: 0, totalStudents: 1, completedCount: 1, inProgressCount: 0, notStartedCount: 0, scheduledCount: 0, expiredCount: 0 },
            lastRefreshed: null,
            error: null,
        });
        fixture.detectChanges();

        expect(text(fixture)).toContain('Çalışma takibi');
        expect(fixture.nativeElement.querySelector('app-activity-tracking-table')).not.toBeNull();
        expect(text(fixture)).toContain('7 doğru · 3 yanlış');
        expect(fixture.nativeElement.querySelector('[data-testid="hardest-question"]')).toBeNull();
        expect(text(fixture)).not.toContain('75');
        expect(text(fixture)).not.toMatch(/%/);
    });
});
