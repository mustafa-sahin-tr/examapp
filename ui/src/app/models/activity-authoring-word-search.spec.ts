// Issue #586: kelime avı formu — deterministik ızgara üretimi, yönler, doğrulama, plan + çözüm, kayıtlı ızgaranın korunması.
import {
  WordSearchForm,
  buildWordSearchDefinition,
  emptyWordSearchForm,
  generateWordSearch,
  parseWordSearchDefinition,
  validateWordSearchForm,
} from './activity-authoring-word-search';
import { parseDefinition } from './activity-authoring';
import { parseWordSearchPlan, parseWordSearchSolution, spell } from './word-search';

const form = (patch: Partial<WordSearchForm> = {}): WordSearchForm => ({
  ...emptyWordSearchForm(),
  words: [
    { word: 'kalp', label: '' },
    { word: 'Böbrek', label: 'Kanı süzen organ' },
    { word: 'mide', label: '' },
  ],
  ...patch,
});

const clues = (patch: Partial<WordSearchForm> = {}): WordSearchForm =>
  form({
    show: 'clues',
    words: [
      { word: 'kalp', label: 'Kanı pompalar' },
      { word: 'Böbrek', label: 'Kanı süzen organ' },
      { word: 'mide', label: 'Besinleri sindirir' },
    ],
    ...patch,
  });

describe('activity-authoring-word-search (#586)', () => {
  it('generate_IsDeterministic_SeedChangesLayout_AllWordsSpelledOnGrid', () => {
    const a = generateWordSearch(form());
    expect(generateWordSearch(form())).toEqual(a);
    expect(generateWordSearch(form({ seed: 2 }))).not.toEqual(a);
    expect(a.letters.length).toBe(8);
    expect(a.letters.every((row) => [...row].length === 8)).toBe(true);

    const def = buildWordSearchDefinition(form());
    const plan = parseWordSearchPlan(def.plan)!;
    const solution = parseWordSearchSolution(def.solution, plan);
    expect(spell(plan, solution['w1'])).toBe('KALP');
    expect(spell(plan, solution['w2'])).toBe('BÖBREK');
    expect(plan.words.map((w) => w.label)).toEqual(['KALP', 'BÖBREK', 'MİDE']);
    expect(def.plan).not.toContain('from');
  });

  it('directions_OnlyAllowedOnesUsed', () => {
    for (let seed = 1; seed <= 10; seed++) {
      for (const line of generateWordSearch(form({ seed })).lines) {
        // Çapraz ve ters kapalı: yalnız sağa ya da aşağı.
        expect(line!.to[0] >= line!.from[0] && line!.to[1] >= line!.from[1]).toBe(true);
        expect(line!.to[0] === line!.from[0] || line!.to[1] === line!.from[1]).toBe(true);
      }
    }
  });

  it('validate_ReportsEmptyDuplicateAndUnplaced', () => {
    expect(validateWordSearchForm(form())).toEqual([]);
    const keys = (f: WordSearchForm) => validateWordSearchForm(f).map((e) => e.key);
    expect(keys(form({ words: [{ word: '', label: '' }] }))).toContain('wordSearchWordRequired');
    expect(keys(form({ words: [{ word: 'kalp', label: '' }, { word: 'KALP', label: '' }] }))).toContain('wordSearchDuplicate');
    expect(keys(form({ rows: 4, cols: 4, words: [{ word: 'BÖBREKLER', label: '' }] }))).toContain('wordSearchUnplaced');
    expect(keys(form({ rows: 30 }))).toContain('wordSearchGridSize');
  });

  it('show_WordsListsWordsOnly_CluesListsCluesAndRequiresEach', () => {
    const labels = (f: WordSearchForm) => JSON.parse(buildWordSearchDefinition(f).plan).words.map((w: { label: string }) => w.label);
    // Kelime kipinde yazılmış ipucu listeye girmez (kip değişince kaybolmasın diye formda kalır).
    expect(labels(form())).toEqual(['KALP', 'BÖBREK', 'MİDE']);
    expect(labels(clues())).toEqual(['Kanı pompalar', 'Kanı süzen organ', 'Besinleri sindirir']);
    const keys = (f: WordSearchForm) => validateWordSearchForm(f).map((e) => e.key);
    expect(keys(form({ show: 'clues' }))).toContain('wordSearchClueRequired');
    expect(keys(clues({ words: [{ word: 'kalp', label: 'Kalp' }] }))).toContain('wordSearchClueIsWord');
    expect(keys(clues())).toEqual([]);
  });

  it('parse_InfersShowFromLabels', () => {
    const words = parseWordSearchDefinition(buildWordSearchDefinition(form()).plan, buildWordSearchDefinition(form()).solution)!;
    expect(words.show).toBe('words');
    expect(words.words.every((w) => w.label === '')).toBe(true);
    const def = buildWordSearchDefinition(clues());
    const back = parseWordSearchDefinition(def.plan, def.solution)!;
    expect(back.show).toBe('clues');
    expect(back.words.map((w) => w.label)).toEqual(['Kanı pompalar', 'Kanı süzen organ', 'Besinleri sindirir']);
    // Eski karışık kayıt (bir ipucu, iki kelime): ipucu kipi, eksik ipuçları doğrulamada istenir.
    const mixed = JSON.parse(def.plan);
    mixed.words[0].label = 'KALP';
    const old = parseWordSearchDefinition(JSON.stringify(mixed), def.solution)!;
    expect(old.show).toBe('clues');
    expect(old.words[0].label).toBe('');
    expect(validateWordSearchForm(old).map((e) => e.key)).toContain('wordSearchClueRequired');
  });

  it('parse_KeepsSavedGridUntilContentChanges', () => {
    const def = buildWordSearchDefinition(clues({ seed: 5, diagonal: true }));
    const back = parseDefinition('wordSearch', def.plan, def.solution) as WordSearchForm;
    expect(back.words.map((w) => w.word)).toEqual(['KALP', 'BÖBREK', 'MİDE']);
    expect(back.words[1].label).toBe('Kanı süzen organ');
    // İçerik aynı: aynı ızgara (tohum bilinmese de).
    expect(JSON.parse(buildWordSearchDefinition(back).plan).letters).toEqual(JSON.parse(def.plan).letters);
    // Kelime değişince yeniden üretilir.
    const changed = { ...back, words: [...back.words, { word: 'akciğer', label: '' }] };
    expect(JSON.parse(buildWordSearchDefinition(changed).plan).letters).not.toEqual(JSON.parse(def.plan).letters);
    expect(parseWordSearchDefinition(def.plan, null)).toBeNull();
  });
});
