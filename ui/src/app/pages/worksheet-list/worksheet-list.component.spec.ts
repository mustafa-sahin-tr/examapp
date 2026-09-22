import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';

import { WorksheetListComponent } from './worksheet-list.component';
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
  let testService: jasmine.SpyObj<TestService>;
  let authService: jasmine.SpyObj<AuthService>;

  function configure(isStudent = true): ComponentFixture<WorksheetListComponent> {
    testService = jasmine.createSpyObj<TestService>('TestService', [
      'listWorksheets',
      'getActiveAssignments',
      'delete',
      'copyWorksheet',
    ]);
    testService.listWorksheets.and.returnValue(
      of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 12 } as Paged<Test>)
    );
    testService.getActiveAssignments.and.returnValue(of([]));

    const subjectService = jasmine.createSpyObj<SubjectService>('SubjectService', ['loadCategories']);
    subjectService.loadCategories.and.returnValue(of([]));

    const gradesService = jasmine.createSpyObj<GradesService>('GradesService', ['getGrades']);
    gradesService.getGrades.and.returnValue(of([]));

    authService = jasmine.createSpyObj<AuthService>('AuthService', ['hasRole']);
    authService.hasRole.and.callFake((role: string) => (isStudent ? role === 'Student' : role === 'Teacher'));

    TestBed.configureTestingModule({
      imports: [WorksheetListComponent, translocoTesting],
      providers: [
        { provide: TestService, useValue: testService },
        { provide: SubjectService, useValue: subjectService },
        { provide: GradesService, useValue: gradesService },
        { provide: AuthService, useValue: authService },
        { provide: LocaleService, useValue: localeServiceStub },
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { data: {}, paramMap: convertToParamMap({}) },
            queryParams: of({}),
          },
        },
      ],
    });

    const created = TestBed.createComponent(WorksheetListComponent);
    created.detectChanges();
    return created;
  }

  beforeEach(() => {
    fixture = configure(true);
    component = fixture.componentInstance;
  });

  afterEach(() => TestBed.resetTestingModule());

  it('should create', () => {
    expect(component).toBeTruthy();
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
      expect(assigned.every((t) => t.isAssigned === true)).toBeTrue();
      expect(explore.every((t) => t.isAssigned !== true)).toBeTrue();
    });

    it('discoverAssignedTests_EmptyPagedList_ReturnsEmpty', () => {
      setPaged([]);

      expect(component.discoverAssignedTests()).toEqual([]);
      expect(component.discoverExploreTests()).toEqual([]);
    });
  });

  describe('compact cards (issue #188)', () => {
    let router: jasmine.SpyObj<Router>;

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
      router = TestBed.inject(Router) as jasmine.SpyObj<Router>;
      router.navigate.calls.reset();
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

      expect(router.navigate).toHaveBeenCalledOnceWith(['/test', 5]);
    });

    it('resumeProgressPercent_NoInstance_ReturnsNull', () => {
      expect(component.resumeProgressPercent({ id: 1, name: 'x' } as Test)).toBeNull();
    });

    it('resumeProgressPercent_InstanceWithoutTotal_ReturnsZero', () => {
      expect(component.resumeProgressPercent({ id: 1, name: 'x', instance: { status: 0 } } as Test)).toBe(0);
    });

    it('discoverAssigned_RendersCompactCardsWithDueDateOnly_ExploreKeepsRichCards', () => {
      component.assignments.set([
        { assignmentId: 1, worksheetId: 11, name: 'Atanan Sınav', endAt: '2026-03-12T10:00:00Z' } as AssignedWorksheet,
      ]);
      setPaged([assignedTest, exploreTest]);
      fixture.detectChanges();

      const assignedSection = section(0);
      const exploreSection = section(1);
      const cards = compactCards(assignedSection);

      expect(cards.length).toBe(1);
      expect(richCards(assignedSection).length).toBe(0);
      expect(cards[0].querySelector('.ctc__title')?.textContent?.trim()).toBe('Atanan Sınav');
      expect(cards[0].querySelector('.ctc__due')?.textContent?.trim()).toBe(
        `Bitiş: ${new Date('2026-03-12T10:00:00Z').toLocaleDateString('tr')}`
      );
      expect(cards[0].querySelector('.ctc__progress')).toBeNull();
      expect(compactCards(exploreSection).length).toBe(0);
      expect(richCards(exploreSection).length).toBe(1);
    });

    it('discoverAssigned_NoAssignmentRecord_RendersCompactCardWithoutDue', () => {
      setPaged([assignedTest]);
      fixture.detectChanges();

      const card = compactCards(section(0))[0];

      expect(card).toBeTruthy();
      expect(card.querySelector('.ctc__due')).toBeNull();
    });

    it('discoverAssigned_CardActivated_NavigatesToWorksheet', () => {
      setPaged([assignedTest]);
      fixture.detectChanges();

      compactCards(section(0))[0].click();

      expect(router.navigate).toHaveBeenCalledOnceWith(['/test', 11]);
    });

    it('discoverAssigned_ListViewMode_StillRendersCompactCards', () => {
      setPaged([assignedTest, exploreTest]);
      component.viewMode.set('list');
      fixture.detectChanges();

      const grid = section(0).querySelector('.wl__compact-grid') as HTMLElement;

      expect(grid.classList).toContain('wl__compact-grid--list');
      expect(compactCards(section(0)).length).toBe(1);
      expect(richCards(section(1)).length).toBe(1);
    });

    it('discoverAssigned_Empty_KeepsEmptyMessage', () => {
      setPaged([exploreTest]);
      fixture.detectChanges();

      expect(section(0).querySelector('.wl__empty--section p')?.textContent?.trim()).toBe(
        'Sana atanmış aktif bir sınav yok.'
      );
      expect(compactCards(section(0)).length).toBe(0);
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

  describe('onCopyWorksheet', () => {
    let router: jasmine.SpyObj<Router>;
    let snackBar: jasmine.SpyObj<MatSnackBar>;

    beforeEach(() => {
      router = TestBed.inject(Router) as jasmine.SpyObj<Router>;
      snackBar = TestBed.inject(MatSnackBar) as jasmine.SpyObj<MatSnackBar>;
      spyOn(snackBar, 'open');
    });

    it('onCopyWorksheet_Success_CallsServiceThenSnackBarAndNavigatesToNewWorksheet', () => {
      testService.copyWorksheet.and.returnValue(of({ worksheetId: 321 } as any));

      component.onCopyWorksheet(12);

      expect(testService.copyWorksheet).toHaveBeenCalledWith(12);
      expect(snackBar.open).toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledWith(['/exam', 321]);
    });

    it('onCopyWorksheet_Error_ShowsSnackBarAndDoesNotNavigate', () => {
      router.navigate.calls.reset();
      testService.copyWorksheet.and.returnValue(throwError(() => ({ error: { message: 'Kopyalama başarısız.' } })));

      component.onCopyWorksheet(9);

      expect(snackBar.open).toHaveBeenCalledWith('Kopyalama başarısız.', 'Tamam', jasmine.any(Object));
      expect(router.navigate).not.toHaveBeenCalled();
    });
  });
});
