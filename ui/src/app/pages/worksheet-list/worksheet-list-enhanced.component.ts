import { CommonModule } from '@angular/common';
import { Component, ElementRef, inject, signal, ViewChild, computed } from '@angular/core';
import { WorksheetCardComponent } from '../worksheet-card/worksheet-card.component';
import { FormControl, FormsModule, ReactiveFormsModule, FormBuilder, FormGroup } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TestService } from '../../services/test.service';
import { InstanceSummary, Paged, Test } from '../../models/test-instance';
import { CompletedWorksheetCardComponent } from '../completed-worksheet/completed-worksheet-card.component';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { SubjectService } from '../../services/subject.service';
import { Subject } from '../../models/subject';
import { CustomCheckboxComponent } from '../../shared/components/ms-checkbox/ms-checkbox.component';
import { IsStudentDirective } from '../../shared/directives/is-student.directive';
import { toSignal } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { GradesService } from '../../services/grades.service';

@Component({
  selector: 'app-worksheet-list-enhanced',
  templateUrl: './worksheet-list-enhanced.component.html',
  standalone: true,
  imports: [
    CommonModule,
    WorksheetCardComponent,
    MatAutocompleteModule,
    MatInputModule,
    MatSelectModule,
    MatCheckboxModule,
    MatButtonModule,
    MatIconModule,
    ReactiveFormsModule,
    CompletedWorksheetCardComponent,
    CustomCheckboxComponent,
    RouterModule,
    FormsModule,
    PaginationComponent,
    IsStudentDirective,
  ],
})
export class WorksheetListEnhancedComponent {
  testService = inject(TestService);
  subjectService = inject(SubjectService);
  route = inject(ActivatedRoute);
  router = inject(Router);
  fb = inject(FormBuilder);

  // Form controls
  searchForm: FormGroup;

  // Signals
  newestWorksheetsSignal = toSignal(this.testService.getLatest(1));
  hotTestsSignal = signal<Test[]>([]);
  relevantTestsSignal = signal<Test[]>([]);
  pagedWorksheetsSignal = signal<Paged<Test>>({
    items: [],
    totalCount: 0,
    pageNumber: 0,
    pageSize: 0,
  });
  completedTestSignal = toSignal(this.testService.getCompleted(1));
  subjectsSignal = toSignal(this.subjectService.loadCategories());
  gradeService = inject(GradesService);
  gradesSignal = toSignal(this.gradeService.getGrades());

  // State
  currentSection = signal<'newest' | 'hot' | 'completed' | 'search' | 'relevant'>('newest');
  selectedSubjectIds = signal<number[]>([]);
  selectedGradeIds = signal<number[]>([]);
  pageNumber = signal(1);
  pageSize = 12;
  isLoading = signal(false);

  @ViewChild('newestContainer', { static: false }) newestContainer!: ElementRef;
  @ViewChild('hotContainer', { static: false }) hotContainer!: ElementRef;
  @ViewChild('relevantContainer', { static: false }) relevantContainer!: ElementRef;

  // Computed properties
  filteredSubjects = computed(() => {
    const subjects = this.subjectsSignal() || [];
    return subjects.filter(
      (subject) => this.selectedSubjectIds().length === 0 || this.selectedSubjectIds().includes(subject.id)
    );
  });

  totalPages = computed(() => Math.ceil(this.pagedWorksheetsSignal().totalCount / this.pageSize));

  images = ['honey-back.png', 'rect-back.png', 'triangle-back.png', 'diamond-back.png'];

  constructor() {
    this.searchForm = this.fb.group({
      searchTerm: [''],
      sortBy: ['newest'],
      difficulty: ['all'],
      duration: ['all'],
    });

    // Setup search debouncing
    this.searchForm
      .get('searchTerm')
      ?.valueChanges.pipe(debounceTime(300), distinctUntilChanged())
      .subscribe(() => {
        this.performSearch();
      });
  }

  ngOnInit() {
    const initialWorksheets = this.route.snapshot.data['worksheets'] as Paged<Test>;
    this.pagedWorksheetsSignal.set(initialWorksheets);

    this.route.queryParams.subscribe((params) => {
      const search = params['search'] ?? '';
      this.searchForm.patchValue({ searchTerm: search });
      if (search) {
        this.currentSection.set('search');
        this.performSearch();
      }
    });

    // Load hot tests
    this.loadHotTests();

    // Load relevant tests
    this.loadRelevantTests();
  }

  public getBackgroundImage(id: number): string {
    const randomIndex = id % this.images.length;
    return this.images[randomIndex];
  }

