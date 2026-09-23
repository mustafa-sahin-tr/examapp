import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { TeacherService } from './teacher.service';
import { TeacherOwnActivitySummary, TeacherStudentsActivitySummary } from '../models/teacher-dashboard.model';

describe('TeacherService activity summaries (issue #56)', () => {
  let service: TeacherService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TeacherService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getOwnActivitySummary_DefaultDays_GetsGatewayPathWithDaysSeven', () => {
    const body: TeacherOwnActivitySummary = { worksheetsCreated: 3, assignmentsCreated: 2, activeStudents: 3 };
    let result: TeacherOwnActivitySummary | undefined;

    service.getOwnActivitySummary().subscribe((r) => (result = r));

    const req = httpMock.expectOne((r) => r.url === '/api/exam/teacher/own-activity-summary');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('days')).toBe('7');
    // teacherId token'dan gelir; query'de olmamalı.
    expect(req.request.params.has('teacherId')).toBeFalse();
    req.flush(body);

    expect(result).toEqual(body);
  });

  it('getOwnActivitySummary_CustomDays_SendsGivenDays', () => {
    service.getOwnActivitySummary(30).subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/exam/teacher/own-activity-summary');
    expect(req.request.params.get('days')).toBe('30');
    req.flush({ worksheetsCreated: 0, assignmentsCreated: 0, activeStudents: 0 });
  });

  it('getStudentsActivitySummary_DefaultDays_GetsGatewayPathWithDaysSeven', () => {
    const body: TeacherStudentsActivitySummary = {
      totalQuestionsSolved: 16,
      totalCorrectCount: 8,
      totalTimeSeconds: 150,
      topStudents: [{ studentId: 12, studentName: 'Bora B.', questionsSolved: 8, correctCount: 2, timeSeconds: 40 }],
    };
    let result: TeacherStudentsActivitySummary | undefined;

    service.getStudentsActivitySummary().subscribe((r) => (result = r));

    const req = httpMock.expectOne((r) => r.url === '/api/exam/teacher/students-activity-summary');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('days')).toBe('7');
    req.flush(body);

    expect(result).toEqual(body);
  });

  it('getStudentsActivitySummary_CustomDays_SendsGivenDays', () => {
    service.getStudentsActivitySummary(30).subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/exam/teacher/students-activity-summary');
    expect(req.request.params.get('days')).toBe('30');
    req.flush({ totalQuestionsSolved: 0, totalCorrectCount: 0, totalTimeSeconds: 0, topStudents: [] });
  });
});
