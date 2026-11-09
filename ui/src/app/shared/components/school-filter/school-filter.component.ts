import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { catchError, of, switchMap, tap, timer } from 'rxjs';
import { SchoolOption } from '../../../models/taxonomy';
import { SchoolService } from '../../../services/school.service';
import { SchoolFilterUnassignedLabel, SchoolFilterValue } from './school-filter.model';

/** Açılır listede aynı anda gösterilen en fazla okul; arama daraltır. */
export const SCHOOL_FILTER_MAX_OPTIONS = 20;

/** Yazma bitince sunucuya sorulana kadar beklenen süre (ms). */
export const SCHOOL_FILTER_DEBOUNCE_MS = 250;

/**
 * Admin listeleri için okul filtresi (issue #152; #153 öğrenci listesi de kullanır).
 * Seçenekler: "Tümü", "Bağımsız / okulsuz" ve yazdıkça `GET /api/exam/school`'dan aranan okullar.
 * Arama başarısız olursa "Tümü" ve "Bağımsız" seçenekleri kullanılabilir kalır.
 *
 * Derin linkteki `schoolId` bulunamazsa (silinmiş/yanlış id) değer "Tümü"ye çekilir ve `valueChange` yayılır;
 * ebeveyn URL'i/listeyi buna göre günceller. İstek başka bir nedenle düşerse değere dokunulmaz.
 *
 * Kullanım: `<app-school-filter [value]="filter()" (valueChange)="onFilterChange($event)" />`
 * Değer → query dönüşümü için `schoolFilterToQuery()`.
 */
@Component({
  selector: 'app-school-filter',
  standalone: true,
  imports: [MatAutocompleteModule, MatFormFieldModule, MatInputModule, TranslocoDirective],
  providers: [provideTranslocoScope('admin')],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './school-filter.component.html',
  styleUrls: ['./school-filter.component.scss'],
})
export class SchoolFilterComponent {
  private readonly schoolService = inject(SchoolService);
  private readonly transloco = inject(TranslocoService);

  readonly value = model<SchoolFilterValue>('all');
  readonly disabled = input(false);
  /**
   * "Okula bağlı olmayanlar" seçeneğinin etiketi (`admin.schoolFilter.*` anahtarı).
   * Öğretmen listesi "Bağımsız / okulsuz" (varsayılan), öğrenci listesi "Okulsuz" kullanır.
   */
  readonly unassignedLabel = input<SchoolFilterUnassignedLabel>('unassigned');

  /** Yazılan arama; null iken giriş seçili değerin adını gösterir. */
  readonly query = signal<string | null>(null);
  private readonly term = computed(() => this.query()?.trim() ?? '');

  private readonly status = signal<'loading' | 'ready' | 'error'>('loading');
  readonly schools = signal<SchoolOption[]>([]);
  readonly loading = computed(() => this.status() === 'loading');
  readonly loadFailed = computed(() => this.status() === 'error');

  /** Seçili okulun adı: listeden seçilince oradan, derin linkte tek okul isteğinden gelir. */
  private readonly selected = signal<SchoolOption | null>(null);
  readonly selectedName = computed(() => {
    const school = this.selected();
    return school?.id === this.value() ? school!.name : '';
  });

  readonly displayWith = (value: SchoolFilterValue | null): string => this.labelOf(value ?? 'all');

  constructor() {
    toObservable(this.term)
      .pipe(
        tap(() => this.status.set('loading')),
        switchMap((term) =>
          (term ? timer(SCHOOL_FILTER_DEBOUNCE_MS) : of(0)).pipe(
            switchMap(() => this.schoolService.search(term, SCHOOL_FILTER_MAX_OPTIONS)),
            catchError(() => of(null)),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((schools) => {
        this.schools.set(schools ?? []);
        this.status.set(schools ? 'ready' : 'error');
      });

    effect((onCleanup) => {
      const value = this.value();
      if (typeof value !== 'number' || untracked(this.selected)?.id === value) return;
      const request = this.schoolService.getById(value).subscribe({
        next: (school) => this.selected.set(school),
        error: (err: HttpErrorResponse) => {
          if (err.status === 404) this.value.set('all');
        },
      });
      onCleanup(() => request.unsubscribe());
    });
  }

  onInput(text: string): void {
    this.query.set(text);
  }

  onSelected(event: MatAutocompleteSelectedEvent): void {
    const value = event.option.value as SchoolFilterValue;
    if (typeof value === 'number') this.selected.set(this.schools().find((s) => s.id === value) ?? null);
    this.value.set(value);
    this.query.set(null);
  }

  /** Panel seçimsiz kapandı: yarım kalan arama atılır, giriş seçili değere döner. */
  onClosed(): void {
    this.query.set(null);
  }

  private labelOf(value: SchoolFilterValue): string {
    if (typeof value === 'number') return this.schools().find((s) => s.id === value)?.name ?? this.selectedName();
    return this.transloco.translate<string>(`admin.schoolFilter.${value === 'all' ? 'all' : this.unassignedLabel()}`);
  }
}
