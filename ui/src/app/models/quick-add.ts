import { BookImportJob } from './book-import';

/** Issue #1053 (epic #1048): hızlı soru ekleme. İş ve fotoğraf satırları kitap aktarımı tipleriyle gelir (`BookImportJob*`). */

/** Sunucudaki sınırlar (`QuickAddService.MaxPhotos`, `MaxPhotoBytes`). */
export const QUICK_ADD_MAX_PHOTOS = 10;
export const QUICK_ADD_MAX_PHOTO_BYTES = 15 * 1024 * 1024;
/** Sunucunun çözebildiği türler; iOS kamerası `image/*` ile JPEG verir. */
export const QUICK_ADD_IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'];

export interface QuickAddPhotoInput {
  file: File;
  /** İsteğe bağlı cevap anahtarı ("A C B D"); boş → anahtar yok. */
  answers: string;
}

export interface StartQuickAddRequest {
  gradeId: number;
  subjectId: number | null;
  photos: QuickAddPhotoInput[];
}

/** Kartın durum adı: kitap aktarımı durumları dört öğretmen durumuna indirgenir (Sırada / İşleniyor / İncelemeye hazır / Başarısız). */
export type QuickAddPhase = 'queued' | 'processing' | 'ready' | 'completed' | 'failed' | 'cancelled';

export function quickAddPhase(job: Pick<BookImportJob, 'status'>): QuickAddPhase {
  switch (job.status) {
    case 'Queued':
    case 'AwaitingBudget':
    case 'AwaitingCostApproval':
      return 'queued';
    case 'Running':
    case 'AwaitingConfirmation':
      return 'processing';
    case 'ReadyForReview':
      return 'ready';
    case 'Completed':
      return 'completed';
    case 'Cancelled':
      return 'cancelled';
    default:
      return 'failed';
  }
}

/** Durum yoklaması sürer mi (iş bitmedi). */
export function isQuickAddPending(job: Pick<BookImportJob, 'status'>): boolean {
  const phase = quickAddPhase(job);
  return phase === 'queued' || phase === 'processing';
}

/** "Yeniden dene": hatalı ya da takılmış iş (sunucu `retry` aynı koşulda kabul eder). */
export function canRetryQuickAdd(job: Pick<BookImportJob, 'status' | 'stalled'>): boolean {
  return job.status === 'Failed' || job.stalled;
}

/** Fotoğraf bir görsel türü mü (tür boşsa uzantıya bakılır; bazı Android galerileri türü boş verir). */
export function isQuickAddImage(file: File): boolean {
  if (file.type) return QUICK_ADD_IMAGE_TYPES.includes(file.type.toLowerCase());
  return /\.(jpe?g|png|webp)$/i.test(file.name);
}
