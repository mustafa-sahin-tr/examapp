import { Component, DestroyRef, OnInit, WritableSignal, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import {
  TranslocoDirective,
  TranslocoPipe,
  TranslocoService,
  provideTranslocoScope,
} from '@jsverse/transloco';
import { Observable, finalize, take } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { adminListErrorMessage } from '../../../shared/utils/school-paged-list';
import { adminActionErrorMessage } from '../../../shared/utils/admin-action-error.util';
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
 * Issue #94 / #234 — Admin onay paneli: bekleyen öğretmen başvuruları (bağımsız öğretmen başvurusu ya da
 * okul bağlantısı talebi; satırda tür etiketi gösterilir).
 * Liste tek seferde yüklenir; onay/red sonrası satır listeden düşürülür (backend zaten Pending dışını
 * döndürmez, yeniden fetch gereksiz). Aksiyon durumu satır bazlı tutulur (`actingIds`), diğer satırlar
 * kullanılabilir kalır.
 *
 * Issue #262: listede e-posta maskeli gelir; admin satır bazında "E-postayı göster" ile audit'li detay ucundan
 * tam adresi ister. Tam adresler yalnız bellekte (`revealedEmails`, teacherId → e-posta) tutulur; satır listeden
 * düşünce ya da liste yeniden yüklenince ilgili kayıt atılır.
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
    MatTooltipModule,
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

  readonly displayedColumns = ['fullName', 'type', 'email', 'appliedAt', 'actions'];

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly applications = signal<PendingTeacherApplication[]>([]);
  /** Şu an approve/reject isteği süren teacherId'ler (satır bazlı disable). */
  readonly actingIds = signal<ReadonlySet<number>>(new Set());
  /** Issue #262: talep üzerine açılan TAM e-postalar (teacherId → e-posta). */
  readonly revealedEmails = signal<ReadonlyMap<number, string>>(new Map());
  /** Tam e-posta isteği süren teacherId'ler (satır bazlı buton disable + spinner). */
  readonly revealingIds = signal<ReadonlySet<number>>(new Set());

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
        next: (list) => {
          this.applications.set(list);
          // Artık listede olmayan başvuruların tam e-postasını bellekte tutma.
          const ids = new Set(list.map((a) => a.teacherId));
          this.revealedEmails.update((map) => new Map([...map].filter(([id]) => ids.has(id))));
        },
        error: (err: HttpErrorResponse) => {
          this.applications.set([]);
          this.revealedEmails.set(new Map());
          // Admin liste uçlarıyla ortak yorum: 403 yetki, 429 istek limiti (issue #262), diğerleri genel hata.
          this.error.set(adminListErrorMessage(err, (key) => this.text(key)));
        },
      });
  }

  isActing(teacherId: number): boolean {
    return this.actingIds().has(teacherId);
  }

  isRevealing(teacherId: number): boolean {
    return this.revealingIds().has(teacherId);
  }

  /** Satırda gösterilecek e-posta: açıldıysa tam adres, değilse listedeki maskeli adres. */
  emailFor(row: PendingTeacherApplication): string {
    return this.revealedEmails().get(row.teacherId) ?? row.email;
  }

  isEmailRevealed(teacherId: number): boolean {
    return this.revealedEmails().has(teacherId);
  }

  /**
   * Issue #262: tam e-postayı audit'li detay ucundan ister. 404 → başvuru artık bekleyen değil (listeyi yenile);
   * 429 → istek limiti (varsa `Retry-After` saniyesi ile); diğer hatalar genel mesaj.
   */
  revealEmail(row: PendingTeacherApplication): void {
    const id = row.teacherId;
    if (this.isRevealing(id) || this.isEmailRevealed(id)) {
      return;
    }
    this.setFlag(this.revealingIds, id, true);

    this.adminService
      .getTeacherApplication(id)
      .pipe(
        finalize(() => this.setFlag(this.revealingIds, id, false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (detail) => {
          const email = detail.email?.trim();
          if (!email) {
            this.snackBar.open(this.text('emailUnavailable'), this.text('close'), { duration: 4000 });
            return;
          }
          this.revealedEmails.update((map) => new Map(map).set(id, email));
        },
        error: (err: HttpErrorResponse) => {
          // 403/429 (+ `Retry-After` saniyesi)/502/diğer → `admin.approvals.emailErrors.*`.
          const message = adminActionErrorMessage(err, `${ADMIN_SCOPE}.approvals.emailErrors`, (key, params) =>
            this.transloco.translate<string>(key, params),
          );
          this.snackBar.open(message, this.text('close'), { duration: 5000 });
          // 404: başvuru artık bekleyen değil (arada onaylanmış/reddedilmiş) → listeyi güncelle.
          if (err.status === 404) {
            this.load();
          }
        },
      });
  }

  /** auth-api ad çözümlemesi başarısızsa boş gelir; userId ile ayırt edilebilir fallback göster. */
  displayName(row: PendingTeacherApplication): string {
    const name = row.fullName?.trim();
    return name ? name : this.text('unnamed', { userId: row.userId });
  }

  /** Issue #234: satırın başvuru türü etiketi — "Bağımsız" ya da "Okul: <ad>". */
  typeLabel(row: PendingTeacherApplication): string {
    if (row.isIndependentTutor) {
      return this.text('types.independent');
    }
    const schoolName = row.requestedSchoolName?.trim();
    return schoolName
      ? this.text('types.school', { schoolName })
      : this.text('types.schoolUnknown', { schoolId: row.requestedSchoolId ?? '—' });
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
          this.removeRow(id);
          this.snackBar.open(successMessage, this.text('close'), { duration: 3000 });
        },
        error: (err: HttpErrorResponse) => {
          this.snackBar.open(this.extractMessage(err, this.text('actionFailed')), this.text('close'), {
            duration: 5000,
          });
          // 404 / 409: kayıt artık Pending değil; listeyi güncel tut.
          if (err.status === 404 || err.status === 409) {
            this.removeRow(id);
          }
        },
      });
  }

  private removeRow(id: number): void {
    this.applications.update((list) => list.filter((a) => a.teacherId !== id));
    this.revealedEmails.update((map) => {
      const next = new Map(map);
      next.delete(id);
      return next;
    });
  }


  private setActing(id: number, on: boolean): void {
    this.setFlag(this.actingIds, id, on);
  }

  private setFlag(target: WritableSignal<ReadonlySet<number>>, id: number, on: boolean): void {
    target.update((set) => {
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
