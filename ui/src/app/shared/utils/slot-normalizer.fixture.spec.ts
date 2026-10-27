/**
 * Issue #575: ortak normalleştirme fixture'ı — sunucu (`SlotAnswerNormalizer`, xUnit) ile aynı dosyayı okur. Fixture
 * doğruluk kaynağıdır: bir vektör düşerse TS normalleştirici düzeltilir, fixture değil.
 */
import fixture from '../../../../../shared/fixtures/slot-normalization.v1.json';

import { SlotKind, SlotSolution, parseSlotFillPlan, parseSlotFillSolution, solutionOf } from '../../models/slot-fill';
import { isSlotAnswerCorrect } from './slot-normalizer';

interface FixtureVector {
  kind: SlotKind;
  solution: Record<string, unknown>;
  answer: string | null;
  expected: boolean;
  note?: string;
}

/**
 * Vektörün çözümü uygulamanın gerçek ayrıştırıcısından geçer (`parseSlotFillSolution`, varsayılanlar dahil): tek kutulu
 * bir plan kutunun türünü belirler.
 */
function solutionFor(kind: SlotKind, solution: Record<string, unknown>): SlotSolution {
  const input = kind === 'palette' ? { kind, symbols: ['∠'] } : kind === 'select' ? { kind, options: [{ id: 'o1', label: 'a' }] } : { kind, entry: 'plain' };
  const plan = parseSlotFillPlan(JSON.stringify({ version: 1, type: 'slotFill', slots: [{ id: 'x', box: { x: 0, y: 0, width: 1, height: 1 }, input }] }));
  const parsed = solutionOf(parseSlotFillSolution(JSON.stringify({ x: solution }), plan), 'x');
  if (!parsed) throw new Error(`çözüm ayrıştırılamadı: ${JSON.stringify(solution)}`);
  return parsed;
}

const vectors = (fixture as unknown as { version: number; vectors: FixtureVector[] }).vectors;

describe('slot normalization shared fixture (#575)', () => {
  it('fixtureIsV1AndCoversAllKinds', () => {
    expect((fixture as unknown as { version: number }).version).toBe(1);
    expect(vectors.length).toBeGreaterThan(100);
    expect(new Set(vectors.map((v) => v.kind))).toEqual(new Set(['number', 'text', 'palette']));
  });

  it.each(vectors.map((v, i) => [i, v] as const))('vector %i', (_i, v) => {
    const actual = isSlotAnswerCorrect(v.kind, solutionFor(v.kind, v.solution), v.answer);
    expect({ note: v.note, kind: v.kind, solution: v.solution, answer: v.answer, result: actual }).toEqual({
      note: v.note,
      kind: v.kind,
      solution: v.solution,
      answer: v.answer,
      result: v.expected,
    });
  });
});
