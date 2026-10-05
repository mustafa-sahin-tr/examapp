import { ComponentFixture, TestBed } from '@angular/core/testing';

import { WizardProgressComponent, WizardProgressLabels } from './wizard-progress.component';

const LABELS: WizardProgressLabels = {
  ariaLabel: 'Sihirbaz ilerlemesi',
  stop: 'Soru',
  done: 'tamamlandı, düzenle',
  current: 'şu an',
  upcoming: 'sırada',
  branch: 'yola göre değişir',
  final: 'Program bilgileri',
};

describe('WizardProgressComponent (issue #135)', () => {
  let fixture: ComponentFixture<WizardProgressComponent>;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [WizardProgressComponent] }).compileComponents();
    fixture = TestBed.createComponent(WizardProgressComponent);
    el = fixture.nativeElement;
    fixture.componentRef.setInput('labels', LABELS);
  });

  function render(inputs: { done: number; remaining?: number; branchUnknown?: boolean; atFinal?: boolean; counterText?: string }): void {
    Object.entries(inputs).forEach(([k, v]) => fixture.componentRef.setInput(k, v));
    fixture.detectChanges();
  }

  const items = (): HTMLElement[] => Array.from(el.querySelectorAll<HTMLElement>('.wp__item'));

  it('stops_DonePlusCurrentPlusRemainingPlusForm', () => {
    render({ done: 2, remaining: 2, counterText: 'Soru 3 / 6' });
    expect(fixture.componentInstance.total()).toBe(6);
    expect(items().length).toBe(6);
    expect(el.querySelectorAll('button.wp__stop').length).toBe(2);
    expect(items()[2].getAttribute('aria-current')).toBe('step');
    expect(items().filter((i) => i.hasAttribute('aria-current')).length).toBe(1);
    expect(items()[5].querySelector('mat-icon')?.textContent?.trim()).toBe('flag');
    expect(el.querySelector('.wp__counter')?.textContent?.trim()).toBe('Soru 3 / 6');
  });

  it('doneStop_HasLabelAndEmitsGoTo', () => {
    render({ done: 2, remaining: 1 });
    const emitted: number[] = [];
    fixture.componentInstance.goTo.subscribe((i) => emitted.push(i));
    const second = el.querySelectorAll<HTMLButtonElement>('button.wp__stop')[1];
    expect(second.getAttribute('aria-label')).toBe('Soru 2: tamamlandı, düzenle');
    second.click();
    expect(emitted).toEqual([1]);
  });

  it('branchUnknown_MarksFirstUpcomingAsBranchNode', () => {
    render({ done: 0, remaining: 4, branchUnknown: true });
    expect(items()[1].classList).toContain('wp__item--branch');
    expect(items()[1].querySelector('mat-icon')?.textContent?.trim()).toBe('call_split');
    expect(items()[2].classList).not.toContain('wp__item--branch');
  });

  it('atFinal_FormIsCurrentStop', () => {
    render({ done: 5, remaining: 0, atFinal: true });
    expect(items().length).toBe(6);
    const last = items()[5];
    expect(last.getAttribute('aria-current')).toBe('step');
    expect(last.querySelector('.wp__stop')?.getAttribute('aria-label')).toBe('Program bilgileri: şu an');
  });

  it('mobileBar_ExposesProgressbarValues', () => {
    render({ done: 2, remaining: 2, counterText: 'Soru 3 / 6' });
    const bar = el.querySelector('.wp__bar')!;
    expect(bar.getAttribute('role')).toBe('progressbar');
    expect(bar.getAttribute('aria-valuemin')).toBe('0');
    expect(bar.getAttribute('aria-valuemax')).toBe('6');
    expect(bar.getAttribute('aria-valuenow')).toBe('3');
    expect(bar.getAttribute('aria-valuetext')).toBe('Soru 3 / 6');
    expect(bar.querySelectorAll('.wp__segment').length).toBe(6);
    expect(bar.querySelectorAll('.wp__segment--done').length).toBe(2);
  });
});
