import type { MockedObject } from "vitest";
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { By } from '@angular/platform-browser';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatRadioButton } from '@angular/material/radio';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { of, switchMap, throwError, timer } from 'rxjs';

import { TestCreateEnhancedComponent } from './test-create-enhanced.component';
import { TestService } from '../../services/test.service';
import { AuthService } from '../../services/auth.service';
import { WorksheetStudentVisibility, WorksheetTeacherSharing } from '../../models/test-instance';
import { VisibilitySectionComponent } from '../../shared/components/visibility-section/visibility-section.component';
import { BookService } from '../../services/book.service';
import { GradesService } from '../../services/grades.service';
import { SubjectService } from '../../services/subject.service';

import { TranslocoTestingModule } from '@jsverse/transloco';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import rootTr from '../../../../public/i18n/tr.json';
import testCreateTr from '../../../../public/i18n/test-create/tr.json';

/** Gercek scope sozlugu yuklenir; anahtar bozulursa test kirilir (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
    // Scope sozlugu hem scope yolu (provideTranslocoScope yukleyicisi) hem de kok 'tr' icine
    // gomulu olarak verilir; sablondaki 'prefix' bicimi ikincisinden cozulur.
    langs: { tr: { ...rootTr, 'test-create': testCreateTr }, 'test-create/tr': testCreateTr },
    translocoConfig: {
        availableLangs: [...SUPPORTED_LOCALE_CODES],
        defaultLang: DEFAULT_LOCALE,
        // TranslocoTestingModule uygulamanin config'ini almaz; scope oneki kebab kalsin (issue #183).
        scopes: { keepCasing: true },
    },
    preloadLangs: true,
});

describe('TestCreateEnhancedComponent', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    let testService: MockedObject<TestService>;
    let router: MockedObject<Router>;
    let snackBar: MockedObject<MatSnackBar>;

    function configure(
        idParam: string | null,
        exam: Record<string, unknown> = {},
        query: Record<string, string> = {}
    ): ComponentFixture<TestCreateEnhancedComponent> {
        // Guard: component reads history.state.testValue when there is no :id param.
        window.history.replaceState({}, '');

        testService = {
            create: vi.fn().mockName("TestService.create"),
            get: vi.fn().mockName("TestService.get"),
            updateWorksheetBackgroundImage: vi.fn().mockName("TestService.updateWorksheetBackgroundImage"),
            updateVisibility: vi.fn().mockName("TestService.updateVisibility")
        } as unknown as MockedObject<TestService>;
        testService.get.mockReturnValue(of({ id: 1, name: 'X', gradeId: 1, maxDurationSeconds: 600, isPracticeTest: false, ...exam } as any));
        testService.create.mockReturnValue(of({ message: 'ok', examId: 42 }));
        testService.updateWorksheetBackgroundImage.mockReturnValue(of({ imageUrl: 'http://img/x.png' } as any));

        const bookService = {
            getAll: vi.fn().mockName("BookService.getAll"),
            getTestsByBook: vi.fn().mockName("BookService.getTestsByBook")
        };
        bookService.getAll.mockReturnValue(of([]));
        bookService.getTestsByBook.mockReturnValue(of([]));

        const gradesService = {
            getGrades: vi.fn().mockName("GradesService.getGrades")
        };
        gradesService.getGrades.mockReturnValue(of([{ id: 1, name: '5' }]));

        const subjectService = {
            loadCategories: vi.fn().mockName("SubjectService.loadCategories"),
            getSubjectsByGrade: vi.fn().mockName("SubjectService.getSubjectsByGrade"),
            getTopicsBySubjectAndGrade: vi.fn().mockName("SubjectService.getTopicsBySubjectAndGrade"),
            getSubTopicsByTopic: vi.fn().mockName("SubjectService.getSubTopicsByTopic")
        };
        subjectService.loadCategories.mockReturnValue(of([]));
        subjectService.getSubjectsByGrade.mockReturnValue(of([]));
        subjectService.getTopicsBySubjectAndGrade.mockReturnValue(of([]));
        subjectService.getSubTopicsByTopic.mockReturnValue(of([]));

        router = {
            navigate: vi.fn().mockName("Router.navigate"),
            currentNavigation: vi.fn().mockName("Router.currentNavigation") as never
        } as unknown as MockedObject<Router>;
        router.currentNavigation.mockReturnValue(null as any);

        TestBed.configureTestingModule({
            imports: [TestCreateEnhancedComponent, translocoTesting],
            providers: [
                provideNoopAnimations(),
                { provide: TestService, useValue: testService },
                { provide: BookService, useValue: bookService },
                { provide: GradesService, useValue: gradesService },
                { provide: SubjectService, useValue: subjectService },
                { provide: Router, useValue: router },
                // Alt bileşen app-visibility-section okul bilgisini AuthService.user'dan okur (issue #191).
                {
                    provide: AuthService,
                    useValue: {
                        user: signal({ schoolId: 1 }),
                        hasRole: (r: string) => r === 'Teacher',
                        hasRealmRole: (r: string) => false,
                    },
                },
                // Headless Chrome (800x600 yatay) Handset'e girer; radio DOM'unu doğrulamak için masaüstü sabitlenir.
                { provide: BreakpointObserver, useValue: { observe: () => of({ matches: false, breakpoints: {} }) } },
                {
                    provide: ActivatedRoute,
                    useValue: {
                        snapshot: {
                            paramMap: convertToParamMap(idParam ? { id: idParam } : {}),
                            queryParamMap: convertToParamMap(query),
                        },
                    },
                },
            ],
        });

        const fixture = TestBed.createComponent(TestCreateEnhancedComponent);
        // MatSnackBar is provided by MatSnackBarModule (not root); stub it on the instance.
        snackBar = fixture.componentInstance.snackBar as MockedObject<MatSnackBar>;
        vi.spyOn(snackBar, 'open').mockReturnValue(undefined as never);
        fixture.detectChanges();
        return fixture;
    }

    afterEach(() => TestBed.resetTestingModule());

    function fileEvent(file: File): Event {
        return { target: { files: [file], value: 'C:\\fakepath\\x' } } as unknown as Event;
    }

    it('builds', () => {
        const fixture = configure(null);
        expect(fixture.componentInstance).toBeTruthy();
    });

    describe('isEditMode', () => {
        it('is true when the route carries a positive id (exam/5)', () => {
            const c = configure('5').componentInstance;
            expect(c.isEditMode).toBe(true);
            expect(c.id).toBe(5);
        });

        it('is false when there is no id param (exam)', () => {
            expect(configure(null).componentInstance.isEditMode).toBe(false);
        });
    });

    /** Issue #105: öğrenci yorum/soru anahtarı. */
    describe('commentsEnabled', () => {
        it('new form defaults to on and sends true', () => {
            const c = configure(null).componentInstance;
            expect(c.testForm.get('commentsEnabled')!.value).toBe(true);
            c.testForm.patchValue({ name: 'Yeni', gradeId: 1 });

            c.onSubmit();

            expect(vi.mocked(testService.create).mock.lastCall![0].commentsEnabled).toBe(true);
        });

        it('edit mode patches commentsEnabled=false from the worksheet and sends it back', () => {
            const c = configure('5', { commentsEnabled: false }).componentInstance;
            expect(c.testForm.get('commentsEnabled')!.value).toBe(false);

            c.onSubmit();

            expect(vi.mocked(testService.create).mock.lastCall![0].commentsEnabled).toBe(false);
        });

        it('edit mode without the field (older API) keeps it on', () => {
            const c = configure('5').componentInstance;
            expect(c.testForm.get('commentsEnabled')!.value).toBe(true);
        });
    });

    describe('onSubmit', () => {
        it('calls testService.create exactly once for a valid form (no double-save)', () => {
            const c = configure(null).componentInstance;
            c.testForm.patchValue({ name: 'Yeni Test', gradeId: 1 });

            c.onSubmit();

            expect(testService.create).toHaveBeenCalledTimes(1);
        });

        it('does not call testService.create for an invalid form', () => {
            const c = configure(null).componentInstance;
            c.testForm.patchValue({ name: '', gradeId: '' });

            c.onSubmit();

            expect(testService.create).not.toHaveBeenCalled();
            expect(snackBar.open).toHaveBeenCalled();
        });

        it('reloads the component instead of navigating in edit mode', () => {
            const c = configure('5').componentInstance;
            vi.spyOn(c, 'reloadComponent').mockImplementation(() => { });
            c.testForm.patchValue({ name: 'Düzenlenen', gradeId: 1 });

            c.onSubmit();

            expect(c.reloadComponent).toHaveBeenCalledWith(42);
            expect(router.navigate).not.toHaveBeenCalled();
        });

        it('navigates to /exam/:examId after creating a new test', () => {
            const c = configure(null).componentInstance;
            c.testForm.patchValue({ name: 'Yeni Test', gradeId: 1 });

            c.onSubmit();

            expect(router.navigate).toHaveBeenCalledWith(['/exam', 42]);
        });

        it('onSubmit_SuccessFalseResponse_ShowsServerMessageWithoutSuccessToastOrNavigation (#598)', () => {
            const c = configure(null).componentInstance;
            c.testForm.patchValue({ name: 'Yeni Test', gradeId: 1 });
            testService.create.mockReturnValue(of({ success: false, message: 'Kitap testi seçilmedi!', examId: 0 }));

            c.onSubmit();

            expect(router.navigate).not.toHaveBeenCalled();
            expect(snackBar.open).toHaveBeenCalledTimes(1);
            expect(snackBar.open.mock.lastCall![0]).toBe('Kitap testi seçilmedi!');
        });
    });

    describe('isMiniform', () => {
        it('isMiniform_ModeIsMiniform_ReturnsTrue', () => {
            const c = configure(null).componentInstance;
            c.mode = 'miniform';
            expect(c.isMiniform).toBe(true);
        });

        it('isMiniform_DefaultMode_ReturnsFalse', () => {
            expect(configure(null).componentInstance.isMiniform).toBe(false);
        });
    });

    describe('createAndContinue', () => {
        it('createAndContinue_ValidForm_CallsOnCreateAsyncWithoutSubtopicRequirement', () => {
            const c = configure(null).componentInstance;
            const spy = vi.spyOn(c, 'onCreateAsync').mockReturnValue(of({ message: 'ok', examId: 42 }));
            vi.spyOn(c, 'reloadComponent').mockImplementation(() => {
            });

            c.createAndContinue();

            expect(spy).toHaveBeenCalledTimes(1);

            expect(spy).toHaveBeenCalledWith(false);
        });

        it('createAndContinue_EmitsCreatedAndContinueWithExamId', () => {
            const c = configure(null).componentInstance;
            vi.spyOn(c, 'onCreateAsync').mockReturnValue(of({ message: 'ok', examId: 42 }));
            vi.spyOn(c, 'reloadComponent').mockImplementation(() => {
            });
            const emitted = vi.fn().mockName('createdAndContinue');
            c.createdAndContinue.subscribe(emitted);

            c.createAndContinue();

            expect(emitted).toHaveBeenCalledWith(42);
        });

        it('createAndContinue_AlreadyCreatingInline_DoesNotCallOnCreateAsyncAgain', () => {
            const c = configure(null).componentInstance;
            const spy = vi.spyOn(c, 'onCreateAsync').mockReturnValue(of({ message: 'ok', examId: 42 }));
            c.creatingInline = true;

            c.createAndContinue();

            expect(spy).not.toHaveBeenCalled();
        });
    });

    describe('onCreateAsync', () => {
        it('onCreateAsync_RequireSubtopicDefaultAndMissingSubtopic_ErrorsWithoutCallingCreate', () => {
            const c = configure(null).componentInstance;
            c.testForm.patchValue({ name: 'Yeni Test', gradeId: 1, subtopicId: null });

            let errored = false;
            c.onCreateAsync().subscribe({ error: () => (errored = true) });

            expect(errored).toBe(true);
            expect(testService.create).not.toHaveBeenCalled();
        });

        it('onCreateAsync_RequireSubtopicFalse_CallsCreateEvenWithoutSubtopic', () => {
            const c = configure(null).componentInstance;
            c.testForm.patchValue({ name: 'Yeni Test', gradeId: 1, subtopicId: null });

            c.onCreateAsync(false).subscribe();

            expect(testService.create).toHaveBeenCalledTimes(1);
        });

        it('onCreateAsync_SuccessFalseResponse_ErrorsWithServerMessage (#610)', () => {
            const c = configure(null).componentInstance;
            testService.create.mockReturnValue(of({ success: false, message: 'Kitap testi seçilmedi!', examId: 0 }));

            let message: string | undefined;
            c.onCreateAsync(false).subscribe({ error: (e) => (message = e.error?.message) });

            expect(message).toBe('Kitap testi seçilmedi!');
        });
    });

    describe('createAndContinue (#610)', () => {
        it('createAndContinue_SuccessFalseResponse_DoesNotEmitAndShowsMessage', () => {
            const c = configure(null).componentInstance;
            testService.create.mockReturnValue(of({ success: false, message: 'Kitap testi seçilmedi!', examId: 0 }));
            const snack = vi.spyOn(c.snackBar, 'open');
            const emitted = vi.fn();
            c.createdAndContinue.subscribe(emitted);

            c.createAndContinue();

            expect(emitted).not.toHaveBeenCalled();
            expect(c.creatingInline).toBe(false);
            expect(snack.mock.lastCall![0]).toBe('Kitap testi seçilmedi!');
        });
    });

    describe('onVisibilityChange', () => {
        /** Gerçek HTTP gibi asenkron hata: iyimser değer bir kez render edilir, sonra geri alınır. */
        function failAsync(status: number, error: unknown): void {
            testService.updateVisibility.mockReturnValue(timer(0).pipe(switchMap(() => throwError(() => new HttpErrorResponse({ status, error })))));
        }

        function child(fixture: ComponentFixture<TestCreateEnhancedComponent>): VisibilitySectionComponent {
            return fixture.debugElement.query(By.directive(VisibilitySectionComponent)).componentInstance;
        }

        function checkedRadioValue(fixture: ComponentFixture<TestCreateEnhancedComponent>): unknown {
            const radios = fixture.debugElement.queryAll(By.directive(MatRadioButton));
            return radios.find((r) => (r.componentInstance as MatRadioButton).checked)?.componentInstance.value;
        }

        /** Kullanıcı alt bileşende SchoolOnly'yi seçer → emit → parent PUT'u başlatır. */
        async function selectSchoolOnlyViaChild(fixture: ComponentFixture<TestCreateEnhancedComponent>): Promise<void> {
            child(fixture).onTeacherSharingChange(WorksheetTeacherSharing.SchoolOnly);
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(0);
            fixture.detectChanges();
        }

        it('shows the backend message on 400 and rolls the child selection back to Private (issue #191)', async () => {
            const fixture = configure('5');
            const c = fixture.componentInstance;
            snackBar.open.mockClear();
            const message = 'Okulsuz kullanıcı "Sadece okulum" seçemez.';
            failAsync(400, { success: false, message });

            await selectSchoolOnlyViaChild(fixture);

            expect(snackBar.open).toHaveBeenCalledWith(message, expect.any(String), expect.any(Object));
            expect(c.teacherSharing()).toBe(WorksheetTeacherSharing.Private);
            expect(c.isSavingVisibility()).toBe(false);
            // Geri alma alt bileşene input üzerinden akmalı: hem input hem seçili radio Private.
            expect(child(fixture).teacherSharing).toBe(WorksheetTeacherSharing.Private);
            expect(checkedRadioValue(fixture)).toBe(WorksheetTeacherSharing.Private);
        });

        it('falls back to the generic message when 400 carries no message', async () => {
            const fixture = configure('5');
            snackBar.open.mockClear();
            failAsync(400, { success: false });

            await selectSchoolOnlyViaChild(fixture);

            expect(snackBar.open).toHaveBeenCalledWith(testCreateTr.snackbar.visibilityFailed, expect.any(String), expect.any(Object));
            expect(child(fixture).teacherSharing).toBe(WorksheetTeacherSharing.Private);
        });

        it('shows the forbidden message on 403 and rolls back', async () => {
            const fixture = configure('5');
            snackBar.open.mockClear();
            failAsync(403, null);

            await selectSchoolOnlyViaChild(fixture);

            expect(snackBar.open).toHaveBeenCalledWith(testCreateTr.snackbar.visibilityForbidden, expect.any(String), expect.any(Object));
            expect(child(fixture).teacherSharing).toBe(WorksheetTeacherSharing.Private);
            expect(checkedRadioValue(fixture)).toBe(WorksheetTeacherSharing.Private);
        });

        it('keeps the new value when the PUT succeeds', () => {
            const fixture = configure('5');
            testService.updateVisibility.mockReturnValue(of({ teacherSharing: WorksheetTeacherSharing.SchoolOnly, studentVisibility: WorksheetStudentVisibility.Normal } as any));

            fixture.componentInstance.onVisibilityChange({
                teacherSharing: WorksheetTeacherSharing.SchoolOnly,
                studentVisibility: WorksheetStudentVisibility.Normal,
            });

            expect(testService.updateVisibility).toHaveBeenCalledWith(5, expect.objectContaining({ teacherSharing: 3 }));
            expect(fixture.componentInstance.teacherSharing()).toBe(WorksheetTeacherSharing.SchoolOnly);
        });
    });

    describe('onImageSelected', () => {
        it('rejects files larger than 2MB and does not upload', () => {
            const c = configure('5').componentInstance;
            snackBar.open.mockClear();
            const big = new File([new Uint8Array(2 * 1024 * 1024 + 16)], 'big.png', { type: 'image/png' });

            c.onImageSelected(fileEvent(big));

            expect(testService.updateWorksheetBackgroundImage).not.toHaveBeenCalled();
            expect(snackBar.open).toHaveBeenCalled();
        });

        it('rejects non-image files and does not upload', () => {
            const c = configure('5').componentInstance;
            snackBar.open.mockClear();
            const txt = new File(['hello'], 'a.txt', { type: 'text/plain' });

            c.onImageSelected(fileEvent(txt));

            expect(testService.updateWorksheetBackgroundImage).not.toHaveBeenCalled();
            expect(snackBar.open).toHaveBeenCalled();
        });

        it('uploads a valid image in edit mode', () => {
            const c = configure('5').componentInstance;
            const png = new File(['x'], 'a.png', { type: 'image/png' });

            c.onImageSelected(fileEvent(png));

            expect(testService.updateWorksheetBackgroundImage).toHaveBeenCalledTimes(1);
            expect(testService.updateWorksheetBackgroundImage).toHaveBeenCalledWith(5, expect.any(File));
        });

        it('does not upload in create mode (no worksheet id yet)', () => {
            const c = configure(null).componentInstance;
            const png = new File(['x'], 'a.png', { type: 'image/png' });

            c.onImageSelected(fileEvent(png));

            expect(testService.updateWorksheetBackgroundImage).not.toHaveBeenCalled();
        });
    });

    // Issue #544: Excel ile toplu test yükleme kaldırıldı; yeni test sayfasında yalnız tekil form kalır.
    describe('Excel toplu yükleme yok (#544)', () => {
        it('createMode_TestFormHasNoApplyToAllTriggers', () => {
            const fixture = configure(null);
            const icons: string[] = Array.from(fixture.nativeElement.querySelectorAll('app-test-form button mat-icon')).map((i) => ((i as HTMLElement).textContent || '').trim());

            expect(icons).not.toContain('done_all');
        });
    });

    // Issue #573: Tür (Test | Etkinlik sayfası).
    describe('kind (#573)', () => {
        const el = (fixture: ComponentFixture<TestCreateEnhancedComponent>) => fixture.nativeElement as HTMLElement;

        it('newForm_DefaultsToTest_SendsKind0', () => {
            const fixture = configure(null);
            const c = fixture.componentInstance;
            c.testForm.patchValue({ name: 'Yeni', gradeId: 1 });

            c.onSubmit();

            expect(vi.mocked(testService.create).mock.lastCall![0].kind).toBe(0);
            expect(el(fixture).querySelector('[data-testid="kind-toggle"]')).not.toBeNull();
        });

        it('activity_HidesDurationAndPractice_LabelsInstruction_SendsZeroDuration', () => {
            const fixture = configure(null);
            const c = fixture.componentInstance;
            c.testForm.patchValue({ name: 'Etkinlik', gradeId: 1, maxDurationMinutes: 25, isPracticeTest: true });

            (el(fixture).querySelector('[data-testid="kind-activity"] button') as HTMLButtonElement).click();
            fixture.detectChanges();

            expect(el(fixture).querySelector('input[formcontrolname="maxDurationMinutes"]')).toBeNull();
            expect(el(fixture).querySelector('label[for="tf-description"]')?.textContent?.trim()).toBe('Yönerge');
            expect(el(fixture).querySelector('h1')?.textContent).toContain('Etkinlik');
            c.onSubmit();
            const payload = vi.mocked(testService.create).mock.lastCall![0];
            expect(payload.kind).toBe(1);
            expect(payload.maxDurationSeconds).toBe(0);
            expect(payload.isPracticeTest).toBe(false);
        });

        it('queryKindActivity_PreselectsActivity', () => {
            const c = configure(null, {}, { kind: 'activity' }).componentInstance;

            expect(c.isActivity).toBe(true);
        });

        it('editSolvedWorksheet_KindToggleLockedWithHint', () => {
            const fixture = configure('5', { kind: 1, maxDurationSeconds: 0, instanceCount: 3 });
            fixture.detectChanges();

            expect(fixture.componentInstance.kindLocked()).toBe(true);
            expect(fixture.componentInstance.testForm.get('kind')!.disabled).toBe(true);
            expect(el(fixture).querySelector('[data-testid="kind-locked"]')).not.toBeNull();
            fixture.componentInstance.onSubmit();
            expect(vi.mocked(testService.create).mock.lastCall![0].kind).toBe(1);
        });

        it('save409KindLocked_RevertsKindLocksToggleAndShowsServerMessage', () => {
            const fixture = configure('5', { kind: 0, instanceCount: 0 });
            const c = fixture.componentInstance;
            testService.create.mockReturnValue(
                throwError(
                    () =>
                        new HttpErrorResponse({
                            status: 409,
                            error: { success: false, conflict: true, errorCode: 'WorksheetKindLocked', message: 'Tür kilitli.' },
                        })
                )
            );
            c.testForm.patchValue({ kind: 1 });

            c.onSubmit();
            fixture.detectChanges();

            expect(snackBar.open).toHaveBeenCalledWith('Tür kilitli.', expect.any(String), expect.any(Object));
            expect(c.kindLocked()).toBe(true);
            expect(c.testForm.getRawValue().kind).toBe(0);
            expect(c.testForm.get('maxDurationMinutes')!.enabled).toBe(true);
        });
    });
});
