import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import hintsTr from '../../../../../public/i18n/hints/tr.json';
import { HintState } from '../../../models/hints.model';
import { HintsService } from '../../../services/hints.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { StepHintsComponent } from './step-hints.component';

describe('StepHintsComponent (issue #1113)', () => {
  const state = (steps: string[], extra: Partial<HintState> = {}): HintState => ({
    questionId: 5,
    steps: steps.map((text, i) => ({ step: i + 1, text })),
    totalSteps: 2,
    unavailable: false,
    remaining: 298,
    ...extra,
  });

  let service: { get: ReturnType<typeof vi.fn>; next: ReturnType<typeof vi.fn> };

  function create(): ComponentFixture<StepHintsComponent> {
    TestBed.configureTestingModule({
      imports: [StepHintsComponent, translocoTestingModule({ langs: { 'hints/tr': hintsTr } })],
      providers: [{ provide: HintsService, useValue: service }],
    });
    const fixture = TestBed.createComponent(StepHintsComponent);
    fixture.componentRef.setInput('questionId', 5);
    fixture.detectChanges();
    return fixture;
  }

  const el = (f: ComponentFixture<StepHintsComponent>) => f.nativeElement as HTMLElement;
  const click = (f: ComponentFixture<StepHintsComponent>) => {
    (el(f).querySelector('[data-testid="hint-next"]') as HTMLButtonElement).click();
    f.detectChanges();
  };
  const steps = (f: ComponentFixture<StepHintsComponent>) =>
    [...el(f).querySelectorAll('[data-testid="hint-step"]')].map((s) =>
      [...s.querySelectorAll('span')].map((x) => x.textContent!.trim()).join(' ')
    );

  it('makes no request until clicked, then opens the first step and the next one', () => {
    service = {
      get: vi.fn().mockReturnValue(of(state([], { totalSteps: null }))),
      next: vi
        .fn()
        .mockReturnValueOnce(of(state(['Topla.'])))
        .mockReturnValueOnce(of(state(['Topla.', 'Birleştir.']))),
    };
    const f = create();
    expect(service.get).not.toHaveBeenCalled();
    expect(el(f).querySelector('[data-testid="hint-next"]')!.textContent).toContain('İpucu al');

    click(f);
    expect(steps(f)).toEqual(['1. adım Topla.']);
    expect(el(f).querySelector('[data-testid="hints-remaining"]')!.textContent).toContain('298');
    expect(el(f).querySelector('[data-testid="hint-next"]')!.textContent).toContain('Sonraki ipucu');

    click(f);
    expect(steps(f)).toEqual(['1. adım Topla.', '2. adım Birleştir.']);
    expect(el(f).querySelector('[data-testid="hint-next"]')).toBeNull();
  });

  it('shows already opened steps without spending a hint', () => {
    service = { get: vi.fn().mockReturnValue(of(state(['Topla.']))), next: vi.fn() };
    const f = create();
    click(f);
    expect(service.next).not.toHaveBeenCalled();
    expect(steps(f)).toEqual(['1. adım Topla.']);
  });

  it('says when there is no hint and leaves plan limits to the upgrade dialog', () => {
    service = { get: vi.fn().mockReturnValue(of(state([], { unavailable: true, totalSteps: null }))), next: vi.fn() };
    const f = create();
    click(f);
    expect(el(f).querySelector('[data-testid="hints-unavailable"]')).not.toBeNull();

    TestBed.resetTestingModule();
    service = {
      get: vi.fn().mockReturnValue(of(state([], { totalSteps: null }))),
      next: vi
        .fn()
        .mockReturnValue(
          throwError(() => new HttpErrorResponse({ status: 403, error: { code: 'entitlement.notInPlan' } }))
        ),
    };
    const g = create();
    click(g);
    expect(el(g).querySelector('[data-testid="hints-error"]')).toBeNull();
  });
});
