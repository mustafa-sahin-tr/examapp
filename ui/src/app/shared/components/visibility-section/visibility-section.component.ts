import { Component, EventEmitter, Input, OnDestroy, Output, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { MatRadioModule } from '@angular/material/radio';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
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
  imports: [CommonModule, MatRadioModule, MatSlideToggleModule, MatSelectModule, MatFormFieldModule, MatIconModule],
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
  readonly isMobile = signal(false);
  private readonly breakpointSub: Subscription;

  readonly teacherSharingOptions: { value: WorksheetTeacherSharing; label: string; description: string }[] = [
    {
      value: WorksheetTeacherSharing.Private,
      label: 'Özel',
      description: 'Yalnızca siz ve admin görür/düzenler/atar.',
    },
    {
      value: WorksheetTeacherSharing.PublicView,
      label: 'Herkese Açık (Görüntüleme)',
      description: 'Tüm öğretmenler görüntüler. Atamak için sizden onay gerekir.',
    },
    {
      value: WorksheetTeacherSharing.PublicAssignable,
      label: 'Herkese Açık (Atanabilir)',
      description: 'Tüm öğretmenler görüntüler ve onaysız kendi öğrencilerine atayabilir.',
    },
  ];

  readonly selectedTeacherSharingOption = computed(
    () => this.teacherSharingOptions.find((option) => option.value === this._teacherSharing())
  );

  readonly summary = computed(() => {
    const teacherText = this.teacherSharingSummary(this._teacherSharing());
    const studentText = this.studentVisibilitySummary(this._studentVisibility());
    return `Şu an: ${teacherText} ${studentText}`;
  });

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

  private teacherSharingSummary(value: WorksheetTeacherSharing): string {
    switch (value) {
      case WorksheetTeacherSharing.PublicView:
        return 'Tüm öğretmenler görüntüleyebilir, atamak için onayınız gerekir.';
      case WorksheetTeacherSharing.PublicAssignable:
        return 'Tüm öğretmenler görüntüleyebilir ve onaysız atayabilir.';
      case WorksheetTeacherSharing.Private:
      default:
        return 'Yalnızca siz ve admin görebilir/düzenleyebilir/atayabilirsiniz.';
    }
  }

  private studentVisibilitySummary(value: WorksheetStudentVisibility): string {
    return value === WorksheetStudentVisibility.Restricted
      ? 'Öğrenciler yalnızca atandığında görebilir.'
      : 'Öğrenciler keşfet listesinde görebilir.';
  }
}
