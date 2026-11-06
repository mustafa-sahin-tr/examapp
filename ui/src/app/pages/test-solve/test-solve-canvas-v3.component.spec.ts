import type { Mock, MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NEVER, Observable, Subject, of, throwError } from 'rxjs';
import { TranslocoTestingModule } from '@jsverse/transloco';

import { TestSolveCanvasComponentv3 } from './test-solve-canvas-v3.component';
import { isTestCompletedRejection, isTimeExpiredRejection } from './test-solve-canvas-enhanced.component';
import { TestService } from '../../services/test.service';
import { QuestionReportService } from '../../services/question-report.service';
import { QuestionReportDialogData } from './question-report-dialog.component';
import { readScratchNotes, writeScratchNotes } from './test-solve-scratch-store';
import { TestInstance, TestSessionResult, TestStatus } from '../../models/test-instance';
import { ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { ReviewDialogData } from './test-solve-review-dialog.component';
import { TestFinishRetryDialogComponent } from './test-finish-retry-dialog.component';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import testSolveTr from '../../../../public/i18n/test-solve/tr.json';

/** Gerçek sözlük yüklenir; anahtar bozulursa test kırılır (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
    langs: { 'test-solve/tr': testSolveTr },
    translocoConfig: {
        availableLangs: [...SUPPORTED_LOCALE_CODES],
        defaultLang: DEFAULT_LOCALE,
        scopes: { keepCasing: true },
    },
    preloadLangs: true,
});

const OK: TestSessionResult = { success: true, message: '', objectId: 0, notFound: false, forbidden: false, conflict: false };
const FAILED: TestSessionResult = { ...OK, success: false, message: '' };
const NOT_IN_PROGRESS: TestSessionResult = { ...FAILED, conflict: true, errorCode: 'TestNotInProgress' };
const conflict409 = () => new HttpErrorResponse({ status: 409, error: NOT_IN_PROGRESS });
const TIME_UP: TestSessionResult = { ...NOT_IN_PROGRESS, reason: 'TimeExpired' };
const timeUp409 = () => new HttpErrorResponse({ status: 409, error: TIME_UP });

/** Mikrotask + makrotask kuyruğunu boşaltır (promise zinciri ve afterClosed aboneliği için). */
const settle = () => vi.advanceTimersByTimeAsync(0);

function buildInstance(overrides: Partial<TestInstance> = {}): TestInstance {
    const question = (id: number, isExample = false) => ({ id, isExample, isCanvasQuestion: false });
    return {
        id: 7,
        testName: 'Deneme',
        status: TestStatus.Started,
        maxDurationSeconds: 3600,
        isPracticeTest: false,
        worksheetId: 42,
        testInstanceQuestions: [
            { id: 101, order: 1, selectedAnswerId: 11, timeTaken: 5, question: question(1) },
            { id: 102, order: 2, selectedAnswerId: 0, timeTaken: 0, question: question(2) },
            { id: 103, order: 3, selectedAnswerId: 0, timeTaken: 0, question: question(3) },
            // Örnek soru boş sayılmaz.
            { id: 104, order: 4, selectedAnswerId: 0, timeTaken: 0, question: question(4, true) },
        ],
        ...overrides,
    } as unknown as TestInstance;
}

describe('TestSolveCanvasComponentv3 — finish flow (issue #383)', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    let fixture: ComponentFixture<TestSolveCanvasComponentv3>;
    let component: TestSolveCanvasComponentv3;
    let testService: MockedObject<TestService>;
    let dialog: MockedObject<MatDialog>;
    let snackBar: MockedObject<MatSnackBar>;
    let navigateSpy: Mock;

    function dialogReturns(result: unknown) {
        dialog.open.mockReturnValue({ afterClosed: () => of(result) } as ReturnType<MatDialog['open']>);
    }

    beforeEach(async () => {
        localStorage.clear();
        testService = {
            saveAnswer: vi.fn().mockName("TestService.saveAnswer"),
            completeTest: vi.fn().mockName("TestService.completeTest"),
            getCanvasTestWithAnswers: vi.fn().mockName("TestService.getCanvasTestWithAnswers"),
            convertTestInstanceToRegions: vi.fn().mockName("TestService.convertTestInstanceToRegions")
        } as unknown as MockedObject<TestService>;
        testService.completeTest.mockReturnValue(of(OK));
        testService.saveAnswer.mockReturnValue(of(OK));
        dialog = {
            open: vi.fn().mockName("MatDialog.open"),
            closeAll: vi.fn().mockName("MatDialog.closeAll"),
            openDialogs: [],
        } as unknown as MockedObject<MatDialog>;
        snackBar = {
            open: vi.fn().mockName("MatSnackBar.open")
        } as unknown as MockedObject<MatSnackBar>;

        await TestBed.configureTestingModule({
            imports: [TestSolveCanvasComponentv3, translocoTesting],
            providers: [
                provideRouter([]),
                { provide: ActivatedRoute, useValue: { paramMap: NEVER } },
                { provide: TestService, useValue: testService },
            ],
        })
            .overrideProvider(MatDialog, { useValue: dialog })
            .overrideProvider(MatSnackBar, { useValue: snackBar })
            .compileComponents();

        navigateSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        fixture = TestBed.createComponent(TestSolveCanvasComponentv3);
        component = fixture.componentInstance;
        // ngOnInit/loadTest çalıştırılmaz (detectChanges yok); state doğrudan kurulur.
        component.testInstance = buildInstance();
        component.currentIndex.set(0);
        // Bitirme testleri: aktif sorunun cevabı henüz kaydedilmedi.
        component['markQuestionDirty'](0);
    });

    afterEach(() => fixture.destroy());

    it('review dialog lists empty and bookmarked questions; cancel keeps the test running', async () => {
        dialogReturns(undefined);
        component.bookmarkedQuestions.set(new Set([1]));

        component.confirmFinishTest();
        await settle();

        expect(dialog.open).toHaveBeenCalledTimes(1);
        const data = vi.mocked(dialog.open).mock.lastCall![1]?.data as ReviewDialogData;
        expect(data.emptyIndexes).toEqual([1, 2]);
        expect(data.bookmarkedIndexes).toEqual([1]);
        expect(data.labels.summary).toContain('2 boş');
        expect(testService.saveAnswer).not.toHaveBeenCalled();
        expect(testService.completeTest).not.toHaveBeenCalled();
        expect(navigateSpy).not.toHaveBeenCalled();
        expect(component.finishing()).toBe(false);
    });

    it('review dialog: choosing a question goes there without finishing the test', async () => {
        dialogReturns({ goTo: 1 });
        const select = vi.spyOn(component, 'selectFromMap').mockImplementation(() => undefined);

        component.confirmFinishTest();
        await settle();

        expect(select).toHaveBeenCalledWith(1);
        expect(testService.completeTest).not.toHaveBeenCalled();
    });

    it('review action replaces next only on the last question', () => {
        component.currentIndex.set(0);
        expect(component.isOnLastQuestion()).toBe(component.canGoNext() === false);
    });

    it('leave: untimed test explains answers are saved and navigates to the test', async () => {
        dialogReturns(true);
        component.testInstance.maxDurationSeconds = 0;
        component.testInstance.worksheetId = 77;

        component.leaveAndContinueLater();
        await settle();

        const data = vi.mocked(dialog.open).mock.lastCall![1]?.data as ConfirmDialogData;
        expect(data.message).toBe(testSolveTr.leave.message);
        expect(navigateSpy).toHaveBeenCalledWith(['/test', 77]);
        expect(testService.completeTest).not.toHaveBeenCalled();
    });

    it('leave: timed test says time keeps running; cancel stays', async () => {
        dialogReturns(false);
        component.testInstance.maxDurationSeconds = 600;

        component.leaveAndContinueLater();
        await settle();

        const data = vi.mocked(dialog.open).mock.lastCall![1]?.data as ConfirmDialogData;
        expect(data.message).toBe(testSolveTr.leave.messageTimed);
        expect(navigateSpy).not.toHaveBeenCalled();
    });

    it('leave: failed save keeps the student on the screen', async () => {
        testService.saveAnswer.mockReturnValue(of({ success: false } as TestSessionResult));
        dialogReturns(true);
        component.testInstance.maxDurationSeconds = 600;

        component.leaveAndContinueLater();
        await settle();

        expect(navigateSpy).not.toHaveBeenCalled();
    });

    // Issue #821: bildirim, menü ya da geri ile ayrılış da aynı pencereden geçer.
    it('route leave: asks, and allows only after the student confirms', async () => {
        dialogReturns(false);
        expect(await component.canLeave()).toBe(false);

        dialogReturns(true);
        expect(await component.canLeave()).toBe(true);
        const data = vi.mocked(dialog.open).mock.lastCall![1]?.data as ConfirmDialogData;
        expect(data.title).toBe(testSolveTr.leave.title);
    });

    it('route leave: failed save keeps the student on the screen', async () => {
        testService.saveAnswer.mockReturnValue(of({ success: false } as TestSessionResult));
        dialogReturns(true);
        component.testInstance.maxDurationSeconds = 600;

        expect(await component.canLeave()).toBe(false);
    });

    it('route leave: the leave button does not ask twice', async () => {
        dialogReturns(true);
        component.leaveAndContinueLater();
        await settle();
        vi.mocked(dialog.open).mockClear();

        expect(component.canLeave()).toBe(true);
        expect(dialog.open).not.toHaveBeenCalled();
    });

    it('route leave: a finished session leaves without asking', () => {
        (component as unknown as { redirectedToResult: boolean }).redirectedToResult = true;

        expect(component.canLeave()).toBe(true);
        expect(dialog.open).not.toHaveBeenCalled();
    });

    it('beforeunload: warns only while an answer is unsaved', () => {
        const idle = new Event('beforeunload', { cancelable: true }) as BeforeUnloadEvent;
        component.onBeforeUnload(idle);
        expect(idle.defaultPrevented).toBe(false);
    });

    it('confirm: completeTest is sent only after the pending last-answer save resolves', async () => {
        const save$ = new Subject<TestSessionResult>();
        testService.saveAnswer.mockReturnValue(save$);
        dialogReturns(true);

        component.confirmFinishTest();
        await settle();

        expect(testService.saveAnswer).toHaveBeenCalledTimes(1);

        expect(testService.saveAnswer).toHaveBeenCalledWith(expect.objectContaining({ testQuestionId: 101, selectedAnswerId: 11, testInstanceId: 7 }));
        expect(testService.completeTest).not.toHaveBeenCalled();

        save$.next(OK);
        save$.complete();
        await settle();

        expect(testService.completeTest).toHaveBeenCalledTimes(1);

        expect(testService.completeTest).toHaveBeenCalledWith(7);
        expect(navigateSpy).toHaveBeenCalledWith(['/test', 42], { replaceUrl: true });
    });

    it('waits for an earlier in-flight save too, not only the current question', async () => {
        const earlier$ = new Subject<TestSessionResult>();
        // İlk çağrı uçuşta kalır; bitirirken gönderilen kayıtlar (aktif soru + dirty soru 2) hemen başarılı.
        let calls = 0;
        testService.saveAnswer.mockImplementation(() => (calls++ === 0 ? (earlier$ as Observable<TestSessionResult>) : of(OK)));
        // Önceki sorudan (ör. otomatik geçiş) kalan uçuştaki kayıt.
        component.autoNextQuestion.set(false);
        component.selectAnswerForQuestion(22, 1);

        const done = component.completeTest();
        await settle();
        expect(testService.completeTest).not.toHaveBeenCalled();

        earlier$.next(OK);
        earlier$.complete();
        await done;

        expect(testService.completeTest).toHaveBeenCalledTimes(1);
    });

    it('failed last save (HTTP error): does not complete, shows error, allows retry', async () => {
        testService.saveAnswer.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

        await component.completeTest();

        expect(testService.completeTest).not.toHaveBeenCalled();
        expect(navigateSpy).not.toHaveBeenCalled();
        expect(component.finishing()).toBe(false);
        expect(component.showToast()).toBe(true);
        expect(component.toastType()).toBe('error');

        // Tekrar dene: bu sefer kayıt başarılı → tamamlanır.
        testService.saveAnswer.mockReturnValue(of(OK));
        await component.completeTest();

        expect(testService.completeTest).toHaveBeenCalledTimes(1);

        expect(testService.completeTest).toHaveBeenCalledWith(7);
        expect(navigateSpy).toHaveBeenCalledWith(['/test', 42], { replaceUrl: true });
    });

    it('failed last save (200 + success:false): does not complete', async () => {
        testService.saveAnswer.mockReturnValue(of(FAILED));

        await component.completeTest();

        expect(testService.completeTest).not.toHaveBeenCalled();
        expect(component.finishing()).toBe(false);
    });

    it('save rejected with 409 TestNotInProgress (#367): no complete call, toast + redirect to result', async () => {
        testService.saveAnswer.mockReturnValue(throwError(conflict409));

        await component.completeTest();

        expect(testService.completeTest).not.toHaveBeenCalled();
        expect(snackBar.open).toHaveBeenCalledTimes(1);
        expect(navigateSpy).toHaveBeenCalledTimes(1);
        expect(navigateSpy).toHaveBeenCalledWith(['/test', 42], { replaceUrl: true });
    });

    it('a background save rejected as completed (outside finish) redirects to the result page', async () => {
        testService.saveAnswer.mockReturnValue(throwError(conflict409));

        component['persistQuestionState'](0);
        await settle();

        expect(snackBar.open).toHaveBeenCalledTimes(1);
        expect(navigateSpy).toHaveBeenCalledTimes(1);
        expect(navigateSpy).toHaveBeenCalledWith(['/test', 42], { replaceUrl: true });
    });

    it('a 409 without the TestNotInProgress code is a plain failure: no redirect, no complete', async () => {
        testService.saveAnswer.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));

        await component.completeTest();

        expect(snackBar.open).not.toHaveBeenCalled();
        expect(navigateSpy).not.toHaveBeenCalled();
        expect(testService.completeTest).not.toHaveBeenCalled();
        expect(component.toastType()).toBe('error');
    });

    it('end-test failure keeps the student on the test and does not navigate', async () => {
        testService.completeTest.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

        await component.completeTest();

        expect(navigateSpy).not.toHaveBeenCalled();
        expect(component.finishing()).toBe(false);
        expect(component.toastType()).toBe('error');
    });

    it('navigates to /tests when the worksheet id is unknown (never to /student-profile)', async () => {
        component.testInstance = buildInstance({ worksheetId: undefined });

        await component.completeTest();

        expect(navigateSpy).toHaveBeenCalledTimes(1);

        expect(navigateSpy).toHaveBeenCalledWith(['/tests'], { replaceUrl: true });
    });

    for (const status of [TestStatus.Completed, TestStatus.Expired]) {
        it(`opening /testsolve for a non-Started (${TestStatus[status]}) instance redirects to the result page`, async () => {
            testService.getCanvasTestWithAnswers.mockReturnValue(of(buildInstance({ status })));

            await component.loadTest(7);

            expect(navigateSpy).toHaveBeenCalledTimes(1);

            expect(navigateSpy).toHaveBeenCalledWith(['/test', 42], { replaceUrl: true });
            expect(testService.convertTestInstanceToRegions).not.toHaveBeenCalled();
        });
    }

    // Issue #573: etkinlik oturumu test ekranında açılmaz; oynatıcıya yönlenir ve sayaç/soru yüklemesi başlamaz.
    it('opening /testsolve for an activity instance redirects to the activity player', async () => {
        testService.getCanvasTestWithAnswers.mockReturnValue(of(buildInstance({ kind: 1 })));

        await component.loadTest(7);

        expect(navigateSpy).toHaveBeenCalledTimes(1);
        expect(navigateSpy).toHaveBeenCalledWith(['/activity', 7], { replaceUrl: true });
        expect(testService.convertTestInstanceToRegions).not.toHaveBeenCalled();
    });

    it('time up: finishes without a confirm dialog and goes to the result page', async () => {
        component.testInstance = buildInstance({ maxDurationSeconds: 2 });

        component.startTimer();
        await vi.advanceTimersByTimeAsync(2000);
        await vi.advanceTimersByTimeAsync(0);

        expect(dialog.open).not.toHaveBeenCalled();
        expect(testService.saveAnswer).toHaveBeenCalledTimes(1);
        expect(testService.completeTest).toHaveBeenCalledTimes(1);
        expect(testService.completeTest).toHaveBeenCalledWith(7);
        expect(navigateSpy).toHaveBeenCalledTimes(1);
        expect(navigateSpy).toHaveBeenCalledWith(['/test', 42], { replaceUrl: true });

        // Sayaç durdu: sonraki saniyelerde tekrar tetiklenmez.
        await vi.advanceTimersByTimeAsync(3000);
        await vi.advanceTimersByTimeAsync(0);
        expect(testService.completeTest).toHaveBeenCalledTimes(1);
    });

    it('time up + failed last save: UI is locked by a non-closable retry dialog; retry completes', async () => {
        component.testInstance = buildInstance({ maxDurationSeconds: 1 });
        testService.saveAnswer.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 }))).mockReturnValueOnce(of(OK));
        const retryClosed$ = new Subject<boolean>();
        dialog.open.mockReturnValue({ afterClosed: () => retryClosed$ } as unknown as ReturnType<MatDialog['open']>);

        component.startTimer();
        await vi.advanceTimersByTimeAsync(1000);
        await vi.advanceTimersByTimeAsync(0);

        expect(testService.completeTest).not.toHaveBeenCalled();
        expect(dialog.open).toHaveBeenCalledTimes(1);
        const [dialogComponent, config] = vi.mocked(dialog.open).mock.lastCall!;
        expect(dialogComponent).toBe(TestFinishRetryDialogComponent);
        expect(config?.disableClose).toBe(true);
        expect(component.finishing()).toBe(true);

        // Sayaç yeniden başlamadı: bekleyince yeni deneme/istek yok.
        await vi.advanceTimersByTimeAsync(5000);
        await vi.advanceTimersByTimeAsync(0);
        expect(testService.saveAnswer).toHaveBeenCalledTimes(1);

        retryClosed$.next(true);
        retryClosed$.complete();
        await vi.advanceTimersByTimeAsync(0);

        expect(testService.saveAnswer).toHaveBeenCalledTimes(2);
        expect(testService.completeTest).toHaveBeenCalledTimes(1);
        expect(testService.completeTest).toHaveBeenCalledWith(7);
        expect(navigateSpy).toHaveBeenCalledTimes(1);
        expect(navigateSpy).toHaveBeenCalledWith(['/test', 42], { replaceUrl: true });
    });

    it('multi-question view: on finish all dirty questions are re-saved (also ones outside the view), clean ones are not', async () => {
        component.autoNextQuestion.set(false);
        // Tek görünümde soru 3 (index 2) cevaplandı ama kaydı hiç gönderilmedi; sonra 2'li görünüme geçildi.
        component.currentIndex.set(2);
        component.selectAnswer(33);
        component.currentIndex.set(0);
        component.setQuestionsPerView(2);
        // Soru 1 (index 0) yüklemeden cevaplı geldi (dirty değil); soru 2 bu görünümde cevaplandı.
        component['dirtyQuestionIndices'].delete(0);
        component.selectAnswerForQuestion(22, 1);

        await component.completeTest();

        const sentIds = vi.mocked(testService.saveAnswer).mock.calls.map(([req]) => req.testQuestionId);
        expect(sentIds.sort()).toEqual([102, 103]);
        expect(testService.completeTest).toHaveBeenCalledTimes(1);
        expect(testService.completeTest).toHaveBeenCalledWith(7);
    });

    it('retry dialog closed without an explicit retry (e.g. by navigation) does not re-finish', async () => {
        component.testInstance = buildInstance({ maxDurationSeconds: 1 });
        testService.saveAnswer.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
        dialog.open.mockReturnValue({ afterClosed: () => of(undefined) } as unknown as ReturnType<MatDialog['open']>);

        component.startTimer();
        await vi.advanceTimersByTimeAsync(1000);
        await vi.advanceTimersByTimeAsync(0);

        expect(dialog.open).toHaveBeenCalledTimes(1);
        expect(testService.saveAnswer).toHaveBeenCalledTimes(1);
        expect(testService.completeTest).not.toHaveBeenCalled();
        expect(component.finishing()).toBe(false);
    });

    // ---- issue #396: timer from the server's remainingSeconds; time-up message ----

    it('the timer starts from the server remaining time, not from zero (reload keeps the elapsed time)', async () => {
        component.testInstance = buildInstance({ maxDurationSeconds: 600, remainingSeconds: 3 });

        component.initTimerFromServer();
        expect(component.testDuration).toBe(597);

        component.startTimer();
        await vi.advanceTimersByTimeAsync(2000);
        await vi.advanceTimersByTimeAsync(0);
        expect(testService.completeTest).not.toHaveBeenCalled();

        await vi.advanceTimersByTimeAsync(1000);
        await vi.advanceTimersByTimeAsync(0);
        expect(dialog.open).not.toHaveBeenCalled();
        expect(testService.completeTest).toHaveBeenCalledTimes(1);
        expect(testService.completeTest).toHaveBeenCalledWith(7);
    });

    it('elapsed time follows the wall clock, so a throttled background tab does not drift', async () => {
        component.testInstance = buildInstance({ maxDurationSeconds: 600, remainingSeconds: 100 });
        // The deadline was computed 40 s ago (tab in the background, interval ticks were throttled away).
        component.initTimerFromServer(Date.now() - 40000);

        component.startTimer();
        await vi.advanceTimersByTimeAsync(1000);

        expect(component.testDuration).toBe(541); // 600 - (100 - 41)
        component.testTimerSubscription.unsubscribe();
    });

    it('opening a test whose server time is already up finishes immediately', async () => {
        component.testInstance = buildInstance({ maxDurationSeconds: 600, remainingSeconds: 0 });

        component.initTimerFromServer();
        component.startTimer();
        await vi.advanceTimersByTimeAsync(0);

        expect(dialog.open).not.toHaveBeenCalled();
        expect(testService.completeTest).toHaveBeenCalledTimes(1);
        expect(testService.completeTest).toHaveBeenCalledWith(7);
    });

    it('a test without a time limit never auto-finishes', async () => {
        component.testInstance = buildInstance({ maxDurationSeconds: 0, remainingSeconds: null });

        component.initTimerFromServer();
        component.startTimer();
        await vi.advanceTimersByTimeAsync(5000);
        await vi.advanceTimersByTimeAsync(0);

        expect(testService.completeTest).not.toHaveBeenCalled();
        expect(component.testDuration).toBe(5);
        component.testTimerSubscription.unsubscribe();
    });

    it('a save rejected because time is up shows the time-up message and goes to the result page', async () => {
        testService.saveAnswer.mockReturnValue(throwError(timeUp409));

        await component.completeTest();

        expect(testService.completeTest).not.toHaveBeenCalled();
        expect(snackBar.open).toHaveBeenCalledTimes(1);
        expect(vi.mocked(snackBar.open).mock.lastCall![0]).toBe(testSolveTr.finish.timeUp);
        expect(navigateSpy).toHaveBeenCalledTimes(1);
        expect(navigateSpy).toHaveBeenCalledWith(['/test', 42], { replaceUrl: true });
    });

    it('end-test rejected because time is up (200 body) shows the time-up message', async () => {
        testService.completeTest.mockReturnValue(of(TIME_UP));

        await component.completeTest();

        expect(vi.mocked(snackBar.open).mock.lastCall![0]).toBe(testSolveTr.finish.timeUp);
        expect(navigateSpy).toHaveBeenCalledTimes(1);
        expect(navigateSpy).toHaveBeenCalledWith(['/test', 42], { replaceUrl: true });
    });

    it('a plain not-in-progress rejection keeps the generic message', async () => {
        testService.saveAnswer.mockReturnValue(throwError(conflict409));

        await component.completeTest();

        expect(vi.mocked(snackBar.open).mock.lastCall![0]).toBe(testSolveTr.finish.alreadyCompleted);
    });

    describe('saving, resume, and timing (issue #777)', () => {
        const sentQuestionIds = () => vi.mocked(testService.saveAnswer).mock.calls.map(([req]) => req.testQuestionId);

        beforeEach(() => {
            component['dirtyQuestionIndices'].clear();
        });

        it('choosing an answer saves it at once, and jumping from the map sends nothing more', async () => {
            component.selectAnswerForQuestion(22, 1);
            expect(testService.saveAnswer).toHaveBeenCalledTimes(1);
            expect(testService.saveAnswer).toHaveBeenCalledWith(
                expect.objectContaining({ testQuestionId: 102, selectedAnswerId: 22, testInstanceId: 7 })
            );

            await settle();
            await component.focusOnQuestion(2);
            expect(testService.saveAnswer).toHaveBeenCalledTimes(1);
        });

        it('moving on does not send an unchanged answer again, because every save counts in the statistics', async () => {
            component.regions.set([{}, {}, {}, {}] as never);
            component.selectAnswerForQuestion(22, 0);
            await settle();

            await component.nextQuestion();
            await component.prevQuestion();
            component.selectAnswerForQuestion(22, 0);
            await settle();

            expect(testService.saveAnswer).toHaveBeenCalledTimes(1);
        });

        it('an example question and a practice test send nothing', () => {
            component.selectAnswerForQuestion(44, 3);
            expect(testService.saveAnswer).not.toHaveBeenCalled();

            component.testInstance = buildInstance({ isPracticeTest: true });
            component.selectAnswerForQuestion(22, 1);
            expect(testService.saveAnswer).not.toHaveBeenCalled();
        });

        it('saves for one question run in order, so a late reply cannot overwrite a newer answer', async () => {
            const first$ = new Subject<TestSessionResult>();
            const second$ = new Subject<TestSessionResult>();
            testService.saveAnswer.mockReturnValueOnce(first$).mockReturnValueOnce(second$);

            component.selectAnswerForQuestion(21, 1);
            component.selectAnswerForQuestion(22, 1);
            await settle();
            expect(first$.observed).toBe(true);
            expect(second$.observed).toBe(false);

            first$.next(OK);
            first$.complete();
            await settle();
            expect(second$.observed).toBe(true);

            second$.next(OK);
            second$.complete();
            await settle();
            expect(component.saveStatus()).toBe('saved');
        });

        it('shows saving and saved', async () => {
            const save$ = new Subject<TestSessionResult>();
            testService.saveAnswer.mockReturnValue(save$);
            expect(component.saveStatus()).toBe('idle');

            component.selectAnswerForQuestion(22, 1);
            expect(component.saveStatus()).toBe('saving');

            await settle();
            save$.next(OK);
            save$.complete();
            await settle();
            expect(component.saveStatus()).toBe('saved');
        });

        it('a failed save stays visible until a retry succeeds', async () => {
            testService.saveAnswer.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));

            component.selectAnswerForQuestion(22, 1);
            await settle();
            await vi.advanceTimersByTimeAsync(60000);
            expect(component.saveStatus()).toBe('failed');

            component.retryFailedSaves();
            expect(testService.saveAnswer).toHaveBeenCalledTimes(2);
            await settle();
            expect(component.saveStatus()).toBe('saved');
        });

        it('clearing an answer reaches the server and removes the choice', async () => {
            component.regions.set([{ id: 1, answers: [] }] as never);
            component.selectedChoices.set(new Map([[1, { id: 11 } as never]]));

            component.clearAnswer();
            await settle();

            const [request] = vi.mocked(testService.saveAnswer).mock.lastCall!;
            expect(request).toEqual(expect.objectContaining({ testQuestionId: 101, selectedAnswerId: 0 }));
            expect(request.answerPayload).toBeUndefined();
            expect(component.selectedChoices().has(1)).toBe(false);
            expect(component.testInstance.testInstanceQuestions[0].selectedAnswerId).toBeFalsy();
        });

        it('a clear that failed is sent again on finish', async () => {
            component.regions.set([{ id: 1, answers: [] }] as never);
            testService.saveAnswer.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));
            component.clearAnswer();
            await settle();

            await component.completeTest();

            expect(sentQuestionIds()).toEqual([101, 101]);
            expect(testService.completeTest).toHaveBeenCalledTimes(1);
        });

        describe('resume', () => {
            function loaded(overrides: Partial<TestInstance> = {}) {
                testService.getCanvasTestWithAnswers.mockReturnValue(of(buildInstance(overrides)));
                testService.convertTestInstanceToRegions.mockReturnValue([]);
            }

            it('reload returns to the question the student was on, with bookmarks', async () => {
                localStorage.setItem(
                    'testSolve.instance.7',
                    JSON.stringify({ questionId: 103, bookmarkedQuestionIds: [102, 999], lowTimeNotified: false })
                );
                loaded();

                await component.loadTest(7);
                TestBed.tick();

                expect(component.currentIndex()).toBe(2);
                expect(component.viewStartIndex()).toBe(2);
                expect([...component.bookmarkedQuestions()]).toEqual([1]);
            });

            it('without a saved position it opens the first empty question, skipping answered and example ones', async () => {
                loaded();

                await component.loadTest(7);

                expect(component.currentIndex()).toBe(1);
                expect(component.viewStartIndex()).toBe(1);
            });

            it('remembers the position and bookmarks on this device, and forgets them after the finish', async () => {
                loaded();
                await component.loadTest(7);

                await component.focusOnQuestion(2);
                component.toggleBookmark();
                TestBed.tick();
                expect(JSON.parse(localStorage.getItem('testSolve.instance.7')!)).toEqual({
                    questionId: 103,
                    bookmarkedQuestionIds: [103],
                    lowTimeNotified: false,
                });

                await component.completeTest();
                TestBed.tick();
                expect(localStorage.getItem('testSolve.instance.7')).toBeNull();
            });

            it('broken saved data falls back to the defaults', async () => {
                localStorage.setItem('testSolve.instance.7', '{not json');
                loaded();

                await component.loadTest(7);

                expect(component.currentIndex()).toBe(1);
                expect(component.bookmarkedQuestions().size).toBe(0);
            });
        });

        describe('auto-advance', () => {
            it('is off by default', () => {
                expect(component.autoNextQuestion()).toBe(false);
            });

            it('is remembered on this device', () => {
                component.toggleAutoNextQuestion();
                const next = TestBed.createComponent(TestSolveCanvasComponentv3);
                expect(next.componentInstance.autoNextQuestion()).toBe(true);
                next.destroy();
            });

            it('moves on after a first answer, but not when an answer is changed', async () => {
                component.autoNextQuestion.set(true);
                component.regions.set([{}, {}, {}, {}] as never);
                component.currentIndex.set(1);
                component.viewStartIndex.set(1);

                component.selectAnswerForQuestion(22, 1);
                await vi.advanceTimersByTimeAsync(300);
                expect(component.currentIndex()).toBe(2);

                component.currentIndex.set(1);
                component.viewStartIndex.set(1);
                component.selectAnswerForQuestion(23, 1);
                await vi.advanceTimersByTimeAsync(300);
                expect(component.currentIndex()).toBe(1);
            });

            it('does not move on if the student already went to another question', async () => {
                component.autoNextQuestion.set(true);
                component.regions.set([{}, {}, {}, {}] as never);
                component.currentIndex.set(1);

                component.selectAnswerForQuestion(22, 1);
                component.currentIndex.set(0);
                await vi.advanceTimersByTimeAsync(300);

                expect(component.currentIndex()).toBe(0);
            });
        });

        describe('shortcuts', () => {
            const press = (key: string) => {
                const event = new KeyboardEvent('keydown', { key, ctrlKey: true, cancelable: true });
                document.dispatchEvent(event);
                return event;
            };

            it('stop working when the screen closes', () => {
                component.ngOnInit();
                expect(press('b').defaultPrevented).toBe(true);
                expect(component.isQuestionBookmarked(0)).toBe(true);

                fixture.destroy();

                expect(press('b').defaultPrevented).toBe(false);
                expect(press('f').defaultPrevented).toBe(false);
            });

            // #841: düz tuşlar — A–E şık seçer, ← → soru değiştirir.
            const plain = (key: string, target: EventTarget = document) => {
                const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
                target.dispatchEvent(event);
                return event;
            };

            it('letters pick the matching choice and arrows change the question (#841)', async () => {
                component.regions.set([
                    { id: 1, answers: [{ id: 31, label: 'A' }, { id: 32, label: 'B' }] },
                    {}, {}, {},
                ] as never);
                const select = vi.spyOn(component, 'selectChoiceForQuestion').mockImplementation(() => undefined);

                expect(plain('b').defaultPrevented).toBe(true);
                expect(select).toHaveBeenCalledWith(expect.objectContaining({ id: 32 }), 0);
                expect(plain('x').defaultPrevented).toBe(false);

                expect(plain('ArrowRight').defaultPrevented).toBe(true);
                await settle();
                expect(component.currentIndex()).toBe(1);
            });

            it('plain keys are ignored while typing (#841)', () => {
                component.regions.set([{ id: 1, answers: [{ id: 31, label: 'A' }] }, {}, {}, {}] as never);
                const select = vi.spyOn(component, 'selectChoiceForQuestion').mockImplementation(() => undefined);
                const input = document.createElement('input');
                document.body.appendChild(input);

                expect(plain('a', input).defaultPrevented).toBe(false);
                expect(plain('ArrowRight', input).defaultPrevented).toBe(false);
                expect(select).not.toHaveBeenCalled();
                input.remove();
            });

            it('have no entries for views without a visible control', () => {
                component.ngOnInit();
                for (const key of ['1', '2', '4', 'h']) {
                    expect(press(key).defaultPrevented).toBe(false);
                }
                expect(component.questionsPerView()).toBe(1);
            });
        });

        it('finish stays secondary until the last question or no empty question (#841)', async () => {
            component.regions.set([{}, {}, {}, {}] as never);
            expect(component.finishReady()).toBe(false);

            await component.focusOnQuestion(3);
            expect(component.finishReady()).toBe(true);
        });

        describe('low time notice', () => {
            it('appears once at 5 minutes left, then hides', async () => {
                component.testInstance = buildInstance({ maxDurationSeconds: 302 });

                component.startTimer();
                await vi.advanceTimersByTimeAsync(1000);
                expect(component.lowTimeNoticeVisible()).toBe(false);
                expect(component.isTimeLow()).toBe(false);

                await vi.advanceTimersByTimeAsync(1000);
                expect(component.lowTimeNoticeVisible()).toBe(true);
                expect(component.isTimeLow()).toBe(true);

                component.dismissLowTimeNotice();
                await vi.advanceTimersByTimeAsync(5000);
                expect(component.lowTimeNoticeVisible()).toBe(false);
                component.testTimerSubscription.unsubscribe();
            });

            it('is not shown again after a reload', async () => {
                localStorage.setItem(
                    'testSolve.instance.7',
                    JSON.stringify({ questionId: 101, bookmarkedQuestionIds: [], lowTimeNotified: true })
                );
                testService.getCanvasTestWithAnswers.mockReturnValue(
                    of(buildInstance({ maxDurationSeconds: 3600, remainingSeconds: 120 } as Partial<TestInstance>))
                );
                testService.convertTestInstanceToRegions.mockReturnValue([]);

                await component.loadTest(7);

                expect(component.isTimeLow()).toBe(true);
                expect(component.lowTimeNoticeVisible()).toBe(false);
                component.testTimerSubscription.unsubscribe();
            });

            it('is not shown in a test of 5 minutes or less, or in an untimed test', async () => {
                component.testInstance = buildInstance({ maxDurationSeconds: 300 });
                component.startTimer();
                await vi.advanceTimersByTimeAsync(1000);
                expect(component.lowTimeNoticeVisible()).toBe(false);
                component.testTimerSubscription.unsubscribe();

                component.testInstance = buildInstance({ maxDurationSeconds: 0 });
                component.testDuration = 0;
                component.startTimer();
                await vi.advanceTimersByTimeAsync(1000);
                expect(component.lowTimeNoticeVisible()).toBe(false);
                component.testTimerSubscription.unsubscribe();
            });
        });

        describe('counts', () => {
            function withPayloadAndExample() {
                const instance = buildInstance();
                instance.testInstanceQuestions[1].answerPayload = '{"answers":["a"]}';
                instance.testInstanceQuestions[3].selectedAnswerId = 44;
                component.testInstance = instance;
            }

            it('map, counts, and the finish dialog agree for payload and example questions', () => {
                withPayloadAndExample();
                component['updateAnsweredCount']();

                expect(component.answeredCount()).toBe(2);
                expect(component.remainingCount()).toBe(1);
                expect(component.unansweredCount()).toBe(1);
                expect(component.progressPercentage()).toBe(67);
                expect(component['isQuestionAnswered'](component.testInstance.testInstanceQuestions[1])).toBe(true);
            });

            it('"Go to empty" skips example questions', async () => {
                component.testInstance = buildInstance();
                component.currentIndex.set(2);
                component.testInstance.testInstanceQuestions[1].selectedAnswerId = 22;
                component.testInstance.testInstanceQuestions[2].selectedAnswerId = 33;
                const focus = vi.spyOn(component, 'focusOnQuestion');

                component.goToNextUnanswered();

                expect(focus).not.toHaveBeenCalled();
                expect(component.toastMessage()).toBe(testSolveTr.toast.allAnswered);
            });
        });

        describe('loading', () => {
            it('a load error offers a retry that opens the test', async () => {
                vi.spyOn(console, 'error').mockImplementation(() => undefined);
                component.testInstanceId = 7;
                testService.getCanvasTestWithAnswers.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));
                await component.loadTest(7);
                expect(component.loadError()).toBe(true);
                expect(component.loadingTest()).toBe(false);

                testService.getCanvasTestWithAnswers.mockReturnValue(of(buildInstance()));
                testService.convertTestInstanceToRegions.mockReturnValue([]);
                component.retryLoadTest();
                expect(component.loadingTest()).toBe(true);
                expect(component.loadError()).toBe(false);
                await settle();
                expect(component.loadingTest()).toBe(false);
                component.testTimerSubscription.unsubscribe();
            });

            it('a slow question image shows progress; a fast one does not', async () => {
                let finish!: () => void;
                const slow = new Promise<void>((resolve) => (finish = resolve));
                const waiting = component['whileQuestionLoads'](slow);

                await vi.advanceTimersByTimeAsync(299);
                expect(component.questionLoading()).toBe(false);
                await vi.advanceTimersByTimeAsync(1);
                expect(component.questionLoading()).toBe(true);

                finish();
                await waiting;
                expect(component.questionLoading()).toBe(false);
            });
        });

        describe('question report (#781)', () => {
            const reportData = () => dialog.open.mock.calls[0][1]?.data as QuestionReportDialogData;

            it('confirms only after the dialog returns a saved report', async () => {
                const toast = vi.fn();
                component['showToastMessage'] = toast;
                dialogReturns(undefined);

                component.reportQuestion();
                await settle();
                expect(dialog.open).toHaveBeenCalledTimes(1);
                expect(toast).not.toHaveBeenCalled();

                dialogReturns({ alreadyReported: false });
                component.reportQuestion();
                await settle();
                expect(toast).toHaveBeenCalledWith(testSolveTr.report.saved, 'success');

                dialogReturns({ alreadyReported: true });
                component.reportQuestion();
                await settle();
                expect(toast).toHaveBeenCalledWith(testSolveTr.report.alreadyReported, 'success');
            });

            it('sends the current question and session', () => {
                const create = vi.spyOn(TestBed.inject(QuestionReportService), 'create').mockReturnValue(of({ alreadyReported: false }));
                dialogReturns(undefined);
                component.currentIndex.set(1);

                component.reportQuestion();
                reportData().submit('unreadable', 'blurry').subscribe();

                expect(create).toHaveBeenCalledWith(component.testInstance.testInstanceQuestions[1].question.id, {
                    testInstanceId: component.testInstance.id,
                    reason: 'unreadable',
                    note: 'blurry',
                });
            });
        });

        it('finishing removes the scratch notes of this session (#782)', () => {
            writeScratchNotes(component.testInstance.id, { 1: { strokes: [], text: 'x' } });

            component['navigateToResult']();

            expect(readScratchNotes(component.testInstance.id)).toEqual({});
        });

        it('the old copy is gone', () => {
            expect(JSON.stringify(testSolveTr)).not.toMatch(/Sınav|sınav|Canvas/);
        });
    });
});

