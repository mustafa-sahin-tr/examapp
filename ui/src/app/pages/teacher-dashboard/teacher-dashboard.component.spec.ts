import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { TeacherDashboardComponent } from './teacher-dashboard.component';
import { TeacherService } from '../../services/teacher.service';
import {
  TeacherDashboardSummary,
  TeacherLaggingStudent,
  TeacherWorksheetOverview,
} from '../../models/teacher-dashboard.model';

describe('TeacherDashboardComponent', () => {
  let fixture: ComponentFixture<TeacherDashboardComponent>;
  let component: TeacherDashboardComponent;
  let teacherService: jasmine.SpyObj<TeacherService>;
  let router: Router;

  const summary: TeacherDashboardSummary = { totalWorksheets: 3, totalUniqueStudents: 10 };

  const worksheetRows: TeacherWorksheetOverview[] = [
    { worksheetId: 1, name: 'WS 1', assignedStudentCount: 4, completionPercentage: 75 },
    { worksheetId: 2, name: 'WS 2', assignedStudentCount: 2, completionPercentage: 25 },
  ];

  const laggingRows: TeacherLaggingStudent[] = [
    {
      studentId: 1,
      studentName: 'Ayşe Yılmaz',
      worksheetId: 1,
      worksheetName: 'WS 1',
      completionPercentage: 0,
      isLowCompletion: true,
      isExpired: false,
    },
    {
      studentId: 2,
      studentName: 'Mehmet Kaya',
      worksheetId: 2,
      worksheetName: 'WS 2',
      completionPercentage: 0,
      isLowCompletion: true,
      isExpired: true,
    },
  ];

  function configure(): ComponentFixture<TeacherDashboardComponent> {
    teacherService = jasmine.createSpyObj<TeacherService>('TeacherService', [
      'getDashboardSummary',
      'getWorksheetsOverview',
      'getLaggingStudents',
    ]);
    teacherService.getDashboardSummary.and.returnValue(of(summary));
    teacherService.getWorksheetsOverview.and.returnValue(of(worksheetRows));
    teacherService.getLaggingStudents.and.returnValue(of(laggingRows));

    TestBed.configureTestingModule({
      imports: [TeacherDashboardComponent],
      providers: [{ provide: TeacherService, useValue: teacherService }, provideRouter([])],
    });

    const created = TestBed.createComponent(TeacherDashboardComponent);
    router = TestBed.inject(Router);
    return created;
  }

  it('loadWorksheetsOverview_SuccessfulResponse_FillsWorksheetsSignal', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges(); // triggers ngOnInit -> loadWorksheetsOverview

    expect(component.worksheets()).toEqual(worksheetRows);
    expect(component.worksheetsError()).toBeNull();
  });

  it('loadWorksheetsOverview_RequestFails_SetsWorksheetsError', () => {
    fixture = configure();
    component = fixture.componentInstance;
    teacherService.getWorksheetsOverview.and.returnValue(throwError(() => new Error('network error')));

    fixture.detectChanges();

    expect(component.worksheets()).toEqual([]);
    expect(component.worksheetsError()).toBe('Sınav listesi alınırken bir sorun oluştu.');
  });

  it('completionClass_PercentageAtOrAboveFifty_ReturnsSuccessClass', () => {
    fixture = configure();
    component = fixture.componentInstance;

    expect(component.completionClass(50)).toBe('is-success');
    expect(component.completionClass(75)).toBe('is-success');
  });

  it('completionClass_PercentageBelowFifty_ReturnsWarningClass', () => {
    fixture = configure();
    component = fixture.componentInstance;

    expect(component.completionClass(49.99)).toBe('is-warning');
    expect(component.completionClass(0)).toBe('is-warning');
  });

  it('openWorksheet_Called_NavigatesToWorksheetDetailRoute', () => {
    fixture = configure();
    component = fixture.componentInstance;
    const navigateSpy = spyOn(router, 'navigate').and.resolveTo(true);

    component.openWorksheet(worksheetRows[0]);

    expect(navigateSpy).toHaveBeenCalledWith(['/test', worksheetRows[0].worksheetId]);
  });

  // ── Issue #55: Geride Kalan Öğrenciler ────────────────────────────────────

  it('loadLaggingStudents_SuccessfulResponse_FillsLaggingStudentsSignal', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges(); // triggers ngOnInit -> loadLaggingStudents

    expect(component.laggingStudents()).toEqual(laggingRows);
    expect(component.laggingStudentsError()).toBeNull();
  });

  it('loadLaggingStudents_RequestFails_SetsLaggingStudentsError', () => {
    fixture = configure();
    component = fixture.componentInstance;
    teacherService.getLaggingStudents.and.returnValue(throwError(() => new Error('network error')));

    fixture.detectChanges();

    expect(component.laggingStudents()).toEqual([]);
    expect(component.laggingStudentsError()).toBe('Geride kalan öğrenci listesi alınırken bir sorun oluştu.');
  });

  it('laggingStudentsEmpty_NoRows_IsTrue', () => {
    fixture = configure();
    component = fixture.componentInstance;
    teacherService.getLaggingStudents.and.returnValue(of([]));

    fixture.detectChanges();

    expect(component.laggingStudentsEmpty()).toBeTrue();
  });

  it('laggingStudentsEmpty_HasRows_IsFalse', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges();

    expect(component.laggingStudentsEmpty()).toBeFalse();
  });

  it('laggingStudentsTable_RowWithBothFlagsTrue_RendersWarningAndDangerChips', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges();

    const chips = fixture.nativeElement.querySelectorAll('.lagging-table .flag-chips .completion-chip');
    const classesByRow: string[][] = [];
    chips.forEach((chip: HTMLElement) => classesByRow.push(Array.from(chip.classList)));

    // Row 0 (isLowCompletion=true, isExpired=false): only the warning chip.
    // Row 1 (isLowCompletion=true, isExpired=true): both warning and danger chips.
    const warningChips = Array.from(chips as NodeListOf<HTMLElement>).filter((c) =>
      c.classList.contains('is-warning'),
    );
    const dangerChips = Array.from(chips as NodeListOf<HTMLElement>).filter((c) =>
      c.classList.contains('is-danger'),
    );

    expect(warningChips.length).toBe(2); // both rows are low completion
    expect(dangerChips.length).toBe(1); // only the second row is expired
  });

  it('laggingStudentsTable_RowWithOnlyLowCompletion_RendersOnlyWarningChip', () => {
    fixture = configure();
    component = fixture.componentInstance;
    teacherService.getLaggingStudents.and.returnValue(of([laggingRows[0]]));

    fixture.detectChanges();

    const chips: HTMLElement[] = Array.from(
      fixture.nativeElement.querySelectorAll('.lagging-table .flag-chips .completion-chip'),
    );

    expect(chips.length).toBe(1);
    expect(chips[0].classList.contains('is-warning')).toBeTrue();
    expect(chips[0].classList.contains('is-danger')).toBeFalse();
  });
});
