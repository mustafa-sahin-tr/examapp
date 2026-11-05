import type { MockedObject } from 'vitest';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoService } from '@jsverse/transloco';
import { Observable, of, throwError } from 'rxjs';

import { TopicPerformance, TopicPerformanceItem, TopicProgressTimeline, TopicProgressWeek } from '../../../models/topic-performance.model';
import { LocaleService } from '../../../services/locale.service';
import { TopicPerformanceService } from '../../../services/topic-performance.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { TopicProgressCardComponent } from './topic-progress-card.component';
import topicProgressTr from '../../../../../public/i18n/topic-progress/tr.json';
import topicProgressEn from '../../../../../public/i18n/topic-progress/en.json';

function item(topicId: number | null, name: string, correct: number, wrong: number): TopicPerformanceItem {
  return {
    subjectId: 3,
    subjectName: 'Matematik',
    topicId,
    topicName: name,
    subTopicId: null,
    subTopicName: null,
    correct,
    wrong,
    blank: 0,
    total: correct + wrong,
    accuracyPct: 50,
  };
}

function performance(items: TopicPerformanceItem[]): TopicPerformance {
  return { from: '2026-08-01T00:00:00Z', to: '2026-11-01T00:00:00Z', groupBy: 'topic', items, unclassifiedCount: 0 };
}

function week(weekStart: string, answered: number, correct: number, lowSample = false): TopicProgressWeek {
  return { weekStart, answered, correct, accuracyPct: answered > 0 ? (correct * 100) / answered : null, lowSample };
}

function timeline(weeks: TopicProgressWeek[]): TopicProgressTimeline {
  return { topicId: 1, subTopicId: null, weeks };
}

