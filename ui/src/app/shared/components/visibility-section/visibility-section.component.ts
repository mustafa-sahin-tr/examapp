import { Component, EventEmitter, Input, OnDestroy, Output, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { MatRadioModule } from '@angular/material/radio';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Subscription } from 'rxjs';
import { WorksheetStudentVisibility, WorksheetTeacherSharing } from '../../../models/test-instance';

export interface VisibilityChange {
  teacherSharing: WorksheetTeacherSharing;
  studentVisibility: WorksheetStudentVisibility;
}

@Component({
  selector: 'app-visibility-section',
  standalone: true,
  templateUrl: './visibility-section.component.html',
  styleUrl: './visibility-section.component.scss',
  imports: [
    CommonModule,
    MatRadioModule,
    MatSlideToggleModule,
    MatSelectModule,
    MatFormFieldModule,
    MatIconModule,
    TranslocoDirective,
  ],
})
export class VisibilitySectionComponent implements OnDestroy {
  readonly WorksheetTeacherSharing = WorksheetTeacherSharing;
  readonly WorksheetStudentVisibility = WorksheetStudentVisibility;

  @Input() set teacherSharing(value: WorksheetTeacherSharing | undefined) {
    this._teacherSharing.set(value ?? WorksheetTeacherSharing.Private);
  }
  get teacherSharing(): WorksheetTeacherSharing {
    return this._teacherSharing();
  }

  @Input() set studentVisibility(value: WorksheetStudentVisibility | undefined) {
    this._studentVisibility.set(value ?? WorksheetStudentVisibility.Normal);
  }
  get studentVisibility(): WorksheetStudentVisibility {
    return this._studentVisibility();
  }

  @Input() disabled = false;

  @Output() visibilityChange = new EventEmitter<VisibilityChange>();

  private readonly _teacherSharing = signal<WorksheetTeacherSharing>(WorksheetTeacherSharing.Private);
  private readonly _studentVisibility = signal<WorksheetStudentVisibility>(WorksheetStudentVisibility.Normal);

  private readonly breakpointObserver = inject(BreakpointObserver);
  private readonly transloco = inject(TranslocoService);
  readonly isMobile = signal(false);
  private readonly breakpointSub: Subscription;

  /** Etiket/açıklama metinleri `shared.visibilitySection.*` altından çözülür (issue #183). */
  readonly teacherSharingOptions: { value: WorksheetTeacherSharing; key: string }[] = [
    { value: WorksheetTeacherSharing.Private, key: 'private' },
    { value: WorksheetTeacherSharing.PublicView, key: 'publicView' },
    { value: WorksheetTeacherSharing.PublicAssignable, key: 'publicAssignable' },
  ];

  readonly selectedTeacherSharingOption = computed(
    () => this.teacherSharingOptions.find((option) => option.value === this._teacherSharing())
  );

  readonly summary = computed(() =>
    this.t('shared.visibilitySection.summary', {
      teacher: this.teacherSharingSummary(this._teacherSharing()),
      student: this.studentVisibilitySummary(this._studentVisibility()),
    })
  );

  constructor() {
    this.breakpointSub = this.breakpointObserver.observe([Breakpoints.Handset]).subscribe((state) => {
      this.isMobile.set(state.matches);
    });
  }

  ngOnDestroy(): void {
    this.breakpointSub.unsubscribe();
  }

  onTeacherSharingChange(value: WorksheetTeacherSharing): void {
    this._teacherSharing.set(value);
    this.emitChange();
  }

  onStudentVisibilityToggle(checked: boolean): void {
    this._studentVisibility.set(checked ? WorksheetStudentVisibility.Restricted : WorksheetStudentVisibility.Normal);
    this.emitChange();
  }

  private emitChange(): void {
    this.visibilityChange.emit({
      teacherSharing: this._teacherSharing(),
      studentVisibility: this._studentVisibility(),
    });
  }

  /** Seçilen seçeneğin çevrilmiş etiketi/açıklaması (şablon için). */
  optionLabel(key: string): string {
    return this.t(`shared.visibilitySection.teacherSharing.${key}.label`);
  }

  optionDescription(key: string): string {
    return this.t(`shared.visibilitySection.teacherSharing.${key}.description`);
  }

  studentVisibilityDescription(): string {
    return this._studentVisibility() === WorksheetStudentVisibility.Restricted
      ? this.t('shared.visibilitySection.studentVisibility.restricted')
      : this.t('shared.visibilitySection.studentVisibility.normal');
  }

  private teacherSharingSummary(value: WorksheetTeacherSharing): string {
    switch (value) {
      case WorksheetTeacherSharing.PublicView:
        return this.t('shared.visibilitySection.summaryTeacher.publicView');
      case WorksheetTeacherSharing.PublicAssignable:
        return this.t('shared.visibilitySection.summaryTeacher.publicAssignable');
      case WorksheetTeacherSharing.Private:
      default:
        return this.t('shared.visibilitySection.summaryTeacher.private');
    }
  }

  private studentVisibilitySummary(value: WorksheetStudentVisibility): string {
    return value === WorksheetStudentVisibility.Restricted
      ? this.t('shared.visibilitySection.summaryStudent.restricted')
      : this.t('shared.visibilitySection.summaryStudent.normal');
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(key, params) ?? '';
  }
}
