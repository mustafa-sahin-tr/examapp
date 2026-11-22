import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter, finalize, take } from 'rxjs';
import { FormsModule, NgForm } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Grade } from '../../models/student';
import {
  ANNOUNCEMENT_BODY_MAX,
  ANNOUNCEMENT_TITLE_MAX,
  AnnouncementOptions,
  AnnouncementSendRequest,
  AnnouncementService,
  AnnouncementTarget,
  SentAnnouncement,
} from '../../services/announcement.service';
import { GradesService } from '../../services/grades.service';
import { LocaleService } from '../../services/locale.service';
import { SchoolSection, SchoolSectionService, SchoolSectionStudent } from '../../services/school-section.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { backendMessage } from '../../shared/utils/admin-action-error.util';

export const ANNOUNCEMENTS_SCOPE = 'announcements';

/** Seçili öğrenci hedefinde bir şubeden yüklenen en çok öğrenci (backend sınırı 200). */
const SECTION_STUDENT_PAGE = 100;

/**
 * Issue #1281 (#1071 S6): okul duyuruları. Okulun öğretmeni şubesine ya da şubeden seçtiği öğrencilere, okul yöneticisi ayrıca
 * okulun tamamına ya da bir sınıfa duyuru gönderir; öğrenciler, velileri ve (okul genelinde) öğretmenler uygulama içi bildirim
 * alır. Altta gönderilen duyurular alıcı sayılarıyla listelenir. Yetki ve alıcılar sunucuda.
 */
