import { Component, Inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { FormsModule } from '@angular/forms';
import { StudyPage } from '../../models/study-page';

export interface AddStudyPagesDialogData {
  availableStudyPages: Array<StudyPage & { selected?: boolean; startDate?: Date | null; endDate?: Date | null }>;
  selectedDate?: Date | null;
}

export interface AddStudyPagesDialogResult {
  selectedPages: Array<StudyPage & { selected: boolean; startDate: Date; endDate: Date }>;
}

@Component({
  selector: 'app-add-study-pages-dialog',
  standalone: true,
  imports: [
    CommonModule,
    MatDialogModule,
    MatCardModule,
    MatCheckboxModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatDatepickerModule,
    MatNativeDateModule,
    FormsModule,
  ],
  templateUrl: './add-study-pages-dialog.component.html',
  styleUrls: ['./add-study-pages-dialog.component.scss'],
})
export class AddStudyPagesDialogComponent implements OnInit {
  studyPages: Array<StudyPage & { selected?: boolean; startDate?: Date | null; endDate?: Date | null }> = [];
  selectedDate: Date | null = null;

  constructor(
    public dialogRef: MatDialogRef<AddStudyPagesDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: AddStudyPagesDialogData
  ) {}

  ngOnInit(): void {
    this.studyPages = this.data.availableStudyPages;
    this.selectedDate = this.data.selectedDate || null;
  }

  togglePageSelection(page: StudyPage & { selected?: boolean; startDate?: Date | null; endDate?: Date | null }): void {
    page.selected = !page.selected;
    this.fillDatesIfNeeded(page);
  }

  onCheckboxChange(page: StudyPage & { selected?: boolean; startDate?: Date | null; endDate?: Date | null }): void {
    this.fillDatesIfNeeded(page);
  }

  private fillDatesIfNeeded(
    page: StudyPage & { selected?: boolean; startDate?: Date | null; endDate?: Date | null }
  ): void {
    // Auto-fill dates if a date was selected and page is being selected
    if (page.selected && this.selectedDate && !page.startDate && !page.endDate) {
      page.startDate = new Date(this.selectedDate);
      page.endDate = new Date(this.selectedDate);
    }
  }

  trackByPageId(index: number, page: StudyPage): number {
    return page.id;
  }

  onCancel(): void {
    this.dialogRef.close();
  }

  onSave(): void {
    const selected = this.studyPages.filter((p) => p.selected && p.startDate && p.endDate);
    if (selected.length === 0) {
      return;
    }
    this.dialogRef.close({ selectedPages: selected });
  }

  get hasValidSelections(): boolean {
    return this.studyPages.some((p) => p.selected && p.startDate && p.endDate && p.startDate <= p.endDate);
  }
}
