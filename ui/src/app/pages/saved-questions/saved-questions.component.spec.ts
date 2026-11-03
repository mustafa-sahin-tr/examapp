import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { SavedQuestion } from '../../models/practice';
import { PracticeService } from '../../services/practice.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { SavedQuestionsComponent } from './saved-questions.component';

const item: SavedQuestion = { questionId: 1, subjectId: 10, subjectName: 'Matematik', topicName: null, savedAt: '2026-11-01T10:00:00Z' };

describe('SavedQuestionsComponent (issue #532)', () => {
  let practice: {
    getSavedQuestions: ReturnType<typeof vi.fn>;
    startSession: ReturnType<typeof vi.fn>;
    unsaveQuestion: ReturnType<typeof vi.fn>;
  };

  function create(items: SavedQuestion[]): ComponentFixture<SavedQuestionsComponent> {
    practice = {
      getSavedQuestions: vi.fn().mockReturnValue(of({ totalCount: items.length, items })),
      startSession: vi.fn().mockReturnValue(of({ id: 9 })),
      unsaveQuestion: vi.fn().mockReturnValue(of(undefined)),
    };
    TestBed.configureTestingModule({
      imports: [SavedQuestionsComponent, translocoTestingModule()],
      providers: [provideRouter([]), { provide: PracticeService, useValue: practice }],
    });
    const fixture = TestBed.createComponent(SavedQuestionsComponent);
    fixture.detectChanges();
    return fixture;
  }

  const el = (fixture: ComponentFixture<SavedQuestionsComponent>) => fixture.nativeElement as HTMLElement;

  it('empty_ExplainsHowToSave', () => {
    const fixture = create([]);
    expect(el(fixture).querySelector('[data-test="saved-empty"]')).not.toBeNull();
    expect(el(fixture).querySelector('[data-test="saved-start"]')).toBeNull();
  });

  it('start_OpensASavedSession', () => {
    const fixture = create([item]);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    el(fixture).querySelector<HTMLButtonElement>('[data-test="saved-start"]')?.click();

    expect(practice.startSession).toHaveBeenCalledWith({ saved: true });
    expect(navigate).toHaveBeenCalledWith(['/practice'], { queryParams: { session: 9 } });
  });

  it('remove_TakesTheRowOff_AndKeepsItOnError', () => {
    const fixture = create([item]);
    const component = fixture.componentInstance;

    practice.unsaveQuestion.mockReturnValueOnce(throwError(() => new Error('x')));
    component.remove(item);
    expect(component.items().length).toBe(1);
    expect(component.actionError()).toBe('remove');

    component.remove(item);
    expect(practice.unsaveQuestion).toHaveBeenCalledWith(1);
    expect(component.items().length).toBe(0);
  });
});
