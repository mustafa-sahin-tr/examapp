// Issue #582: textSelect model yardımcıları — plan / çözüm / cevap ayrıştırma, payload, istemci kontrolü, hedef gruplama.
import {
  TOO_MANY_EXTRA_MIN,
  buildTextSelectPayload,
  clipText,
  gradeTextSelect,
  groupConsecutiveTokens,
  isBlankTextSelectPayload,
  isTextSelectType,
  parseTextSelectAnswer,
  parseTextSelectFeedback,
  parseTextSelectPlan,
  parseTextSelectSolution,
  textSelectFeedbackFromCounts,
  tokensByLine,
  withSelection,
  withTextValue,
} from './text-select';

const box = { x: 0, y: 0, width: 10, height: 10 };

const PLAN_JSON = JSON.stringify({
  version: 1,
  type: 'textSelect',
  mode: 'correct',
  tokens: [
    { id: 't1', text: 'Sonun', line: 0, boxes: [box] },
    { id: 't2', text: 'da', line: 0, boxes: [box] },
    { id: 't3', text: 'gökte', line: 1, boxes: [box] },
    { id: 't4', text: 'ki', line: 1, boxes: [box] },
    { id: 't5', text: 'kuş', line: 1, boxes: [box] },
  ],
});

const PUNCT_JSON = JSON.stringify({
  version: 1,
  type: 'textSelect',
  mode: 'punctuation',
  palette: ['.', ' , ', '!', '.'],
  tokens: [
    { id: 't1', text: 'Eyvah', line: 0, boxes: [box] },
    { id: 'g1', text: '( )', line: 0, gap: true, boxes: [box] },
    { id: 'g2', text: '', line: 0, gap: true, boxes: [box] },
  ],
});

