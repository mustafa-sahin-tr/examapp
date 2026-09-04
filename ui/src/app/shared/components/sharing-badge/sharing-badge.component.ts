import { Component, Input, computed, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';

import { WorksheetTeacherSharing } from '../../../models/test-instance';

/**
 * Küçük chip-style rozet: sahibi olunmayan (paylaşılan) worksheet satırlarında
 * `teacherSharing` seviyesini ve isteğe bağlı sahip adını gösterir.
 * Bkz. issue #11 — public sınavların diğer öğretmenlere salt-görüntüleme listesi.
 */
@Component({
  selector: 'app-sharing-badge',
  standalone: true,
  imports: [MatIconModule, MatTooltipModule],
  templateUrl: './sharing-badge.component.html',
  styleUrl: './sharing-badge.component.scss',
})
export class SharingBadgeComponent {
  private readonly sharingSignal = signal<WorksheetTeacherSharing | null>(null);
  private readonly ownerNameSignal = signal<string | null | undefined>(null);

  @Input({ required: true })
  set teacherSharing(value: WorksheetTeacherSharing) {
    this.sharingSignal.set(value);
  }
  get teacherSharing(): WorksheetTeacherSharing | null {
    return this.sharingSignal();
  }

  @Input()
  set ownerName(value: string | null | undefined) {
    this.ownerNameSignal.set(value);
  }
  get ownerName(): string | null | undefined {
    return this.ownerNameSignal();
  }

  /** `Private` (veya henüz set edilmemiş) durumda rozet gösterilmez — bu bileşen yalnız paylaşılan satırlar için anlamlıdır. */
  readonly visible = computed(() => {
    const value = this.sharingSignal();
    return value === WorksheetTeacherSharing.PublicView || value === WorksheetTeacherSharing.PublicAssignable;
  });

  readonly icon = computed(() => {
    switch (this.sharingSignal()) {
      case WorksheetTeacherSharing.PublicAssignable:
        return 'assignment_turned_in';
      case WorksheetTeacherSharing.PublicView:
        return 'visibility';
      default:
        return 'visibility';
    }
  });

  readonly label = computed(() => {
    switch (this.sharingSignal()) {
      case WorksheetTeacherSharing.PublicAssignable:
        return 'Herkese Açık · Atanabilir';
      case WorksheetTeacherSharing.PublicView:
        return 'Herkese Açık · Görüntüleme';
      case WorksheetTeacherSharing.Private:
      default:
        return '';
    }
  });
}
