/**
 * Issue #577: işaretleme ekranının kitap kaydı (`/api/exam/labeling/books`, Admin + QuestionTrainer).
 * Basılı sayfa = dosya sayfası + ofset; ofset negatif olabilir, fasiküllü kitapta birden çok aralık olur ve basılı
 * numaralar tekrar edebilir. Aralıklar dosya sayfası cinsinden, iki ucu dahil.
 */
/** Sunucudaki `BookPageMap.MaxFilePage`: dosya ve basılı sayfa üst sınırı (daha büyük sayı sayfa numarası sayılmaz). */
export const MAX_BOOK_FILE_PAGE = 100_000;

export interface LabelingBookOffsetRange {
  fileFrom: number;
  fileTo: number;
  offset: number;
}

export interface LabelingBookPageRange {
  fileFrom: number;
  fileTo: number;
}

/** İçindekiler kaydı (istek): sınıf + ders zorunlu, konu / alt konu isteğe bağlı. */
export interface LabelingBookTocInput {
  fileFrom: number;
  fileTo: number;
  gradeId: number;
  subjectId: number;
  topicId?: number | null;
  subTopicId?: number | null;
}

/** İçindekiler kaydı (yanıt, `LabelingBookTocDto`): taksonomi adları ve aralığın basılı karşılığı (ofset yoksa null). */
export interface LabelingBookTocEntry extends LabelingBookTocInput {
  printedFrom: number | null;
  printedTo: number | null;
  gradeName: string | null;
  subjectName: string | null;
  topicName: string | null;
  subTopicName: string | null;
}

export interface LabelingBook {
  id: number;
  name: string;
  /** Aynı içeriğin asıl baskısı (ör. kurumsal baskı → ana kitap); yoksa null. */
  canonicalBookId: number | null;
  canonicalBookName: string | null;
  notes: string | null;
  offsets: LabelingBookOffsetRange[];
  answerKeyRanges: LabelingBookPageRange[];
  toc: LabelingBookTocEntry[];
}

/** `POST` / `PUT /{id}`: aralık kümeleri tümüyle değiştirilir. */
export interface LabelingBookSaveRequest {
  name: string;
  canonicalBookId: number | null;
  notes: string | null;
  offsets: LabelingBookOffsetRange[];
  answerKeyRanges: LabelingBookPageRange[];
  toc: LabelingBookTocInput[];
}

/** `GET /{id}/pages/{filePage}`: dosya sayfasının basılı numarası, cevap anahtarı mı, içindekiler eşlemesi. */
export interface LabelingBookPageInfo {
  filePage: number;
  /** Ofset aralığı yoksa null. */
  printedPage?: number | null;
  isAnswerKey: boolean;
  toc?: LabelingBookTocEntry | null;
  /** Sayfaya düşen içindekiler satırı artık geçersiz (konu / ders silinmiş): `toc` null döner. */
  tocStale?: boolean;
}

/** `GET /{id}/printed/{printedPage}?near=`: basılı numaraya karşılık gelen dosya sayfaları (yakına göre sıralı). */
export interface LabelingBookPrintedLookup {
  printedPage: number;
  filePages: number[];
}

/** Bank kaydında 409 `LabelingCropAlreadyInBank`: aynı kırpma (SHA-256) bankada var. */
export interface LabelingCropDuplicate {
  /** İstekteki `questions` sırası (hangi soru tekrar ediyor). */
  questionIndex: number;
  /** Başka bir kullanıcının kaydı: ayrıntı (soru id'si, kitap, sayfa) gönderilmez. */
  otherOwner?: boolean;
  questionId?: number;
  bookName?: string;
  filePage?: number;
  printedPage?: number | null;
  activityLabel?: string | null;
  itemLabel?: string | null;
}

export interface LabelingCropConflict {
  message?: string;
  errorCode: 'LabelingCropAlreadyInBank';
  existing: LabelingCropDuplicate[];
}
