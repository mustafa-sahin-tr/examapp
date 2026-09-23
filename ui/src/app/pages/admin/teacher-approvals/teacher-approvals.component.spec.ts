import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';

import { TeacherApprovalsComponent } from './teacher-approvals.component';
import { AdminService } from '../../../services/admin.service';
import { SignalRService } from '../../../services/signalr.service';
import { PendingTeacherApplication } from '../../../models/teacher-application.model';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

describe('TeacherApprovalsComponent — Type Label (Issue #234)', () => {
  let component: TeacherApprovalsComponent;
  let adminService: jasmine.SpyObj<AdminService>;
  let signalR: jasmine.SpyObj<SignalRService>;

  function createComponent(): void {
    adminService = jasmine.createSpyObj('AdminService', [
      'getPendingTeacherApplications',
      'approveTeacherApplication',
      'rejectTeacherApplication',
    ]);
    signalR = jasmine.createSpyObj('SignalRService', [], { teacherApplicationSubmitted$: of(undefined) });

    adminService.getPendingTeacherApplications.and.returnValue(of([]));

    TestBed.configureTestingModule({
      imports: [TeacherApprovalsComponent, translocoTestingModule({ langs: { 'admin/tr': adminTr } })],
      providers: [
        { provide: AdminService, useValue: adminService },
        { provide: SignalRService, useValue: signalR },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj('MatSnackBar', ['open']) },
      ],
    });

    component = TestBed.createComponent(TeacherApprovalsComponent).componentInstance;
  }

  it('should create', () => {
    createComponent();
    expect(component).toBeTruthy();
  });

  // ── Issue #234: Başvuru türü etiketi (Bağımsız / Okul: <ad> / Okul #id) ──────

  it('typeLabel_IndependentTutor_ReturnsIndependentLabel', () => {
    createComponent();

    const app: PendingTeacherApplication = {
      teacherId: 1,
      userId: 100,
      fullName: 'Ali Öğretmen',
      email: 'ali@test.com',
      appliedAt: new Date().toISOString(),
      isIndependentTutor: true,
      requestedSchoolId: null,
      requestedSchoolName: null,
    };

    const label = component.typeLabel(app);
    expect(label).toBeTruthy();
    expect(label === 'Bağımsız').toBeTrue();
  });

  it('typeLabel_SchoolConnectionWithSchoolName_ReturnsOkulWithSchoolName', () => {
    createComponent();

    const app: PendingTeacherApplication = {
      teacherId: 2,
      userId: 101,
      fullName: 'Ayşe Öğretmen',
      email: 'ayse@test.com',
      appliedAt: new Date().toISOString(),
      isIndependentTutor: false,
      requestedSchoolId: 5,
      requestedSchoolName: 'Atatürk Lisesi',
    };

    const label = component.typeLabel(app);
    expect(label).toBeTruthy();
    expect(label === 'Okul: Atatürk Lisesi').toBeTrue();
  });

  it('typeLabel_SchoolConnectionWithoutSchoolName_UsesSchoolIdFallback', () => {
    createComponent();

    const app: PendingTeacherApplication = {
      teacherId: 3,
      userId: 102,
      fullName: 'Mehmet Öğretmen',
      email: 'mehmet@test.com',
      appliedAt: new Date().toISOString(),
      isIndependentTutor: false,
      requestedSchoolId: 42,
      requestedSchoolName: null,
    };

    const label = component.typeLabel(app);
    expect(label).toBeTruthy();
    expect(label === 'Okul #42').toBeTrue();
  });

  it('typeLabel_SchoolConnectionWithBlankSchoolName_UsesFallback', () => {
    createComponent();

    const app: PendingTeacherApplication = {
      teacherId: 4,
      userId: 103,
      fullName: 'Fatma Öğretmen',
      email: 'fatma@test.com',
      appliedAt: new Date().toISOString(),
      isIndependentTutor: false,
      requestedSchoolId: 99,
      requestedSchoolName: '   ',
    };

    const label = component.typeLabel(app);
    expect(label).toBeTruthy();
    expect(label === 'Okul #99').toBeTrue();
  });

  it('typeLabel_TwoApplicationsDifferentTypes_ReturnCorrectLabelsForEach', () => {
    createComponent();

    const independent: PendingTeacherApplication = {
      teacherId: 1,
      userId: 100,
      fullName: 'Ali Bağımsız',
      email: 'ali@test.com',
      appliedAt: '2026-09-20T10:00:00Z',
      isIndependentTutor: true,
      requestedSchoolId: null,
      requestedSchoolName: null,
    };

    const schoolBased: PendingTeacherApplication = {
      teacherId: 2,
      userId: 101,
      fullName: 'Ayşe Okul',
      email: 'ayse@test.com',
      appliedAt: '2026-09-21T14:30:00Z',
      isIndependentTutor: false,
      requestedSchoolId: 7,
      requestedSchoolName: 'Cumhuriyet Ortaokulu',
    };

    const independentLabel = component.typeLabel(independent);
    const schoolLabel = component.typeLabel(schoolBased);

    expect(independentLabel).toBeTruthy();
    expect(independentLabel === 'Bağımsız').toBeTrue();
    expect(schoolLabel).toBeTruthy();
    expect(schoolLabel === 'Okul: Cumhuriyet Ortaokulu').toBeTrue();
  });
});
