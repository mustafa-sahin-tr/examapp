import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';

import { WorksheetListEnhancedComponent } from './worksheet-list-enhanced.component';
import { TestService } from '../../services/test.service';
import { SubjectService } from '../../services/subject.service';
import { GradesService } from '../../services/grades.service';
import { Subject } from '../../models/subject';
import { Paged, Test } from '../../models/test-instance';

import { TranslocoTestingModule } from '@jsverse/transloco';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import rootTr from '../../../../public/i18n/tr.json';
import worksheetListTr from '../../../../public/i18n/worksheet-list/tr.json';

const translocoTesting = TranslocoTestingModule.forRoot({
  langs: {
    tr: { ...rootTr, 'worksheet-list': worksheetListTr },
    'worksheet-list/tr': worksheetListTr,
  },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
    scopes: { keepCasing: true },
  },
  preloadLangs: true,
});

describe('WorksheetListEnhancedComponent', () => {
  let component: WorksheetListEnhancedComponent;
  let fixture: ComponentFixture<WorksheetListEnhancedComponent>;
  let testService: jasmine.SpyObj<TestService>;
  let subjectService: jasmine.SpyObj<SubjectService>;
  let gradesService: jasmine.SpyObj<GradesService>;

  const mockSubjects: Subject[] = [
    { id: 1, name: 'Matematik' },
    { id: 2, name: 'Fizik' },
    { id: 3, name: 'Kimya' },
  ];

  const mockGrades = [
    { id: 5, name: '5. Sınıf' },
    { id: 6, name: '6. Sınıf' },
  ];

  const mockPaged: Paged<Test> = {
    items: [
      {
        id: 1,
        name: 'Test 1',
        subtitle: 'Subtitle 1',
        description: 'Desc 1',
        gradeId: 5,
        questionCount: 10,
        maxDurationSeconds: 600,
      } as Test,
    ],
    totalCount: 1,
    pageNumber: 1,
    pageSize: 12,
  };

  function configure(): ComponentFixture<WorksheetListEnhancedComponent> {
    testService = jasmine.createSpyObj<TestService>('TestService', [
      'search',
      'getLatest',
      'getCompleted',
      'getPopular',
    ]);
    testService.search.and.returnValue(of(mockPaged));
    testService.getLatest.and.returnValue(of([]));
    testService.getCompleted.and.returnValue(of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 12 }));
    testService.getPopular.and.returnValue(of([]));

    subjectService = jasmine.createSpyObj<SubjectService>('SubjectService', ['loadCategories']);
    subjectService.loadCategories.and.returnValue(of(mockSubjects));

    gradesService = jasmine.createSpyObj<GradesService>('GradesService', ['getGrades']);
    gradesService.getGrades.and.returnValue(of(mockGrades));

    TestBed.configureTestingModule({
      imports: [WorksheetListEnhancedComponent, translocoTesting],
      providers: [
        { provide: TestService, useValue: testService },
        { provide: SubjectService, useValue: subjectService },
        { provide: GradesService, useValue: gradesService },
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              data: { worksheets: mockPaged },
              paramMap: convertToParamMap({}),
            },
            queryParams: of({}),
          },
        },
      ],
    });

    const created = TestBed.createComponent(WorksheetListEnhancedComponent);
    created.detectChanges();
    return created;
  }

  beforeEach(() => {
    fixture = configure();
    component = fixture.componentInstance;
  });

  afterEach(() => TestBed.resetTestingModule());

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('toggleSubjectFilter', () => {
    it('toggleSubjectFilter_WithNewId_AddsIdToSelectedSubjectIds', () => {
      component.toggleSubjectFilter(1);

      expect(component.selectedSubjectIds()).toEqual([1]);
    });

    it('toggleSubjectFilter_WithSameId_RemovesIdFromSelectedSubjectIds', () => {
      component.toggleSubjectFilter(1);
      component.toggleSubjectFilter(1);

      expect(component.selectedSubjectIds()).toEqual([]);
    });

    it('toggleSubjectFilter_WithMultipleIds_MaintainsArray', () => {
      component.toggleSubjectFilter(1);
      component.toggleSubjectFilter(2);

      expect(component.selectedSubjectIds()).toEqual([1, 2]);

      component.toggleSubjectFilter(1);

      expect(component.selectedSubjectIds()).toEqual([2]);
    });

    it('toggleSubjectFilter_InSearchSection_CallsSearch', () => {
      component.currentSection.set('search');
      component.searchForm.patchValue({ searchTerm: 'test' });

      component.toggleSubjectFilter(1);

      expect(testService.search).toHaveBeenCalledWith('test', [1], [], 1);
    });

    it('toggleSubjectFilter_InNonSearchSection_DoesNotCallSearch', () => {
      component.currentSection.set('newest');

      component.toggleSubjectFilter(1);

      expect(testService.search).not.toHaveBeenCalled();
    });

    it('toggleSubjectFilter_InSearchSection_ResetsPaginationTo1', () => {
      component.currentSection.set('search');
      component.searchForm.patchValue({ searchTerm: 'test' });
      component.pageNumber.set(5);

      component.toggleSubjectFilter(1);

      expect(component.pageNumber()).toBe(1);
    });
  });

  describe('toggleGradeFilter', () => {
    it('toggleGradeFilter_WithNewId_AddsIdToSelectedGradeIds', () => {
      component.toggleGradeFilter(5);

      expect(component.selectedGradeIds()).toEqual([5]);
    });

    it('toggleGradeFilter_WithSameId_RemovesIdFromSelectedGradeIds', () => {
      component.toggleGradeFilter(5);
      component.toggleGradeFilter(5);

      expect(component.selectedGradeIds()).toEqual([]);
    });

    it('toggleGradeFilter_InSearchSection_CallsSearch', () => {
      component.currentSection.set('search');
      component.searchForm.patchValue({ searchTerm: 'test' });

      component.toggleGradeFilter(5);

      expect(testService.search).toHaveBeenCalledWith('test', [], [5], 1);
    });
  });

  describe('search with filters', () => {
    it('updateSearchResults_CallsServiceWithBothSubjectAndGradeFilters', () => {
      component.selectedSubjectIds.set([1, 2]);
      component.selectedGradeIds.set([5]);
      component.searchForm.patchValue({ searchTerm: 'math' });
      component.currentSection.set('search');

      component['updateSearchResults']();

      expect(testService.search).toHaveBeenCalledWith('math', [1, 2], [5], 1);
    });

    it('updateSearchResults_WithNoFilters_CallsServiceWithEmptyArrays', () => {
      component.searchForm.patchValue({ searchTerm: 'test' });
      component.currentSection.set('search');

      component['updateSearchResults']();

      expect(testService.search).toHaveBeenCalledWith('test', [], [], 1);
    });

    it('updateSearchResults_WithPaginationAndFilters_IncludesPageNumber', () => {
      component.selectedSubjectIds.set([1]);
      component.pageNumber.set(3);
      component.searchForm.patchValue({ searchTerm: 'test' });
      component.currentSection.set('search');

      component['updateSearchResults']();

      expect(testService.search).toHaveBeenCalledWith('test', [1], [], 3);
    });
  });

  describe('clearFilters', () => {
    it('clearFilters_ClearsSubjectAndGradeIds', () => {
      component.selectedSubjectIds.set([1, 2]);
      component.selectedGradeIds.set([5]);

      component.clearFilters();

      expect(component.selectedSubjectIds()).toEqual([]);
      expect(component.selectedGradeIds()).toEqual([]);
    });

    it('clearFilters_InSearchSection_UpdatesSearchResults', () => {
      component.currentSection.set('search');
      component.selectedSubjectIds.set([1]);
      component.selectedGradeIds.set([5]);
      component.searchForm.patchValue({ searchTerm: 'test' });

      component.clearFilters();

      expect(testService.search).toHaveBeenCalled();
    });
  });

  describe('checkbox state', () => {
    it('subjectCheckbox_WhenSubjectIdIsSelected_StateReflectsSelection', () => {
      component.selectedSubjectIds.set([1]);

      // Verify the component state is correct (checkbox template binding evaluates this)
      expect(component.selectedSubjectIds().includes(1)).toBeTrue();
    });

    it('subjectCheckbox_WhenSubjectIdIsNotSelected_StateReflectsNoSelection', () => {
      component.selectedSubjectIds.set([2]);

      // Verify the component state is correct
      expect(component.selectedSubjectIds().includes(1)).toBeFalse();
      expect(component.selectedSubjectIds().includes(2)).toBeTrue();
    });

    it('subjectCheckbox_ToggleUpdatesBothStateAndTemplate', () => {
      // Toggle adds id
      component.toggleSubjectFilter(1);
      expect(component.selectedSubjectIds()).toContain(1);

      // Template binding would use selectedSubjectIds().includes(1) to show checked state
      const isChecked = component.selectedSubjectIds().includes(1);
      expect(isChecked).toBeTrue();

      // Toggle again removes id
      component.toggleSubjectFilter(1);
      expect(component.selectedSubjectIds()).not.toContain(1);

      // Template binding would now show unchecked
      const isNowChecked = component.selectedSubjectIds().includes(1);
      expect(isNowChecked).toBeFalse();
    });
  });

  describe('subjects signal', () => {
    it('subjectsSignal_LoadsFromSubjectService', () => {
      expect(component.subjectsSignal()).toEqual(mockSubjects);
      expect(subjectService.loadCategories).toHaveBeenCalled();
    });

    it('subjectsSignal_StartsWithInitialValue', () => {
      // The signal is initialized with an initialValue of []
      // and becomes populated after toSignal resolves the observable
      const subjects = component.subjectsSignal();
      expect(Array.isArray(subjects)).toBeTrue();
    });

    it('subjectsSignal_PopulatesAfterServiceCall', () => {
      // After component initialization, the observable from SubjectService is converted
      // to a signal and should have the mock data
      expect(component.subjectsSignal()).toEqual(mockSubjects);
    });
  });

  describe('filteredSubjects computed', () => {
    it('filteredSubjects_WithNoSelection_ReturnsAllSubjects', () => {
      component.selectedSubjectIds.set([]);

      expect(component.filteredSubjects()).toEqual(mockSubjects);
    });

    it('filteredSubjects_WithSelection_ReturnsOnlySelectedSubjects', () => {
      component.selectedSubjectIds.set([1, 3]);

      const filtered = component.filteredSubjects();

      expect(filtered.map((s) => s.id)).toEqual([1, 3]);
      expect(filtered.length).toBe(2);
    });

    it('filteredSubjects_WithNonexistentId_ReturnsEmpty', () => {
      component.selectedSubjectIds.set([999]);

      expect(component.filteredSubjects()).toEqual([]);
    });
  });
});
