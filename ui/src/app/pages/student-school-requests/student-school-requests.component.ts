import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter, finalize, take } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { LocaleService } from '../../services/locale.service';
import { StudentSchoolRequestService } from '../../services/student-school-request.service';
import { StudentSchoolDecisionResponse, StudentSchoolRequest } from '../../models/student-school-request.model';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/components/confirm-dialog/confirm-dialog.component';

/** Çeviriler kendi Transloco scope'unda: `public/i18n/student-school-requests/<lang>.json`. */
export const STUDENT_SCHOOL_REQUESTS_SCOPE = 'student-school-requests';

/**
 * Issue #361: "Bekleyen öğrenci başvuruları" — öğrencinin kendi kaydında seçtiği okul üyeliğinin onayı/reddi.
 * Aynı komponent admin (`/admin/student-school-requests`, tüm okullar) ve okulun onaylı öğretmeni
 * (`/student-school-requests`, yalnız kendi okulu) için kullanılır; kapsam tamamen sunucuda.
 */
@Component({
  selector: 'app-student-school-requests',
  standalone: true,
  imports: [
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    MatTableModule,
    TranslocoDirective,
    TranslocoPipe,
  ],
  providers: [provideTranslocoScope(STUDENT_SCHOOL_REQUESTS_SCOPE)],
  templateUrl: './student-school-requests.component.html',
  styleUrl: './student-school-requests.component.scss',
})
export class StudentSchoolRequestsComponent implements OnInit {
  private readonly service = inject(StudentSchoolRequestService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);

  protected readonly displayedColumns = ['student', 'number', 'grade', 'school', 'date', 'actions'];

  protected readonly rows = signal<StudentSchoolRequest[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(20);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly actingId = signal<number | null>(null);
  protected readonly isEmpty = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  constructor() {
    this.preloadScope();
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service
      .list(this.pageIndex() + 1, this.pageSize())
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (page) => {
          const items = page?.items ?? [];
          this.rows.set(items);
          this.totalCount.set(page?.totalCount ?? 0);
          // Son sayfadaki son satır karar sonrası boşaldıysa bir önceki sayfaya dön.
          if (items.length === 0 && this.pageIndex() > 0 && (page?.totalCount ?? 0) > 0) {
            this.pageIndex.set(this.pageIndex() - 1);
            this.load();
          }
        },
        error: (err: HttpErrorResponse) => {
          this.error.set(this.messageOf(err) || this.text('messages.loadFailed'));
        },
      });
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  protected approve(row: StudentSchoolRequest): void {
    this.act(row.studentId, 'approve', this.text('messages.approved'));
  }

  protected reject(row: StudentSchoolRequest): void {
    const data: ConfirmDialogData = {
      title: this.text('rejectDialog.title'),
      message: this.transloco.translate<string>(`${STUDENT_SCHOOL_REQUESTS_SCOPE}.rejectDialog.message`, {
        name: row.fullName || this.text('unknownStudent'),
        school: row.schoolName,
      }),
      confirmText: this.text('actions.reject'),
      icon: 'person_remove',
      confirmColor: 'warn',
    };
    this.dialog
      .open(ConfirmDialogComponent, { data })
      .afterClosed()
      .pipe(
        filter((confirmed) => confirmed === true),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(() => this.act(row.studentId, 'reject', this.text('messages.rejected')));
  }

  private act(studentId: number, action: 'approve' | 'reject', successMsg: string): void {
    if (this.actingId() !== null) {
      return;
    }
    this.actingId.set(studentId);
    const call = action === 'approve' ? this.service.approve(studentId) : this.service.reject(studentId);
    call
      .pipe(
        finalize(() => this.actingId.set(null)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          this.snackBar.open(successMsg, this.text('messages.ok'), { duration: 3000 });
          this.load();
        },
        error: (err: HttpErrorResponse) => {
          this.snackBar.open(this.messageOf(err) || this.text('messages.actionFailed'), this.text('messages.ok'), {
            duration: 4000,
          });
          // 404/409: başvuru artık bu listede değil (başka bir onaylayıcı karar verdi) — listeyi tazele.
          if (err.status === 404 || err.status === 409) {
            this.load();
          }
        },
      });
  }

  protected formatDate(iso: string): string {
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) {
      return '—';
    }
    return date.toLocaleDateString(this.localeService.localeDefinition().angularLocale, {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });
  }

  private messageOf(err: HttpErrorResponse): string | undefined {
    return (err?.error as StudentSchoolDecisionResponse | null)?.message || undefined;
  }

  /** Scope'a göreli anahtarı senkron çözer; sözlük şablon render edilirken yüklenmiş olur. */
  private text(key: string): string {
    return this.transloco.translate<string>(`${STUDENT_SCHOOL_REQUESTS_SCOPE}.${key}`) ?? '';
  }

  /** Şablon dışı metinler (snackbar, dialog) senkron okunduğu için scope önceden yüklenir. */
  private preloadScope(): void {
    this.transloco
      .load(`${STUDENT_SCHOOL_REQUESTS_SCOPE}/${this.transloco.getActiveLang()}`)
      .pipe(take(1))
      .subscribe();
  }
}
