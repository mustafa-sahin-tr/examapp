import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { CreateProgramRequest, ProgramSubject } from '../../models/program.interfaces';
import { LocaleService } from '../../services/locale.service';
import { ProgramService } from '../../services/program.service';
import { OptionCardComponent } from '../../shared/components/option-card/option-card.component';
import { OptionGroupComponent } from '../../shared/components/option-card/option-group.component';
import {
  QUESTIONS_PER_DAY,
  STUDY_DURATIONS,
  SUBJECTS_PER_DAY,
  StudyType,
  WEEK_DAYS,
  WizardChoices,
  buildPreview,
  isEndAfterStart,
  toLocalDateString,
  toUserSelections,
} from './program-wizard.logic';

/** Sayfanın Transloco scope'u: `public/i18n/program-create/<lang>.json` (issue #183). */
const SCOPE = 'program-create';

/** #885: dört adım. */
export const PROGRAM_STEPS = ['amount', 'days', 'subjects', 'until'] as const;
export type ProgramWizardStep = (typeof PROGRAM_STEPS)[number];

/** Bitiş için hazır süreler (gün). */
export const PROGRAM_LENGTHS = [14, 30, 60] as const;
const DEFAULT_PROGRAM_DAYS = 30;

/**
 * Program oluşturma sihirbazı. #885: altı soru yerine dört adım — 1) Ne kadar? (süreli ya da soru sayılı, süre/soru ve günde
 * kaç ders tek ekranda), 2) Hangi günler? (yedi gün çipi, hepsi açık), 3) Hangi dersler zor? (öğrencinin sınıfının dersleri,
 * `GET api/program/subjects`), 4) Ne zamana kadar? (hazır süreler ya da tarih; ad kendiliğinden "Kasım programı"). Sağda seçimlere
 * göre canlı örnek hafta (sunucunun takvim kuralıyla aynı dağılım).
 */
