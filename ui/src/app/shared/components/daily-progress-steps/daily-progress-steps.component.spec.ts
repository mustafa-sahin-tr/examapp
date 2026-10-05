import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import {
  DailyProgressStepsComponent,
  DailyStepAriaLabelFn,
  DailyStepResult,
} from './daily-progress-steps.component';

@Component({
  standalone: true,
  imports: [DailyProgressStepsComponent],
  template: `<app-daily-progress-steps [variant]="variant()" [total]="total()" [results]="results()"
    [done]="done()" [currentIndex]="currentIndex()" [ariaLabel]="ariaLabel()" [valueText]="valueText()"
    [stepAriaLabel]="label" />`,
})
class HostComponent {
  readonly variant = signal<'bar' | 'steps'>('steps');
  readonly total = signal(5);
  readonly results = signal<DailyStepResult[]>([]);
  readonly done = signal<number | null>(null);
  readonly currentIndex = signal<number | null>(null);
  readonly ariaLabel = signal('Günlük set');
  readonly valueText = signal('');
  readonly label: DailyStepAriaLabelFn = (n, state) => `${n}:${state}`;
}

describe('DailyProgressStepsComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HostComponent] });
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
  });

  it('steps_MixedResults_RendersListWithStateClassesIconsAndLabels', () => {
    host.results.set(['correct', 'wrong', 'skipped']);
    host.currentIndex.set(3);
    fixture.detectChanges();

    const list = fixture.debugElement.query(By.css('ol.daily-steps'));
    expect(list.attributes['role']).toBe('list');
    expect(list.attributes['aria-label']).toBe('Günlük set');

    const items = fixture.debugElement.queryAll(By.css('.daily-steps__item'));
    expect(items.length).toBe(5);
    expect(items.map((i) => i.attributes['aria-label'])).toEqual([
      '1:correct',
      '2:wrong',
      '3:skipped',
      '4:current',
      '5:pending',
    ]);
    expect(items[0].classes['daily-steps__item--correct']).toBeTrue();
    expect(items[1].classes['daily-steps__item--wrong']).toBeTrue();
    expect(items[0].nativeElement.textContent.trim()).toBe('check');
    expect(items[1].nativeElement.textContent.trim()).toBe('close');
    expect(items[3].attributes['aria-current']).toBe('step');
    expect(items[4].attributes['aria-current']).toBeUndefined();
    expect(items[4].nativeElement.textContent.trim()).toBe('5');
  });

  it('steps_ThreeQuestionSet_RendersThreeSteps', () => {
    host.total.set(3);
    fixture.detectChanges();

    expect(fixture.debugElement.queryAll(By.css('.daily-steps__item')).length).toBe(3);
  });

  it('bar_DoneCount_RendersProgressbarWithAriaValues', () => {
    host.variant.set('bar');
    host.done.set(2);
    host.currentIndex.set(2);
    host.valueText.set('5 sorudan 2 tanesi çözüldü');
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('ol'))).toBeNull();
    const bar = fixture.debugElement.query(By.css('.daily-bar'));
    expect(bar.attributes['role']).toBe('progressbar');
    expect(bar.attributes['aria-valuemin']).toBe('0');
    expect(bar.attributes['aria-valuemax']).toBe('5');
    expect(bar.attributes['aria-valuenow']).toBe('2');
    expect(bar.attributes['aria-valuetext']).toBe('5 sorudan 2 tanesi çözüldü');
    expect(bar.attributes['aria-label']).toBe('Günlük set');

    const segments = fixture.debugElement.queryAll(By.css('.daily-bar__segment'));
    expect(segments.length).toBe(5);
    expect(segments.filter((s) => s.classes['daily-bar__segment--done']).length).toBe(2);
    expect(segments[2].classes['daily-bar__segment--current']).toBeTrue();
  });

  it('bar_DoneAboveTotal_ClampsValueNow', () => {
    host.variant.set('bar');
    host.total.set(3);
    host.done.set(7);
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.daily-bar')).attributes['aria-valuenow']).toBe('3');
  });
});
