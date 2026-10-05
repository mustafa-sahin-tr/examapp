import { Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { DIRECT_MESSAGES_SCOPE } from '../../../models/direct-message.model';

export interface DmBlockDialogData {
  /** true: engelle onayı; false: engeli kaldır onayı. */
  block: boolean;
  studentName: string;
}

/** Issue #106 — öğretmenin "Engelle / Engeli kaldır" onayı. İsteği çağıran atar; dialog yalnız `true` döner. */
@Component({
  selector: 'app-dm-block-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, TranslocoDirective],
  providers: [provideTranslocoScope(DIRECT_MESSAGES_SCOPE)],
  template: `
    <div class="md" *transloco="let t; prefix: 'direct-messages.blockDialog'">
      <header class="md__header">
        <mat-icon class="md__icon" aria-hidden="true">{{ data.block ? 'block' : 'lock_open' }}</mat-icon>
        <h2 mat-dialog-title class="md__title">{{ data.block ? t('blockTitle') : t('unblockTitle') }}</h2>
      </header>
      <mat-dialog-content class="md__content">
        <p class="md__intro" data-testid="dm-block-body">
          {{ data.block ? t('blockBody', { name: data.studentName }) : t('unblockBody', { name: data.studentName }) }}
        </p>
      </mat-dialog-content>
      <mat-dialog-actions class="md__actions">
        <button mat-stroked-button type="button" data-testid="dm-block-cancel" [mat-dialog-close]="false">
          {{ t('cancel') }}
        </button>
        <button
          mat-flat-button
          type="button"
          data-testid="dm-block-confirm"
          [class.md__danger]="data.block"
          [mat-dialog-close]="true"
        >
          {{ data.block ? t('confirmBlock') : t('confirmUnblock') }}
        </button>
      </mat-dialog-actions>
    </div>
  `,
  styleUrls: ['./dm-block-dialog.component.scss'],
})
export class DmBlockDialogComponent {
  protected readonly data = inject<DmBlockDialogData>(MAT_DIALOG_DATA);
}

export function openDmBlockDialog(dialog: MatDialog, data: DmBlockDialogData): MatDialogRef<DmBlockDialogComponent, boolean> {
  return dialog.open<DmBlockDialogComponent, DmBlockDialogData, boolean>(DmBlockDialogComponent, {
    data,
    autoFocus: 'first-tabbable',
    restoreFocus: true,
    width: '440px',
    maxWidth: '94vw',
  });
}
