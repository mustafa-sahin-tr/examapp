// Issue #586: mantık tablosu formu — cevap tablosundan seçenek + çözüm, öğretmenin seçenek sırası, satır / kategori ekleme-silme,
// doğrulama, plan + çözüm, geri kurma.
import {
  LogicGridForm,
  addLogicCategory,
  addLogicSubject,
  buildLogicGridDefinition,
  isRowOrder,
  moveLogicOption,
  removeLogicCategory,
  removeLogicSubject,
  shuffleLogicOptions,
  validateLogicGridForm,
} from './activity-authoring-logic-grid';
import { parseDefinition } from './activity-authoring';

// Cevap tablosu: Ali → Yeşil, Ayşe → Kırmızı, Can → Mavi; öğrenci Mavi, Kırmızı, Yeşil sırasıyla görür.
const FORM: LogicGridForm = {
  kind: 'logicGrid',
  subjectLabel: 'Çocuk',
  subjects: ['Ali', 'Ayşe', 'Can'],
  categories: [{ label: 'Renk', values: ['Yeşil', 'Kırmızı', 'Mavi'], order: [2, 1, 0] }],
  clues: ['Ali maviyi sevmez.', ' '],
};

describe('activity-authoring-logic-grid (#586)', () => {
  it('validate_Pilot_AndErrors', () => {
    expect(validateLogicGridForm(FORM)).toEqual([]);
    const keys = (f: LogicGridForm) => validateLogicGridForm(f).map((e) => e.key);
    const values = (v: string[]) => ({ ...FORM, categories: [{ ...FORM.categories[0], values: v }] });
    expect(keys(values(['Yeşil', 'yeşil', 'Mavi']))).toContain('logicDuplicate');
    expect(keys(values(['Yeşil', '', 'Mavi']))).toContain('logicLabelRequired');
    expect(keys({ ...FORM, subjects: ['Ali', 'ali', 'Can'] })).toContain('logicDuplicate');
    expect(keys({ ...FORM, subjectLabel: '' })).toContain('logicLabelRequired');
  });

  it('build_OptionsInTeacherOrder_SolutionFromAnswerTable_RoundTrips', () => {
    const def = buildLogicGridDefinition(FORM);
    const plan = JSON.parse(def.plan);
    expect(plan.subject.items.map((s: { id: string }) => s.id)).toEqual(['p1', 'p2', 'p3']);
    expect(plan.categories[0].items).toEqual([
      { id: 'o1', label: 'Mavi' },
      { id: 'o2', label: 'Kırmızı' },
      { id: 'o3', label: 'Yeşil' },
    ]);
    expect(plan.clues).toEqual([{ id: 'q1', text: 'Ali maviyi sevmez.' }]);
    expect(JSON.parse(def.solution)).toEqual({ assign: { p1: { c1: 'o3' }, p2: { c1: 'o2' }, p3: { c1: 'o1' } } });
    expect(def.plan).not.toContain('assign');

    const back = parseDefinition('logicGrid', def.plan, def.solution) as LogicGridForm;
    expect(back.categories).toEqual(FORM.categories);
    expect(back.clues).toEqual(['Ali maviyi sevmez.']);
  });

  it('parse_DraftWithoutSolution_FillsRowsInPlanOrder', () => {
    const def = buildLogicGridDefinition(FORM);
    const back = parseDefinition('logicGrid', def.plan, null) as LogicGridForm;
    expect(back.categories[0]).toEqual({ label: 'Renk', values: ['Mavi', 'Kırmızı', 'Yeşil'], order: [0, 1, 2] });
  });

  it('rows_And_Categories_KeepTableAndOrderAligned', () => {
    const bigger = addLogicSubject(FORM);
    expect(bigger.subjects.length).toBe(4);
    expect(bigger.categories[0].values).toEqual(['Yeşil', 'Kırmızı', 'Mavi', '']);
    expect(bigger.categories[0].order).toEqual([2, 1, 0, 3]);
    // Ayşe silinince Kırmızı da gider; sıra kalan satırlara göre yeniden numaralanır.
    const smaller = removeLogicSubject(FORM, 1);
    expect(smaller.subjects).toEqual(['Ali', 'Can']);
    expect(smaller.categories[0]).toEqual({ label: 'Renk', values: ['Yeşil', 'Mavi'], order: [1, 0] });
    expect(removeLogicSubject(smaller, 0)).toBe(smaller); // en az 2 satır
    const two = addLogicCategory(FORM);
    expect(two.categories[1]).toEqual({ label: '', values: ['', '', ''], order: [0, 1, 2] });
    expect(removeLogicCategory(two, 0).categories).toEqual([two.categories[1]]);
  });

  it('order_MoveAndShuffle_RowOrderWarning', () => {
    expect(moveLogicOption(FORM, 0, 0, 1).categories[0].order).toEqual([1, 2, 0]);
    expect(moveLogicOption(FORM, 0, 0, -1)).toEqual(FORM); // kenardan taşmaz
    expect(isRowOrder([0, 1, 2])).toBe(true);
    expect(isRowOrder(FORM.categories[0].order)).toBe(false);
    const rowOrder = { ...FORM, categories: [{ ...FORM.categories[0], order: [0, 1, 2] }] };
    let seed = 0;
    const shuffled = shuffleLogicOptions(rowOrder, 0, () => [0.1, 0.9, 0.5, 0.3][seed++ % 4]);
    expect([...shuffled.categories[0].order].sort()).toEqual([0, 1, 2]);
    expect(isRowOrder(shuffled.categories[0].order)).toBe(false);
  });
});
