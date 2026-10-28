// Issue #586: mantık tablosu formu — boyut değişimi, doğrulama, plan + çözüm, geri kurma.
import {
  LogicGridForm,
  addLogicCategory,
  buildLogicGridDefinition,
  removeLogicCategory,
  resizeLogicSubjects,
  validateLogicGridForm,
} from './activity-authoring-logic-grid';
import { parseDefinition } from './activity-authoring';

const FORM: LogicGridForm = {
  kind: 'logicGrid',
  subjectLabel: 'Çocuk',
  subjects: ['Ali', 'Ayşe', 'Can'],
  categories: [{ label: 'Renk', options: ['Mavi', 'Kırmızı', 'Yeşil'] }],
  clues: ['Ali maviyi sevmez.', ' '],
  answer: [[2], [1], [0]],
};

describe('activity-authoring-logic-grid (#586)', () => {
  it('validate_Pilot_AndErrors', () => {
    expect(validateLogicGridForm(FORM)).toEqual([]);
    const keys = (f: LogicGridForm) => validateLogicGridForm(f).map((e) => e.key);
    expect(keys({ ...FORM, answer: [[2], [2], [0]] })).toContain('logicAnswerDuplicate');
    expect(keys({ ...FORM, answer: [[2], [null], [0]] })).toContain('logicAnswerMissing');
    expect(keys({ ...FORM, subjects: ['Ali', 'ali', 'Can'] })).toContain('logicDuplicate');
    expect(keys({ ...FORM, subjectLabel: '' })).toContain('logicLabelRequired');
  });

  it('resize_And_Categories_KeepTablesAligned', () => {
    const bigger = resizeLogicSubjects(FORM, 4);
    expect(bigger.subjects.length).toBe(4);
    expect(bigger.categories[0].options.length).toBe(4);
    expect(bigger.answer[3]).toEqual([null]);
    const smaller = resizeLogicSubjects(FORM, 2);
    // Can'ın kaldırılmasıyla 2 dizinli seçenek de kalktı: Ali'nin seçimi silinir.
    expect(smaller.answer).toEqual([[null], [1]]);
    const two = addLogicCategory(FORM);
    expect(two.categories[1].options.length).toBe(3);
    expect(two.answer[0]).toEqual([2, null]);
    expect(removeLogicCategory(two, 0).answer[0]).toEqual([null]);
  });

  it('build_ProducesPlanAndBijectiveSolution_RoundTrips', () => {
    const def = buildLogicGridDefinition(FORM);
    const plan = JSON.parse(def.plan);
    expect(plan.subject.items.map((s: { id: string }) => s.id)).toEqual(['p1', 'p2', 'p3']);
    expect(plan.categories[0].items.map((o: { id: string }) => o.id)).toEqual(['o1', 'o2', 'o3']);
    expect(plan.clues).toEqual([{ id: 'q1', text: 'Ali maviyi sevmez.' }]);
    expect(JSON.parse(def.solution)).toEqual({ assign: { p1: { c1: 'o3' }, p2: { c1: 'o2' }, p3: { c1: 'o1' } } });
    expect(def.plan).not.toContain('assign');

    const back = parseDefinition('logicGrid', def.plan, def.solution) as LogicGridForm;
    expect(back.answer).toEqual([[2], [1], [0]]);
    expect(back.clues).toEqual(['Ali maviyi sevmez.']);
  });
});
