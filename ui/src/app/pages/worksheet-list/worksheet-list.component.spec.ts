import type { MockedObject } from "vitest";
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';

import { WorksheetListComponent } from './worksheet-list.component';
import { StudentService } from '../../services/student.service';
import { MatDialog } from '@angular/material/dialog';
import { WorksheetAssignmentDialogData, WorksheetAssignmentDialogResult } from '../worksheet-detail/components/assignment-dialog/worksheet-assignment-dialog.component';
import { TestService } from '../../services/test.service';
import { SubjectService } from '../../services/subject.service';
import { GradesService } from '../../services/grades.service';
import { AuthService } from '../../services/auth.service';
import { LocaleService } from '../../services/locale.service';
import { AssignedWorksheet } from '../../models/assignment';
import { Paged, Test } from '../../models/test-instance';
import { localeDefinitionOf } from '../../models/locale';

import { TranslocoTestingModule } from '@jsverse/transloco';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import rootTr from '../../../../public/i18n/tr.json';
import worksheetListTr from '../../../../public/i18n/worksheet-list/tr.json';

/** Gercek scope sozlugu yuklenir; anahtar bozulursa test kirilir (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
    // Scope sozlugu hem scope yolu (provideTranslocoScope yukleyicisi) hem de kok 'tr' icine
    // gomulu olarak verilir; sablondaki 'prefix' bicimi ikincisinden cozulur.
    langs: { tr: { ...rootTr, 'worksheet-list': worksheetListTr }, 'worksheet-list/tr': worksheetListTr },
    translocoConfig: {
        availableLangs: [...SUPPORTED_LOCALE_CODES],
        defaultLang: DEFAULT_LOCALE,
        // TranslocoTestingModule uygulamanin config'ini almaz; scope oneki kebab kalsin (issue #183).
        scopes: { keepCasing: true },
    },
    preloadLangs: true,
});

/** Tarih biçimi tarayıcı diline bağlı kalmasın: aktif dil sabit 'tr' (issue #188 kompakt kart bitiş etiketi). */
const localeServiceStub = {
    locale: signal('tr' as const).asReadonly(),
    localeDefinition: signal(localeDefinitionOf('tr')).asReadonly(),
};

