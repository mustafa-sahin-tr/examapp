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
import { Observable, finalize } from 'rxjs';
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
 */
@Component({
  selector: 'app-teacher-approvals',
  standalone: true,
  imports: [DatePipe, MatButtonModule, MatDialogModule, MatIconModule, MatProgressSpinnerModule, MatTableModule],
  templateUrl: './teacher-approvals.component.html',
  styleUrls: ['./teacher-approvals.component.scss'],
})
export class TeacherApprovalsComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly signalR = inject(SignalRService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  readonly displayedColumns = ['fullName', 'email', 'appliedAt', 'actions'];

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly applications = signal<PendingTeacherApplication[]>([]);
  /** Şu an approve/reject isteği süren teacherId'ler (satır bazlı disable). */
  readonly actingIds = signal<ReadonlySet<number>>(new Set());

  readonly isEmpty = computed(() => !this.loading() && !this.error() && this.applications().length === 0);

  /** Satır kaldırılınca mat-table diğer satırları yeniden oluşturmasın (spinner/focus korunur). */
  readonly trackByTeacherId = (_: number, row: PendingTeacherApplication): number => row.teacherId;

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
          this.error.set(this.extractMessage(err, 'Başvurular alınırken bir sorun oluştu.'));
        },
      });
  }

  isActing(teacherId: number): boolean {
    return this.actingIds().has(teacherId);
  }

  /** auth-api ad çözümlemesi başarısızsa boş gelir; userId ile ayırt edilebilir fallback göster. */
  displayName(row: PendingTeacherApplication): string {
    const name = row.fullName?.trim();
    return name ? name : `İsimsiz (kullanıcı #${row.userId})`;
  }

  approve(row: PendingTeacherApplication): void {
    this.act(row, this.adminService.approveTeacherApplication(row.teacherId), 'Başvuru onaylandı.');
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
        this.act(row, this.adminService.rejectTeacherApplication(row.teacherId, reason), 'Başvuru reddedildi.');
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
            this.snackBar.open(res.message || 'İşlem tamamlanamadı.', 'Kapat', { duration: 4000 });
            return;
          }
          this.applications.update((list) => list.filter((a) => a.teacherId !== id));
          this.snackBar.open(successMessage, 'Kapat', { duration: 3000 });
        },
        error: (err: HttpErrorResponse) => {
          this.snackBar.open(this.extractMessage(err, 'İşlem tamamlanamadı.'), 'Kapat', { duration: 5000 });
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

  private extractMessage(err: HttpErrorResponse, fallback: string): string {
    const body = err.error as Partial<TeacherApplicationActionResult> | null;
    return body?.message || fallback;
  }
}
