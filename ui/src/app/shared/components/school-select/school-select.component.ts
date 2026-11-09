import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  inject,
  input,
  linkedSignal,
  model,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective } from '@jsverse/transloco';
import { catchError, of, switchMap, tap, timer } from 'rxjs';
import { SchoolOption } from '../../../models/taxonomy';
import { SchoolService } from '../../../services/school.service';

/** Açılır listede aynı anda gösterilen en fazla seçenek; arama daraltır. */
export const SCHOOL_SELECT_MAX_OPTIONS = 50;

/** Yazma bitince sunucuya sorulana kadar beklenen süre (ms). */
export const SCHOOL_SELECT_DEBOUNCE_MS = 250;

/**
 * Issue #277 — aranabilir okul seçimi (mat-autocomplete). Seçenekler yazdıkça `GET /api/exam/school`'dan gelir; serbest metin
 * gönderilmez, yalnız listeden seçilen okulun id'si `value` olur. Yazı seçili okulun adından saparsa seçim düşer (null).
 * Kullananlar: kayıt sihirbazı (öğrenci/öğretmen), admin öğrenci okul değişikliği dialog'u.
 */
@Component({
  selector: 'app-school-select',
  standalone: true,
  imports: [
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
  ],
  templateUrl: './school-select.component.html',
  styleUrl: './school-select.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SchoolSelectComponent {
  private readonly schoolService = inject(SchoolService);

  /** Seçili okul id'si; seçim yoksa null. */
  readonly value = model<number | null>(null);
  /** Varsayılan etiket yerine gösterilecek (çevrilmiş) metin. */
  readonly label = input<string | null>(null);
  /** Alanın altında gösterilecek (çevrilmiş) açıklama. */
  readonly hint = input<string | null>(null);
  readonly required = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  /** Listeden çıkarılacak okul (ör. admin dialog'unda öğrencinin mevcut okulu). */
  readonly excludeSchoolId = input<number | null>(null);

  /** Seçilen okulun tamamı (ad göstermek isteyen üst komponentler için); seçim düşünce null. */
  readonly selectionChange = output<SchoolOption | null>();
  /**
   * "Metin yazıldı ama listeden okul seçilmedi" durumu değişince yayınlanır (bkz. `unmatchedText`). Üst form bu durumda
   * gönderimi engelleyebilir — aksi halde yazılan okul sessizce `null` olarak giderdi.
   */
  readonly unmatchedChange = output<boolean>();
  private lastUnmatched = false;

  private readonly reloadTick = signal(0);
  private readonly status = signal<'loading' | 'ready' | 'error'>('loading');
  private readonly results = signal<SchoolOption[]>([]);

  readonly loading = computed(() => this.status() === 'loading');
  readonly loadFailed = computed(() => this.status() === 'error');
  readonly schools = computed(() => {
    const excluded = this.excludeSchoolId();
    return this.results().filter((s) => s.id !== excluded);
  });

  /** Listeden seçilen okul; dışarıdan gelen id bunda yoksa tek okul isteğiyle çözülür. */
  private readonly picked = signal<SchoolOption | null>(null);
  private readonly fetched = signal<SchoolOption | null>(null);

  readonly selectedSchool = computed(() => {
    const id = this.value();
    if (id == null) return null;
    return [this.picked(), this.fetched()].find((s) => s?.id === id) ?? null;
  });

  /**
   * Girişteki metin. Seçim (dışarıdan da olabilir) değişince okul adına eşitlenir. Seçim dışarıdan düşünce metin de
   * temizlenir; kullanıcı yazarak düşürdüyse `onInput` yazdığı metni üstüne yazar.
   */
  readonly query = linkedSignal<SchoolOption | null, string>({
    source: this.selectedSchool,
    computation: (school, previous) => school?.name ?? (previous?.source ? '' : (previous?.value ?? '')),
  });

  /** Seçim yapılmışken metin okul adıdır; arama boş sayılır ki başka okul kolayca seçilebilsin. */
  private readonly searchTerm = computed(() => {
    const selected = this.selectedSchool();
    return selected && this.query() === selected.name ? '' : this.query().trim();
  });
  private readonly request = computed(() => ({ term: this.searchTerm(), tick: this.reloadTick() }));

  readonly isEmpty = computed(() => this.status() === 'ready' && !this.searchTerm() && this.schools().length === 0);
  readonly filtered = this.schools;

  /** Metin yazılmış ama listeden seçilmemiş → kullanıcıya "listeden seçin" ipucu. */
  readonly unmatchedText = computed(() => !this.selectedSchool() && this.query().trim().length > 0);

  /** mat-autocomplete seçimden sonra girişe bu metni yazar. */
  readonly displayWith = (id: number | null): string =>
    id == null ? '' : (this.schools().find((s) => s.id === id)?.name ?? '');

  constructor() {
    toObservable(this.request)
      .pipe(
        tap(() => this.status.set('loading')),
        switchMap(({ term }) =>
          (term ? timer(SCHOOL_SELECT_DEBOUNCE_MS) : of(0)).pipe(
            switchMap(() => this.schoolService.search(term, SCHOOL_SELECT_MAX_OPTIONS)),
            catchError(() => of(null)),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((schools) => {
        if (schools) this.results.set(schools);
        this.status.set(schools ? 'ready' : 'error');
      });

    toObservable(this.value)
      .pipe(
        switchMap((id) =>
          id == null || this.picked()?.id === id ? of(null) : this.schoolService.getById(id).pipe(catchError(() => of(null))),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((school) => this.fetched.set(school));
  }

  retry(): void {
    this.reloadTick.update((n) => n + 1);
  }

  onInput(text: string): void {
    const selected = this.selectedSchool();
    if (selected && text !== selected.name) {
      this.value.set(null);
      this.selectionChange.emit(null);
    }
    this.query.set(text);
    this.emitUnmatched();
  }

  onSelected(event: MatAutocompleteSelectedEvent): void {
    this.select(event.option.value as number);
  }

  select(id: number): void {
    const school = this.schools().find((s) => s.id === id) ?? null;
    if (!school) return;
    this.picked.set(school);
    this.value.set(school.id);
    this.query.set(school.name);
    this.selectionChange.emit(school);
    this.emitUnmatched();
  }

  clear(): void {
    if (this.value() != null) this.selectionChange.emit(null);
    this.value.set(null);
    this.query.set('');
    this.emitUnmatched();
  }

  private emitUnmatched(): void {
    const unmatched = this.unmatchedText();
    if (unmatched === this.lastUnmatched) return;
    this.lastUnmatched = unmatched;
    this.unmatchedChange.emit(unmatched);
  }

  optionSubtitle(school: SchoolOption): string {
    return [school.districtName, school.provinceName].filter((part): part is string => !!part).join(', ');
  }
}