// Issue #574: matris seçim dalı — tür normalizasyonu, payload kaydı ve seçimsiz cevabın boş sayılması.
describe('TestSolveCanvasComponentv3 — matrixChoice (#574)', () => {
    let fixture: ComponentFixture<TestSolveCanvasComponentv3>;
    let component: TestSolveCanvasComponentv3;
    let testService: MockedObject<TestService>;
    const MATRIX = (selections: Record<string, string[]>) => JSON.stringify({ version: 1, type: 'matrixChoice', selections });

    beforeEach(async () => {
        testService = {
            saveAnswer: vi.fn().mockName('TestService.saveAnswer'),
            completeTest: vi.fn(),
            getCanvasTestWithAnswers: vi.fn(),
            convertTestInstanceToRegions: vi.fn(),
        } as unknown as MockedObject<TestService>;
        testService.saveAnswer.mockReturnValue(of(OK));
        await TestBed.configureTestingModule({
            imports: [TestSolveCanvasComponentv3, translocoTesting],
            providers: [
                provideRouter([]),
                { provide: ActivatedRoute, useValue: { paramMap: NEVER } },
                { provide: TestService, useValue: testService },
            ],
        }).compileComponents();
        fixture = TestBed.createComponent(TestSolveCanvasComponentv3);
        component = fixture.componentInstance;
        component.testInstance = buildInstance();
        component.currentIndex.set(0);
    });

    afterEach(() => fixture.destroy());

    it('interactionKind_IsCaseInsensitive_UnknownIsMcq', () => {
        expect(component.interactionKind('matrixChoice')).toBe('matrixChoice');
        expect(component.interactionKind('MatrixChoice')).toBe('matrixChoice');
        expect(component.interactionKind('DRAGDROPLABELING')).toBe('dragDropLabeling');
        expect(component.interactionKind(null)).toBe('mcq');
        expect(component.interactionKind('somethingElse')).toBe('mcq');
        expect(component.interactionKind('slotFill')).toBe('slotFill');
        expect(component.interactionKind('SLOTFILL')).toBe('slotFill');
        expect(component.interactionKind('DragPlace')).toBe('dragPlace');
        expect(component.interactionKind('MATCH')).toBe('match');
        expect(component.interactionKind('OpenText')).toBe('openText');
        expect(component.interactionKind('TextSelect')).toBe('textSelect');
    });

    it('blankDragPlaceAndMatchPayloads_AreSentButCountAsUnanswered (#576)', () => {
        const place = (placements: object[]) => JSON.stringify({ version: 1, type: 'dragPlace', placements });
        const match = (pairs: object[]) => JSON.stringify({ version: 1, type: 'match', pairs });
        component.saveDragDropAnswerForQuestion(place([{ targetId: 't1', cardId: 'c1' }]), 1);
        expect(component.unansweredCount()).toBe(1);
        component.saveDragDropAnswerForQuestion(place([]), 1);
        expect(vi.mocked(testService.saveAnswer).mock.lastCall![0].answerPayload).toBe(place([]));
        expect(component.unansweredCount()).toBe(2);
        component.saveDragDropAnswerForQuestion(match([{ leftId: 'l1', rightId: 'r1' }]), 1);
        expect(component.unansweredCount()).toBe(1);
        component.saveDragDropAnswerForQuestion(match([]), 1);
        expect(component.unansweredCount()).toBe(2);
    });

    it('blankTextSelectPayload_IsSentButCountsAsUnanswered (#582)', () => {
        const text = (selected: string[], values: Record<string, string> = {}) =>
            JSON.stringify({ version: 1, type: 'textSelect', selected, values });
        component.saveDragDropAnswerForQuestion(text(['t1']), 1);
        expect(component.unansweredCount()).toBe(1);
        component.saveDragDropAnswerForQuestion(text([], { g1: ' ' }), 1);
        expect(vi.mocked(testService.saveAnswer).mock.lastCall![0].answerPayload).toBe(text([], { g1: ' ' }));
        expect(component.unansweredCount()).toBe(2);
    });

    it('completeTest_FlushesTextSelectPlayersByToken (#582)', () => {
        const player = { flush: vi.fn() };
        (component as unknown as { flushablePlayers: () => unknown[] }).flushablePlayers = () => [player];
        (component as unknown as { flushInteractivePlayers: () => void }).flushInteractivePlayers();
        expect(player.flush).toHaveBeenCalledTimes(1);
    });

    it('completeTest_FlushesSlotFillPlayersBeforeSaving (#575)', async () => {
        const order: string[] = [];
        const player = { flush: vi.fn(() => order.push('flush')) };
        (component as unknown as { slotFillPlayers: () => unknown[] }).slotFillPlayers = () => [player];
        testService.completeTest.mockImplementation(() => {
            order.push('complete');
            return of(OK);
        });

        await component.completeTest();

        expect(player.flush).toHaveBeenCalledTimes(1);
        expect(order[0]).toBe('flush');
    });

    it('blankSlotFillPayload_IsSentButCountsAsUnanswered (#575)', () => {
        const slot = (values: Record<string, string>) => JSON.stringify({ version: 1, type: 'slotFill', values });
        component.saveDragDropAnswerForQuestion(slot({ s1: '3/4' }), 1);
        expect(component.unansweredCount()).toBe(1);
        component.saveDragDropAnswerForQuestion(slot({}), 1);

        expect(vi.mocked(testService.saveAnswer).mock.lastCall![0].answerPayload).toBe(slot({}));
        expect(component.unansweredCount()).toBe(2);
    });

    it('matrixPayload_IsSavedThroughThePayloadPath', () => {
        const payload = MATRIX({ r1: ['c2'] });

        component.saveDragDropAnswerForQuestion(payload, 1);

        expect(testService.saveAnswer).toHaveBeenCalledWith(
            expect.objectContaining({ testQuestionId: 102, selectedAnswerId: 0, answerPayload: payload, testInstanceId: 7 })
        );
        expect(component.unansweredCount()).toBe(1);
    });

    it('blankMatrixPayload_IsSentButCountsAsUnanswered', () => {
        component.saveDragDropAnswerForQuestion(MATRIX({ r1: ['c2'] }), 1);
        component.saveDragDropAnswerForQuestion(MATRIX({}), 1);

        expect(vi.mocked(testService.saveAnswer).mock.lastCall![0].answerPayload).toBe(MATRIX({}));
        expect(component.unansweredCount()).toBe(2);
    });
});

describe('isTestCompletedRejection', () => {
    it('only the TestNotInProgress error code counts; a bare 409 or conflict flag does not', () => {
        expect(isTestCompletedRejection(conflict409())).toBe(true);
        expect(isTestCompletedRejection({ success: false, errorCode: 'TestNotInProgress' })).toBe(true);
        expect(isTestCompletedRejection(new HttpErrorResponse({ status: 409 }))).toBe(false);
        expect(isTestCompletedRejection({ success: false, conflict: true })).toBe(false);
        expect(isTestCompletedRejection(new HttpErrorResponse({ status: 500 }))).toBe(false);
        expect(isTestCompletedRejection({ success: false, conflict: false })).toBe(false);
        expect(isTestCompletedRejection(null)).toBe(false);
    });

    it('issue #396: the time-up reason is recognised only together with the TestNotInProgress code', () => {
        expect(isTimeExpiredRejection(timeUp409())).toBe(true);
        expect(isTimeExpiredRejection(TIME_UP)).toBe(true);
        expect(isTimeExpiredRejection(conflict409())).toBe(false);
        expect(isTimeExpiredRejection({ success: false, reason: 'TimeExpired' })).toBe(false);
        expect(isTimeExpiredRejection(null)).toBe(false);
    });
});
