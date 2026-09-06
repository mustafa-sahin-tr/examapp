import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';
import { Subject, of, throwError } from 'rxjs';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { WorksheetDetailComponent } from './worksheet-detail.component';
import { TestService } from '../../services/test.service';
import { GradesService } from '../../services/grades.service';
import { AuthService } from '../../services/auth.service';
import { StudentService } from '../../services/student.service';

describe('WorksheetDetailComponent', () => {
  let component: WorksheetDetailComponent;
  let testService: jasmine.SpyObj<TestService>;
  let router: jasmine.SpyObj<Router>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  beforeEach(() => {
    testService = jasmine.createSpyObj<TestService>('TestService', ['getWorksheetDetail', 'copyWorksheet']);
    testService.getWorksheetDetail.and.returnValue(of({} as any));

    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    TestBed.configureTestingModule({
      imports: [WorksheetDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TestService, useValue: testService },
        { provide: Router, useValue: router },
        { provide: MatSnackBar, useValue: snackBar },
        { provide: MatDialog, useValue: jasmine.createSpyObj<MatDialog>('MatDialog', ['open']) },
        { provide: AuthService, useValue: { hasRole: () => false } },
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

  describe('copyWorksheet', () => {
    it('copyWorksheet_Success_SnackBarThenNavigateToNewWorksheetAndResetLoading', () => {
      testService.copyWorksheet.and.returnValue(of({ worksheetId: 777 } as any));

      (component as any)['copyWorksheet']();

      expect(testService.copyWorksheet).toHaveBeenCalledWith(5);
      expect(snackBar.open).toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledWith(['/exam', 777]);
      expect((component as any)['copyLoading']()).toBeFalse();
    });

    it('copyWorksheet_WhilePending_SetsCopyLoadingTrueThenFalseOnComplete', () => {
      const gate = new Subject<any>();
      testService.copyWorksheet.and.returnValue(gate.asObservable());

      (component as any)['copyWorksheet']();
      expect((component as any)['copyLoading']()).toBeTrue();

      gate.next({ worksheetId: 1 });
      gate.complete();
      expect((component as any)['copyLoading']()).toBeFalse();
    });

    it('copyWorksheet_Error_ShowsSnackBarAndResetsLoadingWithoutNavigation', () => {
      testService.copyWorksheet.and.returnValue(throwError(() => ({ error: { message: 'Kopyalama başarısız' } })));

      (component as any)['copyWorksheet']();

      expect(snackBar.open).toHaveBeenCalledWith('Kopyalama başarısız', 'Tamam', jasmine.any(Object));
      expect(router.navigate).not.toHaveBeenCalled();
      expect((component as any)['copyLoading']()).toBeFalse();
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
    const testService = jasmine.createSpyObj<TestService>('TestService', [
      'getWorksheetDetail',
      'copyWorksheet',
      'get',
    ]);
    testService.getWorksheetDetail.and.returnValue(
      of({
        worksheet: { id: 12, canEdit: true, canAssign: true },
        plannedReminder: {
          scheduledFor: new Date(2026, 9, 1, 9, 0).toISOString(),
          remindBeforeMinutes: 60,
          status: 'Pending',
        },
        attempts: [],
      } as any),
    );
    testService.get.and.returnValue(of(null as any));

    const router = jasmine.createSpyObj<Router>('Router', ['navigate']);

    TestBed.configureTestingModule({
      imports: [WorksheetDetailComponent, NoopAnimationsModule],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TestService, useValue: testService },
        { provide: Router, useValue: router },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']) },
        { provide: MatDialog, useValue: jasmine.createSpyObj<MatDialog>('MatDialog', ['open']) },
        { provide: AuthService, useValue: { hasRole: () => false } },
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
    expect((component as any)['reminderEditing']()).toBeTrue();
    expect((component as any)['showReminderForm']()).toBeTrue();
  });
});
