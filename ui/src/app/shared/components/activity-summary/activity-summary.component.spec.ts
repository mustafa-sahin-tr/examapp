import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ActivitySummaryComponent } from './activity-summary.component';
import { translocoTestingModule } from '../../testing/transloco-testing';

describe('ActivitySummaryComponent (#573, #572)', () => {
  let fixture: ComponentFixture<ActivitySummaryComponent>;
  const el = () => fixture.nativeElement as HTMLElement;
  const q = (id: string) => el().querySelector(`[data-testid="${id}"]`) as HTMLElement | null;

  function setup(inputs: Record<string, unknown> = {}): void {
    TestBed.configureTestingModule({ imports: [ActivitySummaryComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(ActivitySummaryComponent);
    Object.entries(inputs).forEach(([key, value]) => fixture.componentRef.setInput(key, value));
    fixture.detectChanges();
  }

  it('finished_ShowsTitleAndInfoLine_NoScore', () => {
    setup({ result: { correct: 8, total: 10 } });

    expect(el().textContent).toContain('Etkinlik bitti');
    expect(q('summary-info')?.textContent?.trim()).toBe('8 / 10 doğru');
    expect(el().textContent).not.toMatch(/%|puan|XP/i);
  });

  it('withoutIssue572Data_TodayAndBadgeCardsHidden', () => {
    setup({ result: null });

    expect(q('summary-info')).toBeNull();
    expect(q('today-card')).toBeNull();
    expect(q('badge-card')).toBeNull();
  });

  // Issue #574: birim rozet kodunun önekinden — süre rozeti dakika, seri rozeti gün.
  it.each([
    ['activity-time-2', 'Odak rozeti: 40/60 dk', '20 dk daha!'],
    ['activity-streak-1', 'Odak rozeti: 40/60 gün', '20 gün daha!'],
    ['activity-count-3', 'Odak rozeti: 40/60 etkinlik', '20 etkinlik daha!'],
  ])('badgeUnit_FromCodePrefix_%s', (code, line, remaining) => {
    setup({ badge: { code, name: 'Odak', current: 40, target: 60, earned: false } });

    expect(q('badge-card')?.textContent).toContain(line);
    expect(q('badge-card')?.textContent).toContain(remaining);
  });

  it('withData_TodayAndBadgeCardsRender', () => {
    setup({
      today: { activityCount: 3, activeMinutes: 24, streakDays: 2 },
      badge: { code: 'activity-count-1', name: 'Kâşif', current: 8, target: 10, earned: false },
    });

    expect(q('today-card')?.textContent).toContain('3 etkinlik · 24 dk etkin süre · 2 gün üst üste');
    expect(q('badge-card')?.textContent).toContain('Kâşif rozeti: 8/10 etkinlik');
    expect(q('badge-card')?.textContent).toContain('2 etkinlik daha!');
    expect(el().textContent).not.toMatch(/%|puan|XP/i);
  });

  it('nextButton_OnlyWhenThereIsANextActivity', () => {
    setup({ hasNext: false });
    expect(q('next-activity')).toBeNull();

    fixture.componentRef.setInput('hasNext', true);
    fixture.detectChanges();
    const events: string[] = [];
    fixture.componentInstance.next.subscribe(() => events.push('next'));
    fixture.componentInstance.backToList.subscribe(() => events.push('list'));
    (q('next-activity') as HTMLButtonElement).click();
    (q('back-to-list') as HTMLButtonElement).click();

    expect(events).toEqual(['next', 'list']);
  });

  it('retake_HiddenByDefault_ShownWhenCanRetakeAndEmits (#609)', () => {
    setup();
    expect(q('summary-retake')).toBeNull();

    TestBed.resetTestingModule();
    setup({ canRetake: true });
    const emitted: unknown[] = [];
    fixture.componentInstance.retake.subscribe(() => emitted.push(true));
    const button = q('summary-retake') as HTMLButtonElement;
    expect(button.textContent).toContain('Yeniden çöz');
    button.click();
    expect(emitted.length).toBe(1);
  });

  it('retake_BusyDisablesButton (#609)', () => {
    setup({ canRetake: true, retakeBusy: true });

    expect((q('summary-retake') as HTMLButtonElement).disabled).toBe(true);
  });
});