@Component({
  selector: 'app-announcements',
  standalone: true,
  imports: [
    FormsModule,
    PageHeaderComponent,
    MatButtonModule,
    MatButtonToggleModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(ANNOUNCEMENTS_SCOPE)],
  templateUrl: './announcements.component.html',
  styleUrl: './announcements.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AnnouncementsComponent implements OnInit {
  private readonly service = inject(AnnouncementService);
  private readonly sectionService = inject(SchoolSectionService);
  private readonly gradesService = inject(GradesService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly form = viewChild(NgForm);

  protected readonly titleMax = ANNOUNCEMENT_TITLE_MAX;
  protected readonly bodyMax = ANNOUNCEMENT_BODY_MAX;
  protected readonly pageSize = 10;

  protected readonly options = signal<AnnouncementOptions | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly grades = signal<Grade[]>([]);
  protected readonly sections = signal<SchoolSection[]>([]);

  protected readonly target = signal<AnnouncementTarget>('Section');
  protected readonly gradeId = signal<number | null>(null);
  protected readonly sectionId = signal<number | null>(null);
  protected readonly sectionStudents = signal<SchoolSectionStudent[]>([]);
  protected readonly studentsLoading = signal(false);
  protected readonly chosen = signal<ReadonlySet<number>>(new Set());
  protected readonly title = signal('');
  protected readonly body = signal('');
  protected readonly sending = signal(false);
  protected readonly sendError = signal<string | null>(null);

  protected readonly sent = signal<SentAnnouncement[]>([]);
  protected readonly sentTotal = signal(0);
  protected readonly sentPage = signal(0);
  protected readonly sentLoading = signal(false);
  protected readonly sentError = signal(false);

  protected readonly targets = computed<AnnouncementTarget[]>(() =>
    this.options()?.canTargetSchoolAndGrade ? ['School', 'Grade', 'Section', 'Students'] : ['Section', 'Students'],
  );
  protected readonly selectedSection = computed(() => this.sections().find((s) => s.id === this.sectionId()) ?? null);
  protected readonly allChosen = computed(
    () => this.sectionStudents().length > 0 && this.sectionStudents().every((s) => this.chosen().has(s.studentId)),
  );
  protected readonly canSend = computed(() => {
    const title = this.title().trim();
    const body = this.body().trim();
    if (!title || !body || title.length > this.titleMax || body.length > this.bodyMax || this.sending()) {
      return false;
    }
    switch (this.target()) {
      case 'School':
        return true;
      case 'Grade':
        return this.gradeId() != null;
      case 'Section':
        return this.sectionId() != null;
      case 'Students':
        return this.chosen().size > 0;
    }
  });

  constructor() {
    this.transloco.load(`${ANNOUNCEMENTS_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
  }

  ngOnInit(): void {
    this.service
      .options()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (o) => {
          this.options.set(o);
          if (o.canTargetSchoolAndGrade) {
            this.target.set('School');
            this.gradesService
              .getGrades()
              .pipe(takeUntilDestroyed(this.destroyRef))
              .subscribe({ next: (g) => this.grades.set(g ?? []), error: () => this.grades.set([]) });
          }
          this.sectionService
            .list()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({ next: (s) => this.sections.set(s ?? []), error: () => this.sections.set([]) });
        },
        error: (err: HttpErrorResponse) => this.loadError.set(backendMessage(err) ?? this.t('state.error')),
      });
    this.loadSent();
  }

  protected setTarget(target: AnnouncementTarget): void {
    this.target.set(target);
    this.sendError.set(null);
  }

  protected setSection(id: number | null): void {
    this.sectionId.set(id);
    this.chosen.set(new Set());
    this.sectionStudents.set([]);
    if (id == null) {
      return;
    }
    this.studentsLoading.set(true);
    this.sectionService
      .students(id, 1, SECTION_STUDENT_PAGE)
      .pipe(
        finalize(() => this.studentsLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({ next: (p) => this.sectionStudents.set(p.items ?? []), error: () => this.sectionStudents.set([]) });
  }

  protected toggleStudent(id: number, on: boolean): void {
    const next = new Set(this.chosen());
    if (on) {
      next.add(id);
    } else {
      next.delete(id);
    }
    this.chosen.set(next);
  }

  protected toggleAll(on: boolean): void {
    this.chosen.set(on ? new Set(this.sectionStudents().map((s) => s.studentId)) : new Set());
  }

  /** Onay kutusunda ve listede gösterilen hedef adı. */
  protected targetText(): string {
    switch (this.target()) {
      case 'School':
        return this.t('targetText.School', { school: this.options()?.schoolName ?? '' });
      case 'Grade':
        return this.grades().find((g) => g.id === this.gradeId())?.name ?? '';
      case 'Section': {
        const s = this.selectedSection();
        return s ? `${s.gradeName} / ${s.name}` : '';
      }
      case 'Students':
        return this.t('targetText.Students', { count: this.chosen().size });
    }
  }

  protected confirmSend(): void {
    if (!this.canSend()) {
      return;
    }
    const data: ConfirmDialogData = {
      title: this.t('confirm.title'),
      message: this.t(this.target() === 'School' ? 'confirm.messageSchool' : 'confirm.message', { target: this.targetText() }),
      confirmText: this.t('confirm.send'),
      cancelText: this.t('confirm.cancel'),
      icon: 'campaign',
    };
    this.dialog
      .open(ConfirmDialogComponent, { data })
      .afterClosed()
      .pipe(filter((ok) => ok === true))
      .subscribe(() => this.send());
  }

  private send(): void {
    const request: AnnouncementSendRequest = { target: this.target(), title: this.title().trim(), body: this.body().trim() };
    if (request.target === 'Grade') {
      request.gradeId = this.gradeId()!;
    } else if (request.target === 'Section') {
      request.sectionId = this.sectionId()!;
    } else if (request.target === 'Students') {
      request.studentIds = [...this.chosen()];
    }
    this.sending.set(true);
    this.sendError.set(null);
    this.service
      .send(request)
      .pipe(
        finalize(() => this.sending.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (a) => {
          this.snackBar.open(this.t('toast.sent', { recipients: this.recipientsText(a) }), undefined, { duration: 4000 });
          // Dokunulmuş + zorunlu alanlar boşalınca hata kırmızısı görünmesin.
          this.form()?.resetForm();
          this.title.set('');
          this.body.set('');
          this.chosen.set(new Set());
          this.sentPage.set(0);
          this.loadSent();
        },
        error: (err: HttpErrorResponse) => this.sendError.set(backendMessage(err) ?? this.t('state.sendError')),
      });
  }

  protected loadSent(): void {
    this.sentLoading.set(true);
    this.sentError.set(false);
    this.service
      .sent(this.sentPage() + 1, this.pageSize)
      .pipe(
        finalize(() => this.sentLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (p) => {
          this.sent.set(p.items ?? []);
          this.sentTotal.set(p.totalCount ?? 0);
        },
        error: () => this.sentError.set(true),
      });
  }

  protected onSentPage(e: PageEvent): void {
    this.sentPage.set(e.pageIndex);
    this.loadSent();
  }

  /** "12 öğrenci · 9 veli · 4 öğretmen" (sıfırlar yazılmaz). */
  protected recipientsText(a: SentAnnouncement): string {
    const parts: string[] = [];
    if (a.studentCount > 0) parts.push(this.t('count.students', { count: a.studentCount }));
    if (a.parentCount > 0) parts.push(this.t('count.parents', { count: a.parentCount }));
    if (a.teacherCount > 0) parts.push(this.t('count.teachers', { count: a.teacherCount }));
    return parts.join(' · ');
  }

  protected sentTargetText(a: SentAnnouncement): string {
    return a.targetLabel ?? this.t(`sentTarget.${a.target}`);
  }

  protected formatDate(iso: string): string {
    const date = new Date(iso);
    return Number.isNaN(date.getTime())
      ? '—'
      : date.toLocaleString(this.localeService.localeDefinition().angularLocale, {
          day: 'numeric',
          month: 'short',
          year: 'numeric',
          hour: '2-digit',
          minute: '2-digit',
        });
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate(`${ANNOUNCEMENTS_SCOPE}.${key}`, params);
  }
}
