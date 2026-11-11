import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import studyPlanTr from '../../../../../public/i18n/study-plan/tr.json';
import { StudyPlanService } from '../../../services/study-plan.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { StudyPlanProgressComponent } from './study-plan-progress.component';

describe('StudyPlanProgressComponent (issue #1113)', () => {
  function create(child: ReturnType<typeof vi.fn>) {
    TestBed.configureTestingModule({
      imports: [StudyPlanProgressComponent, translocoTestingModule({ langs: { 'study-plan/tr': studyPlanTr } })],
      providers: [{ provide: StudyPlanService, useValue: { child } }],
    });
    const fixture = TestBed.createComponent(StudyPlanProgressComponent);
    fixture.componentRef.setInput('childId', 4);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it("shows the child's progress read-only", () => {
    const child = vi.fn().mockReturnValue(
      of({
        weekStart: '2026-11-09',
        doneCount: 1,
        totalCount: 2,
        tasks: [
          { id: 1, kind: 'mistakes', done: true },
          { id: 2, kind: 'mockExam', title: 'LGS 1', done: false },
        ],
      })
    );
    const el = create(child);

    expect(child).toHaveBeenCalledWith(4);
    expect(el.textContent).toContain('1 / 2 görev tamamlandı');
    const rows = [...el.querySelectorAll('[data-testid="study-plan-progress-task"]')];
    expect(rows.map((r) => r.classList.contains('is-done'))).toEqual([true, false]);
    expect(rows[1].textContent).toContain('Denemeyi çöz: LGS 1');
    expect(el.querySelector('input')).toBeNull();
  });

  it('draws nothing when the plan cannot be read', () => {
    const el = create(vi.fn().mockReturnValue(throwError(() => new Error('403'))));
    expect(el.querySelector('[data-testid="study-plan-progress-card"]')).toBeNull();
  });
});
