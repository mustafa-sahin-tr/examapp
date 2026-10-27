import { LabelingBookOffsetRange, LabelingBookPageRange, MAX_BOOK_FILE_PAGE } from '../../models/labeling-book';

/** Formdaki ham satırlar: boş giriş `null`. */
export interface RangeDraft {
  fileFrom: number | null;
  fileTo: number | null;
}

export interface OffsetDraft extends RangeDraft {
  offset: number | null;
}

export interface TocDraft extends RangeDraft {
  gradeId: number | null;
  subjectId: number | null;
  topicId: number | null;
  subTopicId: number | null;
}

export interface BookDraft {
  name: string;
  offsets: OffsetDraft[];
  answerKeyRanges: RangeDraft[];
  toc: TocDraft[];
}

export type BookSection = 'offsets' | 'answerKeyRanges' | 'toc';

/** `key`: `books.validation.<key>` çeviri anahtarı; `row`: 1-tabanlı satır, `other`: çakışan satır. */
export interface BookValidationError {
  section: BookSection | 'name';
  index: number;
  key: 'nameRequired' | 'fromRequired' | 'toRequired' | 'offsetRequired' | 'fromMin' | 'fromAfterTo' | 'overlap'
    | 'printedMin' | 'toMax' | 'printedMax'
    | 'gradeRequired' | 'subjectRequired' | 'subTopicNeedsTopic';
  params: { row: number; other?: number; max?: number };
}

export const MAX_BOOK_NAME = 200;

function isInt(value: number | null | undefined): value is number {
  return typeof value === 'number' && Number.isInteger(value);
}

/** Aralık alanlarını ve bölüm içi çakışmaları denetler (iki uç dahil: 10-20 ile 20-30 çakışır). */
function validateRanges(section: BookSection, rows: RangeDraft[], errors: BookValidationError[]): void {
  const complete: Array<{ index: number; from: number; to: number }> = [];
  rows.forEach((row, index) => {
    const params = { row: index + 1 };
    if (!isInt(row.fileFrom)) {
      errors.push({ section, index, key: 'fromRequired', params });
    }
    if (!isInt(row.fileTo)) {
      errors.push({ section, index, key: 'toRequired', params });
    }
    if (!isInt(row.fileFrom) || !isInt(row.fileTo)) {
      return;
    }
    if (row.fileFrom < 1) {
      errors.push({ section, index, key: 'fromMin', params });
      return;
    }
    if (row.fileFrom > row.fileTo) {
      errors.push({ section, index, key: 'fromAfterTo', params });
      return;
    }
    if (row.fileTo > MAX_BOOK_FILE_PAGE) {
      errors.push({ section, index, key: 'toMax', params: { ...params, max: MAX_BOOK_FILE_PAGE } });
      return;
    }
    complete.push({ index, from: row.fileFrom, to: row.fileTo });
  });
  for (let i = 0; i < complete.length; i++) {
    for (let j = i + 1; j < complete.length; j++) {
      const a = complete[i];
      const b = complete[j];
      if (a.from <= b.to && b.from <= a.to) {
        errors.push({ section, index: b.index, key: 'overlap', params: { row: b.index + 1, other: a.index + 1 } });
      }
    }
  }
}

/**
 * İstemci tarafı doğrulama (sunucu da denetler): ad zorunlu, her aralıkta 1 ≤ başlangıç ≤ bitiş, aynı bölümde aralıklar
 * çakışmaz (bir dosya sayfasının tek ofseti / tek içindekiler kaydı olur), ofset tam sayı (negatif olabilir, ama ilk
 * basılı sayfa ≥ 1),
 * içindekilerde sınıf + ders zorunlu, alt konu konusuz seçilemez. Bölümler arası çakışma serbest.
 */
export function validateBookDraft(draft: BookDraft): BookValidationError[] {
  const errors: BookValidationError[] = [];
  if (!draft.name.trim()) {
    errors.push({ section: 'name', index: 0, key: 'nameRequired', params: { row: 0 } });
  }
  validateRanges('offsets', draft.offsets, errors);
  draft.offsets.forEach((row, index) => {
    if (!isInt(row.offset)) {
      errors.push({ section: 'offsets', index, key: 'offsetRequired', params: { row: index + 1 } });
    } else if (isInt(row.fileFrom) && row.fileFrom >= 1 && row.fileFrom + row.offset < 1) {
      // Sunucu kuralı: aralığın ilk basılı sayfası 1'den küçük olamaz.
      errors.push({ section: 'offsets', index, key: 'printedMin', params: { row: index + 1 } });
    } else if (isInt(row.fileTo) && row.fileTo + row.offset > MAX_BOOK_FILE_PAGE) {
      // Sunucu kuralı: son basılı sayfa üst sınırı aşamaz.
      errors.push({ section: 'offsets', index, key: 'printedMax', params: { row: index + 1, max: MAX_BOOK_FILE_PAGE } });
    }
  });
  validateRanges('answerKeyRanges', draft.answerKeyRanges, errors);
  validateRanges('toc', draft.toc, errors);
  draft.toc.forEach((row, index) => {
    const params = { row: index + 1 };
    if (row.gradeId == null) {
      errors.push({ section: 'toc', index, key: 'gradeRequired', params });
    }
    if (row.subjectId == null) {
      errors.push({ section: 'toc', index, key: 'subjectRequired', params });
    }
    if (row.subTopicId != null && row.topicId == null) {
      errors.push({ section: 'toc', index, key: 'subTopicNeedsTopic', params });
    }
  });
  return errors;
}

/** Ofset satırının önizlemesi: aralığın iki ucunun basılı karşılığı (eksik alan varsa null). */
export function offsetPreview(row: OffsetDraft): { fileFrom: number; printedFrom: number; fileTo: number; printedTo: number } | null {
  if (!isInt(row.fileFrom) || !isInt(row.fileTo) || !isInt(row.offset)) {
    return null;
  }
  return { fileFrom: row.fileFrom, printedFrom: row.fileFrom + row.offset, fileTo: row.fileTo, printedTo: row.fileTo + row.offset };
}

/** Dosya sayfasının basılı numarası (sunucudaki kuralın aynısı: ilk eşleşen aralık); aralık yoksa null. */
export function printedPageOf(offsets: LabelingBookOffsetRange[], filePage: number): number | null {
  const range = offsets.find((r) => r.fileFrom <= filePage && filePage <= r.fileTo);
  return range ? filePage + range.offset : null;
}

/** Aralıkları kısa metne çevirir: "1–4, 120–130". */
export function formatRanges(ranges: LabelingBookPageRange[]): string {
  return ranges.map((r) => (r.fileFrom === r.fileTo ? `${r.fileFrom}` : `${r.fileFrom}–${r.fileTo}`)).join(', ');
}
