import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import {
  Flashcard,
  FlashcardAssignTarget,
  FlashcardDeckAssignment,
  FlashcardDeckDetail,
  FlashcardDeckSummary,
  FlashcardDueCard,
  FlashcardDueSummary,
  FlashcardMode,
  FlashcardRecall,
  SaveFlashcard,
  SaveFlashcardDeck,
} from '../models/flashcard.model';

/**
 * Sunucu metni HTML temizleyiciden geçirir. Kodlanmadan giden `x<y ve y>z` etiket sanılıp silinir, dönen `a &lt; b`
 * de ekranda olduğu gibi görünür; bu yüzden gidişte kodlanır, gelişte çözülür.
 */
export function encodeFlashcardText(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

export function decodeFlashcardText(text: string): string {
  return text
    .replace(/&nbsp;/g, ' ')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&amp;/g, '&');
}

/** İş kuralı hatasının kodu (400 `{ errorCode }`); yoksa `null`. */
export function flashcardErrorCode(error: unknown): string | null {
  const code = error instanceof HttpErrorResponse ? error.error?.errorCode : null;
  return typeof code === 'string' ? code : null;
}

function decodeDeck<T extends FlashcardDeckSummary>(deck: T): T {
  return { ...deck, title: decodeFlashcardText(deck.title) };
}

function decodeCard<T extends Flashcard>(card: T): T {
  return { ...card, front: decodeFlashcardText(card.front), back: decodeFlashcardText(card.back) };
}

function encodeDeck(deck: SaveFlashcardDeck): SaveFlashcardDeck {
  return { ...deck, title: encodeFlashcardText(deck.title) };
}

function encodeCard(card: SaveFlashcard): SaveFlashcard {
  return { ...card, front: encodeFlashcardText(card.front), back: encodeFlashcardText(card.back) };
}

@Injectable({ providedIn: 'root' })
export class FlashcardService {
  private readonly http = inject(HttpClient);
  private readonly studentUrl = '/api/exam/flashcards';
  private readonly teacherUrl = '/api/exam/teacher/flashcards';

  private baseUrl(mode: FlashcardMode): string {
    return mode === 'teacher' ? this.teacherUrl : this.studentUrl;
  }

  listDecks(mode: FlashcardMode): Observable<FlashcardDeckSummary[]> {
    return this.http
      .get<FlashcardDeckSummary[]>(`${this.baseUrl(mode)}/decks`)
      .pipe(map((decks) => decks.map(decodeDeck)));
  }

  getDeck(mode: FlashcardMode, id: number): Observable<FlashcardDeckDetail> {
    return this.http.get<FlashcardDeckDetail>(`${this.baseUrl(mode)}/decks/${id}`).pipe(
      map((deck) => ({ ...decodeDeck(deck), cards: deck.cards.map(decodeCard) })),
    );
  }

  createDeck(mode: FlashcardMode, deck: SaveFlashcardDeck): Observable<FlashcardDeckSummary> {
    return this.http
      .post<FlashcardDeckSummary>(`${this.baseUrl(mode)}/decks`, encodeDeck(deck))
      .pipe(map(decodeDeck));
  }

  updateDeck(mode: FlashcardMode, id: number, deck: SaveFlashcardDeck): Observable<FlashcardDeckSummary> {
    return this.http
      .put<FlashcardDeckSummary>(`${this.baseUrl(mode)}/decks/${id}`, encodeDeck(deck))
      .pipe(map(decodeDeck));
  }

  deleteDeck(mode: FlashcardMode, id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(mode)}/decks/${id}`);
  }

  addCard(mode: FlashcardMode, deckId: number, card: SaveFlashcard): Observable<Flashcard> {
    return this.http
      .post<Flashcard>(`${this.baseUrl(mode)}/decks/${deckId}/cards`, encodeCard(card))
      .pipe(map(decodeCard));
  }

  updateCard(mode: FlashcardMode, cardId: number, card: SaveFlashcard): Observable<Flashcard> {
    return this.http
      .put<Flashcard>(`${this.baseUrl(mode)}/cards/${cardId}`, encodeCard(card))
      .pipe(map(decodeCard));
  }

  deleteCard(mode: FlashcardMode, cardId: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(mode)}/cards/${cardId}`);
  }

  getDue(deckId: number | null, take = 50): Observable<FlashcardDueCard[]> {
    const deck = deckId === null ? '' : `&deckId=${deckId}`;
    return this.http
      .get<FlashcardDueCard[]>(`${this.studentUrl}/due?take=${take}${deck}`)
      .pipe(map((cards) => cards.map((card) => ({ ...decodeCard(card), deckTitle: decodeFlashcardText(card.deckTitle) }))));
  }

  getDueCount(): Observable<number> {
    return this.getDueSummary().pipe(map((due) => due.count));
  }

  getDueSummary(): Observable<FlashcardDueSummary> {
    return this.http.get<FlashcardDueSummary>(`${this.studentUrl}/due-count`);
  }

  review(cardId: number, recall: FlashcardRecall): Observable<void> {
    return this.http.post<void>(`${this.studentUrl}/cards/${cardId}/review`, { recall });
  }

  listAssignments(deckId: number): Observable<FlashcardDeckAssignment[]> {
    return this.http.get<FlashcardDeckAssignment[]>(`${this.teacherUrl}/decks/${deckId}/assignments`);
  }

  assign(deckId: number, target: FlashcardAssignTarget): Observable<FlashcardDeckAssignment> {
    return this.http.post<FlashcardDeckAssignment>(`${this.teacherUrl}/decks/${deckId}/assignments`, target);
  }

  unassign(deckId: number, assignmentId: number): Observable<void> {
    return this.http.delete<void>(`${this.teacherUrl}/decks/${deckId}/assignments/${assignmentId}`);
  }
}
