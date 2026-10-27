// Issue #583: açık uçlu cevap yazarlığı — plan / çözüm üretimi ve sunucu kurallarıyla aynı denetim.
import {
  buildOpenTextInspector,
  buildOpenTextPlan,
  buildOpenTextSolution,
  createOpenTextState,
  openTextIssues,
  parseLimitInput,
  setOpenTextLimit,
  setOpenTextSolutionText,
} from './open-text-authoring';

describe('open-text authoring (#583)', () => {
  it('defaultState_PlanWithoutLimits_FiveLines_NoSolution', () => {
    const state = createOpenTextState();
    expect(buildOpenTextPlan(state)).toEqual({ version: 1, type: 'openText', minWords: null, maxWords: null, lines: 5 });
    expect(buildOpenTextSolution(state)).toBeNull();
    expect(openTextIssues(state)).toEqual([]);
  });

  it('parseLimitInput_BlankIsNull_NonIntegerIsNaN', () => {
    expect(parseLimitInput('')).toBeNull();
    expect(parseLimitInput('  ')).toBeNull();
    expect(parseLimitInput(' 25 ')).toBe(25);
    expect(parseLimitInput('2.5')).toBeNaN();
    expect(parseLimitInput('-3')).toBeNaN();
  });

  it('issues_RangeMinAboveMaxLinesAndLength', () => {
    let state = setOpenTextLimit(createOpenTextState(), 'minWords', 0);
    state = setOpenTextLimit(state, 'maxWords', 2001);
    state = setOpenTextLimit(state, 'lines', 31);
    expect(openTextIssues(state).map((i) => i.code)).toEqual(['minWordsRange', 'maxWordsRange', 'linesRange']);

    state = setOpenTextLimit(setOpenTextLimit(createOpenTextState(), 'minWords', 50), 'maxWords', 10);
    expect(openTextIssues(state).map((i) => i.code)).toEqual(['minAboveMax']);

    state = setOpenTextLimit(createOpenTextState(), 'maxWords', Number.NaN);
    expect(openTextIssues(state).map((i) => i.code)).toEqual(['maxWordsRange']);
    // Geçersiz sınır plana gitmez (kayıt zaten engellenir).
    expect(buildOpenTextPlan(state).maxWords).toBeNull();

    state = setOpenTextSolutionText(createOpenTextState(), 'sampleAnswer', 'x'.repeat(4001));
    state = setOpenTextSolutionText(state, 'rubric', 'y'.repeat(4001));
    expect(openTextIssues(state).map((i) => i.code)).toEqual(['sampleTooLong', 'rubricTooLong']);
  });

  it('solution_OnlyFilledFields_TrimmedAndControlCharsStripped', () => {
    let state = setOpenTextSolutionText(createOpenTextState(), 'sampleAnswer', '  Örnek\u0000 cevap\nikinci satır  ');
    expect(buildOpenTextSolution(state)).toEqual({ sampleAnswer: 'Örnek cevap\nikinci satır' });
    state = setOpenTextSolutionText(state, 'rubric', 'Ölçüt');
    expect(buildOpenTextSolution(state)).toEqual({ sampleAnswer: 'Örnek cevap\nikinci satır', rubric: 'Ölçüt' });
    expect(buildOpenTextInspector(state)).toMatchObject({ sampleAnswer: '  Örnek cevap\nikinci satır  ', rubric: 'Ölçüt', issues: [] });
  });
});
