import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { catchError, debounceTime, distinctUntilChanged, map, of, switchMap, tap } from 'rxjs';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { StudyPage, StudyPageContentType } from '../../models/study-page';
import { StudyPageService } from '../../services/study-page.service';
import { StudyItemDisplay, describeStudyItem } from '../../shared/utils/study-item-display.util';
import { MY_PROGRAMS_SCOPE } from './my-programs-scope';

export interface AddStudyPagesDialogData {
  selectedDate?: Date | null;
}

export interface AddStudyPagesDialogResult {
  selectedPages: Array<StudyPage & { selected: boolean; startDate: Date; endDate: Date }>;
}

/** Filtre çipi değeri: 'all' ya da bir içerik tipi (enum sayısaldır). */
type TypeFilter = 'all' | StudyPageContentType;

interface TypeFilterOption {
  value: TypeFilter;
  labelKey: string;
}

interface StudyPageSelection {
  page: StudyPage;
  startDate: Date | null;
  endDate: Date | null;
}

interface StudyPageCard {
  page: StudyPage;
  display: StudyItemDisplay;
}

/** Sunucu tarafı filtre sonucu tek sayfada istenen etkinlik sayısı. */
const PAGE_SIZE = 200;
const SEARCH_DEBOUNCE_MS = 300;

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
    MatChipsModule,
    MatFormFieldModule,
    MatInputModule,
    MatDatepickerModule,
    MatProgressSpinnerModule,
    FormsModule,
    TranslocoDirective,
  ],
  // Dialog `MatDialog` ile açıldığı için sayfanın scope'unu devralmaz; kendi provider'ını verir.
  providers: [provideTranslocoScope(MY_PROGRAMS_SCOPE)],
  templateUrl: './add-study-pages-dialog.component.html',
  styleUrls: ['./add-study-pages-dialog.component.scss'],
})
export class AddStudyPagesDialogComponent {
  private readonly dialogRef = inject<MatDialogRef<AddStudyPagesDialogComponent>>(MatDialogRef);
  private readonly data = inject<AddStudyPagesDialogData | null>(MAT_DIALOG_DATA, { optional: true });
  private readonly studyPageService = inject(StudyPageService);
  private readonly transloco = inject(TranslocoService);

  readonly selectedDate: Date | null = this.data?.selectedDate ?? null;

  readonly typeFilterOptions: TypeFilterOption[] = [
    { value: 'all', labelKey: 'addStudyPages.filters.all' },
    { value: StudyPageContentType.Image, labelKey: 'addStudyPages.filters.image' },
    { value: StudyPageContentType.Link, labelKey: 'addStudyPages.filters.link' },
    { value: StudyPageContentType.BookPageRange, labelKey: 'addStudyPages.filters.book' },
  ];

  readonly searchText = signal('');
  readonly typeFilter = signal<TypeFilter>('all');
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly items = signal<StudyPage[]>([]);
  /** "Tekrar dene" için sorguyu yeniden tetikler. */
  private readonly attempt = signal(0);

  /** Seçimler filtre değişse de korunur; tarihler `ngModel` ile kayıt üzerinde düzenlenir. */
  private readonly selections = signal<ReadonlyMap<number, StudyPageSelection>>(new Map());

  private readonly debouncedSearch = toSignal(
    toObservable(this.searchText).pipe(
      debounceTime(SEARCH_DEBOUNCE_MS),
      map((text) => text.trim()),
      distinctUntilChanged(),
    ),
    { initialValue: '' },
  );

  private readonly query = computed(() => ({
    search: this.debouncedSearch(),
    type: this.typeFilter(),
    attempt: this.attempt(),
  }));

  readonly cards = computed<StudyPageCard[]>(() =>
    this.items().map((page) => ({
      page,
      display: describeStudyItem(page, (key, params) => this.transloco.translate<string>(key, params) ?? ''),
    })),
  );

  readonly isFiltered = computed(() => this.typeFilter() !== 'all' || this.searchText().trim().length > 0);

  constructor() {
    toObservable(this.query)
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.error.set(false);
        }),
        switchMap((q) =>
          this.studyPageService
            .getPaged({
              search: q.search || null,
              contentType: q.type === 'all' ? null : q.type,
              pageNumber: 1,
              pageSize: PAGE_SIZE,
            })
            .pipe(
              map((result) => ({ ok: true as const, items: result.items })),
              catchError(() => of({ ok: false as const, items: [] as StudyPage[] })),
            ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        this.items.set(result.items);
        this.error.set(!result.ok);
        this.loading.set(false);
      });
  }

  onSearchChange(text: string): void {
    this.searchText.set(text);
  }

  onTypeFilterChange(value: TypeFilter | null | undefined): void {
    // Seçili çipe tekrar basmak listbox değerini boşaltır; bunu "Tümü" say.
    this.typeFilter.set(value ?? 'all');
  }

  retry(): void {
    this.attempt.update((n) => n + 1);
  }

  selectionOf(page: StudyPage): StudyPageSelection | undefined {
    return this.selections().get(page.id);
  }

  isSelected(page: StudyPage): boolean {
    return this.selections().has(page.id);
  }

  togglePageSelection(page: StudyPage): void {
    this.setSelected(page, !this.isSelected(page));
  }

  setSelected(page: StudyPage, selected: boolean): void {
    const next = new Map(this.selections());
    if (!selected) {
      next.delete(page.id);
    } else if (!next.has(page.id)) {
      // Gün seçilerek açıldıysa tarihler otomatik doldurulur.
      next.set(page.id, {
        page,
        startDate: this.selectedDate ? new Date(this.selectedDate) : null,
        endDate: this.selectedDate ? new Date(this.selectedDate) : null,
      });
    }
    this.selections.set(next);
  }

  onCancel(): void {
    this.dialogRef.close();
  }

  onSave(): void {
    const selected = this.validSelections();
    if (selected.length === 0) {
      return;
    }
    const result: AddStudyPagesDialogResult = {
      selectedPages: selected.map((s) => ({
        ...s.page,
        selected: true,
        startDate: s.startDate,
        endDate: s.endDate,
      })),
    };
    this.dialogRef.close(result);
  }

  get hasValidSelections(): boolean {
    return this.validSelections().length > 0;
  }

  private validSelections(): Array<{ page: StudyPage; startDate: Date; endDate: Date }> {
    return [...this.selections().values()].flatMap((s) =>
      s.startDate && s.endDate && s.startDate <= s.endDate
        ? [{ page: s.page, startDate: s.startDate, endDate: s.endDate }]
        : [],
    );
  }
}