  trackWorksheet(index: number, worksheet: Test): number {
    return worksheet.id || index;
  }

  trackSubject(index: number, subject: Subject): number {
    return subject.id;
  }

  trackGrades(index: number, grade: { id: number }): number {
    return grade.id || index;
  }

  trackCompletedTests(index: number, instance: InstanceSummary): number {
    return instance.id || index;
  }

  // Section navigation
  showSection(section: 'newest' | 'hot' | 'completed' | 'search' | 'relevant') {
    this.currentSection.set(section);
    if (section === 'search') {
      this.performSearch();
    } else if (section === 'relevant') {
      this.loadRelevantTests();
    }
  }

  // Search functionality
  performSearch() {
    if (!this.searchForm.get('searchTerm')?.value) {
      this.currentSection.set('newest');
      return;
    }

    this.currentSection.set('search');
    this.isLoading.set(true);
    this.pageNumber.set(1);
    this.updateSearchResults();

    // Arama terimi değiştiğinde relevant tests'i de güncelle
    this.loadRelevantTests();
  }

  private updateSearchResults(): void {
    const searchTerm = this.searchForm.get('searchTerm')?.value || '';

    this.testService
      .search(searchTerm, this.selectedSubjectIds(), this.selectedGradeIds(), this.pageNumber())
      .subscribe((results) => {
        this.pagedWorksheetsSignal.set(results);
        this.isLoading.set(false);
      });
  }

  // Filter functions
  toggleSubjectFilter(subject: Subject) {
    const currentIds = this.selectedSubjectIds();
    const index = currentIds.indexOf(subject.id);

    if (index > -1) {
      this.selectedSubjectIds.set(currentIds.filter((id) => id !== subject.id));
    } else {
      this.selectedSubjectIds.set([...currentIds, subject.id]);
    }

    if (this.currentSection() === 'search') {
      this.pageNumber.set(1);
      this.updateSearchResults();
    }
  }

  toggleGradeFilter(gradeId: number) {
    const currentIds = this.selectedGradeIds();
    const index = currentIds.indexOf(gradeId);

    if (index > -1) {
      this.selectedGradeIds.set(currentIds.filter((id) => id !== gradeId));
    } else {
      this.selectedGradeIds.set([...currentIds, gradeId]);
    }

    if (this.currentSection() === 'search') {
      this.pageNumber.set(1);
      this.updateSearchResults();
    }
  }

  clearFilters() {
    this.selectedSubjectIds.set([]);
    this.selectedGradeIds.set([]);
    this.searchForm.patchValue({
      sortBy: 'newest',
      difficulty: 'all',
      duration: 'all',
    });

    if (this.currentSection() === 'search') {
      this.pageNumber.set(1);
      this.updateSearchResults();
    }
  }

  // Pagination
  changePage(page: number) {
    this.pageNumber.set(page);
    if (this.currentSection() === 'search') {
      this.updateSearchResults();
    }
  }

  // Navigation functions
  onCardClick(id: number) {
    this.router.navigate(['/test', id]);
  }

  // Carousel navigation
  scrollCarousel(element: HTMLElement, direction: 'left' | 'right') {
    if (!element) return;

    const scrollDistance = 400;
    const scrollAmount = direction === 'left' ? -scrollDistance : scrollDistance;

    element.scrollBy({ left: scrollAmount, behavior: 'smooth' });
  }

  private loadHotTests() {
    // Mock hot tests - replace with actual service call
    this.testService.getLatest(1).subscribe((tests) => {
      this.hotTestsSignal.set(tests.slice(0, 6));
    });
  }

  private loadRelevantTests() {
    // Search term'e göre en alakalı testleri getir
    const searchTerm = this.searchForm.get('searchTerm')?.value || '';
    if (searchTerm.trim()) {
      // Eğer search term varsa, ona göre alakalı testleri getir
      this.testService.search(searchTerm, [], this.selectedGradeIds(), 1, 6).subscribe((results) => {
        this.relevantTestsSignal.set(results.items);
      });
    } else {
      // Eğer search term yoksa, en popüler testleri göster
      this.testService.getLatest(1).subscribe((tests) => {
        this.relevantTestsSignal.set(tests.slice(0, 6));
      });
    }
  }

  // Form handlers
  onSortChange() {
    if (this.currentSection() === 'search') {
      this.pageNumber.set(1);
      this.updateSearchResults();
    }
  }

  onFilterChange() {
    if (this.currentSection() === 'search') {
      this.pageNumber.set(1);
      this.updateSearchResults();
    }
  }
}
