import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { AdminService } from './admin.service';
import { AdminTeacherListItem } from '../models/admin-teacher.model';
import { Paged } from '../models/test-instance';

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
