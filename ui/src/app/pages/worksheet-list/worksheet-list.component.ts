import { CommonModule } from '@angular/common';
import {
  Component,
  ElementRef,
  inject,
  Input,
  signal,
  ViewChild,
} from '@angular/core';
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
  ],
})
export class WorksheetListComponent {
  testService = inject(TestService);
  searchControl = new FormControl('');
  route = inject(ActivatedRoute);
  router = inject(Router);
  subjectService = inject(SubjectService);
  newestWorksheetsSignal = toSignal(this.testService.getLatest(1));
  pagedWorksheetsSignal = signal<Paged<Test>>({
    items: [],
    totalCount: 0,
    pageNumber: 0,
    pageSize: 0,
  });
  completedTestSignal = toSignal(this.testService.getCompleted(1));
  subjectsSignal = toSignal(this.subjectService.loadCategories());
  totalCount = 0;
  pageSize = 10;
  pageNumber = 1;
  scrollDistance = 600;
  selectedSubjectIds: number[] = [];
  selectedGradeId: number | undefined;

  @ViewChild('cardContainer', { static: false }) cardContainer!: ElementRef;

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

  trackCompletedTests(index: number, instance: InstanceSummary): number {
    return instance.id || index;
  }

  ngOnInit() {
    const initialWorksheets = this.route.snapshot.data[
      'worksheets'
    ] as Paged<Test>;
    this.pagedWorksheetsSignal.set(initialWorksheets);
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
    if (
      this.pageNumber * this.pageSize <
      this.pagedWorksheetsSignal().totalCount
    ) {
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
      .search(
        this.searchControl.value || '',
        this.selectedSubjectIds,
        this.selectedGradeId,
        page
      )
      .subscribe((results) => {
        this.pagedWorksheetsSignal.set(results);
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
      if (
        container.scrollLeft + container.clientWidth <
        container.scrollWidth
      ) {
        container.scrollBy({ left: this.scrollDistance, behavior: 'smooth' });
      } else if (
        this.pageNumber * this.pageSize <
        this.pagedWorksheetsSignal().totalCount
      ) {
        // this.nextPage();
      }
    }
  }

  searchQuery = 'dotnet';
  totalResults = 288;
  sortOptions = ['Newest', 'Relevance', 'Popularity'];
  selectedSort = 'Relevance';
}
