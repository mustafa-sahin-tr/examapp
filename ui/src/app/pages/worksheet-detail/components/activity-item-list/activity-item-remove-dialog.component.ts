import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

export interface ActivityItemRemoveDialogData {
  number: number;
  typeName: string;
}

/** Issue #684: madde kaldırma onayı — başlamış öğrencilerin etkilenmediğini söyler. */
@Component({
  selector: 'app-activity-item-remove-dialog',
  standalone: true,
  imports: [MatButtonModule, MatDialogModule, MatIconModule, TranslocoDirective],
  template: `
    <ng-container *transloco="let t; prefix: 'worksheet-detail.activity.items'">
      <h2 mat-dialog-title>{{ t('removeTitle', { number: data.number }) }}</h2>
      <mat-dialog-content>
        <p>{{ t('removeMessage', { type: data.typeName }) }}</p>
        <p class="note" data-testid="remove-note">
          <mat-icon aria-hidden="true">info</mat-icon><span>{{ t('removeNote') }}</span>
        </p>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-stroked-button type="button" [mat-dialog-close]="false">{{ t('cancel') }}</button>
        <button mat-flat-button type="button" class="danger" [mat-dialog-close]="true" data-testid="remove-confirm">
          {{ t('remove') }}
        </button>
      </mat-dialog-actions>
    </ng-container>
  `,
  styles: `
    .note {
      display: flex;
      gap: 8px;
      align-items: flex-start;
      color: var(--ms-text-weak);
    }
    .note mat-icon {
      flex: none;
      font-size: 20px;
      width: 20px;
      height: 20px;
    }
    .danger {
      background: var(--ms-danger-background);
      color: var(--ms-danger-text-inverse);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityItemRemoveDialogComponent {
  readonly data = inject<ActivityItemRemoveDialogData>(MAT_DIALOG_DATA);
}
