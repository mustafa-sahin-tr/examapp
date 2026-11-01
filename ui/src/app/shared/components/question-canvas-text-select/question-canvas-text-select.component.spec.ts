// Issue #582: metin üzerinde işaretleme oynatıcısı — seçim, düzeltme, noktalama paleti, geri bildirim / sayaç, çözüm, metin görünümü.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionRegion } from '../../../models/draws';
import { TextSelectFeedback } from '../../../models/text-select';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { QuestionCanvasTextSelectComponent, TEXT_SELECT_SAVE_DEBOUNCE_MS } from './question-canvas-text-select.component';

const box = (x: number, y = 0) => ({ x, y, width: 20, height: 10 });

const SELECT_PLAN = {
  version: 1,
  type: 'textSelect',
  mode: 'select',
  style: 'circle',
  tokens: [
    { id: 't1', text: 'Sonun', line: 0, boxes: [box(0)] },
    { id: 't2', text: 'da', line: 0, boxes: [box(30)] },
    { id: 't3', text: 'dolaşmış', line: 0, boxes: [box(60), box(0, 20)] },
    { id: 't4', text: 'gökte', line: 1, boxes: [box(30, 20)] },
  ],
};

const PUNCT_PLAN = {
  version: 1,
  type: 'textSelect',
  mode: 'punctuation',
  palette: ['.', ',', '!'],
  tokens: [
    { id: 't1', text: 'Eyvah', line: 0, boxes: [box(0)] },
    { id: 'g1', text: '( )', line: 0, gap: true, boxes: [box(30)] },
    { id: 't2', text: 'geldi', line: 1, boxes: [box(0, 20)] },
    { id: 'g2', text: '( )', line: 1, gap: true, boxes: [box(30, 20)] },
  ],
};

function region(): QuestionRegion {
  return { imageUrl: 'https://cdn.example.test/q1.webp', width: 200, height: 100, passage: null } as unknown as QuestionRegion;
}

