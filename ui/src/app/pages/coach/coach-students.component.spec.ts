import { Component } from '@angular/core';
import { By } from '@angular/platform-browser';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import coachTr from '../../../../public/i18n/coach/tr.json';
import { CoachStudent } from '../../models/coach.model';
import { CoachService } from '../../services/coach.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { CoachStudentComponent } from './coach-student.component';
import { CoachStudentsComponent, coachSignals, daysBetween, localToday } from './coach-students.component';

const student = (overrides: Partial<CoachStudent>): CoachStudent => ({
  id: 1,
  fullName: 'x',
  gradeId: 8,
  relationship: 'School',
  lastActiveDay: '2026-11-07',
  mistakeCount: 0,
  goalCount: 1,
  goalsDone: 0,
  needsAttention: false,
  ...overrides,
});

const students: CoachStudent[] = [
  student({ id: 3, fullName: 'Ada Yılmaz', mistakeCount: 12, lastActiveDay: '2026-10-30', needsAttention: true }),
  student({ id: 5, fullName: 'Can Demir', gradeId: null, relationship: 'PrivateLesson', goalCount: 3, goalsDone: 2 }),
];

@Component({ selector: 'app-stub-student', template: '<p data-test="stub-student">öğrenci</p>' })
class StubStudentComponent {}

