import { AuthoringForm, buildDefinition, parseDefinition, validateForm } from './activity-authoring';
import { FillTableForm, FillTextForm, ShortAnswerForm, WordPoolForm, blankAlternatives } from './activity-authoring-blanks';
import { parseDragPlacePlan } from './drag-place';
import { parseSlotFillPlan } from './slot-fill';

function roundTrip(form: AuthoringForm): AuthoringForm | null {
  const def = buildDefinition(form);
  return parseDefinition(def.interactionType, def.plan, def.solution);
}

describe('activity-authoring-blanks (#590 dilim b)', () => {
  it('boşluk doldurma: yer tutuculu düzen metni, sayı / metin kutusu ve alternatifli çözüm üretir', () => {
    const form: FillTextForm = { kind: 'fillText', text: " Başkent [[Ankara|ankara'dır]], il sayısı [[81]], yarısı [[1/2]]. " };
    expect(validateForm(form)).toEqual([]);

    const def = buildDefinition(form);
    expect(def.interactionType).toBe('slotFill');
    const plan = JSON.parse(def.plan);
    expect(plan.layout).toEqual({ kind: 'text', text: 'Başkent [[s1]], il sayısı [[s2]], yarısı [[s3]].' });
    expect(plan.slots).toEqual([
      { id: 's1', input: { kind: 'text' } },
      { id: 's2', input: { kind: 'number', entry: 'plain' } },
      { id: 's3', input: { kind: 'number', entry: 'fraction' } },
    ]);
    // Plan çözüm taşımaz (#570).
    expect(def.plan).not.toContain('Ankara');
    const solution = JSON.parse(def.solution);
    expect(solution.s1).toEqual({ accept: ['Ankara', "ankara'dır"], optionalSuffixes: [] });
    expect(solution.s2.value).toBe('81');
    expect(parseSlotFillPlan(def.plan)?.layout?.kind).toBe('text');

    expect(roundTrip(form)).toEqual({ kind: 'fillText', text: "Başkent [[Ankara|ankara'dır]], il sayısı [[81]], yarısı [[1/2]]." });
  });

  it('boşluk doldurma: boşluksuz metin ve boş cevap hata verir', () => {
    expect(validateForm({ kind: 'fillText', text: 'Boşluk yok.' }).map((e) => e.key)).toContain('noBlank');
    expect(validateForm({ kind: 'fillText', text: 'Bir [[ | ]] boşluk.' }).map((e) => e.key)).toContain('blankAnswer');
    expect(blankAlternatives(' a | | b ')).toEqual(['a', 'b']);
  });

  it('soru + kısa cevap: liste düzeni, soru metni kutu etiketi', () => {
    const form: ShortAnswerForm = {
      kind: 'shortAnswer',
      rows: [
        { question: 'En uzun nehir?', answers: 'Kızılırmak' },
        { question: '3/4 kaç eder?', answers: '0,75' },
      ],
    };
    expect(validateForm(form)).toEqual([]);
    const plan = JSON.parse(buildDefinition(form).plan);
    expect(plan.layout).toEqual({ kind: 'list' });
    expect(plan.slots[0]).toEqual({ id: 's1', label: 'En uzun nehir?', input: { kind: 'text' } });
    expect(plan.slots[1].input).toEqual({ kind: 'number', entry: 'plain' });
    expect(roundTrip(form)).toEqual(form);

    expect(validateForm({ kind: 'shortAnswer', rows: [{ question: ' ', answers: 'x' }] }).map((e) => e.key)).toEqual([
      'blankQuestion',
    ]);
  });

  it('tablo: başlık + hücre içi boşluklar; başlıkta boşluk yasak', () => {
    const form: FillTableForm = {
      kind: 'fillTable',
      hasHeader: true,
      cells: [
        ['Ülke', 'Başkent'],
        ['Türkiye', '[[Ankara]]'],
        ['Fransa', '[[Paris]] şehri'],
      ],
    };
    expect(validateForm(form)).toEqual([]);
    const plan = JSON.parse(buildDefinition(form).plan);
    expect(plan.layout).toEqual({
      kind: 'table',
      header: ['Ülke', 'Başkent'],
      rows: [
        ['Türkiye', '[[s1]]'],
        ['Fransa', '[[s2]] şehri'],
      ],
    });
    expect(roundTrip(form)).toEqual(form);

    const headerBlank: FillTableForm = { ...form, cells: [['[[x]]', 'B'], ...form.cells.slice(1)] };
    expect(validateForm(headerBlank).map((e) => e.key)).toContain('blankInHeader');
    const noHeader: FillTableForm = { ...form, hasHeader: false };
    expect(JSON.parse(buildDefinition(noHeader).plan).layout.header).toBeUndefined();
    expect(roundTrip(noHeader)).toEqual(noHeader);
  });

  it('kelime havuzu: boşluk = tek kartlık hedef, aynı kelime tek kart (tekrar kullanım), çeldirici ayrı kart', () => {
    const form: WordPoolForm = { kind: 'wordPool', text: '[[Su]] akar, [[taş]] kalır; [[su]] yine akar.', distractors: ['toprak'] };
    expect(validateForm(form)).toEqual([]);

    const def = buildDefinition(form);
    expect(def.interactionType).toBe('dragPlace');
    const plan = JSON.parse(def.plan);
    expect(plan.layout).toEqual({ kind: 'text', text: '[[t1]] akar, [[t2]] kalır; [[t3]] yine akar.' });
    expect(plan.reuse).toBe(true);
    expect(plan.targets).toEqual([
      { id: 't1', capacity: 1 },
      { id: 't2', capacity: 1 },
      { id: 't3', capacity: 1 },
    ]);
    expect(plan.cards.map((c: { label: string }) => c.label)).toEqual(['Su', 'taş', 'toprak']);
    expect(JSON.parse(def.solution)).toEqual({ t1: ['k1'], t2: ['k2'], t3: ['k1'] });
    expect(parseDragPlacePlan(def.plan)?.layout?.kind).toBe('text');

    expect(roundTrip(form)).toEqual({ kind: 'wordPool', text: '[[Su]] akar, [[taş]] kalır; [[Su]] yine akar.', distractors: ['toprak'] });
  });

  it('kelime havuzu: alternatif ve cevapla aynı çeldirici yasak', () => {
    const keys = validateForm({ kind: 'wordPool', text: '[[a|b]] ve [[c]]', distractors: ['C'] }).map((e) => e.key);
    expect(keys).toContain('poolAlternatives');
    expect(keys).toContain('distractorIsAnswer');
  });

  it('formun ifade edemediği slotFill planı (görselli, seçim kutusu) düzenlemeye açılmaz', () => {
    const boxed = JSON.stringify({
      version: 1,
      type: 'slotFill',
      slots: [{ id: 's1', box: { x: 0, y: 0, width: 5, height: 5 }, input: { kind: 'text' } }],
    });
    expect(parseDefinition('slotFill', boxed, '{"s1":{"accept":["a"]}}')).toBeNull();
    const select = JSON.stringify({
      version: 1,
      type: 'slotFill',
      layout: { kind: 'list' },
      slots: [{ id: 's1', label: 'Soru', input: { kind: 'select', options: [{ id: 'o1', label: 'a' }, { id: 'o2', label: 'b' }] } }],
    });
    expect(parseDefinition('slotFill', select, '{"s1":{"optionIds":["o1"]}}')).toBeNull();
  });
  describe('şifre katmanı (#586 dilim d)', () => {
    const base: ShortAnswerForm = {
      kind: 'shortAnswer',
      rows: [
        { question: '3 × 4', answers: '12', letter: 'k' },
        { question: 'Başkent', answers: 'Ankara', letter: 'a' },
        { question: '5 + 5', answers: '10', letter: 'a' },
        { question: 'Renk', answers: 'mavi', letter: '' },
      ],
      cipher: ' kaa  k ',
    };

    it('bağlar harfleri sorulara (aynı harf sırayla dağıtılır), boşluk null; harf çözüme gider', () => {
      expect(validateForm(base)).toEqual([]);
      const def = buildDefinition(base);
      const plan = JSON.parse(def.plan);
      expect(plan.cipher).toEqual({ cells: ['s1', 's2', 's3', null, 's1'] });
      expect(def.plan).not.toContain('"letter"');
      const solution = JSON.parse(def.solution);
      expect(solution.s1.letter).toBe('K');
      expect(solution.s2.letter).toBe('A');
      expect(solution.s4.letter).toBeUndefined();
    });

    it('geri okunur (kelime ve harfler)', () => {
      const back = roundTrip(base) as ShortAnswerForm;
      expect(back.cipher).toBe('KAA K');
      expect(back.rows.map((r) => r.letter)).toEqual(['K', 'A', 'A', '']);
    });

    it('şifresiz form şifre alanı üretmez', () => {
      const def = buildDefinition({ ...base, cipher: '', rows: base.rows.map((r) => ({ ...r, letter: '' })) });
      expect(JSON.parse(def.plan).cipher).toBeUndefined();
    });

    it('eksik / fazla harf ve kelimesiz harf hata verir', () => {
      const keys = (form: ShortAnswerForm) => validateForm(form).map((e) => e.key);
      expect(keys({ ...base, cipher: 'KAR' })).toContain('cipherLetterMissing');
      expect(keys({ ...base, cipher: 'K K' })).toContain('cipherLetterUnused');
      expect(keys({ ...base, cipher: '' })).toContain('cipherWordMissing');
      expect(keys({ ...base, cipher: 'K' })).toContain('cipherWordShort');
      expect(keys({ ...base, cipher: 'KA!' })).toContain('cipherWordChars');
    });
  });
});
