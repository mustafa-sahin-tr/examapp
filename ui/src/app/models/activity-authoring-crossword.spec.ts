// Issue #586: çengel bulmaca formu — doğrulama, plan + çözüm üretimi, geri kurma ve otomatik yerleştirme.
import {
  CrosswordForm,
  autoLayout,
  buildCrosswordDefinition,
  crosswordFormFromDraft,
  parseCrosswordDefinition,
  validateCrosswordForm,
} from './activity-authoring-crossword';
import { parseDefinition } from './activity-authoring';
import { clueCells, parseCrosswordPlan, parseCrosswordSolution } from './crossword';

const FORM: CrosswordForm = {
  kind: 'crossword',
  rows: 3,
  cols: 4,
  entries: [
    { answer: 'kalp', clue: 'Organ', dir: 'across', row: 0, col: 0 },
    { answer: 'kir', clue: 'Kirli', dir: 'down', row: 0, col: 0 },
    { answer: 'raf', clue: 'Raf', dir: 'across', row: 2, col: 0 },
  ],
};

const keys = (form: CrosswordForm) => validateCrosswordForm(form).map((e) => e.key);

describe('activity-authoring-crossword (#586)', () => {
  it('validate_Pilot_HasNoErrors', () => {
    expect(validateCrosswordForm(FORM)).toEqual([]);
  });

  it('validate_ReportsConflictOverlapTouchingAndBounds', () => {
    const entry = (i: number, patch: object) => ({ ...FORM, entries: FORM.entries.map((e, j) => (j === i ? { ...e, ...patch } : e)) });
    expect(keys(entry(1, { answer: 'MİR' }))).toContain('crosswordConflict');
    expect(keys(entry(2, { row: 0, col: 1, answer: 'al' }))).toContain('crosswordOverlap');
    expect(keys({ ...FORM, cols: 6, entries: [...FORM.entries, { answer: 'ab', clue: 'x', dir: 'across', row: 0, col: 4 }] })).toContain(
      'crosswordTouching'
    );
    expect(keys(entry(0, { col: 1 }))).toContain('crosswordOutOfGrid');
    expect(keys(entry(0, { answer: '' }))).toContain('crosswordAnswerRequired');
    expect(keys(entry(0, { answer: 'k-p' }))).toContain('crosswordAnswerChars');
    expect(keys(entry(0, { clue: ' ' }))).toContain('crosswordClueRequired');
    expect(keys({ ...FORM, rows: 21 })).toContain('crosswordGridSize');
  });

  it('build_ProducesNumberedPlanAndUppercaseSolution_RoundTrips', () => {
    const def = buildCrosswordDefinition(FORM);
    expect(def.interactionType).toBe('crossword');
    const plan = JSON.parse(def.plan);
    expect(plan.clues.map((c: { number: number }) => c.number)).toEqual([1, 1, 2]);
    expect(plan.clues[0].length).toBe(4);
    expect(def.plan).not.toContain('KALP');
    expect(JSON.parse(def.solution)).toEqual({ answers: { k1: 'KALP', k2: 'KİR', k3: 'RAF' } });

    const back = parseDefinition('crossword', def.plan, def.solution) as CrosswordForm;
    expect(back.entries.map((e) => e.answer)).toEqual(['KALP', 'KİR', 'RAF']);
    expect(back.entries[1]).toEqual(expect.objectContaining({ dir: 'down', row: 0, col: 0, clue: 'Kirli' }));
    expect(parseCrosswordDefinition('{"type":"crossword","rows":3,"cols":3,"clues":[]}', null)).toBeNull();
  });

  it('autoLayout_CrossesWordsAndProducesValidGrid', () => {
    const form: CrosswordForm = {
      kind: 'crossword',
      rows: 5,
      cols: 5,
      entries: [
        { answer: 'KALP', clue: 'a', dir: 'across', row: 0, col: 0 },
        { answer: 'AKCİĞER', clue: 'b', dir: 'across', row: 0, col: 0 },
        { answer: 'BÖBREK', clue: 'c', dir: 'across', row: 0, col: 0 },
        { answer: 'MİDE', clue: 'd', dir: 'across', row: 0, col: 0 },
      ],
    };
    const { form: laid, unplaced } = autoLayout(form);
    expect(validateCrosswordForm(laid)).toEqual([]);
    expect(unplaced).toEqual([]);
    // Her kelime en az bir hücrede başka bir kelimeyle kesişir.
    const def = buildCrosswordDefinition(laid);
    const plan = parseCrosswordPlan(def.plan)!;
    const answers = parseCrosswordSolution(def.solution, plan);
    const owners = new Map<string, number>();
    for (const clue of plan.clues) for (const [r, c] of clueCells(clue)) owners.set(`${r},${c}`, (owners.get(`${r},${c}`) ?? 0) + 1);
    for (const clue of plan.clues) {
      expect(clueCells(clue).some(([r, c]) => owners.get(`${r},${c}`)! > 1)).toBe(true);
      expect(answers[clue.id].length).toBe(clue.length);
    }
  });

  it('autoLayout_WordWithoutCommonLetter_GoesToOwnRow', () => {
    const form: CrosswordForm = {
      kind: 'crossword',
      rows: 5,
      cols: 5,
      entries: [
        { answer: 'KALP', clue: 'a', dir: 'across', row: 0, col: 0 },
        { answer: 'SÜT', clue: 'b', dir: 'across', row: 0, col: 0 },
      ],
    };
    const { form: laid, unplaced } = autoLayout(form);
    expect(unplaced).toEqual([2]);
    expect(laid.entries[1]).toEqual(expect.objectContaining({ dir: 'across', row: 2, col: 0 }));
    expect(validateCrosswordForm(laid)).toEqual([]);
  });

  describe('görselden çıkarılan taslak (#586 dilim e)', () => {
    const DRAFT = JSON.stringify({
      version: 1,
      type: 'crossword',
      rows: 3,
      cols: 4,
      clues: [
        { id: 'k1', dir: 'across', row: 0, col: 0, length: 4, number: 1, text: 'Organ' },
        { id: 'k2', dir: 'down', row: 0, col: 1, length: 3, number: 2, text: '' },
      ],
    });

    it('ızgara ve kelime yerleri gelir, cevaplar boş, uzunluk cevap yazılana dek tutulur', () => {
      const form = crosswordFormFromDraft(DRAFT)!;
      expect(form.entries.map((e) => [e.answer, e.length])).toEqual([
        ['', 4],
        ['', 3],
      ]);
      const plan = parseCrosswordPlan(buildCrosswordDefinition(form).plan)!;
      expect(plan.clues.map((c) => c.length)).toEqual([4, 3]);
      const keys = validateCrosswordForm(form).map((e) => e.key);
      expect(keys).toContain('crosswordAnswerRequired');
      expect(keys).toContain('crosswordClueRequired');
      expect(keys).not.toContain('crosswordOverlap');
    });

    it('cevap yazılınca uzunluk cevaptan gelir', () => {
      const form = crosswordFormFromDraft(DRAFT)!;
      form.entries[0].answer = 'kalp';
      form.entries[1] = { ...form.entries[1], answer: 'ana', clue: 'Anne' };
      expect(validateCrosswordForm(form)).toEqual([]);
      expect(JSON.parse(buildCrosswordDefinition(form).solution)).toEqual({ answers: { k1: 'KALP', k2: 'ANA' } });
    });

    it('kelime yeri olmayan ya da bozuk plan null', () => {
      expect(crosswordFormFromDraft(JSON.stringify({ version: 1, type: 'crossword', rows: 3, cols: 3, clues: [] }))).toBeNull();
      expect(crosswordFormFromDraft('x')).toBeNull();
    });
  });
});