describe('QuestionCanvasTextSelectComponent (#582)', () => {
  let fixture: ComponentFixture<QuestionCanvasTextSelectComponent>;
  let emitted: string[];

  const el = () => fixture.nativeElement as HTMLElement;
  const q = (testId: string) => el().querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;
  const tokenBox = (id: string) => el().querySelector(`[data-token="${id}"]`) as HTMLButtonElement;
  const word = (id: string) => el().querySelector(`[data-word="${id}"]`) as HTMLButtonElement;
  const last = () => JSON.parse(emitted.at(-1)!);

  afterEach(() => vi.restoreAllMocks());

  function setup(
    plan: object,
    inputs: {
      solution?: object | null;
      answer?: object | null;
      feedback?: TextSelectFeedback | null;
      showSolution?: boolean;
      practiceFeedback?: boolean;
      narrow?: boolean;
    } = {}
  ): void {
    vi.spyOn(window, 'matchMedia').mockImplementation(
      (query: string) => ({ matches: !!inputs.narrow, media: query }) as unknown as MediaQueryList
    );
    TestBed.configureTestingModule({ imports: [QuestionCanvasTextSelectComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(QuestionCanvasTextSelectComponent);
    emitted = [];
    fixture.componentInstance.answerPayloadChange.subscribe((p) => emitted.push(p));
    fixture.componentRef.setInput('questionRegion', region());
    fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(plan));
    fixture.componentRef.setInput('interactionSolutionJson', inputs.solution ? JSON.stringify(inputs.solution) : null);
    fixture.componentRef.setInput('answerPayloadJson', inputs.answer ? JSON.stringify(inputs.answer) : null);
    if (inputs.feedback !== undefined) fixture.componentRef.setInput('feedback', inputs.feedback);
    if (inputs.showSolution !== undefined) fixture.componentRef.setInput('showSolution', inputs.showSolution);
    if (inputs.practiceFeedback !== undefined) fixture.componentRef.setInput('practiceFeedback', inputs.practiceFeedback);
    fixture.componentRef.setInput('activityMode', true);
    fixture.detectChanges();
  }

  function click(target: HTMLElement) {
    target.click();
    fixture.detectChanges();
  }

  it('select_ImageViewTogglesTokens_PayloadInPlanOrder_StyleAndHyphenPart', () => {
    setup(SELECT_PLAN);

    expect(q('ts-overlay')).not.toBeNull();
    expect(el().querySelector('.ts-root')!.classList).toContain('ts-style--circle');
    expect(tokenBox('t1').style.left).toBe('0%');
    expect(tokenBox('t1').style.width).toBe('10%');
    expect(tokenBox('t1').getAttribute('aria-label')).toBe('Sonun');
    expect(tokenBox('t1').getAttribute('aria-pressed')).toBe('false');

    click(tokenBox('t4'));
    // Tirelenmiş kelimenin ikinci kutusu aynı token.
    click(el().querySelector('[data-token-part="t3"]') as HTMLElement);
    expect(last()).toEqual({ version: 1, type: 'textSelect', selected: ['t3', 't4'], values: {} });
    expect(tokenBox('t3').classList).toContain('is-selected');
    expect(tokenBox('t3').getAttribute('aria-label')).toBe('dolaşmış, seçili');

    click(tokenBox('t4'));
    expect(last().selected).toEqual(['t3']);
    // Kontrolden önce sayaç (hedef sayısı) yok.
    expect(q('ts-counter')).toBeNull();
  });

  it('narrowScreen_DefaultsToTextView_ByLines_AndViewCanBeSwitched', () => {
    setup(SELECT_PLAN, { narrow: true });

    expect(q('ts-text')).not.toBeNull();
    expect(q('ts-overlay')).toBeNull();
    const lines = Array.from(el().querySelectorAll('.ts-line')).map((line) =>
      Array.from(line.querySelectorAll('.ts-word__text')).map((w) => w.textContent?.trim())
    );
    expect(lines).toEqual([['Sonun', 'da', 'dolaşmış'], ['gökte']]);

    click(word('t2'));
    expect(last().selected).toEqual(['t2']);
    expect(word('t2').getAttribute('aria-pressed')).toBe('true');

    click(q('ts-view-image')!.querySelector('button')!);
    expect(q('ts-overlay')).not.toBeNull();
    expect(tokenBox('t2').classList).toContain('is-selected');
  });

  it('wideScreen_DefaultsToImageView', () => {
    setup(SELECT_PLAN, { narrow: false });
    expect(fixture.componentInstance.view()).toBe('image');
  });

  it('correct_SelectOpensCorrectionBar_TypingSavesDebounced_DeselectDropsValue', async () => {
    vi.useFakeTimers();
    try {
      setup({ ...SELECT_PLAN, mode: 'correct' });
      click(tokenBox('t1'));
      expect(last()).toEqual({ version: 1, type: 'textSelect', selected: ['t1'], values: {} });
      const input = q('ts-correction') as HTMLInputElement;
      expect(input).not.toBeNull();
      expect(q('ts-correction-bar')!.textContent).toContain('«Sonun» kelimesinin doğrusu');

      input.value = 'Sonunda';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(emitted.length).toBe(1);
      await vi.advanceTimersByTimeAsync(TEXT_SELECT_SAVE_DEBOUNCE_MS);
      expect(last().values).toEqual({ t1: 'Sonunda' });
      fixture.detectChanges();
      expect(tokenBox('t1').querySelector('.ts-box__fix')?.textContent).toBe('Sonunda');

      // Çubuk açık kelimeye yeniden dokunma seçimi ve düzeltmeyi kaldırır.
      click(tokenBox('t1'));
      expect(last()).toEqual({ version: 1, type: 'textSelect', selected: [], values: {} });
      expect(q('ts-correction-bar')).toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });

  it('punctuation_GapOpensPalette_SymbolSavesAndAdvances_ClearEmpties_WordsIgnored', () => {
    setup(PUNCT_PLAN);

    // Noktalamada kelimeler etkileşimsiz: görselde kutu yok.
    expect(tokenBox('t1')).toBeNull();

    click(tokenBox('g1'));
    const palette = q('ts-palette')!;
    expect(palette.getAttribute('aria-label')).toBe('Boşluk 1 noktalama paleti');
    const symbols = Array.from(palette.querySelectorAll<HTMLButtonElement>('[data-symbol]'));
    expect(symbols.map((b) => b.textContent)).toEqual(['.', ',', '!']);

    click(symbols[2]);
    expect(last()).toEqual({ version: 1, type: 'textSelect', selected: [], values: { g1: '!' } });
    expect(tokenBox('g1').querySelector('.ts-box__symbol')?.textContent).toBe('!');
    // Sıradaki boş boşluğa geçti.
    expect(q('ts-palette')!.getAttribute('aria-label')).toBe('Boşluk 2 noktalama paleti');

    click(q('ts-palette')!.querySelector('[data-symbol="."]') as HTMLElement);
    expect(last().values).toEqual({ g1: '!', g2: '.' });
    expect(q('ts-palette')).toBeNull();

    click(tokenBox('g2'));
    click(q('ts-gap-clear')!);
    expect(last().values).toEqual({ g1: '!' });
    click(q('ts-bar-close')!);
    expect(q('ts-palette')).toBeNull();
  });

  it('feedback_MarksCorrectExtraValue_AndShowsCounter', () => {
    setup(
      { ...SELECT_PLAN, mode: 'correct' },
      {
        answer: { version: 1, type: 'textSelect', selected: ['t1', 't3', 't4'], values: { t1: 'Sonunda', t3: 'x' } },
        feedback: {
          rows: [
            { id: 't1', correct: true },
            { id: 't3', correct: false, reason: 'value' },
            { id: 't4', correct: false, reason: 'extra' },
          ],
          found: 1,
          missing: 1,
          extra: 1,
          tooMany: false,
        },
      }
    );

    expect(tokenBox('t1').classList).toContain('is-correct');
    expect(tokenBox('t3').classList).toContain('is-value');
    expect(tokenBox('t4').classList).toContain('is-extra');
    expect(tokenBox('t4').querySelector('.ts-box__tag')?.textContent).toBe('fazla');
    expect(tokenBox('t4').getAttribute('aria-label')).toContain('fazla seçim');
    expect(tokenBox('t3').getAttribute('aria-label')).toContain('düzeltme yanlış');
    expect(q('ts-counter')!.textContent).toBe('Bulunan 1 · Kalan 1 · Fazla 1');
  });

  it('showSolution_ComparesOwnMarks_missedTargetDashed_extraRed', () => {
    setup(
      { ...SELECT_PLAN, mode: 'correct' },
      {
        solution: { targets: [{ tokenIds: ['t1', 't2'], accept: ['Sonunda'] }] },
        answer: { version: 1, type: 'textSelect', selected: ['t4'], values: {} },
        showSolution: true,
      }
    );

    // #679: öğrencinin seçimi kalır (fazla, kırmızı); kaçırılan hedef kesikli.
    expect(tokenBox('t4').classList).toContain('is-selected');
    expect(tokenBox('t4').classList).toContain('is-extra');
    expect(tokenBox('t1').classList).toContain('is-solution');
    expect(tokenBox('t2').classList).toContain('is-solution');
    expect(q('ts-counter')).toBeNull();
    click(tokenBox('t3'));
    expect(emitted.length).toBe(0);
  });

  it('showSolution_Compare_wrongCorrectionShowsAccepted', () => {
    setup(
      { ...SELECT_PLAN, mode: 'correct' },
      {
        solution: { targets: [{ tokenIds: ['t1', 't2'], accept: ['Sonunda'] }] },
        answer: { version: 1, type: 'textSelect', selected: ['t1'], values: { t1: 'Sonra' } },
        showSolution: true,
      }
    );
    expect(tokenBox('t1').classList).toContain('is-value');
    expect(tokenBox('t1').querySelector('.ts-box__fix')?.textContent).toBe('Sonra');
    expect(tokenBox('t1').querySelector('[data-target]')?.textContent).toBe('Sonunda');
  });

  it('showSolution_ShowsTargetsAndAcceptedCorrection_ReadOnly', () => {
    setup(
      { ...SELECT_PLAN, mode: 'correct' },
      {
        solution: { targets: [{ tokenIds: ['t1', 't2'], accept: ['Sonunda'] }] },
        showSolution: true,
      }
    );

    expect(tokenBox('t1').classList).toContain('is-solution');
    expect(tokenBox('t2').classList).toContain('is-solution');
    expect(tokenBox('t4').classList).not.toContain('is-selected');
    expect(tokenBox('t1').querySelector('.ts-box__fix')?.textContent).toBe('Sonunda');
    expect(q('ts-counter')).toBeNull();
    click(tokenBox('t3'));
    expect(emitted.length).toBe(0);
  });

  it('showSolution_Punctuation_ComparesOwnSymbols', () => {
    setup(PUNCT_PLAN, {
      solution: { gaps: { g1: { accept: ['!'] }, g2: { accept: ['.', ','] } } },
      answer: { version: 1, type: 'textSelect', selected: [], values: { g1: '!', g2: '!' } },
      showSolution: true,
      narrow: true,
    });
    expect(word('g1').textContent).toContain('( ! )');
    expect(word('g1').classList).toContain('is-correct');
    expect(word('g2').textContent).toContain('( ! )');
    expect(word('g2').classList).toContain('is-wrong');
    expect(word('g2').querySelector('[data-target]')?.textContent).toBe('. / ,');
  });

  it('showSolution_Punctuation_ShowsAcceptedSymbolsInGaps', () => {
    setup(PUNCT_PLAN, { solution: { gaps: { g1: { accept: ['!'] }, g2: { accept: ['.', ','] } } }, showSolution: true, narrow: true });
    expect(word('g1').textContent).toContain('( ! )');
    expect(word('g2').textContent).toContain('( . / , )');
    expect(word('g2').classList).toContain('is-solution');
  });

  it('practiceFeedback_GradesOnClient_AfterSelection', () => {
    setup(SELECT_PLAN, {
      solution: { targets: [{ tokenIds: ['t1', 't2'] }, { tokenIds: ['t4'] }] },
      practiceFeedback: true,
    });
    expect(q('ts-counter')).toBeNull();

    click(tokenBox('t2'));
    click(tokenBox('t3'));
    expect(tokenBox('t2').classList).toContain('is-correct');
    expect(tokenBox('t3').classList).toContain('is-extra');
    // Örnek soruda select / correct sayacı yok (kalan sayısı hedef sayısını ele verirdi).
    expect(q('ts-counter')).toBeNull();
  });

  it('punctuation_TextView_WordsArePlainSpans_GapsAreButtons', () => {
    setup(PUNCT_PLAN, { narrow: true });
    const w = el().querySelector('[data-word="t1"]') as HTMLElement;
    expect(w.tagName).toBe('SPAN');
    expect(w.hasAttribute('tabindex')).toBe(false);
    expect(w.getAttribute('role')).toBeNull();
    expect(w.textContent?.trim()).toBe('Eyvah');
    expect(word('g1').tagName).toBe('BUTTON');
    expect(el().querySelectorAll('.ts-text button.ts-word').length).toBe(2);
  });

  it('tooManyFeedback_ShowsMessage_NoMarks_CounterKept', () => {
    setup(SELECT_PLAN, {
      answer: { version: 1, type: 'textSelect', selected: ['t1', 't2', 't3', 't4'] },
      feedback: { rows: [], found: 0, missing: 1, extra: 4, tooMany: true },
    });
    expect(q('ts-too-many')!.textContent).toContain('Çok fazla kelime seçtin');
    expect(el().querySelectorAll('.is-correct, .is-extra, .is-value').length).toBe(0);
    expect(q('ts-counter')!.textContent).toBe('Bulunan 0 · Kalan 1 · Fazla 4');
  });

  it('punctuationFeedback_MarksGaps_AndCounter', () => {
    setup(PUNCT_PLAN, {
      answer: { version: 1, type: 'textSelect', values: { g1: '!', g2: ',' } },
      feedback: { rows: [{ id: 'g1', correct: true }, { id: 'g2', correct: false }], found: 0, missing: 0, extra: 0, tooMany: false },
    });
    expect(tokenBox('g1').classList).toContain('is-correct');
    expect(tokenBox('g2').classList).toContain('is-wrong');
    expect(q('ts-counter')!.textContent).toBe('Doğru 1 · Yanlış 1');
  });

  it('restoresAnswerPayload_AndClearAllEmitsEmpty', () => {
    setup(SELECT_PLAN, { answer: { version: 1, type: 'textSelect', selected: ['t2', 'zz', 't1'] } });
    expect(tokenBox('t1').classList).toContain('is-selected');
    expect(tokenBox('t2').classList).toContain('is-selected');

    click(el().querySelector('.ts-clear') as HTMLElement);
    expect(last()).toEqual({ version: 1, type: 'textSelect', selected: [], values: {} });
  });
});
