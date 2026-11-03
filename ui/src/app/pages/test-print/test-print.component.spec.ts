import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { WorksheetKind } from '../../models/activity';
import { Question } from '../../models/question';
import { QuestionService } from '../../services/question.service';
import { TestService } from '../../services/test.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { TestPrintComponent, buildPrintSheet } from './test-print.component';

function question(id: number, overrides: Partial<Question> = {}): Question {
  return {
    id,
    text: `Soru ${id}`,
    imageUrl: '',
    answerColCount: 2,
    answers: [
      { id: id * 10, text: 'Bir' },
      { id: id * 10 + 1, text: 'İki' },
    ],
    correctAnswerId: id * 10 + 1,
    ...overrides,
  } as Question;
}

describe('buildPrintSheet (issue #701)', () => {
  it('leaves out licensed and interactive questions and reports their positions', () => {
    const { items, exclusions } = buildPrintSheet([
      question(1, { printBlock: 'publisherLicense' }),
      question(2),
      question(3, { interactionType: 'matrixChoice' }),
      question(4, { interactionType: 'mcq' }),
      question(5, { printBlock: 'publisherLicense' }),
    ]);

    expect(items.map((i) => [i.number, i.question.id])).toEqual([[1, 2], [2, 4]]);
    expect(exclusions).toEqual([
      { reason: 'publisherLicense', positions: [1, 5] },
      { reason: 'interactive', positions: [3] },
    ]);
  });

  it('prints a shared passage only once for consecutive questions', () => {
    const passage = { id: 7 } as Question['passage'];
    const { items } = buildPrintSheet([question(1, { passage }), question(2, { passage }), question(3)]);

    expect(items.map((i) => i.showPassage)).toEqual([true, false, false]);
  });

  it('takes the key letter from the page order for text answers', () => {
    const { items } = buildPrintSheet([question(1)]);

    expect(items[0].answersInImage).toBe(false);
    expect(items[0].keyLetter).toBe('B');
  });

  it('takes the key letter from the label when answers are inside the image', () => {
    const { items } = buildPrintSheet([
      question(1, {
        imageUrl: '/img/q/question.jpg',
        imageUrlV2: '/img/q/question-v2.jpg',
        answers: [
          { id: 10, text: 'C', imageUrl: '/img/q/a.jpg' },
          { id: 11, text: 'D', imageUrl: '/img/q/b.jpg' },
        ],
        correctAnswerId: 11,
      } as Partial<Question>),
    ]);

    expect(items[0].answersInImage).toBe(true);
    expect(items[0].keyLetter).toBe('D');
  });

  it('falls back to the written answer when there is no correct choice', () => {
    const { items } = buildPrintSheet([
      question(1, { answers: [], correctAnswerId: undefined, practiceCorrectAnswer: '42' }),
      question(2, { answers: [], correctAnswerId: undefined }),
    ]);

    expect(items[0].keyLetter).toBeNull();
    expect(items[0].keyText).toBe('42');
    expect(items[1].keyText).toBeNull();
  });
});

describe('TestPrintComponent (issue #701)', () => {
  let printSpy: ReturnType<typeof vi.spyOn>;

  function create(questions: Question[], kind = WorksheetKind.Test, fail = false): ComponentFixture<TestPrintComponent> {
    const load = fail ? throwError(() => new Error('nope')) : of(questions);
    TestBed.configureTestingModule({
      imports: [TestPrintComponent, translocoTestingModule()],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '5' }) } } },
        { provide: TestService, useValue: { get: vi.fn().mockReturnValue(of({ id: 5, name: 'Kesirler', kind })) } },
        { provide: QuestionService, useValue: { getAll: vi.fn().mockReturnValue(load) } },
      ],
    });
    const fixture = TestBed.createComponent(TestPrintComponent);
    fixture.detectChanges();
    return fixture;
  }

  const all = (fixture: ComponentFixture<TestPrintComponent>, id: string) =>
    Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll(`[data-testid="${id}"]`));

  beforeEach(() => {
    printSpy = vi.spyOn(window, 'print').mockImplementation(() => undefined);
  });

  afterEach(() => printSpy.mockRestore());

  it('renders printable questions and the answer key on its own page', () => {
    const fixture = create([question(1), question(2)]);

    expect(all(fixture, 'print-question').length).toBe(2);
    expect(all(fixture, 'print-key-item').map((e) => e.textContent?.replace(/\s/g, ''))).toEqual(['1.B', '2.B']);
    expect(getComputedStyle(all(fixture, 'print-key')[0]).breakBefore).toBe('page');
  });

  it('keeps a question together across pages', () => {
    const fixture = create([question(1)]);

    expect(getComputedStyle(all(fixture, 'print-question')[0]).breakInside).toBe('avoid');
  });

  it('hides the answer key when the teacher turns it off', () => {
    const fixture = create([question(1)]);

    (fixture.nativeElement.querySelector('[data-testid="print-key-toggle"] input') as HTMLInputElement).click();
    fixture.detectChanges();

    expect(all(fixture, 'print-key').length).toBe(0);
  });

  it('reports excluded questions and still prints the rest', async () => {
    const fixture = create([question(1, { printBlock: 'unpublished' }), question(2)]);

    expect(all(fixture, 'print-exclusions').length).toBe(1);
    all(fixture, 'print-btn')[0].click();
    await fixture.whenStable();

    expect(printSpy).toHaveBeenCalledOnce();
  });

  it('blocks printing when no question can be printed', () => {
    const fixture = create([question(1, { printBlock: 'publisherLicense' })]);

    expect(all(fixture, 'print-empty').length).toBe(1);
    expect((all(fixture, 'print-btn')[0] as HTMLButtonElement).disabled).toBe(true);
    expect(all(fixture, 'print-question').length).toBe(0);
  });

  it('warns about broken images before printing, then prints on the second press', async () => {
    const fixture = create([question(1, { imageUrl: 'data:image/png;base64,broken' })]);
    const img = fixture.nativeElement.querySelector('.q-content img') as HTMLImageElement;
    await new Promise<void>((resolve) => {
      if (img.complete) resolve();
      img.addEventListener('error', () => resolve(), { once: true });
      img.addEventListener('load', () => resolve(), { once: true });
    });

    all(fixture, 'print-btn')[0].click();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(all(fixture, 'print-broken-images').length).toBe(1);
    });
    expect(printSpy).not.toHaveBeenCalled();

    all(fixture, 'print-btn')[0].click();
    await vi.waitFor(() => expect(printSpy).toHaveBeenCalledOnce());
  });

  it('does not offer printing for an activity', () => {
    const fixture = create([question(1)], WorksheetKind.Activity);

    expect(all(fixture, 'print-activity-note').length).toBe(1);
    expect(all(fixture, 'print-btn').length).toBe(0);
    expect(all(fixture, 'print-question').length).toBe(0);
  });

  it('shows an error when the test cannot be loaded', () => {
    const fixture = create([], WorksheetKind.Test, true);

    expect(all(fixture, 'print-load-error').length).toBe(1);
  });
});
