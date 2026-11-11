import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import studyPlanTr from '../../../../public/i18n/study-plan/tr.json';
import { StudyPlan, StudyPlanTask } from '../../models/study-plan.model';
import { StudyPlanService } from '../../services/study-plan.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { StudyPlanComponent } from './study-plan.component';

describe('StudyPlanComponent (issue #1113)', () => {
  const task = (id: number, extra: Partial<StudyPlanTask>): StudyPlanTask => ({
    id,
    kind: 'practice',
    title: null,
    subjectName: null,
    subjectId: null,
    topicId: null,
    worksheetId: null,
    mockExamId: null,
    dueAtUtc: null,
    done: false,
    ...extra,
  });
  const PLAN: StudyPlan = {
    weekStart: '2026-11-09',
    doneCount: 0,
    totalCount: 3,
    tasks: [
      task(1, { kind: 'assignment', title: 'Kesir ödevi', subjectName: 'Matematik', dueAtUtc: '2026-11-12T18:00:00Z' }),
      task(2, { kind: 'practiceTopic', title: 'Kesirler', subjectId: 3, topicId: 9 }),
      task(3, { kind: 'mistakes' }),
    ],
  };

  let service: { current: ReturnType<typeof vi.fn>; setDone: ReturnType<typeof vi.fn> };

  function create(): ComponentFixture<StudyPlanComponent> {
    TestBed.configureTestingModule({
      imports: [
        StudyPlanComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { 'study-plan/tr': studyPlanTr } }),
      ],
      providers: [provideRouter([]), { provide: StudyPlanService, useValue: service }],
    });
    const fixture = TestBed.createComponent(StudyPlanComponent);
    fixture.detectChanges();
    return fixture;
  }

  const el = (f: ComponentFixture<StudyPlanComponent>) => f.nativeElement as HTMLElement;
  const tasks = (f: ComponentFixture<StudyPlanComponent>) => [
    ...el(f).querySelectorAll('[data-testid="study-plan-task"]'),
  ];

  it('lists the tasks with titles and links to the right page', () => {
    service = { current: vi.fn().mockReturnValue(of(PLAN)), setDone: vi.fn() };
    const f = create();

    expect(el(f).querySelector('[data-testid="study-plan-progress"]')!.textContent).toContain('0 / 3 görev tamamlandı');
    const rows = tasks(f);
    expect(rows[0].textContent).toContain('Ödevi bitir: Kesir ödevi');
    expect(rows[0].textContent).toContain('Matematik');
    expect(rows[1].textContent).toContain('Kesirler konusunda pratik yap');
    expect(rows[1].querySelector('a')!.getAttribute('href')).toBe('/practice?subjectId=3&topicId=9');
    expect(rows[2].querySelector('a')!.getAttribute('href')).toBe('/mistakes');
  });

  it('ticks a task and reverts the tick when saving fails', () => {
    const done = { ...PLAN, doneCount: 1, tasks: PLAN.tasks.map((t) => (t.id === 3 ? { ...t, done: true } : t)) };
    service = {
      current: vi.fn().mockReturnValue(of(PLAN)),
      setDone: vi
        .fn()
        .mockReturnValueOnce(of(done))
        .mockReturnValueOnce(throwError(() => new Error('x'))),
    };
    const f = create();

    f.componentInstance.toggle(PLAN.tasks[2], true);
    f.detectChanges();
    expect(service.setDone).toHaveBeenCalledWith(3, true);
    expect(tasks(f)[2].classList).toContain('is-done');
    expect(tasks(f)[2].querySelector('a')).toBeNull();

    f.componentInstance.toggle(PLAN.tasks[0], true);
    f.detectChanges();
    expect(tasks(f)[0].classList).not.toContain('is-done');
  });

  it('offers a retry when the plan cannot be loaded', () => {
    service = { current: vi.fn().mockReturnValue(throwError(() => new Error('x'))), setDone: vi.fn() };
    const f = create();
    expect(el(f).textContent).toContain('Görevler yüklenemedi.');
  });
});
