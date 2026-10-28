import {
  AuthoringForm,
  ClassifyForm,
  MatchForm,
  MatrixForm,
  OrderForm,
  buildDefinition,
  emptyForm,
  parseDefinition,
  validateForm,
} from './activity-authoring';

const TF: [string, string] = ['Doğru', 'Yanlış'];

function roundTrip(form: AuthoringForm): AuthoringForm | null {
  const def = buildDefinition(form);
  return parseDefinition(def.interactionType, def.plan, def.solution);
}

describe('activity-authoring (#590)', () => {
  it('matris: kutusuz etiketli satırlar, kolon id çözümü ve karıştırma bayrağı üretir', () => {
    const form: MatrixForm = {
      kind: 'matrix',
      columns: [...TF],
      multiPerRow: false,
      shuffleRows: true,
      rows: [
        { text: ' Su 100 °C kaynar. ', correct: [0] },
        { text: 'Ay bir yıldızdır.', correct: [1] },
      ],
    };
    expect(validateForm(form)).toEqual([]);

    const def = buildDefinition(form);
    expect(def.interactionType).toBe('matrixChoice');
    const plan = JSON.parse(def.plan);
    expect(plan.shuffleRows).toBe(true);
    expect(plan.rows).toEqual([
      { id: 'r1', label: 'Su 100 °C kaynar.' },
      { id: 'r2', label: 'Ay bir yıldızdır.' },
    ]);
    expect(plan.rows[0].box).toBeUndefined();
    expect(JSON.parse(def.solution)).toEqual({ r1: ['c1'], r2: ['c2'] });
    expect(def.plan).not.toContain('solution');

    expect(roundTrip(form)).toEqual({ ...form, rows: form.rows.map((r) => ({ ...r, text: r.text.trim() })) });
  });

  it('matris: cevapsız satır ve tekli seçimde çok cevap hatadır', () => {
    const form = emptyForm('matrix', TF) as MatrixForm;
    form.rows = [{ text: 'a', correct: [] }];
    expect(validateForm(form).map((e) => e.key)).toContain('rowWithoutAnswer');
    form.rows = [{ text: 'a', correct: [0, 1] }];
    expect(validateForm(form).map((e) => e.key)).toContain('rowWithoutAnswer');
    form.multiPerRow = true;
    expect(validateForm(form)).toEqual([]);
  });

  it('sınıflandırma: kovasız kart çeldiricidir, kova kapasitesi kart sayısını ele vermez', () => {
    const form: ClassifyForm = {
      kind: 'classify',
      buckets: ['Memeli', 'Kuş'],
      cards: [
        { text: 'Yunus', bucket: 0 },
        { text: 'Kartal', bucket: 1 },
        { text: 'Yarasa', bucket: 0 },
        { text: 'Kurbağa', bucket: null },
      ],
    };
    expect(validateForm(form)).toEqual([]);
    const def = buildDefinition(form);
    const plan = JSON.parse(def.plan);
    expect(plan.mode).toBe('place');
    expect(plan.targets.map((t: { capacity: number }) => t.capacity)).toEqual([4, 4]);
    expect(JSON.parse(def.solution)).toEqual({ t1: ['k1', 'k3'], t2: ['k2'] });
    expect(roundTrip(form)).toEqual(form);
  });

  it('sınıflandırma: hiç yerleşmiş kart yoksa hatadır', () => {
    const form: ClassifyForm = { kind: 'classify', buckets: ['A'], cards: [{ text: 'x', bucket: null }] };
    expect(validateForm(form).map((e) => e.key)).toContain('noPlacedCard');
  });

  it('sıralama: konumlar 1..n, her konumun tek kartı', () => {
    const form: OrderForm = { kind: 'order', items: ['Tohum', 'Fide', 'Ağaç'] };
    const def = buildDefinition(form);
    const plan = JSON.parse(def.plan);
    expect(plan.mode).toBe('order');
    expect(plan.targets.map((t: { label: string }) => t.label)).toEqual(['1', '2', '3']);
    expect(JSON.parse(def.solution)).toEqual({ t1: ['k1'], t2: ['k2'], t3: ['k3'] });
    expect(roundTrip(form)).toEqual(form);
    expect(validateForm({ kind: 'order', items: ['tek'] }).map((e) => e.key)).toContain('orderCount');
  });

  it('eşleştirme: çeldiriciler sağa eklenir, yinelenen sağ öğe hatadır', () => {
    const form: MatchForm = {
      kind: 'match',
      pairs: [
        { left: 'Ankara', right: 'Türkiye' },
        { left: 'Paris', right: 'Fransa' },
      ],
      distractors: ['İtalya'],
    };
    expect(validateForm(form)).toEqual([]);
    const def = buildDefinition(form);
    const plan = JSON.parse(def.plan);
    expect(plan.right.map((r: { label: string }) => r.label)).toEqual(['Türkiye', 'Fransa', 'İtalya']);
    expect(plan.prelinked).toEqual([]);
    expect(JSON.parse(def.solution)).toEqual({ l1: 'r1', l2: 'r2' });
    expect(roundTrip(form)).toEqual(form);

    form.distractors = ['fransa'];
    expect(validateForm(form).map((e) => e.key)).toContain('duplicateRight');
  });

  it('düzenleme: sunucunun karıştırıp opak id verdiği kartlar çözümden geri kurulur', () => {
    const plan = JSON.stringify({
      version: 1,
      type: 'dragPlace',
      mode: 'order',
      targets: [
        { id: 't1', label: '1', capacity: 1 },
        { id: 't2', label: '2', capacity: 1 },
      ],
      cards: [
        { id: 'x9', label: 'İkinci' },
        { id: 'a3', label: 'Birinci' },
      ],
    });
    const solution = JSON.stringify({ t1: ['a3'], t2: ['x9'] });
    expect(parseDefinition('dragPlace', plan, solution)).toEqual({ kind: 'order', items: ['Birinci', 'İkinci'] });
  });

  it('görselli (kutulu) plan formla düzenlenemez', () => {
    const plan = JSON.stringify({
      version: 1,
      type: 'matrixChoice',
      columns: [
        { id: 'a', label: 'D' },
        { id: 'b', label: 'Y' },
      ],
      rows: [{ id: 'r', label: 'x', box: { x: 0, y: 0, width: 1, height: 1 } }],
    });
    expect(parseDefinition('matrixChoice', plan, null)).toBeNull();
    expect(parseDefinition('mcq', null, null)).toBeNull();
  });
});
