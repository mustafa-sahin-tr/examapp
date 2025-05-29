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
import { Router } from '@angular/router';
import { ProgramService } from '../../services/program.service';
import { UserProgram } from '../../models/program.interfaces';

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
  ],
  templateUrl: './my-programs.component.html',
  styleUrls: ['./my-programs.component.scss'],
})
export class MyProgramsComponent implements OnInit {
  private programService = inject(ProgramService);
  private router = inject(Router);

  programs: UserProgram[] = [];
  filteredPrograms: UserProgram[] = [];
  loading = true;
  selectedFilter: 'all' | 'active' | 'completed' = 'all';

  ngOnInit(): void {
    this.loadMyPrograms();
  }

  private async loadMyPrograms(): Promise<void> {
    try {
      this.loading = true;
      this.programService.getMyPrograms().subscribe({
        next: (programs) => {
          this.programs = programs;
          this.applyFilter(); // Apply current filter after loading
          this.loading = false;
        },
        error: (error) => {
          console.error('Programs could not be loaded:', error);
          this.loading = false;
        },
      });
    } catch (error) {
      console.error('Programs could not be loaded:', error);
      // Here you might want to show a snackbar or error message
      this.loading = false;
    }
  }

  // Filter functionality
  setFilter(filter: 'all' | 'active' | 'completed'): void {
    this.selectedFilter = filter;
    this.applyFilter();
  }

  private applyFilter(): void {
    switch (this.selectedFilter) {
      case 'active':
        this.filteredPrograms = this.programs.filter(program => this.isActive(program));
        break;
      case 'completed':
        this.filteredPrograms = this.programs.filter(program => this.isCompleted(program));
        break;
      default:
        this.filteredPrograms = [...this.programs];
        break;
    }
  }

  isFilterSelected(filter: 'all' | 'active' | 'completed'): boolean {
    return this.selectedFilter === filter;
  }

  getFilterLabel(filter: 'all' | 'active' | 'completed'): string {
    const labels = {
      all: 'Tümü',
      active: 'Aktif',
      completed: 'Tamamlanan'
    };
    return labels[filter];
  }

  getFilterCount(filter: 'all' | 'active' | 'completed'): number {
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
    // Navigate to program execution/study page
    // This will be implemented later when we have the study interface
    console.log('Continue program:', program);
  }

  viewProgramDetails(program: UserProgram): void {
    // Navigate to program details page
    // This will be implemented later
    console.log('View program details:', program);
  }

  formatDate(dateString: string): string {
    const date = new Date(dateString);
    return date.toLocaleDateString('tr-TR', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  }

  getProgressPercentage(program: UserProgram): number {
    // Calculate progress percentage based on completed days
    // This is a mock calculation - in real implementation,
    // you'd track actual completion status
    const completedDays = this.getCompletedDays(program);
    const studyDuration = program.studyDuration ? parseInt(program.studyDuration, 10) : 30;
    return Math.round((completedDays / studyDuration) * 100);
  }

  getCompletedDays(program: UserProgram): number {
    // Mock calculation - in real implementation,
    // this would come from actual progress tracking
    const daysSinceCreated = Math.floor((Date.now() - new Date(program.createdDate).getTime()) / (1000 * 60 * 60 * 24));
    const studyDuration = program.studyDuration ? parseInt(program.studyDuration, 10) : 30;
    return Math.min(daysSinceCreated, studyDuration);
  }

  // Yeni metodlar - Enhanced UI için
  getActivePrograms(): number {
    return this.programs.filter(program => this.isActive(program)).length;
  }

  getCompletedPrograms(): number {
    return this.programs.filter(program => this.isCompleted(program)).length;
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
    const icons: { [key: string]: string } = {
      'intensive': 'flash_on',
      'regular': 'schedule',
      'flexible': 'tune',
      'weekend': 'weekend',
      'exam': 'quiz'
    };
    return icons[studyType.toLowerCase()] || 'assignment';
  }

  getRemainingDays(program: UserProgram): number {
    const studyDuration = program.studyDuration ? parseInt(program.studyDuration, 10) : 30;
    const completedDays = this.getCompletedDays(program);
    return Math.max(0, studyDuration - completedDays);
  }

  // Yeni aksiyon metodları
  editProgram(program: UserProgram): void {
    this.router.navigate(['/programs/edit', program.id]);
  }

  duplicateProgram(program: UserProgram): void {
    // Program kopyalama işlemi
    console.log('Duplicating program:', program);
    // TODO: Implement program duplication
  }

  deleteProgram(program: UserProgram): void {
    if (confirm('Bu programı silmek istediğinizden emin misiniz?')) {
      // Program silme işlemi
      console.log('Deleting program:', program);
      // TODO: Implement program deletion
    }
  }
}
