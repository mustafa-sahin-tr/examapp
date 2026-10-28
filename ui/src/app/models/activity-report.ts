/**
 * Issue #584: öğretmen etkinlik analizi. Backend: `GET /api/exam/worksheet/{id}/activity-report` (yalnız onaylı öğretmen, kendi
 * atamaları). Analize tamamlanmış ve yeniden çözülmeyen oturumlar girer; cevaplar oturumun son hâlidir.
 */
export interface ActivityReport {
  worksheetId: number;
  worksheetName: string;
  retrievedAt: string;
  /** Atamaların hedeflediği öğrenci (tekil). */
  studentCount: number;
  /** Analize giren öğrenci. */
  analyzedCount: number;
  /** Tamamlamış ama şu an yeniden çözdüğü için analiz dışı. */
  retakingCount: number;
  questions: ActivityReportQuestion[];
  students: ActivityReportStudent[];
}

export interface ActivityReportQuestion {
  worksheetQuestionId: number;
  /** 1'den başlayan madde numarası. */
  number: number;
  interactionType: string | null;
  /** Otomatik kontrol edilebilir mi (açık uçlu / çözümsüz: false). */
  gradable: boolean;
  fullyCorrectCount: number;
  answeredCount: number;
  items: ActivityReportItem[];
}

export interface ActivityReportItem {
  itemId: string;
  label: string;
  /** Çeldirici (çözümde yeri olmayan kart / hedef dışı token): doğru = kullanılmamış. */
  distractor: boolean;
  correctCount: number;
  emptyCount: number;
  studentCount: number;
  /** En sık yanlış cevaplar (çoktan aza). Boş etiket = yalnız "işaretlendi". */
  wrongAnswers: { label: string; count: number }[];
}

export interface ActivityReportStudent {
  studentId: number;
  userId: number;
  studentNumber: string;
  gradeName: string | null;
  /** `questions` sırasıyla. */
  cells: ActivityReportCell[];
}

export interface ActivityReportCell {
  answered: boolean;
  correctCount: number;
  totalCount: number;
}
