import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
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
  loading = true;

  ngOnInit(): void {
    this.loadMyPrograms();
  }

  private async loadMyPrograms(): Promise<void> {
    try {
      this.loading = true;
      this.programService.getMyPrograms().subscribe({
        next: (programs) => {
          this.programs = programs;
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
}
