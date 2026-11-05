import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import {
  COACH_GOALS_PER_WEEK,
  COACH_GOAL_MAX_LENGTH,
  COACH_GOAL_WEEKS_AHEAD,
  COACH_NOTE_MAX_LENGTH,
  CoachGoal,
  CoachNote,
  CoachOverview,
} from '../../models/coach.model';
import { CoachService, coachErrorCode } from '../../services/coach.service';
import { LocaleService } from '../../services/locale.service';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';

/** Sunucunun iş kuralı kodları (`coach.errors`); bilinmeyen kod genel mesaja düşer. */
const ERROR_KEYS = new Set(['EmptyText', 'TooLong', 'MarkupNotAllowed', 'InvalidWeek', 'TooManyGoals', 'TooManyNotes']);

function errorKey(error: unknown): string {
  const code = coachErrorCode(error);
  return code && ERROR_KEYS.has(code) ? code : 'generic';
}

/** `yyyy-MM-dd` + gün; saat dilimi kaymasın diye UTC'de hesaplanır. */
export function addDays(isoDate: string, days: number): string {
  const date = new Date(`${isoDate}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

/**
 * Issue #700: koçun öğrenci sayfası — haftanın özeti, haftalık hedefler (öğrenci ve veli görür) ve yalnız yazan öğretmenin
 * gördüğü ders notları. Hafta listesi sunucunun "bu hafta"sından türetilir (`plan.weekStart`), tarayıcı saatinden değil.
 * Kapsam dışı öğrenci 404: sayfa yalnız "bulunamadı" der.
 */
@Component({
  selector: 'app-coach-student',
  standalone: true,
  imports: [
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope('coach')],
  templateUrl: './coach-student.component.html',
  styleUrl: './coach-student.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CoachStudentComponent implements OnInit {
  private readonly coach = inject(CoachService);
  private readonly dialog = inject(MatDialog);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  readonly studentId = Number(inject(ActivatedRoute).snapshot.paramMap.get('studentId'));
  /** Ad gezinme durumundan gelir; özet yüklenince sunucudaki ad geçer. */
  private readonly navigationName: string | null =
    (inject(Router).currentNavigation()?.extras.state?.['studentName'] as string | undefined) ?? null;

  readonly goalMaxLength = COACH_GOAL_MAX_LENGTH;
  readonly noteMaxLength = COACH_NOTE_MAX_LENGTH;
  readonly goalsPerWeek = COACH_GOALS_PER_WEEK;

  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly loadFailed = signal(false);
  readonly overview = signal<CoachOverview | null>(null);

  readonly studentName = computed(() => this.overview()?.fullName ?? this.navigationName);

  readonly selectedWeek = signal<string | null>(null);
  readonly goals = signal<CoachGoal[]>([]);
  readonly goalsLoading = signal(false);
  readonly goalsFailed = signal(false);
  readonly goalDraft = signal('');
  readonly editingGoalId = signal<number | null>(null);
  readonly goalEditDraft = signal('');
  readonly goalSaving = signal(false);
  /** `coach.errors` altındaki anahtar. */
  readonly goalErrorKey = signal<string | null>(null);

  readonly notes = signal<CoachNote[]>([]);
  readonly notesFailed = signal(false);
  readonly noteDraft = signal('');
  readonly editingNoteId = signal<number | null>(null);
  readonly noteEditDraft = signal('');
  readonly noteSaving = signal(false);
  readonly noteErrorKey = signal<string | null>(null);

  readonly weeks = computed(() => {
    const current = this.overview()?.plan.weekStart;
    if (!current) return [];
    const format = new Intl.DateTimeFormat(this.localeService.localeDefinition().angularLocale, {
      day: 'numeric',
      month: 'long',
      timeZone: 'UTC',
    });
    return Array.from({ length: COACH_GOAL_WEEKS_AHEAD + 1 }, (_, i) => {
      const value = addDays(current, 7 * i);
      return { value, current: i === 0, label: format.format(new Date(`${value}T00:00:00Z`)) };
    });
  });

  readonly goalLimitReached = computed(() => this.goals().length >= COACH_GOALS_PER_WEEK);

  private readonly number = computed(
    () => new Intl.NumberFormat(this.localeService.localeDefinition().angularLocale, { maximumFractionDigits: 2 }),
  );
  private readonly dateTime = computed(
    () => new Intl.DateTimeFormat(this.localeService.localeDefinition().angularLocale, { dateStyle: 'medium', timeStyle: 'short' }),
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.notFound.set(false);
    this.coach
      .getOverview(this.studentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (overview) => {
          this.overview.set(overview);
          this.selectedWeek.set(overview.plan.weekStart);
          this.goals.set(overview.goals);
          this.loading.set(false);
          this.loadNotes();
        },
        error: (error: unknown) => {
          if (error instanceof HttpErrorResponse && error.status === 404) this.notFound.set(true);
          else this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }

  formatNumber(value: number): string {
    return this.number().format(value);
  }

  formatDateTime(value: string): string {
    return this.dateTime().format(new Date(value));
  }

  selectWeek(week: string): void {
    this.selectedWeek.set(week);
    this.editingGoalId.set(null);
    this.goalErrorKey.set(null);
    this.goalsLoading.set(true);
    this.goalsFailed.set(false);
    this.coach
      .listGoals(this.studentId, week)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (goals) => {
          // Yanıt gelmeden başka hafta seçildiyse eski hafta yazılmaz.
          if (this.selectedWeek() !== week) return;
          this.goals.set(goals);
          this.goalsLoading.set(false);
        },
        error: () => {
          if (this.selectedWeek() !== week) return;
          this.goals.set([]);
          this.goalsFailed.set(true);
          this.goalsLoading.set(false);
        },
      });
  }

  addGoal(): void {
    const title = this.goalDraft().trim();
    const weekStart = this.selectedWeek();
    if (!title || !weekStart || this.goalSaving()) return;
    this.goalSaving.set(true);
    this.goalErrorKey.set(null);
    this.coach
      .addGoal(this.studentId, { title, weekStart })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (goal) => {
          if (goal.weekStart === this.selectedWeek()) this.goals.update((goals) => [...goals, goal]);
          this.goalDraft.set('');
          this.goalSaving.set(false);
        },
        error: (error: unknown) => this.goalFailed(error),
      });
  }

  startGoalEdit(goal: CoachGoal): void {
    this.editingGoalId.set(goal.id);
    this.goalEditDraft.set(goal.title);
    this.goalErrorKey.set(null);
  }

  saveGoal(goal: CoachGoal): void {
    const title = this.goalEditDraft().trim();
    if (!title || this.goalSaving()) return;
    this.goalSaving.set(true);
    this.goalErrorKey.set(null);
    this.coach
      .updateGoal(this.studentId, goal.id, title)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (saved) => {
          this.goals.update((goals) => goals.map((g) => (g.id === saved.id ? saved : g)));
          this.editingGoalId.set(null);
          this.goalSaving.set(false);
        },
        error: (error: unknown) => this.goalFailed(error),
      });
  }

  deleteGoal(goal: CoachGoal): void {
    this.goalErrorKey.set(null);
    this.coach
      .deleteGoal(this.studentId, goal.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.goals.update((goals) => goals.filter((g) => g.id !== goal.id)),
        error: () => this.goalErrorKey.set('generic'),
      });
  }

  addNote(): void {
    const body = this.noteDraft().trim();
    if (!body || this.noteSaving()) return;
    this.noteSaving.set(true);
    this.noteErrorKey.set(null);
    this.coach
      .addNote(this.studentId, body)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (note) => {
          this.notes.update((notes) => [note, ...notes]);
          this.noteDraft.set('');
          this.noteSaving.set(false);
        },
        error: (error: unknown) => this.noteFailed(error),
      });
  }

  startNoteEdit(note: CoachNote): void {
    this.editingNoteId.set(note.id);
    this.noteEditDraft.set(note.body);
    this.noteErrorKey.set(null);
  }

  saveNote(note: CoachNote): void {
    const body = this.noteEditDraft().trim();
    if (!body || this.noteSaving()) return;
    this.noteSaving.set(true);
    this.noteErrorKey.set(null);
    this.coach
      .updateNote(this.studentId, note.id, body)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (saved) => {
          this.notes.update((notes) => notes.map((n) => (n.id === saved.id ? saved : n)));
          this.editingNoteId.set(null);
          this.noteSaving.set(false);
        },
        error: (error: unknown) => this.noteFailed(error),
      });
  }

  confirmNoteDelete(note: CoachNote): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          title: this.transloco.translate('coach.notes.deleteTitle'),
          message: this.transloco.translate('coach.notes.deleteText'),
          confirmText: this.transloco.translate('coach.notes.delete'),
          icon: 'delete',
          confirmColor: 'warn',
        },
      })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((confirmed) => {
        if (confirmed) this.deleteNote(note);
      });
  }

  private deleteNote(note: CoachNote): void {
    this.noteErrorKey.set(null);
    this.coach
      .deleteNote(this.studentId, note.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.notes.update((notes) => notes.filter((n) => n.id !== note.id)),
        error: () => this.noteErrorKey.set('generic'),
      });
  }

  private loadNotes(): void {
    this.notesFailed.set(false);
    this.coach
      .listNotes(this.studentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (notes) => this.notes.set(notes),
        error: () => this.notesFailed.set(true),
      });
  }

  private goalFailed(error: unknown): void {
    this.goalSaving.set(false);
    this.goalErrorKey.set(errorKey(error));
  }

  private noteFailed(error: unknown): void {
    this.noteSaving.set(false);
    this.noteErrorKey.set(errorKey(error));
  }
}
