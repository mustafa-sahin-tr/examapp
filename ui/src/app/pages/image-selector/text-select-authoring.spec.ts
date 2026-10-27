// Issue #582: textSelect yazarlığının saf kısmı — satır ekleme, hedef gruplama / ayırma, gap sembolleri, plan / çözüm, sorunlar.
import { parseTextSelectPlan, tokenizeTextLines } from '../../models/text-select';
import {
  addTextLine,
  buildTextSelectInspector,
  buildTextSelectPlan,
  buildTextSelectSolution,
  createTextSelectState,
  parsePaletteText,
  removeTextToken,
  setTextSelectMode,
  setTextTokenText,
  splitTextTarget,
  textSelectIssues,
  textSelectStateFromPlan,
  toggleGapSymbol,
  toggleTextTarget,
} from './text-select-authoring';

const lineBox = (y: number) => ({ x: 0, y, width: 200, height: 20 });

function withText(...lines: string[]) {
  return lines.reduce((state, text, i) => addTextLine(state, lineBox(i * 30), text), createTextSelectState());
}

describe('text-select authoring (#582)', () => {
  it('addTextLine_TokenizesLikeServer_NewIdsDoNotCollide_LinesRenumberedByY', () => {
    let state = withText('Bir gün', 'orman( )');
    expect(state.tokens.map((t) => [t.id, t.text, t.line, !!t.gap])).toEqual([
      ['t1', 'Bir', 0, false],
      ['t2', 'gün', 0, false],
      ['t3', 'orman', 1, false],
      ['g1', '( )', 1, true],
    ]);
    // Aynı kutuyla tokenizer'ın kendisi aynı kutuları üretir.
    expect(state.tokens[0].boxes).toEqual(tokenizeTextLines([{ text: 'Bir gün', box: lineBox(0) }])[0].boxes);

    // Araya (dikeyde ortaya) yeni satır: id'ler boş numaradan, satırlar yeniden sıralanır.
    state = addTextLine(state, lineBox(15), 'ara satır');
    expect(state.tokens.map((t) => [t.id, t.line])).toEqual([
      ['t1', 0],
      ['t2', 0],
      ['t4', 1],
      ['t5', 1],
      ['t3', 2],
      ['g1', 2],
    ]);
    expect(state.selectedTokenId).toBe('t4');
    expect(addTextLine(state, lineBox(90), '   ')).toBe(state);
  });

  it('toggleTarget_AdjacentWordsJoin_BridgingMerges_RetogglingRemoves_SplitSeparates', () => {
    let state = setTextSelectMode(withText('Sonun da gel di kuş'), 'correct');
    state = toggleTextTarget(state, 't1');
    state = toggleTextTarget(state, 't2');
    state = toggleTextTarget(state, 't4');
    expect(state.targets.map((t) => t.tokenIds)).toEqual([['t1', 't2'], ['t4']]);
    // Aradaki kelime iki hedefi birleştirir.
    state = toggleTextTarget(state, 't3');
    expect(state.targets.map((t) => t.tokenIds)).toEqual([['t1', 't2', 't3', 't4']]);
    state = toggleTextTarget(state, 't2');
    expect(state.targets.map((t) => t.tokenIds)).toEqual([['t1', 't3', 't4']]);
    state = splitTextTarget({ ...state, targets: [{ ...state.targets[0], accept: ['x'] }] }, 0);
    expect(state.targets).toEqual([
      { tokenIds: ['t1'], accept: ['x'], optionalSuffixes: [] },
      { tokenIds: ['t3'], accept: [], optionalSuffixes: [] },
      { tokenIds: ['t4'], accept: [], optionalSuffixes: [] },
    ]);
  });

  it('toggleTarget_NewTargetsFollowTextOrder_MaxEightTokensPerTarget', () => {
    let state = withText('a b c d e f g h i j');
    state = toggleTextTarget(state, 't5');
    state = toggleTextTarget(state, 't1');
    expect(state.targets.map((t) => t.tokenIds)).toEqual([['t1'], ['t5']]);
    for (let i = 2; i <= 10; i++) if (i !== 5) state = toggleTextTarget(state, `t${i}`);
    expect(state.targets.every((t) => t.tokenIds.length <= 8)).toBe(true);
    expect(state.targets.flatMap((t) => t.tokenIds).sort()).toHaveLength(10);
  });

  it('removeToken_DropsFromTargetsAndGapAnswers_TextEditKeepsGapFixed', () => {
    let state = setTextSelectMode(withText('Ali( ) geldi'), 'punctuation');
    state = toggleGapSymbol(state, 'g1', '.');
    state = removeTextToken(state, 'g1');
    expect(Object.keys(state.gapAccept)).toEqual([]);
    state = setTextTokenText(state, 't1', '  Veli  ');
    expect(state.tokens[0].text).toBe('Veli');
    expect(setTextTokenText(state, 't1', '  ')).toBe(state);
    expect(parsePaletteText(' . ,  ! . ... ')).toEqual(['.', ',', '!', '...']);
    // 64 sınırı vekil çiftinin ortasına düşerse yarım karakter atılır.
    const long = setTextTokenText(state, 't1', 'a'.repeat(63) + '😀');
    expect(long.tokens[0].text).toBe('a'.repeat(63));
  });

  it('addTextLine_RespectsLineLimit', () => {
    let state = createTextSelectState();
    for (let i = 0; i < 300; i++) state = addTextLine(state, lineBox(i * 30), 'k');
    expect(state.tokens.at(-1)!.line).toBe(299);
    expect(addTextLine(state, lineBox(99999), 'fazla')).toBe(state);
  });

  it('planAndSolution_ByMode_NullWhenNoAnswers', () => {
    const state = withText('Sonun da');
    expect(buildTextSelectSolution(state)).toBeNull();
    const plan = buildTextSelectPlan(toggleTextTarget(state, 't1'));
    // Üretilen plan oynatıcının ayrıştırıcısından geçer, yazarlığa geri yüklenince aynı plan çıkar.
    const parsed = parseTextSelectPlan(JSON.stringify(plan))!;
    expect(buildTextSelectPlan(textSelectStateFromPlan(parsed))).toEqual(plan);

    const correct = toggleTextTarget(setTextSelectMode(state, 'correct'), 't1');
    expect(buildTextSelectSolution(correct)).toEqual({ targets: [{ tokenIds: ['t1'], accept: [] }] });
    expect(textSelectIssues(correct)).toEqual([{ code: 'missingAccept', no: 1 }]);
  });

  it('issues_MatchServerRules', () => {
    expect(textSelectIssues(createTextSelectState())).toEqual([{ code: 'noText' }]);
    const punct = setTextSelectMode(withText('yok boşluk'), 'punctuation');
    expect(textSelectIssues(punct).map((i) => i.code)).toEqual(['noGaps']);
    expect(textSelectIssues({ ...punct, palette: [] }).map((i) => i.code)).toEqual(['noGaps', 'paletteCount']);
    expect(textSelectIssues(withText('a ( ) b')).map((i) => i.code)).toEqual(['gapsNeedPunctuation', 'noTargets']);

    const inspector = buildTextSelectInspector(toggleTextTarget(withText('Sonun da', 'kuş'), 't2'));
    expect(inspector.lines.map((l) => l.tokens.map((t) => [t.id, t.targetNo]))).toEqual([
      [
        ['t1', null],
        ['t2', 1],
      ],
      [['t3', null]],
    ]);
    expect(inspector.targets[0].text).toBe('da');
    expect(inspector.selected?.id).toBe('t3');
  });
});
