import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ActivityEditorComponent } from './activity-editor.component';
import { AuthService } from '../../services/auth.service';
import { LabelingService } from '../../services/labeling.service';
import { QuestionService } from '../../services/question.service';
import { TestService } from '../../services/test.service';
import { StudentService } from '../../services/student.service';
import { SubjectService } from '../../services/subject.service';
import { PassageKind } from '../../models/activity';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import rootTr from '../../../../public/i18n/tr.json';
import editorTr from '../../../../public/i18n/activity-editor/tr.json';

const MATCH_PLAN = JSON.stringify({
  version: 1,
  type: 'match',
  left: [{ id: 'l1', label: 'Ankara' }],
  right: [
    { id: 'zz', label: 'Fransa' },
    { id: 'qq', label: 'Türkiye' },
  ],
  prelinked: [],
});

describe('ActivityEditorComponent (#590)', () => {
  let fixture: ComponentFixture<ActivityEditorComponent>;
  let component: ActivityEditorComponent;
  let questions: { getAll: Mock; saveBulk: Mock; saveQuestion: Mock; updateClassification: Mock };
  let router: Router;
  let labeling: { getAiModels: Mock; activityStructure: Mock };

  async function setup(query: Record<string, string>, list: unknown[] = [], trainer = false, startedCount = 0) {
    labeling = {
      getAiModels: vi.fn().mockReturnValue(
        of([{ id: 'claude-haiku-5-5', displayName: 'Haiku', inputUsdPerMTok: 1, outputUsdPerMTok: 5, isDefault: true }])
      ),
      activityStructure: vi.fn(),
    };
    questions = {
      getAll: vi.fn().mockReturnValue(of(list)),
      saveBulk: vi.fn().mockReturnValue(of({ success: true })),
      saveQuestion: vi.fn().mockReturnValue(of({ success: true })),
      updateClassification: vi.fn().mockReturnValue(of({ success: true })),
    };
    TestBed.configureTestingModule({
      imports: [
        ActivityEditorComponent,
        NoopAnimationsModule,
        translocoTestingModule({
          langs: { tr: { ...rootTr, 'activity-editor': editorTr }, 'activity-editor/tr': editorTr },
        }),
      ],
      providers: [
        provideRouter([]),
        { provide: QuestionService, useValue: questions },
        { provide: TestService, useValue: { get: vi.fn().mockReturnValue(of({ id: 7, instanceCount: startedCount })) } },
        { provide: AuthService, useValue: { hasRealmRole: vi.fn((role: string) => trainer && role === 'QuestionTrainer') } },
        { provide: LabelingService, useValue: labeling },
        { provide: StudentService, useValue: { loadGrades: vi.fn().mockReturnValue(of([])) } },
        {
          provide: SubjectService,
          useValue: {
            getSubjectsByGrade: vi.fn().mockReturnValue(of([])),
            getTopicsBySubjectAndGrade: vi.fn().mockReturnValue(of([])),
            getSubTopicsByTopic: vi.fn().mockReturnValue(of([])),
          },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
      ],
    });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(ActivityEditorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const el = (selector: string) => fixture.nativeElement.querySelector(selector) as HTMLElement | null;

  it('yeni D/Y maddesini görselsiz toplu kayıtla etkinliğe ekler ve etkinliğe döner', async () => {
    await setup({ testId: '9', gradeId: '5' });
    expect(component.matrix()?.columns).toEqual(['Doğru', 'Yanlış']);

    component.setInstruction('Doğru mu, yanlış mı?');
    component.setRowText(0, 'Su 100 °C kaynar.');
    component.toggleRowCorrect(0, 0);
    component.save(false);

    expect(questions.saveBulk).toHaveBeenCalledTimes(1);
    const body = questions.saveBulk.mock.calls[0][0];
    expect(body.imageData).toBeNull();
    expect(body.header).toEqual({ testId: 9, classificationSource: 'Human' });
    expect(body.passages[0]).toMatchObject({ kind: PassageKind.Activity, instruction: 'Doğru mu, yanlış mı?', width: 0 });
    const q = body.questions[0];
    expect(q.interactionType).toBe('matrixChoice');
    expect(JSON.parse(q.interactionPlan).rows).toEqual([{ id: 'r1', label: 'Su 100 °C kaynar.' }]);
    expect(JSON.parse(q.interactionSolution)).toEqual({ r1: ['c1'] });
    expect(q.classification).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith(['/test', 9]);
  });

  it('başlamış öğrenci varsa yeni madde yalnız yeni başlayanlara görünür uyarısı (#10 gezinti); yoksa uyarı yok', async () => {
    await setup({ testId: '9', gradeId: '5' }, [], false, 3);
    expect(component.startedCount()).toBe(3);
    expect(el('[data-testid="started-warning"]')?.textContent).toContain('3 öğrenci');

    fixture.destroy();
    TestBed.resetTestingModule();
    await setup({ testId: '9', gradeId: '5' });
    expect(el('[data-testid="started-warning"]')).toBeNull();
  });

  it('seçilen konu kilitli sınıflandırma olarak gider', async () => {
    await setup({ testId: '9' });
    component.setRowText(0, 'x');
    component.toggleRowCorrect(0, 1);
    component.onClassificationChange({ gradeId: 5, subjectId: 2, topicId: 20, subtopicIds: [200] });
    component.save(false);

    expect(questions.saveBulk.mock.calls[0][0].questions[0].classification).toEqual({
      subjectId: 2,
      topicId: 20,
      subTopicIds: [200],
      source: 'Human',
      locked: true,
    });
  });

  it('eksik form kaydedilmez, hatalar listelenir; konusuz ders de engeller', async () => {
    await setup({ testId: '9' });
    component.save(false);
    fixture.detectChanges();

    expect(questions.saveBulk).not.toHaveBeenCalled();
    expect(el('[data-testid="form-errors"]')?.textContent).toContain('Boş ifade bırakma.');

    component.setRowText(0, 'x');
    component.toggleRowCorrect(0, 0);
    component.onClassificationChange({ gradeId: 5, subjectId: 2, topicId: null, subtopicIds: [] });
    component.save(false);
    expect(questions.saveBulk).not.toHaveBeenCalled();
  });

  it('tür değişince tipe özel form ve önizleme oynatıcısı değişir', async () => {
    await setup({ testId: '9' });
    component.selectKind('classify');
    fixture.detectChanges();
    expect(el('[data-testid="classify-form"]')).not.toBeNull();
    expect(el('[data-testid="preview"] app-question-canvas-drag-place')).not.toBeNull();

    component.selectKind('match');
    fixture.detectChanges();
    expect(el('[data-testid="preview"] app-question-canvas-match')).not.toBeNull();
  });

  it('D/Y matris tablo olarak düzenlenir, hücreye tık doğruyu seçer; sıralama ve taşlarda da ayrı önizleme yok', async () => {
    await setup({ testId: '9' });
    component.selectKind('matrix');
    fixture.detectChanges();
    expect(el('[data-testid="matrix-table"]')).not.toBeNull();
    expect(el('[data-testid="preview"]')).toBeNull();

    (el('[data-cell="0:1"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el('[data-cell="0:1"]')?.getAttribute('aria-pressed')).toBe('true');

    for (const kind of ['order', 'mazeStones'] as const) {
      component.selectKind(kind);
      fixture.detectChanges();
      expect(el('[data-testid="preview"]')).toBeNull();
    }
    component.selectKind('classify');
    fixture.detectChanges();
    expect(el('[data-testid="preview"]')).not.toBeNull();
  });

  it('ışıklı pano oyundaki gibi yukarıdan aşağı, doğru kutu ampulle seçilir; ayrı önizleme yok', async () => {
    await setup({ testId: '9' });
    component.selectKind('mazeBoard');
    fixture.detectChanges();
    expect(el('[data-testid="maze-board-canvas"]')).not.toBeNull();
    expect(el('[data-testid="preview"]')).toBeNull();
    const before = component.mazeBoard()!.rows.length;

    (el('[data-testid="board-add-row"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const rows = [...fixture.nativeElement.querySelectorAll('.ae__board-row')].map((r: Element) => Number(r.getAttribute('data-row')));
    expect(rows[0]).toBe(before); // yeni satır bitişe en yakın, en üstte
    expect(rows[rows.length - 1]).toBe(0);

    (el(`[data-box="0:1"] .ae__tree-branch`) as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(component.mazeBoard()!.rows[0].correct).toBe(1);
  });

  it('odalı labirent ızgarada: kapı yuvası yazınca kapı açılır, odada doğru kapı ve başlangıç / bitiş seçilir', async () => {
    await setup({ testId: '9' });
    component.selectKind('mazeRooms');
    component.setRoomsSize(2, 2);
    fixture.detectChanges();
    expect(el('[data-testid="maze-rooms-canvas"]')).not.toBeNull();
    expect(el('[data-testid="preview"]')).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('.ae__room').length).toBe(4);

    const f = component.mazeRooms()!;
    const right = f.doors.findIndex((d) => d.a === 0 && d.b === 1);
    const input = el(`[data-door="${right}"]`) as HTMLInputElement;
    input.value = '24';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(component.mazeRooms()!.doors[right].label).toBe('24');

    const chip = el('[data-room="0"] .ae__room-answers .ae__tree-branch') as HTMLButtonElement;
    expect(chip.textContent).toContain('→ 24');
    chip.click();
    fixture.detectChanges();
    expect(component.mazeRooms()!.rooms[0].answer).toBe(right);

    (el('[data-room="3"] .ae__room-flag:last-of-type') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(component.mazeRooms()!.finish).toBe(3);
  });

  it('D/Y ağacı: tek ifadeyle başlar, düzey ekleyince ağaç büyür, ayrı önizleme yok, doğru çıkış vurgulanır', async () => {
    await setup({ testId: '9' });
    component.selectKind('mazeTree');
    fixture.detectChanges();
    expect(el('[data-testid="maze-tree-canvas"]')).not.toBeNull();
    expect(el('[data-testid="preview"]')).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('.ae__tree-node').length).toBe(1);
    expect(fixture.nativeElement.querySelectorAll('.ae__tree-exit').length).toBe(2);

    (el('[data-testid="tree-add-level"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.ae__tree-node').length).toBe(3);
    expect(fixture.nativeElement.querySelectorAll('.ae__tree-exit').length).toBe(4);

    // Kök Y (sağ), sağ çocuk D (sol) → soldan 3. çıkış (dizin 2).
    component.setTreeCorrect(0, 1);
    component.setTreeCorrect(2, 0);
    fixture.detectChanges();
    expect(component.treeExit()).toBe(2);
    expect(el('[data-exit="2"]')?.classList).toContain('is-route');
    expect([...component.treeRoute()]).toEqual([0, 2]);

    (el('[data-testid="tree-remove-level"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.ae__tree-node').length).toBe(1);
    expect((el('[data-testid="tree-remove-level"]') as HTMLButtonElement).disabled).toBe(true);
  });

  it('mantık tablosu: satır/kategori/çözümü kaydeder, önizlemede tabloyu çizer (#586)', async () => {
    await setup({ testId: '9' });
    component.selectKind('logicGrid');
    fixture.detectChanges();
    expect(el('[data-testid="logic-grid-form"]')).not.toBeNull();

    component.resizeLogicSubjects(-1);
    component.setLogicSubjectLabel('Çocuk');
    component.setLogicSubject(0, 'Ali');
    component.setLogicSubject(1, 'Ayşe');
    component.setLogicCategoryLabel(0, 'Renk');
    component.setLogicOption(0, 0, 'Mavi');
    component.setLogicOption(0, 1, 'Kırmızı');
    component.setLogicAnswer(0, 0, 1);
    component.setLogicAnswer(1, 0, 0);
    component.setLogicClue(0, 'Ali maviyi sevmez.');
    fixture.detectChanges();
    expect(el('[data-testid="preview"] app-question-canvas-logic-grid')).not.toBeNull();

    component.save(false);
    const q = questions.saveBulk.mock.calls[0][0].questions[0];
    expect(q.interactionType).toBe('logicGrid');
    expect(JSON.parse(q.interactionPlan).categories[0].items.length).toBe(2);
    expect(JSON.parse(q.interactionSolution)).toEqual({ assign: { p1: { c1: 'o2' }, p2: { c1: 'o1' } } });
  });

  it('boyama: hedefleri kare sayısına çevirip kaydeder, önizlemede ızgarayı çizer (#587)', async () => {
    await setup({ testId: '9' });
    component.selectKind('regionPaint');
    fixture.detectChanges();
    expect(el('[data-testid="region-paint-form"]')).not.toBeNull();

    component.setPaintEntry(0, { target: '0,35', label: '0,35' });
    component.addPaintEntry();
    component.setPaintEntry(1, { target: '%25' });
    fixture.detectChanges();
    expect(el('[data-paint-cells="0"]')?.textContent).toContain('35');
    expect(el('[data-testid="preview"] app-question-canvas-region-paint')).not.toBeNull();

    component.save(false);
    const q = questions.saveBulk.mock.calls[0][0].questions[0];
    expect(q.interactionType).toBe('regionPaint');
    expect(JSON.parse(q.interactionPlan).palette).toEqual([
      { id: 'p1', color: 'blue', label: '0,35' },
      { id: 'p2', color: 'red' },
    ]);
    expect(JSON.parse(q.interactionSolution)).toEqual({ counts: { p1: 35, p2: 25 } });
  });

  it('sayı doğrusu: kartları hedef çentikleriyle kaydeder, önizlemede doğruyu çizer (#588)', async () => {
    await setup({ testId: '9' });
    component.selectKind('numberLine');
    fixture.detectChanges();
    expect(el('[data-testid="number-line-form"]')).not.toBeNull();

    component.setLineCard(0, { label: '3/8', target: '3' });
    component.addLineCard();
    component.setLineCard(1, { label: '5/4', target: '' });
    fixture.detectChanges();
    expect(el('[data-line-target="0"]')?.textContent).toContain('3/8');
    expect(el('[data-testid="preview"] app-question-canvas-number-line')).not.toBeNull();

    component.save(false);
    const q = questions.saveBulk.mock.calls[0][0].questions[0];
    expect(q.interactionType).toBe('numberLine');
    expect(JSON.parse(q.interactionPlan).cards).toEqual([
      { id: 'k1', label: '3/8' },
      { id: 'k2', label: '5/4' },
    ]);
    expect(JSON.parse(q.interactionSolution)).toEqual({ positions: { k1: 3, k2: null } });
  });

  it('grafik kurma: kategorileri değer ve hazır sütunla kaydeder, önizlemede grafiği çizer (#588)', async () => {
    await setup({ testId: '9' });
    component.selectKind('barChart');
    fixture.detectChanges();
    expect(el('[data-testid="bar-chart-form"]')).not.toBeNull();

    component.setChartOption({ title: 'Sevilen meyveler' });
    component.setChartCategory(0, { label: 'Elma', value: '6', fixed: true });
    component.setChartCategory(1, { label: 'Armut', value: '4' });
    component.removeChartCategory(2);
    fixture.detectChanges();
    expect(el('[data-testid="preview"] app-question-canvas-bar-chart')).not.toBeNull();

    component.save(false);
    const q = questions.saveBulk.mock.calls[0][0].questions[0];
    expect(q.interactionType).toBe('barChart');
    const plan = JSON.parse(q.interactionPlan);
    expect(plan.categories).toEqual([
      { id: 'c1', label: 'Elma' },
      { id: 'c2', label: 'Armut' },
    ]);
    expect(plan.fixed).toEqual({ c1: 6 });
    expect(JSON.parse(q.interactionSolution)).toEqual({ values: { c2: 4 } });
  });

  it('kareli zeminde çizim: kuralı çözüme kaydeder, uyan boyutları ve önizlemeyi gösterir (#588)', async () => {
    await setup({ testId: '9' });
    component.selectKind('gridDraw');
    fixture.detectChanges();
    expect(el('[data-testid="grid-draw-form"]')).not.toBeNull();

    component.setGridDraw({ perimeter: '14' });
    fixture.detectChanges();
    expect(el('[data-testid="gd-targets"]')?.textContent).toContain('1 × 6, 2 × 5, 3 × 4');
    expect(el('[data-testid="preview"] app-question-canvas-grid-draw')).not.toBeNull();

    component.save(false);
    const q = questions.saveBulk.mock.calls[0][0].questions[0];
    expect(q.interactionType).toBe('gridDraw');
    expect(JSON.parse(q.interactionPlan)).toEqual({ version: 1, type: 'gridDraw', rows: 10, cols: 16 });
    expect(JSON.parse(q.interactionSolution)).toEqual({ perimeter: 14, all: true });
  });

  it('saat kurma: kurulu saat plana, kurulacak saat çözüme kaydedilir; adım dışı dakika reddedilir (#634)', async () => {
    await setup({ testId: '9' });
    component.selectKind('clockSet');
    fixture.detectChanges();
    expect(el('[data-testid="clock-set-form"]')).not.toBeNull();

    component.setClock(0, { label: 'Başlangıç', time: '9.20', fixed: true });
    component.addClock();
    component.setClock(1, { label: 'Bitiş', time: '10.02' });
    fixture.detectChanges();
    expect(component.errors().map((e) => e.key)).toContain('clockTimeStep');

    component.setClock(1, { time: '10.00' });
    fixture.detectChanges();
    expect(component.errors()).toEqual([]);
    expect(el('[data-testid="preview"] app-question-canvas-clock-set')).not.toBeNull();

    component.save(false);
    const q = questions.saveBulk.mock.calls[0][0].questions[0];
    expect(q.interactionType).toBe('clockSet');
    expect(JSON.parse(q.interactionPlan)).toEqual({
      version: 1,
      type: 'clockSet',
      step: 5,
      clocks: [{ id: 'c1', label: 'Başlangıç', fixed: '09:20' }, { id: 'c2', label: 'Bitiş' }],
    });
    expect(JSON.parse(q.interactionSolution)).toEqual({ times: { c2: '10:00' } });
  });

  it('terazi: ağırlıkları plana, kuralı çözüme kaydeder; dengelenemeyen formu reddeder (#632)', async () => {
    await setup({ testId: '9' });
    component.selectKind('balanceScale');
    fixture.detectChanges();
    expect(el('[data-testid="balance-scale-form"]')).not.toBeNull();

    ['5', '9', '10', '15'].forEach((value, i) => component.setBalanceWeight('cards', i, { value }));
    fixture.detectChanges();
    expect(component.errors().map((e) => e.key)).toContain('balanceUnsolvableAll');

    component.setBalanceWeight('cards', 3, { value: '14' });
    fixture.detectChanges();
    expect(component.errors()).toEqual([]);
    expect(el('[data-testid="bs-example"]')?.textContent).toContain('5 kg + 14 kg = 9 kg + 10 kg');
    expect(el('[data-testid="preview"] app-question-canvas-balance-scale')).not.toBeNull();

    component.save(false);
    const q = questions.saveBulk.mock.calls[0][0].questions[0];
    expect(q.interactionType).toBe('balanceScale');
    expect(JSON.parse(q.interactionPlan)).toEqual({
      version: 1,
      type: 'balanceScale',
      unit: 'kg',
      cards: [
        { id: 'k1', value: 5 },
        { id: 'k2', value: 9 },
        { id: 'k3', value: 10 },
        { id: 'k4', value: 14 },
      ],
    });
    expect(JSON.parse(q.interactionSolution)).toEqual({ useAll: true });
  });

  it('kelime avı: ızgarayı kelimelerden üretir, önizlemede çizer ve çözümü ayrı kaydeder (#586)', async () => {
    await setup({ testId: '9' });
    component.selectKind('wordSearch');
    fixture.detectChanges();
    expect(el('[data-testid="word-search-form"]')).not.toBeNull();

    component.setWordSearchEntry(0, { word: 'kalp' });
    component.setWordSearchEntry(1, { word: 'mide' });
    component.setWordSearchEntry(2, { word: 'akciğer', label: 'Solunum organı' });
    fixture.detectChanges();
    expect(el('[data-testid="preview"] app-question-canvas-word-search')).not.toBeNull();
    const before = component.definition().plan;
    component.shuffleWordSearch();
    expect(component.definition().plan).not.toBe(before);

    component.save(false);
    const q = questions.saveBulk.mock.calls[0][0].questions[0];
    expect(q.interactionType).toBe('wordSearch');
    const plan = JSON.parse(q.interactionPlan);
    expect(plan.letters.length).toBe(8);
    expect(plan.words.map((w: { label: string }) => w.label)).toEqual(['KALP', 'MİDE', 'Solunum organı']);
    expect(Object.keys(JSON.parse(q.interactionSolution).lines)).toEqual(['w1', 'w2', 'w3']);
  });

  it('çengel bulmaca: otomatik yerleştirir, önizlemede ızgara çizer ve çözümü ayrı kaydeder (#586)', async () => {
    await setup({ testId: '9' });
    component.selectKind('crossword');
    fixture.detectChanges();
    expect(el('[data-testid="crossword-form"]')).not.toBeNull();

    component.setCrosswordEntry(0, { answer: 'kalp', clue: 'Kanı pompalar' });
    component.setCrosswordEntry(1, { answer: 'kir', clue: 'Temizliğin zıttı' });
    component.autoLayoutCrossword();
    fixture.detectChanges();
    expect(component.crosswordUnplaced()).toEqual([]);
    expect(el('[data-testid="preview"] app-question-canvas-crossword')).not.toBeNull();
    expect(el('[data-testid="cw-auto-result"]')).not.toBeNull();

    component.save(false);
    const q = questions.saveBulk.mock.calls[0][0].questions[0];
    expect(q.interactionType).toBe('crossword');
    const plan = JSON.parse(q.interactionPlan);
    expect(plan.clues.map((c: { dir: string }) => c.dir).sort()).toEqual(['across', 'down']);
    expect(q.interactionPlan).not.toContain('KALP');
    expect(JSON.parse(q.interactionSolution)).toEqual({ answers: { k1: 'KALP', k2: 'KİR' } });
  });

  it('çengel bulmaca: görselden Claude ile ızgara + ipuçlarını okur, cevaplar öğretmende (#586 dilim e)', async () => {
    await setup({ testId: '9' }, [], true);
    component.selectKind('crossword');
    component.image.set({ dataUrl: 'data:image/png;base64,QUJD', src: 'data:image/png;base64,QUJD', width: 400, height: 300 });
    fixture.detectChanges();
    expect(labeling.getAiModels).toHaveBeenCalled();
    expect(el('[data-testid="cw-extract"]')).not.toBeNull();

    labeling.activityStructure.mockReturnValue(
      of({
        results: [
          {
            key: 'crossword',
            interactionType: 'crossword',
            status: 'ok',
            origin: 'live',
            plan: {
              version: 1,
              type: 'crossword',
              rows: 4,
              cols: 4,
              clues: [
                { id: 'k1', dir: 'across', row: 0, col: 0, length: 4, number: 1, text: 'Kanı pompalar' },
                { id: 'k2', dir: 'down', row: 0, col: 2, length: 3, number: 2, text: 'Temizliğin zıttı' },
              ],
            },
            instruction: 'Bulmacayı çözünüz.',
            valid: true,
            validationErrors: [],
            promptVersion: 0,
            inputTokens: 1,
            outputTokens: 1,
            estimatedCostUsd: 0,
            error: null,
          },
        ],
      })
    );
    component.extractCrossword();
    fixture.detectChanges();

    const request = labeling.activityStructure.mock.calls[0][0];
    expect(request.imageBase64).toBe('QUJD');
    expect(request.model).toBe('claude-haiku-5-5');
    expect(request.blocks).toEqual([{ key: 'crossword', x: 0, y: 0, width: 400, height: 300, interactionType: 'crossword' }]);
    const cw = component.crossword()!;
    expect(cw.rows).toBe(4);
    expect(cw.entries.map((e) => [e.answer, e.clue, e.dir, e.row, e.col, e.length])).toEqual([
      ['', 'Kanı pompalar', 'across', 0, 0, 4],
      ['', 'Temizliğin zıttı', 'down', 0, 2, 3],
    ]);
    expect(component.instruction()).toBe('Bulmacayı çözünüz.');
    expect(component.crosswordLength(cw.entries[0])).toBe(4);
    expect(component.errors().some((e) => e.key === 'crosswordAnswerRequired')).toBe(true);

    component.setCrosswordEntry(0, { answer: 'kalp' });
    component.setCrosswordEntry(1, { answer: 'lav' });
    fixture.detectChanges();
    expect(component.errors()).toEqual([]);
    component.setCrosswordEntry(1, { answer: 'lavlar' });
    expect(component.crosswordLengthMismatch(component.crossword()!.entries[1])).toBe(true);
  });

  it('çengel bulmaca: Claude yetkisi olmayan öğretmende görselden çıkarma görünmez', async () => {
    await setup({ testId: '9' });
    component.selectKind('crossword');
    fixture.detectChanges();
    expect(labeling.getAiModels).not.toHaveBeenCalled();
    expect(el('[data-testid="cw-extract"]')).toBeNull();
  });

  it('düzenleme: görselsiz maddeyi plandan kurar ve tekli kayıtla aynı kapsama yazar', async () => {
    await setup({ testId: '9', questionId: '31' }, [
      {
        id: 31,
        interactionType: 'match',
        interactionPlan: MATCH_PLAN,
        interactionSolution: JSON.stringify({ l1: 'qq' }),
        imageUrl: '',
        passage: { id: 77, kind: 1, instruction: 'Başkentleri eşleştir.' },
        topicId: 0,
        subjectId: 0,
        answers: [],
      },
      { id: 32, interactionType: 'mcq', answers: [] },
    ]);

    expect(component.editingId()).toBe(31);
    expect(component.match()).toEqual({ kind: 'match', pairs: [{ left: 'Ankara', right: 'Türkiye' }], distractors: ['Fransa'] });
    expect(component.items().map((i) => i.id)).toEqual([31]); // MCQ formla düzenlenemez, listede yok
    fixture.detectChanges();
    expect(el('[data-testid="kind-locked"]')?.textContent).toContain('Eşleştirme');
    expect(el('.ae__kinds')).toBeNull(); // pasif tür seçenekleri gösterilmez

    component.setPair(0, 'right', 'Türkiye Cumhuriyeti');
    component.save(false);

    const body = questions.saveQuestion.mock.calls[0][0];
    expect(body.id).toBe(31);
    expect(body.passage).toEqual({ id: 77, kind: PassageKind.Activity, instruction: 'Başkentleri eşleştir.' });
    expect(JSON.parse(body.interactionPlan).right.map((r: { label: string }) => r.label)).toEqual([
      'Türkiye Cumhuriyeti',
      'Fransa',
    ]);
    expect(questions.saveBulk).not.toHaveBeenCalled();
    expect(questions.updateClassification).not.toHaveBeenCalled();
  });

  it('kayıt hatası formda kalır ve sunucu mesajını gösterir', async () => {
    await setup({ testId: '9' });
    questions.saveBulk.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 500, error: { message: 'Sunucu hatası' } }))
    );
    component.setRowText(0, 'x');
    component.toggleRowCorrect(0, 0);
    component.save(true);
    fixture.detectChanges();

    expect(el('[data-testid="save-error"]')?.textContent).toContain('Sunucu hatası');
    expect(router.navigate).not.toHaveBeenCalledWith(['/test', 9]);
  });
  it('boşluk doldurma: seçili kelimeyi boşluk yapar, slotFill düzenli plan kaydeder ve önizlemede metin içi kutu çizer', async () => {
    await setup({ testId: '9' });
    component.selectKind('fillText');
    fixture.detectChanges();

    const area = el('[data-testid="fill-text"]') as HTMLTextAreaElement;
    area.value = 'Başkent Ankara.';
    area.dispatchEvent(new Event('input'));
    area.setSelectionRange(8, 14);
    (el('[data-testid="make-blank"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(component.fillText()?.text).toBe('Başkent [[Ankara]].');
    expect(el('[data-testid="preview"] .sf-flow [data-slot-input="s1"]')).not.toBeNull();

    component.save(false);
    const q = questions.saveBulk.mock.calls[0][0].questions[0];
    expect(q.interactionType).toBe('slotFill');
    expect(JSON.parse(q.interactionPlan).layout).toEqual({ kind: 'text', text: 'Başkent [[s1]].' });
    expect(JSON.parse(q.interactionSolution)).toEqual({ s1: { accept: ['Ankara'], optionalSuffixes: [] } });
  });

  it('tablo: odaktaki hücreyi ya da hücrede seçili metni boşluk yapar, boşluktan çıkarır', async () => {
    await setup({ testId: '9' });
    component.selectKind('fillTable');
    component.setTableHeader(false);
    component.setCell(0, 0, '7');
    component.setCell(0, 1, '5 cm');
    fixture.detectChanges();

    const first = el('[data-cell="0:0"]') as HTMLInputElement;
    first.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    (el('[data-testid="table-make-blank"]') as HTMLButtonElement).click();
    expect(component.fillTable()?.cells[0][0]).toBe('[[7]]');
    expect(component.blankCount()).toBe(1);

    const second = el('[data-cell="0:1"]') as HTMLInputElement;
    second.dispatchEvent(new Event('focus'));
    second.setSelectionRange(0, 1);
    fixture.detectChanges();
    (el('[data-testid="table-make-blank"]') as HTMLButtonElement).click();
    expect(component.fillTable()?.cells[0][1]).toBe('[[5]] cm');

    component.setCell(0, 1, '[[bir|1]] metre');
    component.removeTableBlanks();
    expect(component.fillTable()?.cells[0][1]).toBe('bir metre');
  });

  it('tablo: seçme kipinde sürükleyerek birden çok hücre seçer, başlığı atlar ve hepsini tek seferde boşluk yapar', async () => {
    await setup({ testId: '9' });
    component.selectKind('fillTable');
    component.setTableHeader(true);
    component.addTableRow();
    component.setCell(1, 0, 'a');
    component.setCell(2, 1, '[[b]]');
    component.toggleTableSelectMode();
    fixture.detectChanges();

    expect((el('[data-cell="1:0"]') as HTMLInputElement).readOnly).toBe(true);
    const down = new PointerEvent('pointerdown', { bubbles: true, cancelable: true });
    el('[data-cell="0:0"]')!.dispatchEvent(down);
    expect(component.tableSelection().size).toBe(0);

    el('[data-cell="1:0"]')!.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true }));
    el('[data-cell="2:1"]')!.dispatchEvent(new PointerEvent('pointerenter'));
    component.endTableDrag();
    expect([...component.tableSelection()].sort()).toEqual(['1:0', '1:1', '2:0', '2:1']);

    component.makeTableBlanks();
    const cells = component.fillTable()!.cells;
    expect([cells[1][0], cells[1][1], cells[2][0], cells[2][1]]).toEqual(['[[a]]', '[[]]', '[[]]', '[[b]]']);
    expect(cells[0].every((c) => !c.includes('[['))).toBe(true);

    component.removeTableColumn(1);
    expect(component.tableSelection().size).toBe(0);
  });

  it('tablo: seçme kipinden çıkıp hücreye girince eski çoklu seçim bırakılır, seçili metin boşluk olur', async () => {
    await setup({ testId: '9' });
    component.selectKind('fillTable');
    component.setTableHeader(false);
    component.setCell(0, 0, '6');
    component.setCell(0, 1, '50 cm');
    component.toggleTableSelectMode();
    fixture.detectChanges();
    el('[data-cell="0:0"]')!.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true }));
    component.endTableDrag();
    component.makeTableBlanks();
    component.toggleTableSelectMode();
    fixture.detectChanges();

    const cell = el('[data-cell="0:1"]') as HTMLInputElement;
    cell.dispatchEvent(new Event('focus'));
    cell.setSelectionRange(0, 2);
    fixture.detectChanges();
    expect(component.tableSelection().size).toBe(0);
    (el('[data-testid="table-make-blank"]') as HTMLButtonElement).click();

    expect(component.fillTable()?.cells[0]).toEqual(['[[6]]', '[[50]] cm']);
  });

  it('kayıtlı kelime havuzu maddesini düzenlemek için forma açar', async () => {
    const plan = JSON.stringify({
      version: 1,
      type: 'dragPlace',
      mode: 'place',
      reuse: false,
      layout: { kind: 'text', text: 'Su [[t1]] derecede donar.' },
      targets: [{ id: 't1', capacity: 1 }],
      cards: [
        { id: 'ab12cd34', label: '50' },
        { id: 'ef56gh78', label: '0' },
      ],
    });
    await setup({ testId: '9', questionId: '41' }, [
      { id: 41, interactionType: 'dragPlace', interactionPlan: plan, interactionSolution: '{"t1":["ef56gh78"]}', passage: null },
    ]);

    expect(component.wordPool()).toEqual({ kind: 'wordPool', text: 'Su [[0]] derecede donar.', distractors: ['50'] });
    expect(component.items()[0].kind).toBe('wordPool');
  });
});
