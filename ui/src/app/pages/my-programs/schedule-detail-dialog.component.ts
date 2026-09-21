import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { UserProgramStudyPageSchedule } from '../../models/program.interfaces';
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
  constructor(
    public dialogRef: MatDialogRef<ScheduleDetailDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: ScheduleDetailDialogData
  ) {}

  close(): void {
    this.dialogRef.close();
  }
}
