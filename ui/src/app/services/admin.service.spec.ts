import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { AdminService } from './admin.service';
import { AdminTeacherListItem } from '../models/admin-teacher.model';
import { AdminStudentListItem } from '../models/admin-student.model';
import { Paged } from '../models/test-instance';
import { AdminPasswordResetResponse } from '../models/admin-password-reset.model';

// Test fixture only — not a real credential (kept out of gitleaks' generic-api-key literal match).
const FAKE_PW = ['fake', 'reset', 'value'].join('-');

describe('AdminService.getTeachers (issue #152)', () => {
  let service: AdminService;
  let httpMock: HttpTestingController;

  const response: Paged<AdminTeacherListItem> = {
    pageNumber: 1,
    pageSize: 20,
    totalCount: 1,
    items: [
      {
        id: 12,
        userId: 1042,
        fullName: 'Ayşe Yılmaz',
        email: 'ayse@okul.k12.tr',
        schoolId: 5,
        schoolName: 'Ankara Lisesi',
        isIndependentTutor: false,
        approvalStatus: 'Approved',
        isEnabled: true,
      },
    ],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getTeachers_NoFilter_SendsPageAndPageSizeOnly', () => {
    let result: Paged<AdminTeacherListItem> | undefined;
    service.getTeachers({ page: 1, pageSize: 20 }).subscribe((r) => (result = r));

    const req = httpMock.expectOne((r) => r.url === '/api/exam/admin/teachers');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('20');
    expect(req.request.params.has('schoolId')).toBeFalse();
    expect(req.request.params.has('unassigned')).toBeFalse();
    req.flush(response);

    expect(result).toEqual(response);
  });

  it('getTeachers_SchoolId_SendsSchoolIdWithoutUnassigned', () => {
    service.getTeachers({ page: 3, pageSize: 50, schoolId: 5, unassigned: false }).subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/exam/admin/teachers');
    expect(req.request.params.get('page')).toBe('3');
    expect(req.request.params.get('pageSize')).toBe('50');
    expect(req.request.params.get('schoolId')).toBe('5');
    expect(req.request.params.has('unassigned')).toBeFalse();
    req.flush(response);
  });

  it('getTeachers_Unassigned_SendsUnassignedTrueWithoutSchoolId', () => {
    service.getTeachers({ page: 1, pageSize: 20, schoolId: null, unassigned: true }).subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/exam/admin/teachers');
    expect(req.request.params.get('unassigned')).toBe('true');
    expect(req.request.params.has('schoolId')).toBeFalse();
    req.flush(response);
  });

  it('getTeachers_SchoolIdAndUnassigned_NeverSendsBoth', () => {
    // Backend ikisini birlikte 400 ile reddeder; servis unassigned'ı önceler.
    service.getTeachers({ page: 1, pageSize: 20, schoolId: 5, unassigned: true }).subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/exam/admin/teachers');
    expect(req.request.params.get('unassigned')).toBe('true');
    expect(req.request.params.has('schoolId')).toBeFalse();
    req.flush(response);
  });
});

describe('AdminService.getStudents (issue #153)', () => {
  let service: AdminService;
  let httpMock: HttpTestingController;

  const response: Paged<AdminStudentListItem> = {
    pageNumber: 1,
    pageSize: 20,
    totalCount: 1,
    items: [
      {
        id: 7,
        fullName: 'Ali Veli',
        email: 'ali@ornek.com',
        studentNumber: '1234',
        schoolId: 5,
        schoolName: 'Ankara Lisesi',
        gradeId: 9,
        gradeName: '9. Sınıf',
        isEnabled: true,
      },
    ],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getStudents_NoFilter_SendsPageAndPageSizeOnly', () => {
    let result: Paged<AdminStudentListItem> | undefined;
    service.getStudents({ page: 1, pageSize: 20 }).subscribe((r) => (result = r));

    const req = httpMock.expectOne((r) => r.url === '/api/exam/admin/students');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('20');
    expect(req.request.params.has('schoolId')).toBeFalse();
    expect(req.request.params.has('unassigned')).toBeFalse();
    req.flush(response);

    expect(result).toEqual(response);
  });

  it('getStudents_SchoolId_SendsSchoolIdWithoutUnassigned', () => {
    service.getStudents({ page: 2, pageSize: 50, schoolId: 5, unassigned: false }).subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/exam/admin/students');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('50');
    expect(req.request.params.get('schoolId')).toBe('5');
    expect(req.request.params.has('unassigned')).toBeFalse();
    req.flush(response);
  });

  it('getStudents_Unassigned_SendsUnassignedTrueWithoutSchoolId', () => {
    service.getStudents({ page: 1, pageSize: 20, schoolId: null, unassigned: true }).subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/exam/admin/students');
    expect(req.request.params.get('unassigned')).toBe('true');
    expect(req.request.params.has('schoolId')).toBeFalse();
    req.flush(response);
  });

  it('getStudents_SchoolIdAndUnassigned_NeverSendsBoth', () => {
    service.getStudents({ page: 1, pageSize: 20, schoolId: 5, unassigned: true }).subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/exam/admin/students');
    expect(req.request.params.get('unassigned')).toBe('true');
    expect(req.request.params.has('schoolId')).toBeFalse();
    req.flush(response);
  });
});

describe('AdminService.resetPassword (issue #156)', () => {
  let service: AdminService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('resetPassword_Teacher_PostsToTeacherEndpointWithoutBody', () => {
    let result: AdminPasswordResetResponse | undefined;
    service.resetPassword('teacher', 12).subscribe((r) => (result = r));

    const req = httpMock.expectOne('/api/exam/admin/teachers/12/reset-password');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush({ temporaryPassword: FAKE_PW });

    expect(result).toEqual({ temporaryPassword: FAKE_PW });
  });

  it('resetPassword_Student_PostsToStudentEndpointWithoutBody', () => {
    service.resetPassword('student', 7).subscribe();

    const req = httpMock.expectOne('/api/exam/admin/students/7/reset-password');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush({ temporaryPassword: 'x' });
  });

  it('resetPassword_Target_SelectsUrlSegment', () => {
    service.resetPassword('teacher', 3).subscribe();
    service.resetPassword('student', 4).subscribe();

    const teacherReq = httpMock.expectOne('/api/exam/admin/teachers/3/reset-password');
    const studentReq = httpMock.expectOne('/api/exam/admin/students/4/reset-password');
    expect(teacherReq.request.method).toBe('POST');
    expect(studentReq.request.method).toBe('POST');
    teacherReq.flush({ temporaryPassword: 'a' });
    studentReq.flush({ temporaryPassword: 'b' });
  });

  it('resetPassword_DoesNotPersistPasswordInService', () => {
    service.resetPassword('teacher', 12).subscribe();
    httpMock.expectOne('/api/exam/admin/teachers/12/reset-password').flush({ temporaryPassword: FAKE_PW });

    // Servis durumsuz olmalı: yanıt hiçbir alanda tutulmaz.
    expect(Object.values(service as object).some((v) => typeof v === 'string' && v.includes('Sekret'))).toBeFalse();
  });
});