describe('TopicProgressCardComponent (#531)', () => {
  let service: MockedObject<TopicPerformanceService>;
  let fixture: ComponentFixture<TopicProgressCardComponent>;

  async function render(
    topics: Observable<TopicPerformance>,
    series: Observable<TopicProgressTimeline>,
    lang: 'tr' | 'en' = 'tr',
  ): Promise<HTMLElement> {
    service = {
      getMine: vi.fn().mockReturnValue(topics),
      getMyTimeline: vi.fn().mockReturnValue(series),
    } as unknown as MockedObject<TopicPerformanceService>;

    TestBed.configureTestingModule({
      imports: [
        TopicProgressCardComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { 'topic-progress/tr': topicProgressTr, 'topic-progress/en': topicProgressEn } }),
      ],
      providers: [
        { provide: TopicPerformanceService, useValue: service },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: lang === 'tr' ? 'tr-TR' : 'en-US' }) } },
      ],
    });
    TestBed.inject(TranslocoService).setActiveLang(lang);
    fixture = TestBed.createComponent(TopicProgressCardComponent);
    await settle();
    return fixture.nativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const text = (el: Element | null) => el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const q = (el: HTMLElement, test: string) => el.querySelector(`[data-test="${test}"]`);
  const all = (el: HTMLElement, test: string) => [...el.querySelectorAll(`[data-test="${test}"]`)];
  const parts = (el: Element | null) => [...(el?.children ?? [])].map(text);

  const FULL = [week('2026-10-12', 10, 5), week('2026-10-19', 10, 7), week('2026-10-26', 10, 8)];

  it('opens the most answered topic and asks for 12 weeks', async () => {
    await render(of(performance([item(1, 'Kesirler', 1, 1), item(2, 'Açılar', 5, 5), item(null, 'Konusuz', 9, 9)])), of(timeline(FULL)));

    expect(service.getMine).toHaveBeenCalledWith({ days: 90, minQuestions: 1 });
    expect(service.getMyTimeline).toHaveBeenCalledExactlyOnceWith(2, 12);
  });

  it('asks again when another topic is selected', async () => {
    await render(of(performance([item(2, 'Açılar', 5, 5), item(1, 'Kesirler', 1, 1)])), of(timeline(FULL)));

    (fixture.componentInstance as unknown as { selectTopic(id: number): void }).selectTopic(1);
    await settle();

    expect(service.getMyTimeline).toHaveBeenLastCalledWith(1, 12);
  });

  it('draws one line through consecutive weeks and a marker per answered week', async () => {
    const el = await render(of(performance([item(1, 'Açılar', 5, 5)])), of(timeline(FULL)));

    expect(all(el, 'topic-progress-line').length).toBe(1);
    expect(all(el, 'topic-progress-marker').length).toBe(3);
    expect(q(el, 'topic-progress-chart')?.getAttribute('aria-label')).toBe('Matematik · Açılar için haftalık doğruluk');
  });

  it('a week without answers breaks the line instead of drawing zero', async () => {
    const weeks = [week('2026-10-05', 4, 2), week('2026-10-12', 4, 3), week('2026-10-19', 0, 0), week('2026-10-26', 4, 4), week('2026-11-02', 4, 2)];
    const el = await render(of(performance([item(1, 'Açılar', 5, 5)])), of(timeline(weeks)));

    expect(all(el, 'topic-progress-line').length).toBe(2);
    expect(all(el, 'topic-progress-marker').length).toBe(4);
  });

  it('a lone answered week has a marker and no line', async () => {
    const el = await render(of(performance([item(1, 'Açılar', 5, 5)])), of(timeline([week('2026-10-05', 0, 0), week('2026-10-12', 4, 3), week('2026-10-19', 0, 0)])));

    expect(all(el, 'topic-progress-line').length).toBe(0);
    expect(all(el, 'topic-progress-marker').length).toBe(1);
  });

  it('marks a low sample week differently', async () => {
    const el = await render(of(performance([item(1, 'Açılar', 5, 5)])), of(timeline([week('2026-10-12', 10, 5), week('2026-10-19', 2, 1, true)])));

    const markers = all(el, 'topic-progress-marker');
    expect(markers[0].classList).not.toContain('tp__marker--low');
    expect(markers[1].classList).toContain('tp__marker--low');
  });

  it('summary uses the first and last reliable weeks', async () => {
    const weeks = [week('2026-10-05', 1, 1, true), week('2026-10-12', 10, 5), week('2026-10-19', 10, 8), week('2026-10-26', 1, 0, true)];
    const el = await render(of(performance([item(1, 'Açılar', 5, 5)])), of(timeline(weeks)));

    expect(text(q(el, 'topic-progress-summary'))).toBe('Doğruluk: %50 → %80');
  });

  it('does not claim a trend with fewer than two reliable weeks', async () => {
    const el = await render(of(performance([item(1, 'Açılar', 5, 5)])), of(timeline([week('2026-10-12', 10, 5), week('2026-10-19', 2, 1, true)])), 'en');

    expect(text(q(el, 'topic-progress-summary'))).toBe('A few more weeks of answers will show a trend.');
  });

  it('keyboard moves between weeks and the tooltip shows the counts', async () => {
    const el = await render(of(performance([item(1, 'Açılar', 5, 5)])), of(timeline([week('2026-10-12', 0, 0), week('2026-10-19', 10, 7), week('2026-10-26', 2, 1, true)])), 'en');
    const chart = q(el, 'topic-progress-chart') as HTMLElement;

    chart.dispatchEvent(new FocusEvent('focus'));
    await settle();
    expect(parts(q(el, 'topic-progress-tooltip'))).toEqual(['Week of Oct 26', '50% · 1 of 2 correct', 'Few answers this week']);

    chart.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
    await settle();
    expect(parts(q(el, 'topic-progress-tooltip'))).toEqual(['Week of Oct 19', '70% · 7 of 10 correct']);

    chart.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
    await settle();
    expect(parts(q(el, 'topic-progress-tooltip'))).toEqual(['Week of Oct 12', 'No answers']);

    chart.dispatchEvent(new FocusEvent('blur'));
    await settle();
    expect(q(el, 'topic-progress-tooltip')).toBeNull();
  });

  it('the table lists every week, with a dash when there are no answers', async () => {
    const el = await render(of(performance([item(1, 'Açılar', 5, 5)])), of(timeline([week('2026-10-12', 0, 0), week('2026-10-19', 10, 7)])), 'en');

    const rows = [...(q(el, 'topic-progress-table') as HTMLElement).querySelectorAll('tbody tr')].map(parts);
    expect(rows).toEqual([['Oct 12', '0', '0', '–'], ['Oct 19', '10', '7', '70%']]);
  });

  it('shows an empty message when there are no topics and asks for no series', async () => {
    const el = await render(of(performance([])), of(timeline([])));

    expect(q(el, 'topic-progress-empty')).not.toBeNull();
    expect(service.getMyTimeline).not.toHaveBeenCalled();
  });

  it('a failed series can be retried and keeps the selection', async () => {
    const el = await render(of(performance([item(1, 'Açılar', 5, 5)])), throwError(() => new Error('boom')));
    expect(q(el, 'topic-progress-error')).not.toBeNull();

    service.getMyTimeline.mockReturnValue(of(timeline(FULL)));
    (q(el, 'topic-progress-error')!.querySelector('button') as HTMLButtonElement).click();
    await settle();

    expect(service.getMyTimeline).toHaveBeenLastCalledWith(1, 12);
    expect(q(el, 'topic-progress-chart')).not.toBeNull();
  });

  it('a failed topic list can be retried', async () => {
    const el = await render(throwError(() => new Error('boom')), of(timeline(FULL)));
    expect(q(el, 'topic-progress-error')).not.toBeNull();

    service.getMine.mockReturnValue(of(performance([item(1, 'Açılar', 5, 5)])));
    (q(el, 'topic-progress-error')!.querySelector('button') as HTMLButtonElement).click();
    await settle();

    expect(service.getMyTimeline).toHaveBeenCalledExactlyOnceWith(1, 12);
  });

  it('translations have the same keys', () => {
    const keys = (value: object, prefix = ''): string[] =>
      Object.entries(value).flatMap(([key, child]) => (typeof child === 'object' ? keys(child, `${prefix}${key}.`) : [`${prefix}${key}`]));
    expect(keys(topicProgressEn).sort()).toEqual(keys(topicProgressTr).sort());
  });
});
