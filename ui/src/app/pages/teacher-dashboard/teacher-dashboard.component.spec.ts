import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { TeacherDashboardComponent } from './teacher-dashboard.component';
import { TeacherService } from '../../services/teacher.service';
import { TeacherDashboardSummary, TeacherWorksheetOverview } from '../../models/teacher-dashboard.model';

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

  function configure(): ComponentFixture<TeacherDashboardComponent> {
    teacherService = jasmine.createSpyObj<TeacherService>('TeacherService', [
      'getDashboardSummary',
      'getWorksheetsOverview',
    ]);
    teacherService.getDashboardSummary.and.returnValue(of(summary));
    teacherService.getWorksheetsOverview.and.returnValue(of(worksheetRows));

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
});
