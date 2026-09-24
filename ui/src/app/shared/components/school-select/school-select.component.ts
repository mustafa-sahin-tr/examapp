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
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective } from '@jsverse/transloco';
import { catchError, map, of, startWith, switchMap } from 'rxjs';
import { School } from '../../../models/taxonomy';
import { SchoolService } from '../../../services/school.service';

type SchoolsState =
  | { status: 'loading'; schools: School[] }
  | { status: 'ready'; schools: School[] }
  | { status: 'error'; schools: School[] };

/** Açılır listede aynı anda gösterilen en fazla seçenek; arama daraltır. */
export const SCHOOL_SELECT_MAX_OPTIONS = 50;

/** Aramada Türkçe büyük/küçük harf ve aksan farkını yok sayar ("İstanbul" ~ "istanbul"). */
export function normalizeSchoolQuery(text: string): string {
  return text
    .toLocaleLowerCase('tr')
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/ı/g, 'i')
    .trim();
}

/**
 * Issue #277 — aranabilir okul seçimi (mat-autocomplete). Liste `GET /api/exam/school`'dan gelir; serbest metin
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
  readonly selectionChange = output<School | null>();

  private readonly reloadTick = signal(0);
  readonly state = toSignal(
    toObservable(this.reloadTick).pipe(
      switchMap(() =>
        this.schoolService.getSchools().pipe(
          map((schools): SchoolsState => ({ status: 'ready', schools: schools ?? [] })),
          catchError(() => of<SchoolsState>({ status: 'error', schools: [] })),
          startWith<SchoolsState>({ status: 'loading', schools: [] }),
        ),
      ),
    ),
    { initialValue: { status: 'loading', schools: [] } as SchoolsState },
  );

  readonly loading = computed(() => this.state().status === 'loading');
  readonly loadFailed = computed(() => this.state().status === 'error');
  readonly schools = computed(() => {
    const excluded = this.excludeSchoolId();
    return this.state().schools.filter((s) => s.id !== excluded);
  });
  readonly isEmpty = computed(() => this.state().status === 'ready' && this.schools().length === 0);

  readonly selectedSchool = computed(() => {
    const id = this.value();
    return id == null ? null : (this.schools().find((s) => s.id === id) ?? null);
  });

  /**
   * Girişteki metin. Seçim (dışarıdan da olabilir) değişince okul adına eşitlenir; seçim düşünce kullanıcının yazdığı
   * metin korunur.
   */
  readonly query = linkedSignal<School | null, string>({
    source: this.selectedSchool,
    computation: (school, previous) => school?.name ?? previous?.value ?? '',
  });

  readonly filtered = computed(() => {
    const q = normalizeSchoolQuery(this.query());
    const selected = this.selectedSchool();
    // Seçim yapılmışken metin okul adıdır; tüm liste gösterilsin ki başka okul kolayca seçilebilsin.
    const all = this.schools();
    const matches =
      !q || (selected && this.query() === selected.name)
        ? all
        : all.filter((s) => normalizeSchoolQuery(`${s.name} ${s.districtName ?? ''} ${s.provinceName ?? ''}`).includes(q));
    return matches.slice(0, SCHOOL_SELECT_MAX_OPTIONS);
  });

  /** Metin yazılmış ama listeden seçilmemiş → kullanıcıya "listeden seçin" ipucu. */
  readonly unmatchedText = computed(() => !this.selectedSchool() && this.query().trim().length > 0);

  /** mat-autocomplete seçimden sonra girişe bu metni yazar. */
  readonly displayWith = (id: number | null): string =>
    id == null ? '' : (this.schools().find((s) => s.id === id)?.name ?? '');

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
  }

  onSelected(event: MatAutocompleteSelectedEvent): void {
    this.select(event.option.value as number);
  }

  select(id: number): void {
    const school = this.schools().find((s) => s.id === id) ?? null;
    if (!school) return;
    this.value.set(school.id);
    this.query.set(school.name);
    this.selectionChange.emit(school);
  }

  clear(): void {
    if (this.value() != null) this.selectionChange.emit(null);
    this.value.set(null);
    this.query.set('');
  }

  optionSubtitle(school: School): string {
    return [school.districtName, school.provinceName].filter((part): part is string => !!part).join(', ');
  }
}