describe('CoachStudentsComponent (issue #700)', () => {
  function create(list = of(students)): ComponentFixture<CoachStudentsComponent> {
    TestBed.configureTestingModule({
      imports: [CoachStudentsComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'coach/tr': coachTr } })],
      providers: [
        provideRouter([// Fikstür liste komponentidir; rota yalnız çocuğu (sağ panel) onun outlet'ine koyar.
        { path: 'teacher/coach', children: [{ path: ':studentId', component: StubStudentComponent }] }]),
        { provide: CoachService, useValue: { listStudents: () => list } },
      ],
    });
    const fixture = TestBed.createComponent(CoachStudentsComponent);
    fixture.detectChanges();
    return fixture;
  }

  const all = (fixture: ComponentFixture<CoachStudentsComponent>, test: string): HTMLElement[] =>
    Array.from(fixture.nativeElement.querySelectorAll(`[data-test="${test}"]`));
  const text = (el: Element | undefined) => el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  it('lists_StudentsWithRelationship_AndLinksToTheirPage', () => {
    const fixture = create();

    expect(all(fixture, 'coach-student-name').map(text)).toEqual(['Ada Yılmaz', 'Can Demir']);
    expect(all(fixture, 'coach-student-relationship').map(text)).toEqual(['Okul', 'Özel ders']);
    expect(all(fixture, 'coach-student')[1].getAttribute('href')).toBe('/teacher/coach/5');
  });

  it('rows_ShowSignals_AndMarkTheOnesNeedingAttention_issue893', () => {
    const fixture = create();
    const rows = all(fixture, 'coach-student');

    expect(rows[0].classList).toContain('coach__item--attention');
    expect(rows[1].classList).not.toContain('coach__item--attention');
    expect(rows[0].querySelector('[data-signal="mistakes"]')?.classList).toContain('coach__signal--warn');
    expect(text(all(fixture, 'coach-student-signals')[1])).toContain('2/3 hedef');
  });

  it('coachSignals_DescribeActivityMistakesAndGoals_issue893', () => {
    const today = '2026-11-07';
    const keys = (s: CoachStudent) => coachSignals(s, today).map((x) => [x.key, x.params ?? null, x.warn]);

    expect(daysBetween('2026-10-31', today)).toBe(7);
    expect(keys(student({ lastActiveDay: today }))).toEqual([['activeToday', null, false], ['goals', { done: 0, count: 1 }, false]]);
    expect(keys(student({ lastActiveDay: '2026-11-06', mistakeCount: 3 }))).toEqual([
      ['activeYesterday', null, false],
      ['mistakes', { count: 3 }, false],
      ['goals', { done: 0, count: 1 }, false],
    ]);
    expect(keys(student({ lastActiveDay: '2026-11-04', goalCount: 0 }))).toEqual([
      ['activeDaysAgo', { days: 3 }, false],
      ['noGoals', null, true],
    ]);
    expect(keys(student({ lastActiveDay: '2026-10-31', mistakeCount: 10 }))).toEqual([
      ['inactiveDays', { days: 7 }, true],
      ['mistakes', { count: 10 }, true],
      ['goals', { done: 0, count: 1 }, false],
    ]);
    // Hesaplanmamış yanlış sayısı (null) gösterilmez.
    expect(keys(student({ lastActiveDay: null, mistakeCount: null }))[0]).toEqual(['neverActive', null, true]);
    expect(keys(student({ lastActiveDay: null, mistakeCount: null })).length).toBe(2);
  });

  it('twoPanels_OpenTheStudentBesideTheList_AndMarkTheActiveRow_issue893', async () => {
    const fixture = create();
    expect(all(fixture, 'coach-pick').length).toBe(1);

    await TestBed.inject(Router).navigateByUrl('/teacher/coach/5');
    fixture.detectChanges();

    expect(fixture.componentInstance.hasSelection()).toBe(true);
    expect(fixture.nativeElement.querySelector('.coach')?.classList).toContain('coach--selected');
    expect(all(fixture, 'coach-pick').length).toBe(0);
    expect(all(fixture, 'coach-student')[1].getAttribute('aria-current')).toBe('page');
    expect(all(fixture, 'coach-student')[0].getAttribute('aria-current')).toBeNull();
  });

  it('goalChangesInThePanel_UpdateTheRow_issue893', async () => {
    // Satırın uyarısı yalnız hedef sayısından gelsin: son etkinlik bugün (sabit tarih bir hafta sonra "etkin değil" olur).
    const activeToday = students.map((s) => (s.id === 5 ? { ...s, lastActiveDay: localToday() } : s));
    const service = {
      listStudents: () => of(activeToday),
      getOverview: () =>
        of({
          studentId: 5, fullName: 'Can Demir', recentMockExams: [], weakTopics: [], activeMistakeCount: 0, goals: [],
          plan: { weekStart: '2026-11-02', planned: 0, completed: 0, includesProgram: true },
        }),
      listNotes: () => of([]),
      addGoal: () => of({ id: 9, title: 'Hedef', weekStart: '2026-11-02', updatedAt: '2026-11-02T10:00:00Z', completedAt: null }),
      deleteGoal: () => of(undefined),
    };
    TestBed.configureTestingModule({
      imports: [CoachStudentsComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'coach/tr': coachTr } })],
      providers: [
        provideRouter([// Fikstür liste komponentidir; rota yalnız çocuğu (sağ panel) onun outlet'ine koyar.
        { path: 'teacher/coach', children: [{ path: ':studentId', component: CoachStudentComponent }] }]),
        { provide: CoachService, useValue: service },
      ],
    });
    const fixture = TestBed.createComponent(CoachStudentsComponent);
    fixture.detectChanges();
    await TestBed.inject(Router).navigateByUrl('/teacher/coach/5');
    fixture.detectChanges();
    const panel = fixture.debugElement.query(By.directive(CoachStudentComponent)).componentInstance as CoachStudentComponent;
    const row = () => all(fixture, 'coach-student')[1];

    panel.goalDraft.set('Hedef');
    panel.addGoal();
    fixture.detectChanges();
    expect(text(row())).toContain('0/1 hedef');
    expect(row().classList).not.toContain('coach__item--attention');

    panel.deleteGoal(panel.goals()[0]);
    fixture.detectChanges();
    expect(text(row())).toContain('Bu hafta hedef yok');
    expect(row().classList).toContain('coach__item--attention');
  });

  it('search_FiltersByName_IgnoringCase', () => {
    const fixture = create();
    const input = all(fixture, 'coach-search')[0] as HTMLInputElement;

    input.value = 'can';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(all(fixture, 'coach-student-name').map(text)).toEqual(['Can Demir']);

    input.value = 'zz';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(all(fixture, 'coach-students-no-match').length).toBe(1);
  });

  it('empty_ShowsEmptyState', () => {
    expect(all(create(of([])), 'coach-students-empty').length).toBe(1);
  });

  it('error_ShowsRetry', () => {
    const fixture = create(throwError(() => new Error('down')));
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain('Öğrenciler yüklenemedi.');
  });
});
