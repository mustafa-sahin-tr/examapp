import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Subject, of, throwError } from 'rxjs';
import coachTr from '../../../../../public/i18n/coach/tr.json';
import { CoachService } from '../../../services/coach.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { CoachGoalsCardComponent } from './coach-goals-card.component';

@Component({
  standalone: true,
  imports: [CoachGoalsCardComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `<app-coach-goals-card [childId]="childId()" />`,
})
class HostComponent {
  readonly childId = signal<number | null>(null);
}

describe('CoachGoalsCardComponent (issue #700)', () => {
  let service: { getMyGoals: ReturnType<typeof vi.fn>; getChildSummary: ReturnType<typeof vi.fn>; setMyGoalDone: ReturnType<typeof vi.fn> };

  async function create(childId: number | null): Promise<ComponentFixture<HostComponent>> {
    TestBed.configureTestingModule({
      imports: [HostComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'coach/tr': coachTr } })],
      providers: [{ provide: CoachService, useValue: service }],
    });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.childId.set(childId);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    service = { getMyGoals: vi.fn(), getChildSummary: vi.fn(), setMyGoalDone: vi.fn() };
  });

  const q = (fixture: ComponentFixture<HostComponent>, test: string): HTMLElement | null =>
    fixture.nativeElement.querySelector(`[data-test="${test}"]`);
  const all = (fixture: ComponentFixture<HostComponent>, test: string): string[] =>
    Array.from(fixture.nativeElement.querySelectorAll(`[data-test="${test}"]`), (el: Element) => el.textContent?.trim() ?? '');

  it('student_ListsGoalsWithCoachName', async () => {
    service.getMyGoals.mockReturnValue(of([{ id: 1, weekStart: '2026-11-02', title: 'Günde 20 soru', coachName: 'Zeynep Hoca', done: false }]));
    const fixture = await create(null);

    expect(all(fixture, 'coach-goal-item')[0]).toContain('Günde 20 soru');
    expect(all(fixture, 'coach-goal-coach')).toEqual(['Zeynep Hoca']);
    expect(service.getChildSummary).not.toHaveBeenCalled();
  });

  it('student_TicksAGoal_SavesIt_AndRevertsOnError_issue893', async () => {
    service.getMyGoals.mockReturnValue(of([{ id: 4, weekStart: '2026-11-02', title: 'Günde 20 soru', coachName: null, done: false }]));
    const saved = new Subject<unknown>();
    service.setMyGoalDone.mockReturnValue(saved);
    const fixture = await create(null);
    const checkbox = (): HTMLInputElement => q(fixture, 'coach-goal-done')!.querySelector('input')!;

    checkbox().click();
    fixture.detectChanges();
    expect(service.setMyGoalDone).toHaveBeenCalledWith(4, true);
    expect(checkbox().checked).toBe(true);
    expect(checkbox().disabled).toBe(true);

    saved.error(new HttpErrorResponse({ status: 500 }));
    fixture.detectChanges();
    expect(checkbox().checked).toBe(false);
    expect(checkbox().disabled).toBe(false);
    expect(q(fixture, 'coach-goal-mark-error')).not.toBeNull();

    service.setMyGoalDone.mockReturnValue(of({ id: 4, weekStart: '2026-11-02', title: 'Günde 20 soru', coachName: null, done: true }));
    checkbox().click();
    fixture.detectChanges();
    expect(checkbox().checked).toBe(true);
    expect(q(fixture, 'coach-goal-mark-error')).toBeNull();
  });

  it('student_NoGoalsOrError_DrawsNothing', async () => {
    service.getMyGoals.mockReturnValue(of([]));
    expect(q(await create(null), 'coach-goals-card')).toBeNull();

    TestBed.resetTestingModule();
    service.getMyGoals.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    expect(q(await create(null), 'coach-goals-card')).toBeNull();
  });

  it('parent_ShowsPlanCountsAndGoals_WithoutTeacher', async () => {
    service.getChildSummary.mockReturnValue(
      of({ weekStart: '2026-11-02', plannedCount: 3, completedCount: 2, goals: [{ title: 'Paragraf çalış', done: false }, { title: 'Deneme çöz', done: true }] }),
    );
    const fixture = await create(41);

    expect(service.getChildSummary).toHaveBeenCalledWith(41);
    expect(q(fixture, 'coach-summary-plan')?.textContent?.trim()).toBe('Planım: 3 testten 2 tamamlandı');
    expect(all(fixture, 'coach-goal-item')[0]).toBe('Paragraf çalış');
    expect(all(fixture, 'coach-goal-item')[1]).toContain('Deneme çöz');
    // Issue #893: veli işareti görür ama değiştiremez.
    expect(all(fixture, 'coach-goal-done-tag')).toEqual(['check_circleYapıldı']);
    expect(q(fixture, 'coach-goal-done')).toBeNull();
    expect(q(fixture, 'coach-goal-coach')).toBeNull();
  });

  it('parent_NoGoals_SaysSo', async () => {
    service.getChildSummary.mockReturnValue(of({ weekStart: '2026-11-02', plannedCount: 0, completedCount: 0, goals: [] }));
    const fixture = await create(41);

    expect(q(fixture, 'coach-summary-plan')).toBeNull();
    expect(q(fixture, 'coach-summary-no-goals')).not.toBeNull();
  });

  it('parent_NotFound_HidesCard_OtherErrorsShowRetry', async () => {
    service.getChildSummary.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    expect(q(await create(41), 'coach-goals-card')).toBeNull();

    TestBed.resetTestingModule();
    service.getChildSummary.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    expect(q(await create(41), 'coach-goals-error')).not.toBeNull();
  });
});
