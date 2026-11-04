import type { MockedObject } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { Observable, Subject, of, throwError } from 'rxjs';

import { TopicPerformance, TopicPerformanceItem } from '../../../models/topic-performance.model';
import { LocaleService } from '../../../services/locale.service';
import { TopicPerformanceService } from '../../../services/topic-performance.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { WeakTopicsCardComponent } from './weak-topics-card.component';
import weakTopicsTr from '../../../../../public/i18n/weak-topics/tr.json';
import weakTopicsEn from '../../../../../public/i18n/weak-topics/en.json';

@Component({
  standalone: true,
  imports: [WeakTopicsCardComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `<app-weak-topics-card [childId]="childId()" />`,
})
class HostComponent {
  readonly childId = signal<number | null>(null);
}

function item(topicId: number, name: string, accuracyPct: number, correct = 2, wrong = 8): TopicPerformanceItem {
  return {
    subjectId: 3,
    subjectName: 'Matematik',
    topicId,
    topicName: name,
    subTopicId: null,
    subTopicName: null,
    correct,
    wrong,
    blank: 4,
    total: correct + wrong + 4,
    accuracyPct,
  };
}

function performance(items: TopicPerformanceItem[]): TopicPerformance {
  return { from: '2026-10-05T00:00:00Z', to: '2026-11-04T00:00:00Z', groupBy: 'topic', items, unclassifiedCount: 0 };
}

describe('WeakTopicsCardComponent (#521)', () => {
  let service: MockedObject<TopicPerformanceService>;
  let fixture: ComponentFixture<HostComponent>;

  async function render(
    response: Observable<TopicPerformance>,
    childId: number | null = null,
    lang: 'tr' | 'en' = 'tr'
  ): Promise<HTMLElement> {
    service = {
      getMine: vi.fn().mockReturnValue(response),
      getChild: vi.fn().mockReturnValue(response),
    } as unknown as MockedObject<TopicPerformanceService>;

    TestBed.configureTestingModule({
      imports: [
        HostComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { 'weak-topics/tr': weakTopicsTr, 'weak-topics/en': weakTopicsEn } }),
      ],
      providers: [
        provideRouter([]),
        { provide: TopicPerformanceService, useValue: service },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: lang === 'tr' ? 'tr-TR' : 'en-US' }) } },
      ],
    });
    TestBed.inject(TranslocoService).setActiveLang(lang);
    fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.childId.set(childId);
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
  const names = (el: HTMLElement) => [...el.querySelectorAll('[data-test="weak-topic-name"]')].map(text);

  it('student: asks for own topics, keeps server order, shows accuracy and answered count', async () => {
    const el = await render(of(performance([item(2, 'Kesirler', 20), item(1, 'Açılar', 45.5, 5, 6)])));

    expect(service.getMine).toHaveBeenCalledWith({ days: 30, minQuestions: 5 });
    expect(service.getChild).not.toHaveBeenCalled();
    expect(text(el.querySelector('#weak-topics-title'))).toBe('Zorlandığım konular');
    expect(names(el)).toEqual(['Kesirler', 'Açılar']);
    // Boş satır soru sayısına girmez: 2 doğru + 8 yanlış.
    expect(text(q(el, 'weak-topic-stats'))).toBe('%20 doğru · 10 soru');
  });

  it('student: practice link carries subject and topic', async () => {
    const el = await render(of(performance([item(30, 'Kesirler', 20)])));

    const link = q(el, 'weak-topic-practice') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/practice?subjectId=3&topicId=30');
    expect(link.getAttribute('aria-label')).toBe('Kesirler konusunda pratik yap');
  });

  it('lists at most five topics and leaves out topics at or above the weak limit', async () => {
    const el = await render(
      of(
        performance([
          item(1, 'A', 10),
          item(2, 'B', 20),
          item(3, 'C', 30),
          item(4, 'D', 40),
          item(5, 'E', 50),
          item(6, 'F', 60),
          item(7, 'G', 70),
        ])
      )
    );

    expect(names(el)).toEqual(['A', 'B', 'C', 'D', 'E']);
  });

  it('no items: shows the not enough data message', async () => {
    const el = await render(of(performance([])));

    expect(text(q(el, 'weak-topics-empty'))).toContain('en az 5 soru');
    expect(q(el, 'weak-topic')).toBeNull();
    expect(q(el, 'weak-topics-none')).toBeNull();
  });

  it('only strong topics: says nothing looks difficult instead of not enough data', async () => {
    const el = await render(of(performance([item(1, 'A', 70), item(2, 'B', 95)])));

    expect(text(q(el, 'weak-topics-none'))).toContain('Son 30 günde');
    expect(q(el, 'weak-topics-empty')).toBeNull();
  });

  it('parent: asks for the selected child, no practice link', async () => {
    const el = await render(of(performance([item(1, 'Kesirler', 20)])), 42);

    expect(service.getChild).toHaveBeenCalledWith(42, { days: 30, minQuestions: 5 });
    expect(service.getMine).not.toHaveBeenCalled();
    expect(text(el.querySelector('#weak-topics-title'))).toBe('Zorlandığı konular');
    expect(q(el, 'weak-topic')).not.toBeNull();
    expect(q(el, 'weak-topic-practice')).toBeNull();
  });

  it('parent: switching child reloads and drops the previous child data', async () => {
    const next = new Subject<TopicPerformance>();
    const el = await render(of(performance([item(1, 'Kesirler', 20)])), 42);
    service.getChild.mockReturnValue(next);

    fixture.componentInstance.childId.set(43);
    await settle();

    expect(service.getChild).toHaveBeenLastCalledWith(43, { days: 30, minQuestions: 5 });
    expect(q(el, 'weak-topic')).toBeNull();
    expect(q(el, 'weak-topics-loading')).not.toBeNull();

    next.next(performance([item(2, 'Açılar', 30)]));
    next.complete();
    await settle();
    expect(names(el)).toEqual(['Açılar']);
  });

  it('parent: a late response of the previous child is dropped', async () => {
    const first = new Subject<TopicPerformance>();
    const second = new Subject<TopicPerformance>();
    const el = await render(first, 42);
    service.getChild.mockReturnValue(second);

    fixture.componentInstance.childId.set(43);
    await settle();
    second.next(performance([item(2, 'Açılar', 30)]));
    second.complete();
    first.next(performance([item(1, 'Kesirler', 20)]));
    await settle();

    expect(names(el)).toEqual(['Açılar']);
  });

  it('404: hides the card without an error, and shows it again for the next child', async () => {
    const el = await render(throwError(() => new HttpErrorResponse({ status: 404 })), 42);

    expect(q(el, 'weak-topics')).toBeNull();
    expect(q(el, 'weak-topics-error')).toBeNull();

    service.getChild.mockReturnValue(of(performance([item(2, 'Açılar', 30)])));
    fixture.componentInstance.childId.set(43);
    await settle();
    expect(names(el)).toEqual(['Açılar']);
  });

  it('other errors: shows the message, retry loads again', async () => {
    const el = await render(throwError(() => new HttpErrorResponse({ status: 500 })));

    expect(text(q(el, 'weak-topics-error'))).toContain('Konu bilgileri yüklenemedi.');
    service.getMine.mockReturnValue(of(performance([item(1, 'Kesirler', 20)])));

    (q(el, 'weak-topics-error')!.querySelector('button') as HTMLButtonElement).click();
    await settle();

    expect(service.getMine).toHaveBeenCalledTimes(2);
    expect(q(el, 'weak-topics-error')).toBeNull();
    expect(names(el)).toEqual(['Kesirler']);
  });

  it('English: uses the locale for the percent', async () => {
    const el = await render(of(performance([item(1, 'Fractions', 20)])), null, 'en');

    expect(text(el.querySelector('#weak-topics-title'))).toBe('Topics I struggle with');
    expect(text(q(el, 'weak-topic-stats'))).toBe('20% correct · 10 questions');
  });
});
