import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import flashcardsTr from '../../../../public/i18n/flashcards/tr.json';
import { FlashcardDeckSummary, FlashcardMode } from '../../models/flashcard.model';
import { FlashcardService } from '../../services/flashcard.service';
import { GradesService } from '../../services/grades.service';
import { SubjectService } from '../../services/subject.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { FlashcardDecksComponent } from './flashcard-decks.component';

const deck = (id: number, isOwner: boolean, dueCount: number | null = 0): FlashcardDeckSummary => ({
  id,
  title: `Deck ${id}`,
  gradeId: null,
  gradeName: null,
  subjectId: null,
  subjectName: null,
  topicId: null,
  topicName: null,
  cardCount: 2,
  dueCount,
  isOwner,
});

describe('FlashcardDecksComponent (issue #699)', () => {
  let service: { listDecks: ReturnType<typeof vi.fn>; createDeck: ReturnType<typeof vi.fn>; deleteDeck: ReturnType<typeof vi.fn> };
  let dialogResult: boolean;

  function create(decks: FlashcardDeckSummary[], mode: FlashcardMode = 'student'): ComponentFixture<FlashcardDecksComponent> {
    dialogResult = true;
    service = {
      listDecks: vi.fn().mockReturnValue(of(decks)),
      createDeck: vi.fn().mockReturnValue(of(deck(9, true))),
      deleteDeck: vi.fn().mockReturnValue(of(undefined)),
    };
    TestBed.configureTestingModule({
      imports: [FlashcardDecksComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'flashcards/tr': flashcardsTr } })],
      providers: [
        provideRouter([]),
        { provide: FlashcardService, useValue: service },
        { provide: ActivatedRoute, useValue: { snapshot: { data: { mode } } } },
        { provide: MatDialog, useValue: { open: () => ({ afterClosed: () => of(dialogResult) }) } },
        { provide: GradesService, useValue: { getGrades: () => of([]) } },
        { provide: SubjectService, useValue: {} },
      ],
    });
    const fixture = TestBed.createComponent(FlashcardDecksComponent);
    fixture.detectChanges();
    return fixture;
  }

  const query = (fixture: ComponentFixture<FlashcardDecksComponent>, test: string): HTMLElement | null =>
    fixture.nativeElement.querySelector(`[data-test="${test}"]`);

  it('empty_ShowsEmptyState', () => {
    expect(query(create([]), 'decks-empty')).not.toBeNull();
  });

  it('empty_NewDeckButtonInsideTheCardOpensTheForm (#915)', () => {
    const fixture = create([]);

    query(fixture, 'deck-new-empty')!.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-flashcard-deck-form')).not.toBeNull();
    expect(query(fixture, 'deck-new-empty')).toBeNull();
  });

  it('loadError_RetryLoadsAgain', () => {
    const fixture = create([]);
    service.listDecks.mockReturnValueOnce(throwError(() => new Error('x')));
    fixture.componentInstance.load();
    expect(fixture.componentInstance.loadFailed()).toBe(true);
    fixture.componentInstance.load();
    expect(fixture.componentInstance.loadFailed()).toBe(false);
  });

  it('assignedDeck_CanBeReviewed_ButNotEdited', () => {
    const fixture = create([deck(1, false, 3)]);
    expect(query(fixture, 'deck-review')).not.toBeNull();
    expect(query(fixture, 'deck-edit')).toBeNull();
    expect(query(fixture, 'deck-delete')).toBeNull();
    expect(query(fixture, 'decks-review-all')?.textContent).toContain('3');
  });

  it('noDueCards_HidesReviewLinks', () => {
    const fixture = create([deck(1, true, 0)]);
    expect(query(fixture, 'deck-review')).toBeNull();
    expect(query(fixture, 'decks-review-all')).toBeNull();
    expect(query(fixture, 'deck-edit')).not.toBeNull();
  });

  it('teacher_LoadsOwnDecks_WithoutReviewLinks', () => {
    const fixture = create([deck(1, true, null)], 'teacher');
    expect(service.listDecks).toHaveBeenCalledWith('teacher');
    expect(query(fixture, 'deck-review')).toBeNull();
    expect(query(fixture, 'deck-edit')).not.toBeNull();
  });

  it('create_OpensTheNewDeck', () => {
    const fixture = create([], 'teacher');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.componentInstance.create({ title: 'New', gradeId: 1, subjectId: 2, topicId: null });
    expect(navigate).toHaveBeenCalledWith(['/teacher/flashcards', 9]);
  });

  it('create_RuleError_ShowsItsMessage', () => {
    const fixture = create([]);
    service.createDeck.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { errorCode: 'DeckLimitReached' } })));
    fixture.componentInstance.create({ title: 'New', gradeId: null, subjectId: null, topicId: null });
    expect(fixture.componentInstance.errorKey()).toBe('DeckLimitReached');
    expect(fixture.componentInstance.saving()).toBe(false);
  });

  it('delete_NeedsConfirmation', () => {
    const fixture = create([deck(1, true)]);
    dialogResult = false;
    fixture.componentInstance.confirmDelete(deck(1, true));
    expect(service.deleteDeck).not.toHaveBeenCalled();

    dialogResult = true;
    fixture.componentInstance.confirmDelete(deck(1, true));
    expect(service.deleteDeck).toHaveBeenCalledWith('student', 1);
    expect(fixture.componentInstance.decks()).toEqual([]);
  });
});