describe('text-select model (#582)', () => {
  it('parsePlan_DefaultsStyle_DropsBrokenTokens_PaletteOnlyInPunctuation', () => {
    const plan = parseTextSelectPlan(
      JSON.stringify({
        type: 'TextSelect',
        mode: 'select',
        palette: ['.'],
        tokens: [
          { id: 't1', text: 'a', line: 0, boxes: [box] },
          { id: 't1', text: 'dup', line: 0, boxes: [box] },
          { id: '__proto__', text: 'x', line: 0, boxes: [box] },
          { id: 't2', text: '  ', line: 0, boxes: [box] },
          { id: 't3', text: 'b', line: 0, boxes: [] },
        ],
      })
    )!;
    expect(isTextSelectType('TEXTSELECT')).toBe(true);
    expect(plan.style).toBe('highlight');
    expect(plan.palette).toEqual([]);
    expect(plan.tokens.map((t) => t.id)).toEqual(['t1']);

    const punct = parseTextSelectPlan(PUNCT_JSON)!;
    expect(punct.palette).toEqual(['.', ',', '!']);
    expect(punct.tokens.filter((t) => t.gap).map((t) => t.text)).toEqual(['( )', '( )']);
    expect(parseTextSelectPlan(JSON.stringify({ type: 'textSelect', mode: 'nope', tokens: [] }))).toBeNull();
    expect(parseTextSelectPlan('{bozuk')).toBeNull();
  });

  it('parseSolution_SkipsGapUnknownAndReusedTokens_CorrectNeedsAccept', () => {
    const plan = parseTextSelectPlan(PLAN_JSON)!;
    const solution = parseTextSelectSolution(
      JSON.stringify({
        targets: [
          { tokenIds: ['t1', 't2', 'zz'], accept: ['Sonunda'] },
          { tokenIds: ['t2', 't3'], accept: ['gökteki'] },
          { tokenIds: ['t5'] },
        ],
      }),
      plan
    );
    expect(solution.targets).toEqual([
      { tokenIds: ['t1', 't2'], accept: ['Sonunda'], optionalSuffixes: [] },
      { tokenIds: ['t3'], accept: ['gökteki'], optionalSuffixes: [] },
    ]);
    const punct = parseTextSelectPlan(PUNCT_JSON)!;
    const gaps = parseTextSelectSolution(JSON.stringify({ gaps: { g1: { accept: ['!'] }, t1: { accept: ['.'] } } }), punct);
    expect(Object.keys(gaps.gaps)).toEqual(['g1']);
  });

  it('parseAnswer_NormalizesByMode', () => {
    const plan = parseTextSelectPlan(PLAN_JSON)!;
    const answer = parseTextSelectAnswer(
      JSON.stringify({ type: 'textSelect', selected: ['t3', 't1', 'zz', 't1'], values: { t1: 'Sonunda', t5: 'x', t3: '  ' } }),
      plan
    );
    expect(answer.selected).toEqual(['t1', 't3']);
    expect({ ...answer.values }).toEqual({ t1: 'Sonunda' });

    const punct = parseTextSelectPlan(PUNCT_JSON)!;
    const p = parseTextSelectAnswer(JSON.stringify({ type: 'textSelect', selected: ['t1'], values: { g1: ' ! ', g2: '?', t1: '.' } }), punct);
    expect(p.selected).toEqual([]);
    expect({ ...p.values }).toEqual({ g1: '!' });
    expect(parseTextSelectAnswer(JSON.stringify({ type: 'match', selected: ['t1'] }), plan).selected).toEqual([]);
  });

  it('payload_Blank_AndSelectionHelpers', () => {
    const plan = parseTextSelectPlan(PLAN_JSON)!;
    let answer = withSelection(parseTextSelectAnswer(null), plan, 't3', true);
    answer = withSelection(answer, plan, 't1', true);
    answer = withTextValue(answer, 't1', 'Sonunda');
    expect(JSON.parse(buildTextSelectPayload(answer))).toEqual({
      version: 1,
      type: 'textSelect',
      selected: ['t1', 't3'],
      values: { t1: 'Sonunda' },
    });
    answer = withSelection(answer, plan, 't1', false);
    expect({ ...answer.values }).toEqual({});
    expect(isBlankTextSelectPayload(JSON.stringify({ type: 'textSelect', selected: [], values: { g1: ' ' } }))).toBe(true);
    expect(isBlankTextSelectPayload(JSON.stringify({ type: 'textSelect', selected: ['t1'] }))).toBe(false);
    expect(isBlankTextSelectPayload(null)).toBe(true);
    expect(isBlankTextSelectPayload(JSON.stringify({ type: 'slotFill', values: {} }))).toBe(false);
  });

  it('grade_Correct_FoundMissingExtraAndValueReason_LikeServer', () => {
    const plan = parseTextSelectPlan(PLAN_JSON)!;
    const solution = parseTextSelectSolution(
      JSON.stringify({ targets: [{ tokenIds: ['t1', 't2'], accept: ['Sonunda'] }, { tokenIds: ['t3', 't4'], accept: ['gökteki'] }] }),
      plan
    );
    const answer = parseTextSelectAnswer(
      JSON.stringify({ type: 'textSelect', selected: ['t2', 't3', 't5'], values: { t2: 'sonunda.', t3: 'gökte' } }),
      plan
    );
    expect(gradeTextSelect(plan, solution, answer)).toEqual({
      rows: [
        { id: 't2', correct: true, reason: null },
        { id: 't3', correct: false, reason: 'value' },
        { id: 't5', correct: false, reason: 'extra' },
      ],
      found: 1,
      missing: 1,
      extra: 1,
      tooMany: false,
      correctCount: 1,
      totalCount: 3,
    });
    expect(gradeTextSelect(plan, parseTextSelectSolution(null, plan), answer)).toBeNull();
  });

  it('grade_SelectAnyTokenFindsTarget_PunctuationPerGap', () => {
    const plan = parseTextSelectPlan(PLAN_JSON.replace('"correct"', '"select"'))!;
    const solution = parseTextSelectSolution(JSON.stringify({ targets: [{ tokenIds: ['t1', 't2'] }] }), plan);
    const result = gradeTextSelect(plan, solution, parseTextSelectAnswer(JSON.stringify({ type: 'textSelect', selected: ['t1', 't2'] }), plan))!;
    expect(result.rows.every((r) => r.correct)).toBe(true);
    expect([result.found, result.missing, result.extra, result.totalCount]).toEqual([1, 0, 0, 1]);

    const punct = parseTextSelectPlan(PUNCT_JSON)!;
    const gaps = parseTextSelectSolution(JSON.stringify({ gaps: { g1: { accept: ['!'] }, g2: { accept: ['.', ','] } } }), punct);
    const graded = gradeTextSelect(punct, gaps, parseTextSelectAnswer(JSON.stringify({ type: 'textSelect', values: { g1: '!' } }), punct))!;
    expect(graded.rows.map((r) => [r.id, r.correct])).toEqual([
      ['g1', true],
      ['g2', false],
    ]);
    expect([graded.correctCount, graded.totalCount]).toEqual([1, 2]);
  });

  it('feedback_ParsesServerJson_AndDerivesCountsFromCheckTotals', () => {
    expect(
      parseTextSelectFeedback('{"rows":[{"id":"t1","correct":true},{"id":"t5","correct":false,"reason":"extra"}],"found":1,"missing":2,"extra":1}')
    ).toEqual({
      rows: [
        { id: 't1', correct: true, reason: null },
        { id: 't5', correct: false, reason: 'extra' },
      ],
      found: 1,
      missing: 2,
      extra: 1,
      tooMany: false,
    });
    // Sunucu: total = hedef + fazla → kalan = total − bulunan − fazla.
    expect(textSelectFeedbackFromCounts([{ id: 't5', correct: false, reason: 'extra' }], 1, 4)).toEqual({
      rows: [{ id: 't5', correct: false, reason: 'extra' }],
      found: 1,
      missing: 2,
      extra: 1,
      tooMany: false,
    });
    expect(parseTextSelectFeedback('null')).toBeNull();
    expect(parseTextSelectFeedback('{"rows":[{"id":"t1","correct":true}],"found":1,"missing":0,"extra":5,"tooMany":true}')).toEqual({
      rows: [],
      found: 1,
      missing: 0,
      extra: 5,
      tooMany: true,
    });
  });

  it('grade_TooManyExtra_HidesRows_AboveMaxOfThreeAndTargetCount', () => {
    const plan = parseTextSelectPlan(PLAN_JSON.replace('"correct"', '"select"'))!;
    const solution = parseTextSelectSolution(JSON.stringify({ targets: [{ tokenIds: ['t1'] }] }), plan);
    const pick = (...ids: string[]) => parseTextSelectAnswer(JSON.stringify({ type: 'textSelect', selected: ids }), plan);
    // 3 fazla = eşik (aşılmadı); 4 fazla > max(3, 1).
    expect(gradeTextSelect(plan, solution, pick('t2', 't3', 't4'))!.tooMany).toBe(false);
    const result = gradeTextSelect(plan, solution, pick('t1', 't2', 't3', 't4', 't5'))!;
    expect(result).toEqual(expect.objectContaining({ rows: [], tooMany: true, found: 1, missing: 0, extra: 4, correctCount: 1, totalCount: 5 }));
    expect(TOO_MANY_EXTRA_MIN).toBe(3);
  });

  it('clipText_DoesNotSplitSurrogatePair', () => {
    expect(clipText('ab😀', 3)).toBe('ab');
    expect(clipText('ab😀', 4)).toBe('ab😀');
    expect(clipText('abc', 5)).toBe('abc');
  });

  it('groupConsecutiveTokens_And_TokensByLine', () => {
    const plan = parseTextSelectPlan(PLAN_JSON)!;
    expect(groupConsecutiveTokens(['t4', 't1', 't2', 't5'], plan.tokens)).toEqual([['t1', 't2'], ['t4', 't5']]);
    const punct = parseTextSelectPlan(PUNCT_JSON)!;
    // Gap sırayı bölmez ama hedefe girmez.
    expect(groupConsecutiveTokens(['t1'], punct.tokens)).toEqual([['t1']]);
    expect(tokensByLine(plan.tokens).map((l) => [l.line, l.tokens.map((t) => t.id)])).toEqual([
      [0, ['t1', 't2']],
      [1, ['t3', 't4', 't5']],
    ]);
  });
});
