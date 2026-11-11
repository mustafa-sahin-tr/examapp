import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { MistakeItem } from '../../models/practice';
import { PracticeService } from '../../services/practice.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { MistakeNotebookComponent } from './mistake-notebook.component';
import practiceTr from '../../../../public/i18n/practice/tr.json';
import { AskTeacherService } from '../../services/ask-teacher.service';

const item = (questionId: number, subjectId: number, subjectName: string): MistakeItem => ({
  questionId,
  subjectId,
  subjectName,
  topicId: null,
  topicName: null,
  lastWrongAt: '2026-11-01T10:00:00Z',
  wrongCount: 2,
  reason: null,
});

describe('MistakeNotebookComponent (issue #698)', () => {
  let practice: {
    getMistakes: ReturnType<typeof vi.fn>;
    startSession: ReturnType<typeof vi.fn>;
    setMistakeReason: ReturnType<typeof vi.fn>;
  };

  function create(items: MistakeItem[]): ComponentFixture<MistakeNotebookComponent> {
    practice = {
      getMistakes: vi.fn().mockReturnValue(of({ totalCount: items.length, items })),
      startSession: vi.fn().mockReturnValue(of({ id: 9 })),
      setMistakeReason: vi.fn().mockReturnValue(of(undefined)),
    };
    TestBed.configureTestingModule({
      imports: [MistakeNotebookComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'practice/tr': practiceTr } })],
      providers: [
        provideRouter([]),
        { provide: PracticeService, useValue: practice },
        // Issue #1111: "Öğretmene sorduklarım" bölümü boş (hiçbir şey çizmez).
        { provide: AskTeacherService, useValue: { notebook: () => of([]) } },
      ],
    });
    const fixture = TestBed.createComponent(MistakeNotebookComponent);
    fixture.detectChanges();
    return fixture;
  }

  const el = (fixture: ComponentFixture<MistakeNotebookComponent>) => fixture.nativeElement as HTMLElement;

  it('empty_ShowsGoodNewsAndPracticeLink', () => {
    const fixture = create([]);
    expect(el(fixture).querySelector('[data-test="mistakes-empty"]')).not.toBeNull();
    expect(el(fixture).querySelector('[data-test="mistakes-start"]')).toBeNull();
  });

  it('list_RendersItems', () => {
    const fixture = create([item(1, 10, 'Matematik'), item(2, 11, 'Fen')]);
    expect(el(fixture).querySelectorAll('[data-test="mistake-item"]').length).toBe(2);
  });

  it('subjectFilter_NarrowsListAndPractice', () => {
    const fixture = create([item(1, 10, 'Matematik'), item(2, 11, 'Fen')]);
    const component = fixture.componentInstance;
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    component.subjectId.set(11);
    fixture.detectChanges();
    expect(el(fixture).querySelectorAll('[data-test="mistake-item"]').length).toBe(1);

    el(fixture).querySelector<HTMLButtonElement>('[data-test="mistakes-start"]')?.click();
    expect(practice.startSession).toHaveBeenCalledWith({ mistakes: true, subjectIds: [11] });
  });

  it('start_OpensThePracticeSession', () => {
    const fixture = create([item(1, 10, 'Matematik')]);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    fixture.componentInstance.startPractice();

    expect(practice.startSession).toHaveBeenCalledWith({ mistakes: true, subjectIds: [] });
    expect(navigate).toHaveBeenCalledWith(['/practice'], { queryParams: { session: 9 } });
  });

  it('setReason_Saves_AndRevertsOnError', () => {
    const fixture = create([item(1, 10, 'Matematik')]);
    const component = fixture.componentInstance;

    component.setReason(component.items()[0], 'Calculation');
    expect(practice.setMistakeReason).toHaveBeenCalledWith(1, 'Calculation');
    expect(component.items()[0].reason).toBe('Calculation');

    practice.setMistakeReason.mockReturnValue(throwError(() => new Error('x')));
    component.setReason(component.items()[0], 'Time');
    expect(component.items()[0].reason).toBe('Calculation');
    expect(component.saveFailed()).toBe(true);
  });

  // Issue #862: satır soruyu, cevapları, ilerlemeyi gösterir; neden çip ile seçilir; tek soru tekrar çözülür.
  it('row_ShowsQuestion_Answers_Progress_AndTip_issue862', () => {
    const fixture = create([
      { ...item(1, 10, 'Matematik'), imageUrl: 'https://img/1.png', yourAnswer: 'C', correctAnswer: 'B', correctSinceWrong: 1, correctToClear: 2 },
      { ...item(2, 10, 'Matematik'), text: 'Metin sorusu', yourAnswer: null, correctAnswer: 'A', correctSinceWrong: 0, correctToClear: 2 },
    ]);
    const rows = el(fixture).querySelectorAll('[data-test="mistake-item"]');

    expect(rows[0].querySelector('img')?.getAttribute('src')).toBe('https://img/1.png');
    expect(rows[0].querySelector('[data-test="mistake-answers"]')?.textContent).toContain('Senin cevabın C');
    expect(rows[0].querySelector('[data-test="mistake-answers"]')?.textContent).toContain('Doğru B');
    expect(rows[0].querySelectorAll('.mnb__dot.is-done').length).toBe(1);
    expect(rows[1].textContent).toContain('Metin sorusu');
    expect(rows[1].querySelector('[data-test="mistake-answers"]')?.textContent).toContain('Boş bıraktın');
    expect(el(fixture).querySelector('[data-test="mistakes-start"]')?.textContent).toContain('Hepsini çöz (2)');

    (rows[0].querySelector('[data-reason="Attention"] button') as HTMLElement).click(); // çipin iç düğmesi
    fixture.detectChanges();
    expect(practice.setMistakeReason).toHaveBeenCalledWith(1, 'Attention');
    expect(rows[0].querySelector('[data-test="mistake-tip"]')?.textContent).toContain('altını çiz');
  });

  it('retry_StartsASingleQuestionSession_issue862', () => {
    const fixture = create([item(1, 10, 'Matematik'), item(2, 10, 'Matematik')]);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    el(fixture).querySelectorAll<HTMLButtonElement>('[data-test="mistake-retry"]')[1].click();

    expect(practice.startSession).toHaveBeenCalledWith({ mistakes: true, mistakeQuestionId: 2 });
    expect(navigate).toHaveBeenCalledWith(['/practice'], { queryParams: { session: 9 } });
  });

  it('loadError_OffersRetry', () => {
    const fixture = create([]);
    practice.getMistakes.mockReturnValue(throwError(() => new Error('x')));
    fixture.componentInstance.load();
    fixture.detectChanges();
    expect(fixture.componentInstance.loadFailed()).toBe(true);
    expect(el(fixture).querySelector('[role="alert"] button')).not.toBeNull();
  });
});
