import { Component, OnInit, inject } from '@angular/core';
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
  ],
  templateUrl: './my-programs.component.html',
  styleUrls: ['./my-programs.component.scss'],
})
export class MyProgramsComponent implements OnInit {
  private programService = inject(ProgramService);
  private router = inject(Router);
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);

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

  getFilterLabel(filter: ProgramFilter): string {
    const labels: Record<ProgramFilter, string> = {
      all: 'Tümü',
      active: 'Aktif',
      completed: 'Tamamlanan',
    };
    return labels[filter];
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

  formatDate(dateString: string): string {
    const date = new Date(dateString);
    return date.toLocaleDateString('tr-TR', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  }

  /** Backend'in hesapladığı 0-100 arası ilerleme yüzdesi. */
  getProgressPercentage(program: UserProgram): number {
    return Math.min(100, Math.max(0, program.progressPercentage ?? 0));
  }

  /** "3/10 sayfa tamamlandı" biçiminde gerçek sayfa ilerlemesi. */
  getPageProgressText(program: UserProgram): string {
    const completed = program.completedPageCount ?? 0;
    const total = program.totalPageCount ?? 0;
    return `${completed}/${total} sayfa tamamlandı`;
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

  getStatusText(program: UserProgram): string {
    if (this.isCompleted(program)) {
      return 'Tamamlandı';
    } else if (this.isActive(program)) {
      return 'Devam Ediyor';
    } else {
      return 'Başlanmadı';
    }
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
      title: 'Programı Sil',
      message: `"${program.programName}" programını silmek istediğinize emin misiniz? Bu işlem geri alınamaz.`,
      confirmText: 'Evet, Sil',
      cancelText: 'İptal',
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
            this.snackBar.open('Program silindi.', 'Tamam', { duration: 3000 });
          },
          error: () => {
            this.snackBar.open('Program silinemedi. Lütfen tekrar deneyin.', 'Tamam', { duration: 3000 });
          },
        });
      });
  }
}
