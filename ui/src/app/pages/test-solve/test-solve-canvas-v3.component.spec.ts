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
import { TestInstance, TestSessionResult, TestStatus } from '../../models/test-instance';
import { ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
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

    function dialogReturns(result: boolean) {
        dialog.open.mockReturnValue({ afterClosed: () => of(result) } as ReturnType<MatDialog['open']>);
    }

    beforeEach(async () => {
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
            closeAll: vi.fn().mockName("MatDialog.closeAll")
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
    });

    afterEach(() => fixture.destroy());

    it('confirm dialog shows the unanswered count; cancel keeps the test running', async () => {
        dialogReturns(false);

        component.confirmFinishTest();
        await settle();

        expect(dialog.open).toHaveBeenCalledTimes(1);
        const data = vi.mocked(dialog.open).mock.lastCall![1]?.data as ConfirmDialogData;
        expect(data.message).toContain('2 soru boş');
        expect(data.cancelText).toBe('Vazgeç');
        expect(testService.saveAnswer).not.toHaveBeenCalled();
        expect(testService.completeTest).not.toHaveBeenCalled();
        expect(navigateSpy).not.toHaveBeenCalled();
        expect(component.finishing()).toBe(false);
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
        component['persistAnswerForQuestion'](22, 1);

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

        component.persistAnswer(11);
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
