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
});
