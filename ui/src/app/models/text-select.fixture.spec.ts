/**
 * Issue #582: ortak satır → token bölme fixture'ı — sunucu (`TextSelectTokenizer`, xUnit `TextTokenizeFixtureTests`) ile aynı
 * dosyayı okur. Fixture doğruluk kaynağıdır: bir vaka düşerse TS tokenizer düzeltilir, fixture değil.
 */
import fixture from '../../../../shared/fixtures/text-tokenize.v1.json';

import { InteractionBox } from './interaction-ids';
import { TEXT_PIECE_PATTERN_SOURCE, tokenizeTextLines } from './text-select';

interface FixtureToken {
  id: string;
  text: string;
  line: number;
  gap?: boolean;
  boxes: InteractionBox[];
}

interface FixtureCase {
  name: string;
  note: string;
  lines: Array<{ text: string | null; box: InteractionBox }>;
  expected: FixtureToken[];
}

const root = fixture as unknown as { $comment: string[]; version: number; cases: FixtureCase[] };

describe('text-select tokenizer shared fixture (#582)', () => {
  it('fixtureIsV1_AndCommentPinsThePiecePattern', () => {
    expect(root.version).toBe(1);
    expect(root.cases.length).toBeGreaterThanOrEqual(6);
    // Fixture açıklamasındaki desen TS'deki desenle birebir (C# testi de aynı satırı kilitler).
    expect(root.$comment.some((line) => line.includes(TEXT_PIECE_PATTERN_SOURCE))).toBe(true);
  });

  it.each(root.cases.map((c) => [c.name, c] as const))('case %s', (_name, c) => {
    const actual = tokenizeTextLines(c.lines).map((t) => ({ id: t.id, text: t.text, line: t.line, gap: t.gap, boxes: t.boxes }));
    const expected = c.expected.map((t) => ({ id: t.id, text: t.text, line: t.line, gap: t.gap === true, boxes: t.boxes }));
    expect({ note: c.note, tokens: actual }).toEqual({ note: c.note, tokens: expected });
  });
});
