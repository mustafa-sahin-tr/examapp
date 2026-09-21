import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { UserProgramStudyPageSchedule } from '../../models/program.interfaces';
import { StudyItemDisplay, describeStudyItem } from '../../shared/utils/study-item-display.util';
import { MY_PROGRAMS_SCOPE } from './my-programs-scope';

export interface ScheduleDetailDialogData {
  schedule: UserProgramStudyPageSchedule;
  color: string;
}

@Component({
  selector: 'app-schedule-detail-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatIconModule, TranslocoDirective],
  // Dialog `MatDialog` ile açıldığı için sayfanın scope'unu devralmaz; kendi provider'ını verir.
  providers: [provideTranslocoScope(MY_PROGRAMS_SCOPE)],
  templateUrl: './schedule-detail-dialog.component.html',
  styleUrls: ['./schedule-detail-dialog.component.scss'],
})
export class ScheduleDetailDialogComponent {
  private readonly dialogRef = inject<MatDialogRef<ScheduleDetailDialogComponent>>(MatDialogRef);
  private readonly transloco = inject(TranslocoService);
  readonly data = inject<ScheduleDetailDialogData>(MAT_DIALOG_DATA);

  /** Tip bazlı gövde (görsel / link / kitap) için ortak görüntü modeli. */
  readonly display: StudyItemDisplay = describeStudyItem(
    this.data.schedule,
    (key, params) => this.transloco.translate<string>(key, params) ?? '',
  );

  close(): void {
    this.dialogRef.close();
  }
}
