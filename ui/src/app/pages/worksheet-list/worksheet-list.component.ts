import { CommonModule } from '@angular/common';
import { Component, ElementRef, inject, Input, signal, ViewChild } from '@angular/core';
import { WorksheetCardComponent } from '../worksheet-card/worksheet-card.component';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatInputModule } from '@angular/material/input';
import { TestService } from '../../services/test.service';
import { InstanceSummary, Paged, Test } from '../../models/test-instance';
import { CompletedWorksheetCardComponent } from '../completed-worksheet/completed-worksheet-card.component';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { SubjectService } from '../../services/subject.service';
import { Subject } from '../../models/subject';
import { CustomCheckboxComponent } from '../../shared/components/ms-checkbox/ms-checkbox.component';
import { IsStudentDirective } from '../../shared/directives/is-student.directive';
import { toSignal } from '@angular/core/rxjs-interop';
import { GradesService } from '../../services/grades.service';
import { Grade } from '../../models/student';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { finalize } from 'rxjs/operators';

@Component({
  selector: 'app-worksheet-list',
  templateUrl: './worksheet-list.component.html',
  styleUrls: ['./worksheet-list.component.scss'],
  standalone: true,
  imports: [
    CommonModule,
    WorksheetCardComponent,
    MatAutocompleteModule,
    MatInputModule,
    ReactiveFormsModule,
    CompletedWorksheetCardComponent,
    CustomCheckboxComponent,
    RouterModule,
    SectionHeaderComponent,
    FormsModule,
    PaginationComponent,
    IsStudentDirective,
    MatIconModule,
    MatMenuModule,
    MatButtonModule,
  ],
})
export class WorksheetListComponent {
  testService = inject(TestService);
  searchControl = new FormControl('');
  route = inject(ActivatedRoute);
  router = inject(Router);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);
  subjectService = inject(SubjectService);
  newestWorksheetsSignal = signal<Test[]>([]);
  pagedWorksheetsSignal = signal<Paged<Test>>({
    items: [],
    totalCount: 0,
    pageNumber: 0,
    pageSize: 0,
  });
  completedTestSignal = toSignal(this.testService.getCompleted(1));
  subjectsSignal = toSignal(this.subjectService.loadCategories());
  gradesService = inject(GradesService);
  gradesSignal = toSignal(this.gradesService.getGrades());
  totalCount = 0;
  pageSize = 10;
  pageNumber = 1;
  scrollDistance = 600;
  selectedSubjectIds: number[] = [];
  selectedGradeIds: number[] = [];
  deletingWorksheetId = signal<number | null>(null);

  @ViewChild('cardContainer', { static: false }) cardContainer!: ElementRef;

  images = ['honey-back.png', 'rect-back.png', 'triangle-back.png', 'diamond-back.png'];
  public getBackgroundImage(id: number) {
    const randomIndex = id % this.images.length;
    return this.images[randomIndex];
  }

  get totalPages(): number {
    return Math.ceil(this.pagedWorksheetsSignal().totalCount / this.pageSize);
  }

  trackWorksheet(index: number, worksheet: Test): number {
    return worksheet.id || index;
  }

  trackCourse(index: number, course: Test): number {
    return course.id || index;
  }

  trackCategory(index: number, category: Subject): number {
    return category.id;
  }

  trackGrades(index: number, grade: Grade): number {
    return grade.id || index;
  }

  trackCompletedTests(index: number, instance: InstanceSummary): number {
    return instance.id || index;
  }

  ngOnInit() {
    const initialWorksheets = this.route.snapshot.data['worksheets'] as Paged<Test>;
    this.pagedWorksheetsSignal.set(initialWorksheets);

    // En yeni testleri yükle
    this.updateNewestWorksheets();

    this.route.queryParams.subscribe((params) => {
      const search = params['search'] ?? '';
      this.searchControl.setValue(search);
      this.pageNumber = 1;
      this.updatePagedWorksheets(this.pageNumber);
    });
  }

  onEnter() {
    this.pageNumber = 1;
    this.updatePagedWorksheets(this.pageNumber);
  }

  onCardClick(id: number) {
    console.log('Card clicked:', id);
    this.router.navigate(['/test', id]);
  }

  changePage(page: number) {
    console.log('Page changed:', page);
    this.pageNumber = page;
    this.updatePagedWorksheets(this.pageNumber);
  }

  nextPage() {
    console.log('Next page');
    if (this.pageNumber * this.pageSize < this.pagedWorksheetsSignal().totalCount) {
      this.pageNumber++;
      this.updatePagedWorksheets(this.pageNumber);
    }
  }

  prevPage() {
    console.log('Prev page');
    if (this.pageNumber > 1) {
      this.pageNumber--;
      this.updatePagedWorksheets(this.pageNumber);
    }
  }

  private updatePagedWorksheets(page: number): void {
    this.testService
      .search(this.searchControl.value || '', this.selectedSubjectIds, this.selectedGradeIds, page)
      .subscribe((results) => {
        this.pagedWorksheetsSignal.set(results);
      });
  }

  private updateNewestWorksheets(): void {
    this.testService.getLatest(1).subscribe((latestTests) => {
      this.newestWorksheetsSignal.set(latestTests);
    });
  }

  onAutocompleteSelect(worksheetName: string) {
    this.searchControl.setValue(worksheetName, { emitEvent: false });
    this.onEnter();
  }

  onCheckboxChange(event: { checked: boolean; value: any }) {
    const subjectId = event.value.id;
    if (event.checked) {
      if (!this.selectedSubjectIds.includes(subjectId)) {
        this.selectedSubjectIds.push(subjectId);
        this.pageNumber = 1;
        this.updatePagedWorksheets(this.pageNumber);
      }
    } else {
      const index = this.selectedSubjectIds.indexOf(subjectId);
      if (index > -1) {
        this.selectedSubjectIds.splice(index, 1);
        this.pageNumber = 1;
        this.updatePagedWorksheets(this.pageNumber);
      }
    }
    console.log('Checkbox State:', event.checked, 'Value:', event.value);
  }

  onGradeCheckboxChange(event: { checked: boolean; value: any }) {
    if (event.checked) {
      if (!this.selectedGradeIds.includes(event.value.id)) {
        this.selectedGradeIds = [...this.selectedGradeIds, event.value.id];
      }
    } else {
      this.selectedGradeIds = this.selectedGradeIds.filter((id) => id !== event.value.id);
    }
    this.pageNumber = 1;
    this.updatePagedWorksheets(this.pageNumber);
    console.log('Grade Checkbox State:', event.checked, 'Value:', event.value);
  }

  handleLeftNavigation() {
    if (this.cardContainer) {
      const container = this.cardContainer.nativeElement;
      if (container.scrollLeft > 0) {
        container.scrollBy({ left: -this.scrollDistance, behavior: 'smooth' });
      } else if (this.pageNumber > 1) {
        // this.prevPage();
      }
    }
  }

  handleRightNavigation() {
    if (this.cardContainer) {
      const container = this.cardContainer.nativeElement;
      if (container.scrollLeft + container.clientWidth < container.scrollWidth) {
        container.scrollBy({ left: this.scrollDistance, behavior: 'smooth' });
      } else if (this.pageNumber * this.pageSize < this.pagedWorksheetsSignal().totalCount) {
        // this.nextPage();
      }
    }
  }

  searchQuery = 'dotnet';
  totalResults = 288;
  sortOptions = ['Newest', 'Relevance', 'Popularity'];
  selectedSort = 'Relevance';

  onDeleteWorksheet(worksheetId: number) {
    console.log('Deleting worksheet:', worksheetId);

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
        title: 'Testi Sil',
        message: 'Bu testi silmek istediğinizden emin misiniz? Bu işlem geri alınamaz ve tüm veriler kaybolacaktır.',
        confirmText: 'Evet, Sil',
        cancelText: 'İptal',
        icon: 'delete_forever',
        confirmColor: 'warn',
      },
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (result) {
        this.performDelete(worksheetId);
      }
    });
  }

  private performDelete(worksheetId: number): void {
    // Loading state'i başlat
    this.deletingWorksheetId.set(worksheetId);

    this.testService
      .delete(worksheetId)
      .pipe(
        finalize(() => {
          // Loading state'i kapat
          this.deletingWorksheetId.set(null);
        })
      )
      .subscribe({
        next: (response) => {
          console.log('Test başarıyla silindi:', response);

          // Başarı mesajı göster
          this.showSuccessMessage('✅ Test başarıyla silindi!');

          // Listeyi güncelle
          this.updatePagedWorksheets(this.pageNumber);

          // En yeni testleri de güncelle
          this.updateNewestWorksheets();

          // Eğer bu sayfada başka test kalmadıysa ve ilk sayfa değilse, önceki sayfaya git
          const currentItems = this.pagedWorksheetsSignal().items;
          if (currentItems.length === 1 && this.pageNumber > 1) {
            this.pageNumber--;
            this.updatePagedWorksheets(this.pageNumber);
          }
        },
        error: (error) => {
          console.error('Test silinirken hata oluştu:', error);
          this.showErrorMessage('❌ Test silinirken bir hata oluştu. Lütfen tekrar deneyin.');
        },
      });
  }

  private showSuccessMessage(message: string): void {
    this.snackBar.open(message, 'Tamam', {
      duration: 4000,
      horizontalPosition: 'end',
      verticalPosition: 'top',
      panelClass: ['success-snackbar'],
    });
  }

  private showErrorMessage(message: string): void {
    this.snackBar.open(message, 'Kapat', {
      duration: 6000,
      horizontalPosition: 'end',
      verticalPosition: 'top',
      panelClass: ['error-snackbar'],
    });
  }
}
