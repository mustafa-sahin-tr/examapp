import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { LabelingCropDuplicate } from '../../../models/labeling-book';

export interface LabelingCropDuplicateDialogData {
  existing: LabelingCropDuplicate[];
}

/**
 * Issue #577: aynı kırpma (SHA-256) bankada var — mevcut kayıtlar listelenir (soru yeni sekmede açılır). Kapanış sonucu
 * `true`: "Yine de kaydet" (force), aksi hâlde vazgeçildi.
 */
@Component({
  selector: 'app-labeling-crop-duplicate-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, RouterLink, TranslocoDirective],
  providers: [provideTranslocoScope('labeling')],
  templateUrl: './labeling-crop-duplicate-dialog.component.html',
  styleUrls: ['./labeling-crop-duplicate-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class LabelingCropDuplicateDialogComponent {
  readonly data = inject<LabelingCropDuplicateDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<LabelingCropDuplicateDialogComponent, boolean>);

  cancel(): void {
    this.dialogRef.close(false);
  }

  force(): void {
    this.dialogRef.close(true);
  }
}
