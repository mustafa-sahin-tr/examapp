import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ActivityPlayerFrameComponent, ActivityStep, activityStepLabel } from './activity-player-frame.component';
import { translocoTestingModule } from '../../testing/transloco-testing';

/** Görselsiz içerik (#590 öncesi Q6 doğrulaması): çerçeve, görsel olmadan yansıtılan HTML'i taşır. */
@Component({
  standalone: true,
  imports: [ActivityPlayerFrameComponent],
  template: `
    <app-activity-player-frame
      [title]="title()"
      [currentIndex]="index()"
      [steps]="steps()"
      [instruction]="instruction()"
      (stepSelected)="selected.push($event)"
    >
      <p class="html-stim">I have a <span class="gap"></span> and a dog.</p>
      <div activityFooter class="footer-slot">footer</div>
    </app-activity-player-frame>
  `,
})
class HostComponent {
  readonly title = signal('Unit 4 — Match the pictures');
  readonly index = signal(1);
  readonly steps = signal<ActivityStep[]>([
    { state: 'answered' },
    { state: 'empty' },
    { state: 'wrong' },
    { state: 'correct' },
    { state: 'empty' },
  ]);
  readonly instruction = signal<string | null>('Resimleri doğru kelimelerle eşleştir.');
  readonly selected: number[] = [];
}

describe('ActivityPlayerFrameComponent (#573)', () => {
  let fixture: ComponentFixture<HostComponent>;
  const el = () => fixture.nativeElement as HTMLElement;
  const q = (selector: string) => el().querySelector(selector) as HTMLElement | null;
  const steps = () => Array.from(el().querySelectorAll('.apf__step')) as HTMLButtonElement[];

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HostComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  it('header_ShowsTitleAndItemCounter', () => {
    expect(q('.apf__title')?.textContent?.trim()).toBe('Unit 4 — Match the pictures');
    expect(q('[data-testid="item-count"]')?.textContent?.trim()).toBe('Madde 2 / 5');
  });

  it('progressBar_IsDeterminateWithProgressbarRole', () => {
    const bar = q('mat-progress-bar');
    expect(bar?.getAttribute('role')).toBe('progressbar');
    expect(bar?.getAttribute('aria-valuenow')).toBe('40');
    expect(bar?.getAttribute('aria-label')).toBe('İlerleme: madde 2 / 5');
  });

  it('instruction_RendersWhenGiven_HiddenWhenBlank', () => {
    expect(q('[data-testid="instruction"]')?.textContent).toContain('Resimleri doğru kelimelerle eşleştir.');

    fixture.componentInstance.instruction.set('   ');
    fixture.detectChanges();
    expect(q('[data-testid="instruction"]')).toBeNull();
  });

  it('stepper_LettersCurrentStepAndStatesAreNotColorOnly', () => {
    expect(steps().map((s) => s.textContent?.replace(/check|close/g, '').trim())).toEqual(['a', 'b', 'c', 'd', 'e']);
    expect(steps()[1].getAttribute('aria-current')).toBe('step');
    expect(steps()[1].classList.contains('apf__step--current')).toBe(true);
    // Yanlış: ✗ ikonu + metinli aria etiketi (renk tek sinyal değil).
    expect(steps()[2].querySelector('mat-icon')?.textContent).toBe('close');
    expect(steps()[2].getAttribute('aria-label')).toBe('Madde c: yanlış');
    expect(steps()[3].getAttribute('aria-label')).toBe('Madde d: doğru');
    expect(steps()[0].getAttribute('aria-label')).toBe('Madde a: cevaplandı');
    expect(steps()[4].getAttribute('aria-label')).toBe('Madde e: boş');
  });

  it('navigation_PrevNextAndStepEmitIndices_CurrentStepDoesNotEmit', () => {
    q('[data-testid="prev"]')!.click();
    q('[data-testid="next"]')!.click();
    steps()[4].click();
    steps()[1].click(); // geçerli adım

    expect(fixture.componentInstance.selected).toEqual([0, 2, 4]);
  });

  it('navigation_BoundsDisablePrevOnFirstAndNextOnLast', () => {
    fixture.componentInstance.index.set(0);
    fixture.detectChanges();
    expect((q('[data-testid="prev"]') as HTMLButtonElement).disabled).toBe(true);
    expect((q('[data-testid="next"]') as HTMLButtonElement).disabled).toBe(false);

    fixture.componentInstance.index.set(4);
    fixture.detectChanges();
    expect((q('[data-testid="next"]') as HTMLButtonElement).disabled).toBe(true);
  });

  it('contentProjection_ImagelessHtmlAndFooterSlotRender', () => {
    expect(q('.apf__content .html-stim')?.textContent).toContain('I have a');
    expect(q('.apf__content img')).toBeNull();
    expect(q('.footer-slot')?.closest('.apf__content')).toBeNull();
    expect(q('.footer-slot')?.textContent).toBe('footer');
  });

  it('noScoreOrPercentText', () => {
    expect(el().textContent).not.toMatch(/%|puan|XP/i);
  });

  it('activityStepLabel_LettersThenNumbers', () => {
    expect(activityStepLabel(0)).toBe('a');
    expect(activityStepLabel(25)).toBe('z');
    expect(activityStepLabel(26)).toBe('27');
  });
});
