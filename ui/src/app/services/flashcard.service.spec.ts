import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FlashcardService, decodeFlashcardText, encodeFlashcardText, flashcardErrorCode } from './flashcard.service';

describe('FlashcardService (issue #699)', () => {
  let service: FlashcardService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(FlashcardService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('text_RoundTrips_WithoutLosingComparisons', () => {
    const text = 'x<y & y>z';
    expect(encodeFlashcardText(text)).toBe('x&lt;y &amp; y&gt;z');
    expect(decodeFlashcardText(encodeFlashcardText(text))).toBe(text);
  });

  it('decode_DoesNotDoubleDecode', () => {
    expect(decodeFlashcardText('&amp;lt;')).toBe('&lt;');
  });

  it('addCard_EncodesText_AndDecodesResponse', () => {
    let front = '';
    service.addCard('student', 3, { front: 'a<b', back: 'c', image: null, removeImage: false }).subscribe((card) => (front = card.front));

    const req = http.expectOne('/api/exam/flashcards/decks/3/cards');
    expect(req.request.body.front).toBe('a&lt;b');
    req.flush({ id: 1, front: 'a&lt;b', back: 'c', imageUrl: null, order: 0 });
    expect(front).toBe('a<b');
  });

  it('teacherMode_UsesTeacherEndpoints', () => {
    service.listDecks('teacher').subscribe();
    http.expectOne('/api/exam/teacher/flashcards/decks').flush([]);
  });

  it('getDue_SendsDeck_WhenGiven', () => {
    service.getDue(5).subscribe();
    http.expectOne('/api/exam/flashcards/due?take=50&deckId=5').flush([]);
  });

  it('getDueCount_ReturnsCount', () => {
    let count = -1;
    service.getDueCount().subscribe((value) => (count = value));
    http.expectOne('/api/exam/flashcards/due-count').flush({ count: 4 });
    expect(count).toBe(4);
  });

  it('errorCode_ReadsRuleError_OnlyWhenPresent', () => {
    expect(flashcardErrorCode(new HttpErrorResponse({ status: 400, error: { errorCode: 'AlreadyAssigned' } }))).toBe('AlreadyAssigned');
    expect(flashcardErrorCode(new HttpErrorResponse({ status: 500 }))).toBeNull();
  });
});