@Component({
  selector: 'app-program-create',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatChipsModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    OptionCardComponent,
    OptionGroupComponent,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './program-create.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './program-create.component.scss',
})
export class ProgramCreateComponent implements OnInit {
  private readonly programService = inject(ProgramService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly steps = PROGRAM_STEPS;
  protected readonly durations = STUDY_DURATIONS;
  protected readonly questionOptions = QUESTIONS_PER_DAY;
  protected readonly perDayOptions = SUBJECTS_PER_DAY;
  protected readonly weekDays = WEEK_DAYS;
  protected readonly lengths = PROGRAM_LENGTHS;

  readonly step = signal(0);

  // 1) Ne kadar?
  readonly studyType = signal<StudyType>('time');
  readonly duration = signal<string>('25-5');
  readonly questionsPerDay = signal<number>(24);
  readonly subjectsPerDay = signal<number>(2);
  // 2) Hangi günler?
  readonly studyDays = signal<readonly number[]>([...WEEK_DAYS]);
  // 3) Hangi dersler?
  readonly subjects = signal<ProgramSubject[]>([]);
  readonly subjectsLoading = signal(true);
  readonly subjectsError = signal(false);
  readonly difficultIds = signal<readonly number[]>([]);
  // 4) Ne zamana kadar?
  readonly startDate = signal<Date>(startOfToday());
  readonly endDate = signal<Date | null>(addDays(startOfToday(), DEFAULT_PROGRAM_DAYS));
  /** Boşsa ad kendiliğinden ("Kasım programı"). */
  readonly programName = signal('');

  readonly submitting = signal(false);
  /** Son gönderim başarısız: seçimler korunur, bant gösterilir. */
  readonly submitError = signal(false);

  readonly choices = computed<WizardChoices>(() => ({
    studyType: this.studyType(),
    duration: this.duration(),
    questionsPerDay: this.questionsPerDay(),
    subjectsPerDay: this.subjectsPerDay(),
    studyDays: this.studyDays(),
  }));

  /** Seçimlere göre örnek hafta (başlangıçtan itibaren 7 gün). */
  readonly preview = computed(() =>
    buildPreview(this.choices(), this.subjects(), new Set(this.difficultIds()), this.startDate()),
  );

  /** Önizlemede vurgulanan zor dersler (ad ile). */
  readonly difficultNames = computed(() => {
    const ids = new Set(this.difficultIds());
    return new Set(this.subjects().filter((s) => ids.has(s.id)).map((s) => s.name));
  });

  readonly defaultName = computed(() => {
    const month = new Intl.DateTimeFormat(this.locale(), { month: 'long' }).format(this.startDate());
    return this.transloco.translate(`${SCOPE}.until.defaultName`, { month: capitalize(month, this.locale()) });
  });

  readonly endValid = computed(() => {
    const end = this.endDate();
    return !!end && isEndAfterStart(this.startDate(), end);
  });

  /** Adımı geçmeyi engelleyen neden (yoksa null). */
  readonly blocker = computed<string | null>(() => {
    switch (PROGRAM_STEPS[this.step()]) {
      case 'days':
        return this.studyDays().length === 0 ? 'days.needOne' : null;
      case 'subjects':
        return this.subjects().length === 0 ? 'subjects.none' : null;
      case 'until':
        return this.endValid() ? null : 'until.endAfterStart';
      default:
        return null;
    }
  });

  ngOnInit(): void {
    this.loadSubjects();
  }

  protected loadSubjects(): void {
    this.subjectsLoading.set(true);
    this.subjectsError.set(false);
    this.programService
      .getProgramSubjects()
      .pipe(
        finalize(() => this.subjectsLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (list) => this.subjects.set(list ?? []),
        error: () => this.subjectsError.set(true),
      });
  }

  protected next(): void {
    if (this.blocker()) return;
    if (this.step() < PROGRAM_STEPS.length - 1) this.step.update((n) => n + 1);
    else this.createProgram();
  }

  protected back(): void {
    this.step.update((n) => Math.max(0, n - 1));
  }

  protected goTo(index: number): void {
    // Yalnız geçilmiş adımlara dönülür.
    if (index < this.step()) this.step.set(index);
  }

  protected toggleDay(day: number): void {
    this.studyDays.update((days) =>
      days.includes(day) ? days.filter((d) => d !== day) : WEEK_DAYS.filter((d) => d === day || days.includes(d)),
    );
  }

  /** Ders çipi: yalnız kullanıcı tıklaması (giriş bağlamasıyla seçilen çip olay döngüsü kurmasın). */
  protected setDifficult(id: number, selected: boolean): void {
    this.difficultIds.update((ids) => (selected ? (ids.includes(id) ? ids : [...ids, id]) : ids.filter((x) => x !== id)));
  }

  protected setLength(days: number): void {
    this.endDate.set(addDays(this.startDate(), days));
  }

  protected isLength(days: number): boolean {
    const end = this.endDate();
    return !!end && toLocalDateString(end) === toLocalDateString(addDays(this.startDate(), days));
  }

  protected createProgram(): void {
    const end = this.endDate();
    if (!end || !this.endValid() || this.submitting()) return;
    const request: CreateProgramRequest = {
      programName: this.programName().trim() || this.defaultName(),
      description: '',
      startDate: toLocalDateString(this.startDate()),
      endDate: toLocalDateString(end),
      userSelections: toUserSelections(this.choices()),
      difficultSubjectIds: [...this.difficultIds()],
    };
    this.submitting.set(true);
    this.submitError.set(false);
    this.programService
      .createProgram(request)
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.snackBar.open(this.transloco.translate(`${SCOPE}.created`), this.transloco.translate(`${SCOPE}.ok`), {
            duration: 3000,
          });
          void this.router.navigate(['/programs']);
        },
        error: () => this.submitError.set(true),
      });
  }

  /** "Pzt", "Sal" … (dile göre kısa gün adı). */
  protected dayShort(day: number): string {
    // 2024-01-01 Pazartesi.
    return new Intl.DateTimeFormat(this.locale(), { weekday: 'short' }).format(new Date(2024, 0, day));
  }

  protected durationMinutes(value: string): { study: string; rest: string } {
    const [study, rest] = value.split('-');
    return { study, rest };
  }

  protected formatDate(date: Date | null): string {
    return date ? new Intl.DateTimeFormat(this.locale(), { day: 'numeric', month: 'long', year: 'numeric' }).format(date) : '';
  }

  private locale(): string {
    return this.localeService.localeDefinition().angularLocale;
  }
}

function startOfToday(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

function addDays(date: Date, days: number): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days);
}

function capitalize(value: string, locale: string): string {
  return value.charAt(0).toLocaleUpperCase(locale) + value.slice(1);
}
