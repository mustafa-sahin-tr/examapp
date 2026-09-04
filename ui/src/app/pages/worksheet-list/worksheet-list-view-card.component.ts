import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { Router, RouterModule } from '@angular/router';
import { InstanceSummary, Test } from '../../models/test-instance';
import { AssignedWorksheet } from '../../models/assignment';

type CardStatus = 'none' | 'inprogress' | 'completed';

@Component({
  selector: 'app-worksheet-list-view-card',
  standalone: true,
  imports: [CommonModule, RouterModule, MatIconModule, MatMenuModule, MatButtonModule],
  templateUrl: './worksheet-list-view-card.component.html',
  styleUrl: './worksheet-list-view-card.component.scss',
})
export class WorksheetListViewCardComponent {
  @Input({ required: true }) course!: Test;
  @Input() subjectName = '';
  @Input() gradeName = '';
  @Input() assignment: AssignedWorksheet | null = null;
  @Input() viewMode: 'grid' | 'list' = 'grid';
  @Input() isTeacher = false;

  @Output() deleteWorksheet = new EventEmitter<number>();
  @Output() assignWorksheet = new EventEmitter<number>();

  private readonly router = inject(Router);
  private readonly images = ['honey-back.png', 'rect-back.png', 'triangle-back.png', 'diamond-back.png'];

  get coverUrl(): string {
    if (this.course.imageUrl) {
      return this.course.imageUrl;
    }
    const id = Math.abs(this.course.id ?? 0);
    return `/${this.images[id % this.images.length]}`;
  }

  get instance(): InstanceSummary | null {
    return this.course.instance ?? null;
  }

  get status(): CardStatus {
    const s = this.instance?.status;
    if (s === 1) {
      return 'completed';
    }
    if (s === 0) {
      return 'inprogress';
    }
    return 'none';
  }

  get subtitle(): string {
    return [this.subjectName, this.gradeName].filter((v) => !!v).join(' · ');
  }

  get durationMinutes(): number {
    return Math.round((this.course.maxDurationSeconds ?? 0) / 60);
  }

  get answered(): number {
    if (!this.instance) {
      return 0;
    }
    return (this.instance.correctAnswers ?? 0) + (this.instance.wrongAnswers ?? 0);
  }

  get progressPercent(): number {
    const total = this.instance?.totalQuestions || this.course.questionCount || 0;
    if (!total) {
      return 0;
    }
    return Math.min(100, Math.round((this.answered / total) * 100));
  }

  get scorePercent(): number {
    return Math.max(0, Math.min(100, Math.round(this.instance?.score ?? 0)));
  }

  get isAssigned(): boolean {
    return !!this.assignment && !this.assignment.isCompleted;
  }

  get dueLabel(): string | null {
    const endAt = this.assignment?.endAt;
    if (!endAt) {
      return null;
    }
    const diff = new Date(endAt).getTime() - Date.now();
    if (diff <= 0) {
      return 'Süre doldu';
    }
    const days = Math.ceil(diff / (1000 * 60 * 60 * 24));
    if (days <= 1) {
      return 'Bugün son gün';
    }
    return `${days} gün kaldı`;
  }

  get primaryLabel(): string {
    if (this.isTeacher) {
      return 'Düzenle';
    }
    switch (this.status) {
      case 'inprogress':
        return 'Devam Et';
      case 'completed':
        return 'Sonucu Gör';
      default:
        return this.course.isPracticeTest ? 'Çalışmaya Başla' : 'Başla';
    }
  }

  openPrimary(event: Event): void {
    event.stopPropagation();
    if (this.isTeacher) {
      this.router.navigate(['/exam', this.course.id]);
      return;
    }
    this.router.navigate(['/test', this.course.id]);
  }

  openDetail(): void {
    this.router.navigate(['/test', this.course.id]);
  }

  openQuestionCanvas(): void {
    this.router.navigate(['/questioncanvas', this.course.id]);
  }

  emitDelete(): void {
    if (this.course.id != null) {
      this.deleteWorksheet.emit(this.course.id);
    }
  }

  emitAssign(): void {
    if (this.course.id != null) {
      this.assignWorksheet.emit(this.course.id);
    }
  }
}
