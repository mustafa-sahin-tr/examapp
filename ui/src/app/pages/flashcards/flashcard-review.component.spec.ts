import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import flashcardsTr from '../../../../public/i18n/flashcards/tr.json';
import { FlashcardDueCard } from '../../models/flashcard.model';
import { FlashcardService } from '../../services/flashcard.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { FlashcardReviewComponent } from './flashcard-review.component';

const card = (id: number): FlashcardDueCard => ({
  id,
  front: `front ${id}`,
  back: `back ${id}`,
  imageUrl: null,
  order: id,
  deckId: 1,
  deckTitle: 'Deck',
  box: null,
});

describe('FlashcardReviewComponent (issue #699)', () => {
  let service: { getDue: ReturnType<typeof vi.fn>; review: ReturnType<typeof vi.fn>; getDueSummary: ReturnType<typeof vi.fn> };
  let nextDueDay: string | null = null;

  function create(cards: FlashcardDueCard[], deck: string | null = null): ComponentFixture<FlashcardReviewComponent> {
    service = {
      getDue: vi.fn().mockReturnValueOnce(of(cards)).mockReturnValue(of([])),
      review: vi.fn().mockReturnValue(of(undefined)),
      getDueSummary: vi.fn(() => of({ count: 0, nextDueDay, nextDueCount: nextDueDay ? 6 : 0 })),
    };
    TestBed.configureTestingModule({
      imports: [FlashcardReviewComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'flashcards/tr': flashcardsTr } })],
      providers: [
        provideRouter([]),
        { provide: FlashcardService, useValue: service },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(deck ? { deck } : {}) } } },
      ],
    });
    const fixture = TestBed.createComponent(FlashcardReviewComponent);
    fixture.detectChanges();
    return fixture;
  }

  const query = (fixture: ComponentFixture<FlashcardReviewComponent>, test: string): HTMLElement | null =>
    fixture.nativeElement.querySelector(`[data-test="${test}"]`);

  function reveal(fixture: ComponentFixture<FlashcardReviewComponent>): void {
    query(fixture, 'review-reveal')!.click();
    fixture.detectChanges();
  }

  it('nothingDue_SaysWhenTheNextReviewIs (#915)', () => {
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    nextDueDay = `${tomorrow.getFullYear()}-${String(tomorrow.getMonth() + 1).padStart(2, '0')}-${String(tomorrow.getDate()).padStart(2, '0')}`;
    const fixture = create([]);
    nextDueDay = null;

    expect(query(fixture, 'review-done')?.textContent).toContain('Bugün tekrar yok');
    expect(query(fixture, 'review-next')?.textContent).toContain('Sıradaki tekrar: yarın 6 kart.');
  });

  it('nothingDue_NoFutureReview_SaysNothingMore (#915)', () => {
    const fixture = create([]);

    expect(query(fixture, 'review-next')).toBeNull();
  });

  it('backAndRating_HiddenUntilRevealed', () => {
    const fixture = create([card(1)]);
    expect(query(fixture, 'review-front')?.textContent).toContain('front 1');
    expect(query(fixture, 'review-back')).toBeNull();
    expect(query(fixture, 'rate-Good')).toBeNull();

    reveal(fixture);

    expect(query(fixture, 'review-back')?.textContent).toContain('back 1');
    expect(query(fixture, 'rate-Good')).not.toBeNull();
  });

  it('good_MovesOn_AndHidesTheNextBack', () => {
    const fixture = create([card(1), card(2)]);
    reveal(fixture);
    query(fixture, 'rate-Good')!.click();
    fixture.detectChanges();

    expect(service.review).toHaveBeenCalledWith(1, 'Good');
    expect(query(fixture, 'review-front')?.textContent).toContain('front 2');
    expect(query(fixture, 'review-back')).toBeNull();
  });

  it('forgot_ComesBackAtTheEnd', () => {
    const fixture = create([card(1), card(2)]);
    fixture.componentInstance.rate('Forgot');
    expect(fixture.componentInstance.queue().map((c) => c.id)).toEqual([2, 1]);
    expect(fixture.componentInstance.reviewedCount()).toBe(1);
  });

  it('notDue_IsSilentlySkipped', () => {
    const fixture = create([card(1), card(2)]);
    service.review.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { errorCode: 'NotDue' } })));
    fixture.componentInstance.rate('Good');
    expect(fixture.componentInstance.saveFailed()).toBe(false);
    expect(fixture.componentInstance.current()?.id).toBe(2);
  });

  it('serverError_KeepsTheCard_AndShowsError', () => {
    const fixture = create([card(1)]);
    service.review.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    fixture.componentInstance.rate('Good');
    expect(fixture.componentInstance.saveFailed()).toBe(true);
    expect(fixture.componentInstance.current()?.id).toBe(1);
    expect(fixture.componentInstance.saving()).toBe(false);
  });

  it('queueEnd_AsksServerAgain_ThenShowsDone', () => {
    const fixture = create([card(1)]);
    fixture.componentInstance.rate('Good');
    fixture.detectChanges();
    expect(service.getDue).toHaveBeenCalledTimes(2);
    expect(query(fixture, 'review-done')).not.toBeNull();
    expect(fixture.componentInstance.reviewedCount()).toBe(1);
  });

  it('deckQuery_LimitsTheQueue', () => {
    create([], '7');
    expect(service.getDue).toHaveBeenCalledWith(7);
  });

  it('noDeckQuery_ReviewsEverything', () => {
    create([]);
    expect(service.getDue).toHaveBeenCalledWith(null);
  });
});
