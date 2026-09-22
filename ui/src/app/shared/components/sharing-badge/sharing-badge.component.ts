import { Component, Input, computed, inject, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoService } from '@jsverse/transloco';

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
  private readonly transloco = inject(TranslocoService);
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
    return (
      value === WorksheetTeacherSharing.PublicView ||
      value === WorksheetTeacherSharing.PublicAssignable ||
      value === WorksheetTeacherSharing.SchoolOnly
    );
  });

  readonly icon = computed(() => {
    switch (this.sharingSignal()) {
      case WorksheetTeacherSharing.PublicAssignable:
        return 'assignment_turned_in';
      // issue #191: okul içi paylaşım
      case WorksheetTeacherSharing.SchoolOnly:
        return 'school';
      case WorksheetTeacherSharing.PublicView:
        return 'visibility';
      default:
        return 'visibility';
    }
  });

  readonly label = computed(() => {
    switch (this.sharingSignal()) {
      case WorksheetTeacherSharing.PublicAssignable:
        return this.translate('shared.sharingBadge.publicAssignable');
      case WorksheetTeacherSharing.SchoolOnly:
        return this.translate('shared.sharingBadge.schoolOnly');
      case WorksheetTeacherSharing.PublicView:
        return this.translate('shared.sharingBadge.publicView');
      case WorksheetTeacherSharing.Private:
      default:
        return '';
    }
  });

  /** Sahip adı verilmişse tooltip "<ad> tarafından paylaşıldı" olur. */
  readonly tooltip = computed(() => {
    const owner = this.ownerNameSignal();
    return owner ? this.translate('shared.sharingBadge.sharedBy', { owner }) : this.label();
  });

  private translate(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(key, params) ?? '';
  }
}
