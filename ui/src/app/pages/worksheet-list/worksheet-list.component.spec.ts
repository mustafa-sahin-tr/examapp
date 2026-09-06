import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';

import { WorksheetListComponent } from './worksheet-list.component';
import { TestService } from '../../services/test.service';
import { SubjectService } from '../../services/subject.service';
import { GradesService } from '../../services/grades.service';
import { AuthService } from '../../services/auth.service';
import { Paged, Test } from '../../models/test-instance';

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
      imports: [WorksheetListComponent],
      providers: [
        { provide: TestService, useValue: testService },
        { provide: SubjectService, useValue: subjectService },
        { provide: GradesService, useValue: gradesService },
        { provide: AuthService, useValue: authService },
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
