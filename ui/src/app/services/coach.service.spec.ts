import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CoachService } from './coach.service';

describe('CoachService (issue #700)', () => {
  let service: CoachService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(CoachService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('addGoal_EncodesTitle_SendsWeek_AndDecodesResponse', () => {
    let title = '';
    service.addGoal(7, { title: 'a<b & c', weekStart: '2026-11-09' }).subscribe((goal) => (title = goal.title));

    const req = http.expectOne('/api/exam/teacher/coach/students/7/goals');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ title: 'a&lt;b &amp; c', weekStart: '2026-11-09' });
    req.flush({ id: 1, weekStart: '2026-11-09', title: 'a&lt;b &amp; c', updatedAt: '2026-11-05T10:00:00Z' });
    expect(title).toBe('a<b & c');
  });

  it('listGoals_SendsWeekOnlyWhenGiven', () => {
    service.listGoals(7).subscribe();
    http.expectOne('/api/exam/teacher/coach/students/7/goals').flush([]);

    service.listGoals(7, '2026-11-16').subscribe();
    http.expectOne('/api/exam/teacher/coach/students/7/goals?weekStart=2026-11-16').flush([]);
  });

  it('notes_EncodeBody_AndDecodeList', () => {
    let bodies: string[] = [];
    service.listNotes(7).subscribe((notes) => (bodies = notes.map((n) => n.body)));
    http
      .expectOne('/api/exam/teacher/coach/students/7/notes')
      .flush([{ id: 2, body: 'x &gt; y', createdAt: '2026-11-05T10:00:00Z', updatedAt: '2026-11-05T10:00:00Z' }]);
    expect(bodies).toEqual(['x > y']);

    service.updateNote(7, 2, 'x > y').subscribe();
    const req = http.expectOne('/api/exam/teacher/coach/students/7/notes/2');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ body: 'x &gt; y' });
    req.flush({ id: 2, body: 'x &gt; y', createdAt: '2026-11-05T10:00:00Z', updatedAt: '2026-11-05T11:00:00Z' });
  });

  it('overview_DecodesGoalTitles', () => {
    let titles: string[] = [];
    service.getOverview(7).subscribe((o) => (titles = o.goals.map((g) => g.title)));
    http.expectOne('/api/exam/teacher/coach/students/7/overview').flush({
      studentId: 7,
      fullName: 'Ada',
      plan: { weekStart: '2026-11-02', planned: 0, completed: 0, includesProgram: true },
      recentMockExams: [],
      weakTopics: [],
      activeMistakeCount: 0,
      goals: [{ id: 1, weekStart: '2026-11-02', title: '3 &lt; 4', updatedAt: '2026-11-05T10:00:00Z' }],
    });
    expect(titles).toEqual(['3 < 4']);
  });

  it('studentAndParent_UseTheirOwnEndpoints', () => {
    service.getMyGoals().subscribe();
    http.expectOne('/api/exam/student/me/coach-goals').flush([]);

    service.getChildSummary(41).subscribe();
    http.expectOne('/api/exam/parent/children/41/coach-summary').flush({ weekStart: '2026-11-02', plannedCount: 0, completedCount: 0, goals: [] });
  });
});
