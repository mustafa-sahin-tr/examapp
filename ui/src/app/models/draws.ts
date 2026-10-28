export interface AnswerChoice {
  label: string; // "A", "B", "C", "D"
  tag?: string; // Backend tag (A,B,C,D,E)
  order?: number; // Backend order (0,1,2,...)
  x: number;
  y: number;
  width: number;
  height: number;
  isCorrect?: boolean;
  id: number;
  scale?: number;
  imageUrl: string;
}

export interface PassageRegion {
  id: number;
  title: string;
  x: number;
  y: number;
  width: number;
  height: number;
  imageUrl: string;
  imageId: string;
}

export enum ClassificationSource {
  Human = 0,
  AI = 1,
  /** Issue #577: kitabın içindekilerinden (işaretleme ekranı, kaynak kitap seçiliyken havuza kayıt). */
  Book = 2,
}

/**
 * Issue #454: `POST /api/exam/questions/save` içindeki her `questions[]` öğesinin opsiyonel, soru bazında
 * sınıflandırması (backend `BulkQuestionClassificationDto`). Yalnız koordinat JSON modunda kurulan
 * bölgeler taşır; null/0/boş alanlar header'daki test sınıflandırmasına düşer.
 */
export interface QuestionClassificationPayload {
  subjectId: number | null;
  topicId: number | null;
  subTopicIds: number[];
  source: 'AI' | 'Human';
}

export interface QuestionRegion {
  id: number;
  name: string;
  x: number;
  y: number;
  width: number;
  height: number;
  sanitizedHeight?: number;
  passageId: string;
  // Optional classification metadata (used to sync preview navigation with UI forms)
  subjectId?: number;
  topicId?: number;
  difficultyLevel?: number;
  subtopicId?: number;
  // Optional multi-subtopic ids (used for preview classification selector)
  subtopicIds?: number[];
  answers: AnswerChoice[];
  passage?: PassageRegion;
  // Optional authoring/solve hint: show passage first, then reveal question.
  showPassageFirst?: boolean;
  imageId: string;
  imageUrl: string;
  /**
   * issue #365 (S2): canvas görünümünün (v5) gösterdiği, cevap bloğu kırpılmış `question-v2` görselinin
   * sunucuda türetilip imzalanmış adresi. Yoksa (null/undefined) `imageUrl` kullanılır.
   */
  imageUrlV2?: string | null;
  exampleAnswer: string | null | undefined;
  isExample: boolean;
  order?: number;
  classificationSource?: ClassificationSource;
  /** Issue #454: soru bazında kaydedilecek sınıflandırma (yalnız koordinat JSON modu). */
  classification?: QuestionClassificationPayload;
}

export type RegionOrAnswerHit =
  | { type: 'answer'; value: { questionIndex: number; answerIndex: number } }
  | { type: 'question'; value: { questionIndex: number; answerIndex: null } }
  | { type: 'passage'; value: { passageId: string } }
  | { type: 'dropzone'; value: { questionIndex: number; dropZoneId: string } }
  // Issue #574: matris seçim satır kutusu ve hücre kutusu.
  | { type: 'matrixRow'; value: { questionIndex: number; rowId: string } }
  | { type: 'matrixCell'; value: { questionIndex: number; rowId: string; columnId: string } }
  // Issue #575: cevap kutusu.
  | { type: 'slot'; value: { questionIndex: number; slotId: string } }
  // Issue #576: sürükle-yerleştir hedefi / kartı ve eşleştirme öğesi.
  | { type: 'placeTarget'; value: { questionIndex: number; targetId: string } }
  | { type: 'placeCard'; value: { questionIndex: number; cardId: string } }
  | { type: 'matchItem'; value: { questionIndex: number; side: 'left' | 'right'; itemId: string } }
  // Issue #582: metin token'ı (kelime ya da boşluk).
  | { type: 'textToken'; value: { questionIndex: number; tokenId: string } }
  // Issue #587: boyama bölgesi (çokgen).
  | { type: 'paintRegion'; value: { questionIndex: number; regionId: string } }
  | null;
