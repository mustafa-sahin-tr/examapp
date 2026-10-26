import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { TranslocoDirective } from '@jsverse/transloco';

import { AssignmentStudentStatus, TeacherAssignmentStudentSummary } from '../../../../models/assignment';

/** Tablo satırı — sıralama/biçim bileşen içinde; not, puan, sıralama yok. */
export interface ActivityTrackingRow {
  key: number;
  studentNumber: string;
  gradeName: string;
  status: AssignmentStudentStatus;
  firstCheck: { correct: number; wrong: number } | null;
}

const STATUS_ICON: Record<AssignmentStudentStatus, string> = {
  Completed: 'check_circle',
  InProgress: 'timelapse',
  NotStarted: 'radio_button_unchecked',
  Scheduled: 'schedule',
  Expired: 'event_busy',
};

/**
 * Issue #573: öğretmenin atanmış etkinlik sayfası için "Çalışma takibi" tablosu — Öğrenci | Durum (ikon + metin) |
 * Etkin süre (#572 gelene dek "—") | İlk kontrol ("7 doğru · 3 yanlış" ya da "—"). Not/puan/sıralama yok. Birden çok
 * atamada aynı öğrenci tek satırdır (en ileri durum). Scope sözlüğü üst sayfa (`worksheet-detail`) tarafından sağlanır.
 */
@Component({
  selector: 'app-activity-tracking-table',
  standalone: true,
  imports: [MatIconModule, MatTableModule, TranslocoDirective],
  templateUrl: './activity-tracking-table.component.html',
  styleUrl: './activity-tracking-table.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityTrackingTableComponent {
  readonly students = input.required<TeacherAssignmentStudentSummary[]>();
  /** Öğrencinin sınıf adı (DTO'da yoksa üst sayfa sınıf listesinden çözer). */
  readonly gradeLabel = input<(student: TeacherAssignmentStudentSummary) => string>((s) => s.gradeName ?? '');

  readonly columns = ['student', 'status', 'activeTime', 'firstCheck'];

  readonly rows = computed<ActivityTrackingRow[]>(() => {
    const rank: Record<AssignmentStudentStatus, number> = {
      Completed: 4,
      InProgress: 3,
      Expired: 2,
      NotStarted: 1,
      Scheduled: 0,
    };
    const byStudent = new Map<number, TeacherAssignmentStudentSummary>();
    for (const student of this.students()) {
      const existing = byStudent.get(student.studentId);
      if (!existing || rank[student.status] > rank[existing.status]) {
        byStudent.set(student.studentId, student);
      }
    }
    const label = this.gradeLabel();
    return Array.from(byStudent.values()).map((s) => ({
      key: s.studentId,
      studentNumber: s.studentNumber,
      gradeName: label(s),
      status: s.status,
      firstCheck:
        s.firstCheckTotalCount != null && s.firstCheckCorrectCount != null
          ? {
              correct: s.firstCheckCorrectCount,
              wrong: Math.max(0, s.firstCheckTotalCount - s.firstCheckCorrectCount),
            }
          : null,
    }));
  });

  iconFor(status: AssignmentStudentStatus): string {
    return STATUS_ICON[status] ?? STATUS_ICON.NotStarted;
  }

  statusKey(status: AssignmentStudentStatus): string {
    switch (status) {
      case 'Completed':
        return 'completed';
      case 'InProgress':
        return 'inProgress';
      case 'Scheduled':
        return 'scheduled';
      case 'Expired':
        return 'expired';
      default:
        return 'notStarted';
    }
  }
}
