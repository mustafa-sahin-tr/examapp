import { CommonModule } from '@angular/common';
import { Component, ElementRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';
import { WorksheetCardComponent } from '../worksheet-card/worksheet-card.component';
import { TestService } from '../../services/test.service';
import { AssignedWorksheet } from '../../models/assignment';
import { Test } from '../../models/test-instance';

interface AssignmentCardViewModel {
  assignment: AssignedWorksheet;
  test: Test;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, SectionHeaderComponent, WorksheetCardComponent],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss'],
})
export class DashboardComponent implements OnInit {
  private readonly testService = inject(TestService);
  private readonly router = inject(Router);

  private readonly assignments = signal<AssignmentCardViewModel[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly assignmentCards = computed(() => this.assignments());

  readonly scrollDistance = 600;

  @ViewChild('assignmentContainer', { static: false }) assignmentContainer?: ElementRef<HTMLDivElement>;

  ngOnInit(): void {
    this.testService.getActiveAssignments().subscribe({
      next: (assignments) => {
        const viewModels = assignments.map((assignment) => ({
          assignment,
          test: this.mapToTest(assignment),
        }));
        this.assignments.set(viewModels);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Atanmış testler alınırken bir sorun oluştu.');
        this.loading.set(false);
      },
    });
  }

  trackAssignment(index: number, item: AssignmentCardViewModel): number {
    return item.assignment.assignmentId;
  }

  onCardClick(assignment: AssignmentCardViewModel): void {
    this.router.navigate(['/test', assignment.assignment.worksheetId]);
  }

  handleLeftNavigation(): void {
    if (!this.assignmentContainer) {
      return;
    }
    const container = this.assignmentContainer.nativeElement;
    container.scrollBy({ left: -this.scrollDistance, behavior: 'smooth' });
  }

  handleRightNavigation(): void {
    if (!this.assignmentContainer) {
      return;
    }
    const container = this.assignmentContainer.nativeElement;
    container.scrollBy({ left: this.scrollDistance, behavior: 'smooth' });
  }

  getDueLabel(assignment: AssignedWorksheet): string | null {
    if (!assignment.endAt) {
      return null;
    }
    const dueDate = new Date(assignment.endAt);
    return `Bitiş: ${dueDate.toLocaleDateString()}`;
  }

  private mapToTest(assignment: AssignedWorksheet): Test {
    return {
      id: assignment.worksheetId,
      name: assignment.name,
      description: assignment.description,
      gradeId: assignment.gradeId,
      maxDurationSeconds: assignment.maxDurationSeconds,
      isPracticeTest: assignment.isPracticeTest,
      imageUrl: assignment.imageUrl ?? undefined,
      subtitle: assignment.subtitle ?? undefined,
      badgeText: assignment.badgeText ?? undefined,
      bookId: assignment.bookId ?? undefined,
      bookTestId: assignment.bookTestId ?? undefined,
      questionCount: assignment.questionCount,
      subjectId: assignment.subjectId ?? undefined,
      topicId: assignment.topicId ?? undefined,
      subtopicId: assignment.subTopicId ?? undefined,
    };
  }
}
