import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { Translation } from '@jsverse/transloco';

import publicPagesEn from '../../../../../public/i18n/public-pages/en.json';
import publicPagesTr from '../../../../../public/i18n/public-pages/tr.json';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import { ScoreCalculatorComponent } from './score-calculator.component';

function keysOf(dictionary: Translation, prefix = ''): string[] {
  return Object.entries(dictionary).flatMap(([key, value]) =>
    value && typeof value === 'object' ? keysOf(value as Translation, `${prefix}${key}.`) : [`${prefix}${key}`],
  );
}

describe('ScoreCalculatorComponent (#694)', () => {
  let fixture: ComponentFixture<ScoreCalculatorComponent>;
  let el: HTMLElement;

  const type = async (testId: string, value: string) => {
    const input = el.querySelector<HTMLInputElement>(`[data-testid="${testId}"]`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const text = (testId: string) => el.querySelector(`[data-testid="${testId}"]`)?.textContent ?? '';

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        ScoreCalculatorComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { 'public-pages/tr': publicPagesTr, 'public-pages/en': publicPagesEn } }),
      ],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(ScoreCalculatorComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => fixture.destroy());

  it('dictionaries_HaveTheSameKeys', () => {
    expect(keysOf(publicPagesEn.scoreCalculator).sort()).toEqual(keysOf(publicPagesTr.scoreCalculator).sort());
  });

  it('result_IsLabelledAsAnEstimate_AndNamesTheYear', () => {
    expect(text('calc-result')).toContain('2026');
    expect(text('calc-estimate')).toContain('resmî sonuç değil');
  });

  it('emptyForm_ShowsNoScore', () => {
    expect(el.querySelector('[data-testid="calc-empty"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="score-lgs"]')).toBeNull();
  });

  it('typing_UpdatesNetAndScore', async () => {
    await type('turkish-correct', '12');
    const before = text('score-lgs');
    await type('turkish-wrong', '3');
    expect(text('turkish-net')).toContain('11,00');
    expect(text('score-lgs')).toContain('≈');
    expect(text('score-lgs')).not.toEqual(before);
  });

  it('invalidInput_IsRejectedWithAMessage_AndHidesTheScore', async () => {
    await type('turkish-correct', '15');
    await type('turkish-wrong', '6');
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('20');
    expect(el.querySelector('[data-testid="score-lgs"]')).toBeNull();
  });

  it('#896: boxes hint the range, the bad row says its limit, and the result names the row to fix', async () => {
    expect(el.querySelector<HTMLInputElement>('[data-testid="turkish-correct"]')!.placeholder).toBe('0–20');
    await type('mathematics-correct', '15');
    await type('mathematics-wrong', '7');

    expect(el.querySelector('[data-testid="mathematics-correct"]')!.getAttribute('aria-invalid')).toBe('true');
    expect(text('mathematics-net')).toBe('En çok 20');
    expect(text('calc-fix')).toBe('Puanı görmek için düzelt: Matematik.');

    await type('mathematics-wrong', '3');
    expect(el.querySelector('[data-testid="calc-fix"]')).toBeNull();
    expect(text('calc-total-net')).toContain('14,00');
  });

  it('switchingExam_ShowsYksScoreTypes', async () => {
    fixture.componentInstance.selectExam('yks');
    fixture.detectChanges();
    await fixture.whenStable();
    await type('tytTurkish-correct', '30');
    for (const id of ['tyt', 'say', 'ea', 'soz']) {
      expect(el.querySelector(`[data-testid="score-${id}"]`), id).not.toBeNull();
    }
    expect(text('score-tyt')).toContain('≈');
    expect(text('score-say')).not.toContain('≈');
  });

  it('infoDialog_ExplainsTheMethod_WithValuesAndSourcesOfTheSelectedExam', async () => {
    fixture.componentInstance.selectExam('yks');
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('[data-testid="calc-info"]')!.click();
    await fixture.whenStable();
    const dialog = document.querySelector('app-score-calculator-info-dialog')!;
    expect(dialog.querySelectorAll('[data-testid="info-steps"] li').length).toBe(4);
    expect(dialog.querySelectorAll('[data-testid="info-values"] tbody tr').length).toBe(15);
    expect(dialog.querySelector('[data-testid="info-values"] tbody tr')?.textContent).toContain('20,5');
    expect(dialog.querySelector('[data-testid="info-weights"]')?.textContent).toContain('%33');
    expect(dialog.querySelector('[data-testid="info-sources"] a')?.getAttribute('href')).toContain('osym.gov.tr');
    TestBed.inject(MatDialog).closeAll();
  });

  it('input_IsNeverSentAnywhere', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem');
    await type('turkish-correct', '12');
    TestBed.inject(HttpTestingController).expectNone(() => true);
    expect(setItem).not.toHaveBeenCalled();
    setItem.mockRestore();
  });
});
