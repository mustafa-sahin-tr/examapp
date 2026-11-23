/** Issue #1310: öğrencinin bir sorudaki çizimleri (soru görseli üstü + karalama alanı). */
export interface SessionDrawingItem {
  /** `TestInstanceQuestion.id`. */
  instanceQuestionId: number;
  ink: string[];
  strokes: string[];
  text: string | null;
}

/** Öğretmen / velinin gördüğü soru satırı (salt okunur). */
export interface SessionReviewQuestion extends SessionDrawingItem {
  /** Testteki sıra (1'den). */
  number: number;
  imageUrl: string | null;
  /** Görselsiz soruda soru metni. */
  questionText?: string | null;
  interactionType: string | null;
  selectedLetter: string | null;
  correctLetter: string | null;
  outcome: 'correct' | 'wrong' | 'blank';
  timeTakenSeconds: number;
}

export interface SessionReview {
  testInstanceId: number;
  title: string;
  startedAt: string;
  finishedAt: string | null;
  questions: SessionReviewQuestion[];
}

export function hasDrawing(item: Pick<SessionDrawingItem, 'ink' | 'strokes' | 'text'>): boolean {
  return item.ink.length > 0 || item.strokes.length > 0 || !!item.text?.trim();
}
