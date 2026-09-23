import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { Subject, of, throwError } from 'rxjs';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { WorksheetDetailComponent } from './worksheet-detail.component';
import { TestService } from '../../services/test.service';
import { GradesService } from '../../services/grades.service';
import { AuthService, UserProfile } from '../../services/auth.service';
import { Test } from '../../models/test-instance';
import { WorksheetDetail } from '../../models/worksheet-detail';
import { WorksheetAssignmentDialogData } from './components/assignment-dialog/worksheet-assignment-dialog.component';
import { StudentService } from '../../services/student.service';

import { TranslocoTestingModule } from '@jsverse/transloco';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import rootTr from '../../../../public/i18n/tr.json';
import worksheetDetailTr from '../../../../public/i18n/worksheet-detail/tr.json';

/** Gercek scope sozlugu yuklenir; anahtar bozulursa test kirilir (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
  // Scope sozlugu hem scope yolu (provideTranslocoScope yukleyicisi) hem de kok 'tr' icine
  // gomulu olarak verilir; sablondaki 'prefix' bicimi ikincisinden cozulur.
  langs: { tr: { ...rootTr, 'worksheet-detail': worksheetDetailTr }, 'worksheet-detail/tr': worksheetDetailTr },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
    // TranslocoTestingModule uygulamanin config'ini almaz; scope oneki kebab kalsin (issue #183).
    scopes: { keepCasing: true },
  },
  preloadLangs: true,
});

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
      imports: [WorksheetDetailComponent, translocoTesting],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TestService, useValue: testService },
        { provide: Router, useValue: router },
        { provide: MatSnackBar, useValue: snackBar },
        { provide: MatDialog, useValue: jasmine.createSpyObj<MatDialog>('MatDialog', ['open']) },
        { provide: AuthService, useValue: { hasRole: () => false, hasRealmRole: () => false, user: signal(null) } },
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
      imports: [WorksheetDetailComponent, NoopAnimationsModule, translocoTesting],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TestService, useValue: testService },
        { provide: Router, useValue: router },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']) },
        { provide: MatDialog, useValue: jasmine.createSpyObj<MatDialog>('MatDialog', ['open']) },
        { provide: AuthService, useValue: { hasRole: () => false, hasRealmRole: () => false, user: signal(null) } },
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

/** Issue #222: bağımsız (okulsuz) öğretmen sınıf bazlı atama yapamaz. */
describe('WorksheetDetailComponent independent tutor assignment', () => {
  const teacherProfile = (schoolId: number | null): UserProfile => ({
    email: 't@x.com',
    avatar: '',
    fullName: 'Öğretmen',
    id: 1,
    keycloakId: 'kc-1',
    profileId: 1,
    role: 'Teacher',
    schoolId,
  });

  function setup(options: { role: 'Teacher' | 'Admin'; user: UserProfile | null; realmAdmin?: boolean }) {
    const dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    dialog.open.and.returnValue({ afterClosed: () => of(undefined) } as MatDialogRef<unknown>);

    TestBed.configureTestingModule({
      imports: [WorksheetDetailComponent, NoopAnimationsModule, translocoTesting],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TestService, useValue: jasmine.createSpyObj<TestService>('TestService', ['getWorksheetDetail']) },
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']) },
        { provide: AuthService, useValue: { hasRole: (r: string) => r === options.role, hasRealmRole: (r: string) => options.realmAdmin === true && r === 'Admin', user: signal(options.user) } },
        { provide: StudentService, useValue: {} },
        { provide: GradesService, useValue: { getGrades: () => of([]) } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap({}), data: {} },
            params: of({}),
            queryParams: of({}),
            paramMap: of(convertToParamMap({})),
            queryParamMap: of(convertToParamMap({})),
          },
        },
      ],
    });
    // Komponent MatDialogModule import ettiği için spy, komponent seviyesinde de verilir.
    TestBed.overrideComponent(WorksheetDetailComponent, { add: { providers: [{ provide: MatDialog, useValue: dialog }] } });

    const fixture = TestBed.createComponent(WorksheetDetailComponent);
    const component = fixture.componentInstance;
    component.exam = { id: 12 } as Test;
    component['detail'].set({
      worksheet: { id: 12, name: 'Deneme', canEdit: true, canAssign: true },
      attempts: [],
      similarWorksheets: [],
    } as unknown as WorksheetDetail);
    // Uygulama rolü Admin iken sayfa öğretmen görünümünü (ve atama butonlarını) hiç render etmez;
    // o durumda yalnızca computed doğrulanır.
    if (options.role === 'Teacher') {
      fixture.detectChanges();
    }
    return { fixture, component, dialog };
  }

  const buttons = (fixture: { nativeElement: HTMLElement }, id: string) =>
    fixture.nativeElement.querySelectorAll(`[data-testid="${id}"]`);

  it('bağımsız öğretmende "Sınıfa ata" butonları render edilmez ve dialog isIndependentTutor=true alır', () => {
    const { fixture, dialog } = setup({ role: 'Teacher', user: teacherProfile(null) });

    expect(buttons(fixture, 'assign-grade-btn').length).toBe(0);
    const studentButtons = buttons(fixture, 'assign-student-btn');
    expect(studentButtons.length).toBeGreaterThan(0);

    (studentButtons[0] as HTMLButtonElement).click();

    expect(dialog.open).toHaveBeenCalled();
    const config = dialog.open.calls.mostRecent().args[1] as { data: WorksheetAssignmentDialogData };
    expect(config.data.isIndependentTutor).toBeTrue();
  });

  it('okullu öğretmende "Sınıfa ata" butonları görünür', () => {
    const { fixture, component } = setup({ role: 'Teacher', user: teacherProfile(3) });

    expect(component['isIndependentTutor']()).toBeFalse();
    expect(buttons(fixture, 'assign-grade-btn').length).toBeGreaterThan(0);
  });

  it('uygulama rolü Admin olan okulsuz kullanıcı bağımsız sayılmaz', () => {
    const { component } = setup({ role: 'Admin', user: { ...teacherProfile(null), role: 'Admin' } });

    expect(component['isIndependentTutor']()).toBeFalse();
  });

  it('realm rolü Admin olan okulsuz öğretmen hesabında "Sınıfa ata" butonları görünür', () => {
    const { fixture, component } = setup({ role: 'Teacher', user: teacherProfile(null), realmAdmin: true });

    expect(component['isIndependentTutor']()).toBeFalse();
    expect(buttons(fixture, 'assign-grade-btn').length).toBeGreaterThan(0);
  });

  it('profil henüz yokken (user null) "Sınıfa ata" butonları görünür', () => {
    const { fixture, component } = setup({ role: 'Teacher', user: null });

    expect(component['isIndependentTutor']()).toBeFalse();
    expect(buttons(fixture, 'assign-grade-btn').length).toBeGreaterThan(0);
  });
});
