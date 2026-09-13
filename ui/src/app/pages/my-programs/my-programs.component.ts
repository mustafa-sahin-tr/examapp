import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { combineLatest, take } from 'rxjs';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { ProgramService } from '../../services/program.service';
import { UserProgram } from '../../models/program.interfaces';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { MY_PROGRAMS_SCOPE } from './my-programs-scope';

/** Sözlükte karşılığı olan program türleri; bunun dışındaki değer backend'den geldiği gibi gösterilir. */
const KNOWN_STUDY_TYPES = ['intensive', 'regular', 'flexible', 'weekend', 'exam', 'question'];

type ProgramFilter = 'all' | 'active' | 'completed';

@Component({
  selector: 'app-my-programs',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    MatDividerModule,
    MatMenuModule,
    MatDialogModule,
    MatSnackBarModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(MY_PROGRAMS_SCOPE)],
  templateUrl: './my-programs.component.html',
  styleUrls: ['./my-programs.component.scss'],
})
export class MyProgramsComponent implements OnInit {
  private programService = inject(ProgramService);
  private router = inject(Router);
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  programs: UserProgram[] = [];
  filteredPrograms: UserProgram[] = [];
  loading = true;
  loadError = false;
  selectedFilter: ProgramFilter = 'all';

  ngOnInit(): void {
    this.loadMyPrograms();
  }

  loadMyPrograms(): void {
    this.loading = true;
    this.loadError = false;
    this.programService.getMyPrograms().subscribe({
      next: (programs) => {
        this.programs = programs;
        this.applyFilter(); // Apply current filter after loading
        this.loading = false;
      },
      error: () => {
        this.programs = [];
        this.filteredPrograms = [];
        this.loadError = true;
        this.loading = false;
      },
    });
  }

  // Filter functionality
  setFilter(filter: ProgramFilter): void {
    this.selectedFilter = filter;
    this.applyFilter();
  }

  private applyFilter(): void {
    switch (this.selectedFilter) {
      case 'active':
        this.filteredPrograms = this.programs.filter((program) => this.isActive(program));
        break;
      case 'completed':
        this.filteredPrograms = this.programs.filter((program) => this.isCompleted(program));
        break;
      default:
        this.filteredPrograms = [...this.programs];
        break;
    }
  }

  isFilterSelected(filter: ProgramFilter): boolean {
    return this.selectedFilter === filter;
  }

  getFilterCount(filter: ProgramFilter): number {
    switch (filter) {
      case 'active':
        return this.getActivePrograms();
      case 'completed':
        return this.getCompletedPrograms();
      default:
        return this.programs.length;
    }
  }

  createNewProgram(): void {
    this.router.navigate(['/program-create']);
  }

  continueProgram(program: UserProgram): void {
    this.router.navigate(['/programs', program.id, 'detail']);
  }

  viewProgramDetails(program: UserProgram): void {
    this.router.navigate(['/programs', program.id, 'detail']);
  }

  /** Backend'in hesapladığı 0-100 arası ilerleme yüzdesi. */
  getProgressPercentage(program: UserProgram): number {
    return Math.min(100, Math.max(0, program.progressPercentage ?? 0));
  }

  getActivePrograms(): number {
    return this.programs.filter((program) => this.isActive(program)).length;
  }

  getCompletedPrograms(): number {
    return this.programs.filter((program) => this.isCompleted(program)).length;
  }

  isActive(program: UserProgram): boolean {
    const progress = this.getProgressPercentage(program);
    return progress > 0 && progress < 100;
  }

  isCompleted(program: UserProgram): boolean {
    return this.getProgressPercentage(program) >= 100;
  }

  getStatusIcon(program: UserProgram): string {
    if (this.isCompleted(program)) {
      return 'check_circle';
    } else if (this.isActive(program)) {
      return 'play_circle';
    } else {
      return 'pause_circle';
    }
  }

  /** Durum rozetinin çeviri anahtarı (`my-programs.list` önekine göreli). */
  getStatusTextKey(program: UserProgram): string {
    if (this.isCompleted(program)) {
      return 'status.completed';
    } else if (this.isActive(program)) {
      return 'status.active';
    }
    return 'status.notStarted';
  }

  /**
   * Program türünün okunur adı. Backend kod döndürür (`intensive`, `question`…); sözlükte
   * karşılığı yoksa ham değer gösterilir ki yeni bir tür sessizce kaybolmasın.
   */
  studyTypeLabel(studyType: string): string {
    const code = (studyType ?? '').toLowerCase();
    if (!KNOWN_STUDY_TYPES.includes(code)) {
      return studyType ?? '';
    }
    return this.transloco.translate<string>(`${MY_PROGRAMS_SCOPE}.studyType.${code}`) ?? studyType;
  }

  getProgramIcon(studyType: string): string {
    const icons: Record<string, string> = {
      intensive: 'flash_on',
      regular: 'schedule',
      flexible: 'tune',
      weekend: 'weekend',
      exam: 'quiz',
    };
    return icons[(studyType ?? '').toLowerCase()] || 'assignment';
  }

  editProgram(program: UserProgram): void {
    this.router.navigate(['/programs/edit', program.id]);
  }

  deleteProgram(program: UserProgram): void {
    const data: ConfirmDialogData = {
      title: this.text('list.deleteDialog.title'),
      message: this.text('list.deleteDialog.message', { name: program.programName }),
      confirmText: this.text('list.deleteDialog.confirm'),
      cancelText: this.text('list.deleteDialog.cancel'),
      icon: 'delete_forever',
      confirmColor: 'warn',
    };

    this.dialog
      .open(ConfirmDialogComponent, { width: '480px', maxWidth: '90vw', data })
      .afterClosed()
      .subscribe((confirmed: boolean) => {
        if (!confirmed) return;

        this.programService.deleteProgram(program.id).subscribe({
          next: () => {
            this.programs = this.programs.filter((p) => p.id !== program.id);
            this.applyFilter();
            this.notify('list.deleted');
          },
          error: () => {
            this.notify('list.deleteFailed');
          },
        });
      });
  }

  /** Sözlükten senkron metin; sayfa şablonu render olduğunda scope yüklüdür. */
  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${MY_PROGRAMS_SCOPE}.${key}`, params) ?? '';
  }

  /**
   * Snackbar metni ve aksiyon etiketi şablon dışında üretildiği için ikisi de `selectTranslate`
   * ile okunur; bu çağrı scope sözlüğünü yükler ve dil değişiminde doğru metni verir.
   */
  private notify(messageKey: string): void {
    combineLatest([
      this.transloco.selectTranslate<string>(messageKey, {}, MY_PROGRAMS_SCOPE),
      this.transloco.selectTranslate<string>('actions.ok', {}, MY_PROGRAMS_SCOPE),
    ])
      .pipe(take(1), takeUntilDestroyed(this.destroyRef))
      .subscribe(([message, action]) => {
        this.snackBar.open(message, action, { duration: 3000 });
      });
  }
}
