import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import {
  TranslocoDirective,
  TranslocoPipe,
  TranslocoService,
  provideTranslocoScope,
} from '@jsverse/transloco';
import { Observable, finalize, take } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { SignalRService } from '../../../services/signalr.service';
import {
  PendingTeacherApplication,
  TeacherApplicationActionResult,
} from '../../../models/teacher-application.model';
import {
  RejectTeacherApplicationDialogComponent,
  RejectTeacherApplicationDialogData,
} from './reject-teacher-application-dialog/reject-teacher-application-dialog.component';

/**
 * Issue #94 — Admin onay paneli: bekleyen bağımsız öğretmen başvuruları.
 * Liste tek seferde yüklenir; onay/red sonrası satır listeden düşürülür (backend zaten Pending dışını
 * döndürmez, yeniden fetch gereksiz). Aksiyon durumu satır bazlı tutulur (`actingIds`), diğer satırlar
 * kullanılabilir kalır.
 *
 * Yönetim ekranlarının ortak Transloco scope'u: `public/i18n/admin/<lang>.json` (issue #183).
 * Kendi route'undan da (`/admin/teacher-approvals`) açıldığı için scope'u admin-home'dan devralmaz,
 * provider'ı burada da verilir.
 */
const ADMIN_SCOPE = 'admin';

@Component({
  selector: 'app-teacher-approvals',
  standalone: true,
  imports: [
    DatePipe,
    MatButtonModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTableModule,
    TranslocoDirective,
    TranslocoPipe,
  ],
  providers: [provideTranslocoScope(ADMIN_SCOPE)],
  templateUrl: './teacher-approvals.component.html',
  styleUrls: ['./teacher-approvals.component.scss'],
})
export class TeacherApprovalsComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly signalR = inject(SignalRService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);
  private readonly transloco = inject(TranslocoService);

  readonly displayedColumns = ['fullName', 'email', 'appliedAt', 'actions'];

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly applications = signal<PendingTeacherApplication[]>([]);
  /** Şu an approve/reject isteği süren teacherId'ler (satır bazlı disable). */
  readonly actingIds = signal<ReadonlySet<number>>(new Set());

  readonly isEmpty = computed(() => !this.loading() && !this.error() && this.applications().length === 0);

  /** Satır kaldırılınca mat-table diğer satırları yeniden oluşturmasın (spinner/focus korunur). */
  readonly trackByTeacherId = (_: number, row: PendingTeacherApplication): number => row.teacherId;

  constructor() {
    this.preloadScope();
  }

  ngOnInit(): void {
    this.load();

    // Yeni başvuru push'u (SignalR TeacherApplicationSubmitted) gelince listeyi yeniden çek;
    // admin sayfayı elle yenilemek zorunda kalmasın.
    this.signalR.teacherApplicationSubmitted$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.adminService
      .getPendingTeacherApplications()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (list) => this.applications.set(list),
        error: (err: HttpErrorResponse) => {
          this.applications.set([]);
          this.error.set(this.extractMessage(err, this.text('loadFailed')));
        },
      });
  }

  isActing(teacherId: number): boolean {
    return this.actingIds().has(teacherId);
  }

  /** auth-api ad çözümlemesi başarısızsa boş gelir; userId ile ayırt edilebilir fallback göster. */
  displayName(row: PendingTeacherApplication): string {
    const name = row.fullName?.trim();
    return name ? name : this.text('unnamed', { userId: row.userId });
  }

  approve(row: PendingTeacherApplication): void {
    this.act(row, this.adminService.approveTeacherApplication(row.teacherId), this.text('approved'));
  }

  reject(row: PendingTeacherApplication): void {
    if (this.isActing(row.teacherId)) {
      return;
    }
    const data: RejectTeacherApplicationDialogData = { displayName: this.displayName(row) };
    this.dialog
      .open<RejectTeacherApplicationDialogComponent, RejectTeacherApplicationDialogData, string | undefined>(
        RejectTeacherApplicationDialogComponent,
        { data, autoFocus: 'first-tabbable' },
      )
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((reason) => {
        if (!reason) {
          return;
        }
        this.act(row, this.adminService.rejectTeacherApplication(row.teacherId, reason), this.text('rejected'));
      });
  }

  private act(
    row: PendingTeacherApplication,
    call: Observable<TeacherApplicationActionResult>,
    successMessage: string,
  ): void {
    const id = row.teacherId;
    if (this.isActing(id)) {
      return;
    }
    this.setActing(id, true);

    call
      .pipe(
        finalize(() => this.setActing(id, false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (res) => {
          if (!res.success) {
            this.snackBar.open(res.message || this.text('actionFailed'), this.text('close'), { duration: 4000 });
            return;
          }
          this.applications.update((list) => list.filter((a) => a.teacherId !== id));
          this.snackBar.open(successMessage, this.text('close'), { duration: 3000 });
        },
        error: (err: HttpErrorResponse) => {
          this.snackBar.open(this.extractMessage(err, this.text('actionFailed')), this.text('close'), {
            duration: 5000,
          });
          // 404 / 409: kayıt artık Pending değil; listeyi güncel tut.
          if (err.status === 404 || err.status === 409) {
            this.applications.update((list) => list.filter((a) => a.teacherId !== id));
          }
        },
      });
  }

  private setActing(id: number, on: boolean): void {
    this.actingIds.update((set) => {
      const next = new Set(set);
      if (on) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  /** Scope'a göreli anahtarı senkron çözer; sözlük şablon render edilirken yüklenmiş olur. */
  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${ADMIN_SCOPE}.approvals.${key}`, params) ?? '';
  }

  private extractMessage(err: HttpErrorResponse, fallback: string): string {
    const body = err.error as Partial<TeacherApplicationActionResult> | null;
    return body?.message || fallback;
  }

  /**
   * Şablon dışı metinler (snackbar, dialog, hata mesajı) senkron `translate()` ile okunur;
   * sözlük şablon render edilmeden de hazır olsun diye scope burada yüklenir.
   */
  private preloadScope(): void {
    this.transloco
      .load(`${ADMIN_SCOPE}/${this.transloco.getActiveLang()}`)
      .pipe(take(1))
      .subscribe();
  }

}
