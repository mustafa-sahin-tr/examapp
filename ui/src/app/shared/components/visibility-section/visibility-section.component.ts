import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnDestroy,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
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
import { AuthService } from '../../../services/auth.service';

export interface VisibilityChange {
  teacherSharing: WorksheetTeacherSharing;
  studentVisibility: WorksheetStudentVisibility;
}

interface TeacherSharingOption {
  value: WorksheetTeacherSharing;
  key: string;
}

@Component({
  selector: 'app-visibility-section',
  standalone: true,
  templateUrl: './visibility-section.component.html',
  styleUrl: './visibility-section.component.scss',
  // Tüm durum signal/input üzerinden; OnPush güvenli.
  changeDetection: ChangeDetectionStrategy.OnPush,
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
  private readonly authService = inject(AuthService);
  readonly isMobile = signal(false);
  private readonly breakpointSub: Subscription;

  /**
   * Oturumdaki kullanıcının okulu (issue #191). Okulsuz kullanıcı "Sadece okulum" seçemez;
   * backend de 400 ile reddeder. `AuthService.user` signal'ından türetilir: `schoolId` yalnızca
   * exam-api refresh ile (asenkron) geldiği için sonradan dolarsa seçenek kendiliğinden belirir.
   */
  readonly userSchoolId = computed(() => AuthService.schoolIdOf(this.authService.user()));
  readonly hasSchool = computed(() => this.userSchoolId() !== null);

  /**
   * Admin muafiyeti: backend kararı SAHİBİN okuluna göre verir; admin (okulsuz) okullu bir öğretmenin
   * sınavını düzenlerken seçenek gizlenmez. Yetkiyi backend belirler, 400 gelirse snackbar gösterilir.
   */
  readonly isAdmin = this.authService.hasRole('Admin');
  readonly canChooseSchoolOnly = computed(() => this.isAdmin || this.hasSchool());

  /** Etiket/açıklama metinleri `shared.visibilitySection.*` altından çözülür (issue #183). */
  readonly teacherSharingOptions: TeacherSharingOption[] = [
    { value: WorksheetTeacherSharing.Private, key: 'private' },
    { value: WorksheetTeacherSharing.PublicView, key: 'publicView' },
    { value: WorksheetTeacherSharing.PublicAssignable, key: 'publicAssignable' },
    { value: WorksheetTeacherSharing.SchoolOnly, key: 'schoolOnly' },
  ];

  /**
   * Gösterilecek seçenekler: SchoolOnly yalnızca okulu olan kullanıcıya (veya admine) sunulur.
   * Kenar durumu: değer zaten SchoolOnly iken sahip okulsuz kaldıysa seçenek görünür ama devre dışıdır,
   * böylece mevcut durum gizlenmez ve kullanıcı başka bir seçeneğe geçebilir.
   */
  readonly visibleTeacherSharingOptions = computed(() =>
    this.teacherSharingOptions.filter(
      (option) =>
        option.value !== WorksheetTeacherSharing.SchoolOnly ||
        this.canChooseSchoolOnly() ||
        this._teacherSharing() === WorksheetTeacherSharing.SchoolOnly
    )
  );

  /** Okulsuz sahibin elindeki SchoolOnly değeri: seçenek devre dışı, altında kısa not gösterilir. */
  readonly schoolOnlyUnavailable = computed(
    () => !this.canChooseSchoolOnly() && this._teacherSharing() === WorksheetTeacherSharing.SchoolOnly
  );

  /** "unavailable" notunun id'si — radio'ya `aria-describedby` ile bağlanır. */
  readonly schoolOnlyNoteId = 'vs-school-only-note';

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

  /** SchoolOnly seçeneği yalnızca okulu olmayan (admin olmayan) kullanıcı için devre dışıdır. */
  isOptionDisabled(value: WorksheetTeacherSharing): boolean {
    return value === WorksheetTeacherSharing.SchoolOnly && !this.canChooseSchoolOnly();
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
      case WorksheetTeacherSharing.SchoolOnly:
        return this.t('shared.visibilitySection.summaryTeacher.schoolOnly');
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