describe('WorksheetListComponent', () => {
    let component: WorksheetListComponent;
    let fixture: ComponentFixture<WorksheetListComponent>;
    let testService: MockedObject<TestService>;
    let authService: MockedObject<AuthService>;

    function configure(
        isStudent = true,
        queryParams: Record<string, string> = {},
        assignments: AssignedWorksheet[] = []
    ): ComponentFixture<WorksheetListComponent> {
        testService = {
            listWorksheets: vi.fn().mockName("TestService.listWorksheets"),
            getActiveAssignments: vi.fn().mockName("TestService.getActiveAssignments"),
            delete: vi.fn().mockName("TestService.delete"),
            copyWorksheet: vi.fn().mockName("TestService.copyWorksheet"),
            assignWorksheet: vi.fn().mockName("TestService.assignWorksheet")
        } as unknown as MockedObject<TestService>;
        testService.listWorksheets.mockReturnValue(of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 12 } as Paged<Test>));
        testService.getActiveAssignments.mockReturnValue(of(assignments));

        const subjectService = {
            loadCategories: vi.fn().mockName("SubjectService.loadCategories")
        };
        subjectService.loadCategories.mockReturnValue(of([]));

        const gradesService = {
            getGrades: vi.fn().mockName("GradesService.getGrades")
        };
        gradesService.getGrades.mockReturnValue(of([]));

        authService = {
            user: signal(null),
            hasRole: vi.fn().mockName("AuthService.hasRole")
        } as unknown as MockedObject<AuthService>;
        authService.hasRole.mockImplementation((role: string) => (isStudent ? role === 'Student' : role === 'Teacher'));

        TestBed.configureTestingModule({
            imports: [WorksheetListComponent, translocoTesting],
            providers: [
                { provide: TestService, useValue: testService },
                { provide: SubjectService, useValue: subjectService },
                { provide: GradesService, useValue: gradesService },
                { provide: AuthService, useValue: authService },
                { provide: StudentService, useValue: { getLookup: vi.fn().mockReturnValue(of([{ id: 1, fullName: 'Ali' }])) } },
                { provide: LocaleService, useValue: localeServiceStub },
                // Gerçek router: kartlardaki routerLink'ler Angular 20'den beri ngOnChanges'ta createUrlTree çağırıyor (#545).
                provideRouter([]),
                {
                    provide: ActivatedRoute,
                    useValue: {
                        snapshot: { data: {}, paramMap: convertToParamMap({}) },
                        queryParams: of(queryParams),
                    },
                },
            ],
        });

        vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        const created = TestBed.createComponent(WorksheetListComponent);
        created.detectChanges();
        return created;
    }

    beforeEach(() => {
        fixture = configure(true);
        component = fixture.componentInstance;
    });

    afterEach(() => TestBed.resetTestingModule());

    // Issue #712: filtre yokken "kriterlere uygun değil" denmez, sonraki adım önerilir.
    describe('empty states (issue #712)', () => {
        function empty(): HTMLElement | null {
            fixture.detectChanges();
            return (fixture.nativeElement as HTMLElement).querySelector('.wl__empty');
        }

        it('student_NoFilter_OffersPractice', () => {
            expect(empty()?.textContent).toContain('Şu an gösterilecek test yok');
            expect(empty()?.querySelector('a')?.getAttribute('href')).toBe('/practice');
        });

        it('activeFilter_ShowsFilterMessageAndClear', () => {
            component.selectedSubjectIds.set([1]);
            expect(empty()?.textContent).toContain('Bu kriterlere uygun test yok');
            expect(empty()?.querySelector('.wl__btn--ghost')).not.toBeNull();
        });

        it('teacher_NoFilter_OffersCreate', () => {
            TestBed.resetTestingModule();
            fixture = configure(false);
            component = fixture.componentInstance;
            expect(empty()?.textContent).toContain('Henüz test oluşturmadın');
            expect(empty()?.querySelector('.wl__btn--primary')).not.toBeNull();
        });
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    // #557: atanmış testler yalnız öğrenciye yüklenir; öğretmende student-only uç çağrılmaz (403 olurdu).
    it('loads active assignments once for a student', () => {
        expect(testService.getActiveAssignments).toHaveBeenCalledTimes(1);
    });

    it('does not request active assignments for a teacher', () => {
        TestBed.resetTestingModule();
        fixture = configure(false);
        expect(testService.getActiveAssignments).not.toHaveBeenCalled();
    });

    describe('discoverAssignedTests / discoverExploreTests', () => {
        function setPaged(items: Partial<Test>[]): void {
            component.paged.set({
                items: items as Test[],
                totalCount: items.length,
                pageNumber: 1,
                pageSize: 12,
            });
        }

        it('discoverAssignedTests_OnlyIncludesItemsMarkedIsAssignedTrue', () => {
            setPaged([
                { id: 1, name: 'Atanan', isAssigned: true },
                { id: 2, name: 'Kesfet', isAssigned: false },
            ]);

            expect(component.discoverAssignedTests().map((t) => t.id)).toEqual([1]);
        });

        it('discoverExploreTests_OnlyIncludesItemsNotMarkedIsAssignedTrue', () => {
            setPaged([
                { id: 1, name: 'Atanan', isAssigned: true },
                { id: 2, name: 'Kesfet', isAssigned: false },
            ]);

            expect(component.discoverExploreTests().map((t) => t.id)).toEqual([2]);
        });

        it('discoverExploreTests_TreatsUndefinedIsAssignedAsExplore', () => {
            // Backend her satırda isAssigned döner ama savunma amaçlı: alan eksikse "keşfet" sayılmalı.
            setPaged([{ id: 3, name: 'EskiKayit' }]);

            expect(component.discoverExploreTests().map((t) => t.id)).toEqual([3]);
            expect(component.discoverAssignedTests()).toEqual([]);
        });

        it('discoverAssignedTests_and_discoverExploreTests_partition_all_items_without_overlap_or_loss', () => {
            setPaged([
                { id: 1, isAssigned: true },
                { id: 2, isAssigned: false },
                { id: 3, isAssigned: true },
                { id: 4 },
            ]);

            const assigned = component.discoverAssignedTests();
            const explore = component.discoverExploreTests();

            expect(assigned.length + explore.length).toBe(4);
            expect(assigned.every((t) => t.isAssigned === true)).toBe(true);
            expect(explore.every((t) => t.isAssigned !== true)).toBe(true);
        });

        it('discoverAssignedTests_EmptyPagedList_ReturnsEmpty', () => {
            setPaged([]);

            expect(component.discoverAssignedTests()).toEqual([]);
            expect(component.discoverExploreTests()).toEqual([]);
        });
    });

    describe('compact cards (issue #188)', () => {
        let router: MockedObject<Router>;

        const assignedTest: Partial<Test> = { id: 11, name: 'Atanan Sınav', isAssigned: true, imageUrl: 'a.png' };
        const exploreTest: Partial<Test> = { id: 22, name: 'Keşfet Sınavı', isAssigned: false };

        function setPaged(items: Partial<Test>[]): void {
            component.paged.set({ items: items as Test[], totalCount: items.length, pageNumber: 1, pageSize: 12 });
        }

        function compactCards(root: HTMLElement = fixture.nativeElement): HTMLElement[] {
            return Array.from(root.querySelectorAll<HTMLElement>('app-compact-test-card'));
        }

        function richCards(root: HTMLElement = fixture.nativeElement): HTMLElement[] {
            return Array.from(root.querySelectorAll<HTMLElement>('app-worksheet-list-view-card'));
        }

        function section(index: number): HTMLElement {
            const root = fixture.nativeElement as HTMLElement;
            return root.querySelectorAll<HTMLElement>('.wl__section')[index];
        }

        beforeEach(() => {
            router = TestBed.inject(Router) as MockedObject<Router>;
            router.navigate.mockClear();
        });

        it('resumeStrip_InProgressTests_RendersCompactCardsWithProgressBadge', () => {
            component.inProgressTests.set([
                {
                    id: 5,
                    name: 'Devam Eden',
                    questionCount: 10,
                    instance: { correctAnswers: 2, wrongAnswers: 2, totalQuestions: 10, status: 0 },
                } as Test,
            ]);
            fixture.detectChanges();

            const strip = fixture.nativeElement.querySelector('.wl__strip') as HTMLElement;
            const cards = compactCards(strip);

            expect(cards.length).toBe(1);
            expect(cards[0].querySelector('.ctc__title')?.textContent?.trim()).toBe('Devam Eden');
            expect(cards[0].querySelector('.ctc__progress')?.textContent?.trim()).toBe('%40');
            expect(strip.querySelector('.wl__strip-card')).toBeNull();
        });

        it('resumeStrip_CardActivated_NavigatesToWorksheet', () => {
            component.inProgressTests.set([{ id: 5, name: 'Devam Eden', instance: { status: 0 } } as Test]);
            fixture.detectChanges();

            compactCards(fixture.nativeElement.querySelector('.wl__strip'))[0].click();

            expect(router.navigate).toHaveBeenCalledTimes(1);

            expect(router.navigate).toHaveBeenCalledWith(['/test', 5]);
        });

        it('resumeProgressPercent_NoInstance_ReturnsNull', () => {
            expect(component.resumeProgressPercent({ id: 1, name: 'x' } as Test)).toBeNull();
        });

        it('resumeProgressPercent_InstanceWithoutTotal_ReturnsZero', () => {
            expect(component.resumeProgressPercent({ id: 1, name: 'x', instance: { status: 0 } } as Test)).toBe(0);
        });

        it('discover_ShowsOnlyExploreTests_AssignedLiveInTheirOwnTab_issue834', () => {
            setPaged([assignedTest, exploreTest]);
            fixture.detectChanges();

            const sections = (fixture.nativeElement as HTMLElement).querySelectorAll('.wl__section');
            expect(sections.length).toBe(1);
            expect(compactCards(section(0)).length).toBe(0);
            expect(richCards(section(0)).map((c) => c.textContent)).toEqual([expect.stringContaining('Keşfet Sınavı')]);
            expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Atanan Sınav');
        });

        it('otherTabs_RenderRichCardsNotCompact', () => {
            // setTab bucket'ları yeniden yükler (spy boş döner); liste bu yüzden sekme değişiminden sonra set edilir.
            component.setTab('inprogress');
            component.inProgressTests.set([{ id: 7, name: 'Devam', instance: { status: 0 } } as Test]);
            fixture.detectChanges();

            expect(fixture.nativeElement.querySelector('.wl__strip')).toBeNull();
            expect(compactCards().length).toBe(0);
            expect(richCards().length).toBe(1);
        });
    });

    /** Issue #377: "Ata" butonları test detayındaki atama diyaloğuna gider; Excel içe aktarma gizli. */
    describe('teacher assign actions (issue #377)', () => {
        let router: MockedObject<Router>;

        beforeEach(() => {
            TestBed.resetTestingModule();
            fixture = configure(false);
            component = fixture.componentInstance;
            router = TestBed.inject(Router) as MockedObject<Router>;
        });

        const bulkAssignButton = () => fixture.nativeElement.querySelector('.wl__bulkbar .wl__bulk-assign') as HTMLButtonElement | null;
        const bulkHint = () => fixture.nativeElement.querySelector('.wl__bulk-hint') as HTMLElement | null;

        /** Liste görünümüne geçer (fetch boş liste döner) ve satırları doldurur; 5 numaralı test atanamaz. */
        function listWithTests(): void {
            component.setViewMode('list');
            const items = [
                { id: 3, name: 'A', canEdit: true, canAssign: true },
                { id: 4, name: 'B', canEdit: true, canAssign: true },
                { id: 5, name: 'C', canEdit: true, canAssign: false },
            ] as Test[];
            component.paged.set({ items, totalCount: items.length, pageNumber: 1, pageSize: 12 });
        }

        it('onAssign_TestNotInTheList_NavigatesToDetailWithAssignStudentQuery', () => {
            component.onAssign(7);

            expect(router.navigate).toHaveBeenCalledWith(['/test', 7], { queryParams: { assign: 'student' } });
        });

        it('onAssign_AssignableTest_OpensTheDialogInPlace_CancelStaysOnTheList_issue918', () => {
            listWithTests();
            const open = vi.spyOn(fixture.debugElement.injector.get(MatDialog), 'open')
                .mockReturnValue({ afterClosed: () => of(undefined) } as never);

            component.onAssign(3);

            expect(router.navigate).not.toHaveBeenCalled();
            const data = open.mock.lastCall![1]!.data as WorksheetAssignmentDialogData;
            expect(data.worksheetId).toBe(3);
            expect(data.worksheetName).toBe('A');
            expect(data.studentsStatus!()).toBe('loaded');
            expect(testService.assignWorksheet).not.toHaveBeenCalled();
        });

        it('onAssign_DialogConfirmed_SendsTheAssignment_issue918', () => {
            listWithTests();
            const request = { worksheetId: 3 } as WorksheetAssignmentDialogResult['request'];
            vi.spyOn(fixture.debugElement.injector.get(MatDialog), 'open')
                .mockReturnValue({ afterClosed: () => of({ request }) } as never);
            testService.assignWorksheet.mockReturnValue(of({ success: true }) as never);

            component.onAssign(3);

            expect(testService.assignWorksheet).toHaveBeenCalledWith(request);
        });

        it('onAssign_NotAssignableTest_GoesToDetailForThePermissionFlow_issue918', () => {
            listWithTests();
            component.onAssign(5);

            expect(router.navigate).toHaveBeenCalledWith(['/test', 5], { queryParams: { assign: 'student' } });
        });

        it('onAssign_NullId_DoesNotNavigate', () => {
            component.onAssign(null);

            expect(router.navigate).not.toHaveBeenCalled();
        });

        it('teacherHeader_DoesNotRenderExcelImportButton', () => {
            const header = fixture.nativeElement.querySelector('.wl__header-actions') as HTMLElement;

            expect(header).not.toBeNull();
            expect(header.querySelectorAll('button').length).toBe(1);
            expect(header.textContent).not.toContain('Excel');
        });

        it('bulkAssign_SingleSelection_EnabledAndOpensTheDialogForSelectedWorksheet', () => {
            listWithTests();
            component.toggleRowSelection(3);
            fixture.detectChanges();

            expect(component.canBulkAssign()).toBe(true);
            expect(bulkAssignButton()!.disabled).toBe(false);
            expect(bulkHint()).toBeNull();

            const open = vi.spyOn(fixture.debugElement.injector.get(MatDialog), 'open')
                .mockReturnValue({ afterClosed: () => of(undefined) } as never);
            bulkAssignButton()!.click();

            expect((open.mock.lastCall![1]!.data as WorksheetAssignmentDialogData).worksheetId).toBe(3);
        });

        it('bulkAssign_MultipleSelection_DisabledWithSingleOnlyHintAndNoNavigation', () => {
            listWithTests();
            component.toggleRowSelection(3);
            component.toggleRowSelection(4);
            fixture.detectChanges();

            expect(component.canBulkAssign()).toBe(false);
            expect(bulkAssignButton()!.disabled).toBe(true);
            expect(bulkHint()!.textContent).toContain(worksheetListTr.bulk.assignSingleOnly);

            component.onBulkAssign();

            expect(router.navigate).not.toHaveBeenCalled();
        });

        it('bulkAssign_SingleSelectionWithoutAssignPermission_DisabledWithNotAllowedHint', () => {
            listWithTests();
            component.toggleRowSelection(5);
            fixture.detectChanges();

            expect(component.canBulkAssign()).toBe(false);
            expect(bulkAssignButton()!.disabled).toBe(true);
            expect(bulkHint()!.textContent).toContain(worksheetListTr.bulk.assignNotAllowed);

            component.onBulkAssign();

            expect(router.navigate).not.toHaveBeenCalled();
        });
    });

    describe('onCopyWorksheet', () => {
        let router: MockedObject<Router>;
        let snackBar: MockedObject<MatSnackBar>;

        beforeEach(() => {
            router = TestBed.inject(Router) as MockedObject<Router>;
            snackBar = TestBed.inject(MatSnackBar) as MockedObject<MatSnackBar>;
            vi.spyOn(snackBar, 'open').mockReturnValue(undefined as never);
        });

        it('onCopyWorksheet_Success_CallsServiceThenSnackBarAndNavigatesToNewWorksheet', () => {
            testService.copyWorksheet.mockReturnValue(of({ worksheetId: 321 } as any));

            component.onCopyWorksheet(12);

            expect(testService.copyWorksheet).toHaveBeenCalledWith(12);
            expect(snackBar.open).toHaveBeenCalled();
            expect(router.navigate).toHaveBeenCalledWith(['/exam', 321]);
        });

        it('onCopyWorksheet_Error_ShowsSnackBarAndDoesNotNavigate', () => {
            router.navigate.mockClear();
            testService.copyWorksheet.mockReturnValue(throwError(() => ({ error: { message: 'Kopyalama başarısız.' } })));

            component.onCopyWorksheet(9);

            expect(snackBar.open).toHaveBeenCalledWith('Kopyalama başarısız.', 'Tamam', expect.any(Object));
            expect(router.navigate).not.toHaveBeenCalled();
        });
    });

    // Issue #573: Testler | Etkinlikler üst sekmeleri ve tür filtresi.
    describe('kind tabs (#573)', () => {
        const el = () => fixture.nativeElement as HTMLElement;
        const listCalls = () => testService.listWorksheets.mock.calls.map((c) => c[0]);

        it('testsTab_StudentBucketsRequestOnlyTests', () => {
            expect(listCalls().length).toBeGreaterThan(0);
            expect(listCalls().every((f) => f.kind === 0)).toBe(true);
            expect(el().querySelector('app-activity-list')).toBeNull();
        });

        it('assignments_ActivitiesExcludedFromTestTabsAndCountedInTabLabel', () => {
            TestBed.resetTestingModule();
            fixture = configure(true, {}, [
                { worksheetId: 1, name: 'Test', kind: 0 } as AssignedWorksheet,
                { worksheetId: 2, name: 'Etkinlik', kind: 1, isCompleted: false } as AssignedWorksheet,
                { worksheetId: 3, name: 'Bitti', kind: 1, isCompleted: true } as AssignedWorksheet,
            ]);
            component = fixture.componentInstance;
            fixture.detectChanges();

            expect(component.assignments().map((a) => a.worksheetId)).toEqual([1]);
            expect(el().querySelector('[data-testid="kind-tab-activities"]')?.textContent?.trim()).toBe('Etkinlikler (1)');
            expect(testService.getActiveAssignments).toHaveBeenCalledTimes(1);
        });

        it('studentTabs_OrderStartsWithAssigned_issue834', () => {
            const labels = Array.from(el().querySelectorAll('.wl__tabs button')).map((b) => b.getAttribute('data-testid'));
            expect(labels).toEqual(['tab-assigned', 'tab-inprogress', 'tab-discover', 'tab-completed']);
            expect(el().querySelector('nav[mat-tab-nav-bar]')).toBeNull();
        });

        it('openAssignment_OpensAssignedTabByDefault_issue834', () => {
            TestBed.resetTestingModule();
            fixture = configure(true, {}, [{ worksheetId: 1, name: 'Test', kind: 0, isCompleted: false } as AssignedWorksheet]);
            component = fixture.componentInstance;
            fixture.detectChanges();

            expect(component.tab()).toBe('assigned');
            expect(el().querySelector('[data-testid="tab-assigned"]')?.classList).toContain('is-active');
        });

        it('noOpenAssignment_StaysOnDiscover_issue834', () => {
            TestBed.resetTestingModule();
            fixture = configure(true, {}, [{ worksheetId: 1, name: 'Bitti', kind: 0, isCompleted: true } as AssignedWorksheet]);
            component = fixture.componentInstance;
            fixture.detectChanges();

            expect(component.tab()).toBe('discover');
        });

        it('studentActivitiesTab_RendersActivityListWithKindFilters', () => {
            TestBed.resetTestingModule();
            fixture = configure(true, { type: 'activities' });
            component = fixture.componentInstance;
            fixture.detectChanges();

            expect(component.pageKind()).toBe('activities');
            expect(el().querySelector('app-activity-list')).not.toBeNull();
            // #834: öğrencide aynı tek sekme sırası etkinlikte de durur; Test | Etkinlik yanındaki seçici.
            expect(el().querySelector('.wl__tabs')).not.toBeNull();
            expect(el().querySelector('[data-testid="kind-tab-activities"]')?.getAttribute('aria-pressed')).toBe('true');
            expect(testService.getActiveAssignments).toHaveBeenCalledWith(1);
            expect(listCalls().some((f) => f.kind === 1 && f.statuses?.[0] === 0)).toBe(true);
            expect(listCalls().some((f) => f.kind === 1 && f.statuses?.[0] === 1)).toBe(true);
        });

        it('teacherActivitiesTab_DiscoverFetchUsesKindActivityAndCreateOpensActivityForm', () => {
            TestBed.resetTestingModule();
            fixture = configure(false, { type: 'activities' });
            component = fixture.componentInstance;
            const router = TestBed.inject(Router) as unknown as { navigate: ReturnType<typeof vi.fn> };
            testService.listWorksheets.mockClear();

            component.setSort('popular');
            fixture.detectChanges();

            expect(listCalls()[0].kind).toBe(1);
            expect(el().querySelector('.wl__header-actions')?.textContent).toContain('Yeni Etkinlik');
            component.onCreate();
            expect(router.navigate).toHaveBeenCalledWith(['/exam'], { queryParams: { kind: 'activity' } });
        });

        it('setPageKind_WritesTypeQueryParam', () => {
            const router = TestBed.inject(Router) as unknown as { navigate: ReturnType<typeof vi.fn> };

            component.setPageKind('activities');

            expect(router.navigate).toHaveBeenCalledWith(
                [],
                expect.objectContaining({ queryParams: { type: 'activities' }, queryParamsHandling: 'merge' })
            );
        });
    });
});
