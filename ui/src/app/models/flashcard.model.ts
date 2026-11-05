/** Issue #699: bilgi kartları. Metin alanları ekranda düz metindir; HTML kodlaması `FlashcardService`'te kalır. */
export type FlashcardMode = 'student' | 'teacher';

export type FlashcardRecall = 'Forgot' | 'Hard' | 'Good';

export const FLASHCARD_MAX_TEXT_LENGTH = 2000;
export const FLASHCARD_MAX_IMAGE_BYTES = 2 * 1024 * 1024;

export interface SaveFlashcardDeck {
  title: string;
  gradeId: number | null;
  subjectId: number | null;
  topicId: number | null;
}

export interface SaveFlashcard {
  front: string;
  back: string;
  /** Yeni ön yüz görseli, `data:image/...;base64,` biçiminde. Boşsa mevcut görsel kalır. */
  image: string | null;
  removeImage: boolean;
}

export interface FlashcardDeckSummary {
  id: number;
  title: string;
  gradeId: number | null;
  gradeName: string | null;
  subjectId: number | null;
  subjectName: string | null;
  topicId: number | null;
  topicName: string | null;
  cardCount: number;
  /** Öğrenci listesinde bugün gelen kart sayısı; sahip görünümünde boş. */
  dueCount: number | null;
  isOwner: boolean;
}

export interface Flashcard {
  id: number;
  front: string;
  back: string;
  imageUrl: string | null;
  order: number;
}

export interface FlashcardDeckDetail extends FlashcardDeckSummary {
  cards: Flashcard[];
}

export interface FlashcardDueCard extends Flashcard {
  deckId: number;
  deckTitle: string;
  /** Boş: kart yeni, daha önce tekrar edilmedi. */
  box: number | null;
}

export interface FlashcardDeckAssignment {
  id: number;
  studentId: number | null;
  studentName: string | null;
  gradeId: number | null;
  gradeName: string | null;
}

export type FlashcardAssignTarget = { studentId: number; gradeId?: never } | { gradeId: number; studentId?: never };
