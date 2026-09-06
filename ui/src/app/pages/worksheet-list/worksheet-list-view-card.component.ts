import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { Router, RouterModule } from '@angular/router';
import { InstanceSummary, Test, WorksheetTeacherSharing } from '../../models/test-instance';
import { AssignedWorksheet } from '../../models/assignment';
import { SharingBadgeComponent } from '../../shared/components/sharing-badge/sharing-badge.component';

type CardStatus = 'none' | 'inprogress' | 'completed';

@Component({
  selector: 'app-worksheet-list-view-card',
  standalone: true,
  imports: [CommonModule, RouterModule, MatIconModule, MatMenuModule, MatButtonModule, SharingBadgeComponent],
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
  /** Kopyalama isteği uçuşta — kopyala aksiyonu devre dışı (issue #16). */
  @Input() copying = false;

  @Output() deleteWorksheet = new EventEmitter<number>();
  @Output() assignWorksheet = new EventEmitter<number>();
  @Output() copy = new EventEmitter<number>();

  private readonly router = inject(Router);
  private readonly images = ['honey-back.png', 'rect-back.png', 'triangle-back.png', 'diamond-back.png'];

  get coverUrl(): string {
    if (this.course.imageUrl) {
      return this.course.imageUrl;
    }
    const id = Math.abs(this.course.id ?? 0);
    return `/${this.images[id % this.images.length]}`;
  }

  // NOT: `computed()` yerine getter — `course` klasik `@Input` (signal input değil).
  // Saf alan okuması olduğu için change-detection'da maliyeti yok sayılır.
  /** Backend yetki alanı; alan yoksa (eski response) düzenlemeye izin ver. */
  get canEdit(): boolean {
    return this.course.canEdit !== false;
  }

  /** Backend yetki alanı; alan yoksa (eski response) atamaya izin ver. */
  get canAssign(): boolean {
    return this.course.canAssign !== false;
  }

  /** Yalnız istek sahibi admin ise dolu gelir. */
  get createdByName(): string | null {
    return this.course.createdByName ?? null;
  }

  /** Diğer öğretmenin paylaştığı satır mı (issue #11 — "Başkalarının sınavları"). */
  get isShared(): boolean {
    return this.isTeacher && this.course.isOwner === false && this.teacherSharing !== WorksheetTeacherSharing.Private;
  }

  get teacherSharing(): WorksheetTeacherSharing {
    return this.course.teacherSharing ?? WorksheetTeacherSharing.PublicView;
  }

  /** Başkasının public sınavı — kendi hesabına kopyalanabilir (issue #16). */
  get canCopy(): boolean {
    return (
      this.isTeacher &&
      this.course.canEdit === false &&
      this.course.isOwner === false &&
      (this.teacherSharing === WorksheetTeacherSharing.PublicView ||
        this.teacherSharing === WorksheetTeacherSharing.PublicAssignable)
    );
  }

  get ownerName(): string | null {
    return this.course.ownerName ?? null;
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

  /** `assignment` input'u geçilmiş ve tamamlanmamış mı — DTO'daki `Test.isAssigned` ile karıştırılmamalı. */
  get hasActiveAssignmentInput(): boolean {
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

  emitCopy(event: Event): void {
    event.stopPropagation();
    if (this.course.id != null) {
      this.copy.emit(this.course.id);
    }
  }
}
