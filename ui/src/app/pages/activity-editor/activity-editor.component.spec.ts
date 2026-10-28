import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ActivityEditorComponent } from './activity-editor.component';
import { QuestionService } from '../../services/question.service';
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

  async function setup(query: Record<string, string>, list: unknown[] = []) {
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
