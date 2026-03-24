import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Router, RouterModule } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { Subject } from '../../models/subject';
import { Paged } from '../../models/test-instance';
import { StudyPage } from '../../models/study-page';
import { SubjectService } from '../../services/subject.service';
import { StudyPageService } from '../../services/study-page.service';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';

@Component({
  selector: 'app-study-pages',
  standalone: true,
  templateUrl: './study-pages.component.html',
  styleUrls: ['./study-pages.component.scss'],
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    RouterModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatInputModule,
    MatFormFieldModule,
    MatMenuModule,
    MatSelectModule,
    MatDialogModule,
    MatSnackBarModule,
    SectionHeaderComponent,
    PaginationComponent,
  ],
})
export class StudyPagesComponent {
  private studyPageService = inject(StudyPageService);
  private subjectService = inject(SubjectService);
  private router = inject(Router);
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);

  searchControl = new FormControl('');
  subjectsSignal = toSignal(this.subjectService.loadCategories(), { initialValue: [] as Subject[] });

  pagedStudyPagesSignal = signal<Paged<StudyPage>>({
    items: [],
    totalCount: 0,
    pageNumber: 1,
    pageSize: 10,
  });

  pageNumber = 1;
  pageSize = 10;
  selectedSubjectId = signal<number | null>(null);
  deletingId = signal<number | null>(null);

  ngOnInit() {
    this.loadPages(1);
  }

  get totalPages(): number {
    return Math.ceil(this.pagedStudyPagesSignal().totalCount / this.pageSize);
  }

  trackByPage(index: number, page: StudyPage): number {
    return page.id || index;
  }

  onSearch() {
    this.pageNumber = 1;
    this.loadPages(this.pageNumber);
  }

  onSubjectChange(subjectId: number | null) {
    this.selectedSubjectId.set(subjectId);
    this.pageNumber = 1;
    this.loadPages(this.pageNumber);
  }

  changePage(page: number) {
    this.pageNumber = page;
    this.loadPages(this.pageNumber);
  }

  onNewPage() {
    this.router.navigate(['/study-pages/new']);
  }

  onEdit(page: StudyPage) {
    this.router.navigate(['/study-pages', page.id]);
  }

  onDelete(page: StudyPage) {
    const dialogRef = this.dialog.open(ConfirmDialogComponent, {
      width: '500px',
      maxWidth: '90vw',
      disableClose: false,
      hasBackdrop: true,
      backdropClass: 'custom-backdrop',
      panelClass: 'custom-dialog-container',
      enterAnimationDuration: '300ms',
      exitAnimationDuration: '200ms',
      data: {
        title: 'Calisma Sayfasini Sil',
        message: 'Bu calisma sayfasini silmek istediginizden emin misiniz? Bu islem geri alinmaz.',
        confirmText: 'Evet, Sil',
        cancelText: 'Iptal',
        icon: 'delete_forever',
        confirmColor: 'warn',
      },
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (!result) return;

      this.deletingId.set(page.id);
      this.studyPageService.delete(page.id).subscribe({
        next: () => {
          this.snackBar.open('Calisma sayfasi silindi.', 'Tamam', { duration: 2000 });
          this.loadPages(this.pageNumber);
        },
        error: () => {
          this.snackBar.open('Silme isleminde hata olustu.', 'Tamam', { duration: 3000 });
          this.deletingId.set(null);
        },
      });
    });
  }

  private loadPages(page: number) {
    this.studyPageService
      .getPaged({
        search: this.searchControl.value || '',
        subjectId: this.selectedSubjectId(),
        pageNumber: page,
        pageSize: this.pageSize,
      })
      .subscribe((result) => {
        this.pagedStudyPagesSignal.set(result);
        this.deletingId.set(null);
      });
  }

  // Helper methods for the modern card template
  isRecentlyCreated(createTime: string): boolean {
    const createdDate = new Date(createTime);
    const now = new Date();
    const diffInDays = Math.floor((now.getTime() - createdDate.getTime()) / (1000 * 60 * 60 * 24));
    return diffInDays <= 7; // Considered "new" if created within last 7 days
  }

  getStarRating(page: any): number {
    // Calculate rating based on page metrics (imageCount, isPublished status)
    let rating = 3; // Base rating
    
    if (page.imageCount >= 10) rating += 1;
    if (page.imageCount >= 5) rating += 0.5;
    if (page.isPublished) rating += 0.5;
    
    return Math.min(5, rating); // Cap at 5 stars
  }

  getRatingScore(page: any): string {
    const rating = this.getStarRating(page);
    return rating.toFixed(1);
  }
}
