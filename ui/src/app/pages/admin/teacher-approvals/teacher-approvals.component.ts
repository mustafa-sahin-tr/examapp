import { Component, DestroyRef, OnInit, WritableSignal, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
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
import { Observable, Subject, auditTime, catchError, finalize, map, of, switchMap, take, tap } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { adminListErrorMessage } from '../../../shared/utils/school-paged-list';
import { adminActionErrorMessage } from '../../../shared/utils/admin-action-error.util';
import { DEFAULT_PAGE_SIZE, PAGE_SIZE_OPTIONS, lastPageIndex } from '../../../shared/utils/paging.util';
import { provideTranslatedPaginatorIntl } from '../../../shared/utils/paginator-intl.util';
import { SignalRService } from '../../../services/signalr.service';
import { Paged } from '../../../models/test-instance';
import {
  TeacherApplicationActionResult,
  TeacherApplicationListItem,
  TeacherApplicationStatus,
  TeacherApplicationStatusFilter,
} from '../../../models/teacher-application.model';
import {
  RejectTeacherApplicationDialogComponent,
  RejectTeacherApplicationDialogData,
} from './reject-teacher-application-dialog/reject-teacher-application-dialog.component';

/**
 * Issue #94 / #234 — Admin onay paneli: öğretmen başvuruları (bağımsız öğretmen başvurusu ya da okul bağlantısı
 * talebi; satırda tür etiketi gösterilir). Aksiyon durumu satır bazlı tutulur (`actingIds`), diğer satırlar
 * kullanılabilir kalır.
 *
 * Issue #187: "Onay bekleyenler / Tümü" filtresi + server-side sayfalama. Satırda durum chip'i, karar tarihi ve (ret
 * ise) gerekçe gösterilir; Onayla/Reddet yalnız bekleyen satırlarda. Onay/red sonucu yerelde uygulanır
 * (bkz. `applyDecision`); yalnız sayfa boşalırsa yeniden yüklenir.
 * Filtre/sayfa URL'e YAZILMAZ (öğretmen/öğrenci listelerinden farklı): komponent admin-home'da sekme olarak da
 * gömülü; sayfaya ait olmayan `page`/`status` param'ları o rotayı kirletirdi. State komponent-yerel signal'lerdedir.
 *
 * Issue #262: listede e-posta maskeli gelir; admin satır bazında "E-postayı göster" ile audit'li detay ucundan
 * tam adresi ister (issue #187 security review: yalnız bekleyen başvurularda; karar verilmişlerde backend maskeli döner). Tam adresler yalnız bellekte (`revealedEmails`, teacherId → e-posta)
 * tutulur; satır mevcut sayfadan düşünce ilgili kayıt atılır.
 *
 * Yönetim ekranlarının ortak Transloco scope'u: `public/i18n/admin/<lang>.json` (issue #183).
 * Kendi route'undan da (`/admin/teacher-approvals`) açıldığı için scope'u admin-home'dan devralmaz,
 * provider'ı burada da verilir.
 */
const ADMIN_SCOPE = 'admin';

/** SignalR yeni-başvuru push'larından sonra listeyi yeniden çekmeden önce birleştirme penceresi (ms). */
export const PUSH_RELOAD_AUDIT_MS = 5000;

type StatusKey = 'pending' | 'approved' | 'rejected';

/** Başarılı onay/red'in satıra yerelde uygulanacak alanları. */
type Decision = Pick<TeacherApplicationListItem, 'status' | 'rejectionReason'>;

const STATUS_KEYS: Record<TeacherApplicationStatus, StatusKey> = {
  Pending: 'pending',
  Approved: 'approved',
  Rejected: 'rejected',
};

/** Son başarılı yüklemenin sorgusu (429'da görünen liste ile filtre/sayfa kontrolleri tutarlı kalsın). */
interface ListState {
  filter: TeacherApplicationStatusFilter;
  pageIndex: number;
  pageSize: number;
}

type LoadResult =
  | { ok: true; page: Paged<TeacherApplicationListItem> }
  | { ok: false; error: HttpErrorResponse };

@Component({
  selector: 'app-teacher-approvals',
  standalone: true,
  imports: [
    DatePipe,
    MatButtonModule,
    MatButtonToggleModule,
    MatDialogModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatTableModule,
    MatTooltipModule,
    TranslocoDirective,
    TranslocoPipe,
  ],
  providers: [
    provideTranslocoScope(ADMIN_SCOPE),
    provideTranslatedPaginatorIntl(ADMIN_SCOPE, `${ADMIN_SCOPE}.paginator`),
  ],
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

  readonly pageSizeOptions = PAGE_SIZE_OPTIONS;

  /** Issue #187: varsayılan yalnız bekleyenler. */
  readonly filter = signal<TeacherApplicationStatusFilter>('pending');
  /** 0-tabanlı (MatPaginator); backend'e `pageIndex + 1` gider. */
  readonly pageIndex = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly totalCount = signal(0);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly applications = signal<TeacherApplicationListItem[]>([]);
  /** Şu an approve/reject isteği süren teacherId'ler (satır bazlı disable). */
  readonly actingIds = signal<ReadonlySet<number>>(new Set());
  /** Issue #262: talep üzerine açılan TAM e-postalar (teacherId → e-posta). */
  readonly revealedEmails = signal<ReadonlyMap<number, string>>(new Map());
  /** Tam e-posta isteği süren teacherId'ler (satır bazlı buton disable + spinner). */
  readonly revealingIds = signal<ReadonlySet<number>>(new Set());
  /** Son başarılı yüklemenin sorgusu; null → henüz başarılı yükleme yok (429'da mevcut listeyi korumak için). */
  private lastLoaded: ListState | null = null;

  /** Karar tarihi sütunu yalnız "Tümü"nde anlamlı (bekleyenlerde hep boş). */
  readonly displayedColumns = computed(() =>
    this.filter() === 'all'
      ? ['fullName', 'type', 'email', 'appliedAt', 'status', 'decidedAt', 'actions']
      : ['fullName', 'type', 'email', 'appliedAt', 'status', 'actions'],
  );

  readonly isEmpty = computed(() => !this.loading() && !this.error() && this.applications().length === 0);
  /** İlk yükleme / boş listeden yükleme → tam spinner; sayfa/filtre geçişlerinde tablo kalır, üstte ince bar. */
  readonly showSpinner = computed(() => this.loading() && this.applications().length === 0);

  /** Satır kaldırılınca mat-table diğer satırları yeniden oluşturmasın (spinner/focus korunur). */
  readonly trackByTeacherId = (_: number, row: TeacherApplicationListItem): number => row.teacherId;

  /** Çakışan istekler (hızlı filtre/sayfa değişimi, push) `switchMap` ile iptal edilir; son seçim kazanır. */
  private readonly reload$ = new Subject<void>();

  constructor() {
    this.preloadScope();

    this.reload$
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.error.set(null);
        }),
        switchMap(() => {
          const state = this.currentState();
          return this.adminService
            .getTeacherApplications({
              status: state.filter,
              page: state.pageIndex + 1,
              pageSize: state.pageSize,
            })
            .pipe(
              map((page): LoadResult => ({ ok: true, page })),
              catchError((error: HttpErrorResponse) => of<LoadResult>({ ok: false, error })),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.applyResult(result));
  }

  ngOnInit(): void {
    this.load();

    // Yeni başvuru push'u (SignalR TeacherApplicationSubmitted) gelince mevcut filtre/sayfa yeniden çekilir;
    // admin sayfayı elle yenilemek zorunda kalmasın. Issue #262: liste ucu adminin paylaşımlı rate limit
    // kovasını (30/dk) harcar → push patlamaları tek yüklemede birleştirilir.
    this.signalR.teacherApplicationSubmitted$
      .pipe(auditTime(PUSH_RELOAD_AUDIT_MS), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
  }

  /** Mevcut filtre/sayfa ile yeniden yükler (Yenile / Tekrar dene / push / karar sonrası). */
  load(): void {
    this.reload$.next();
  }

  /** Issue #187: filtre değişince 1. sayfaya dönülür. */
  setFilter(value: TeacherApplicationStatusFilter): void {
    if (value === this.filter()) {
      return;
    }
    this.filter.set(value);
    this.pageIndex.set(0);
    this.load();
  }

  onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  private currentState(): ListState {
    return { filter: this.filter(), pageIndex: this.pageIndex(), pageSize: this.pageSize() };
  }

  private applyResult(result: LoadResult): void {
    if (!result.ok) {
      this.loading.set(false);
      const err = result.error;
      // Issue #262: 429 geçicidir — elde liste varsa (ve açılmış e-postalar) korunur, yalnızca uyarı gösterilir.
      // Filtre/sayfa kontrolleri görünen listeyle tutarlı kalsın diye son başarılı sorguya geri alınır.
      if (err.status === 429 && this.lastLoaded) {
        this.filter.set(this.lastLoaded.filter);
        this.pageIndex.set(this.lastLoaded.pageIndex);
        this.pageSize.set(this.lastLoaded.pageSize);
        this.snackBar.open(adminListErrorMessage(err, (key) => this.text(key)), this.text('close'), {
          duration: 5000,
        });
        return;
      }
      this.lastLoaded = null;
      this.applications.set([]);
      this.totalCount.set(0);
      this.revealedEmails.set(new Map());
      // Admin liste uçlarıyla ortak yorum: 403 yetki, 429 istek limiti (issue #262), diğerleri genel hata.
      this.error.set(adminListErrorMessage(err, (key) => this.text(key)));
      return;
    }

    const items = result.page.items ?? [];
    const totalCount = result.page.totalCount;
    // Sayfa boşaldıysa (ör. bekleyenlerde son satır onaylandı) son dolu sayfaya dön. Yalnız gerçekten
    // ötesindeyken: aksi hâlde boş durum gösterilir, döngü olmaz.
    const last = lastPageIndex(totalCount, this.pageSize());
    if (items.length === 0 && totalCount > 0 && this.pageIndex() > last) {
      this.pageIndex.set(last);
      this.load();
      return;
    }

    this.loading.set(false);
    this.lastLoaded = this.currentState();
    this.applications.set(items);
    this.totalCount.set(totalCount);
    // Artık sayfada olmayan başvuruların tam e-postasını bellekte tutma.
    const ids = new Set(items.map((a) => a.teacherId));
    this.revealedEmails.update((m) => new Map([...m].filter(([id]) => ids.has(id))));
  }

  /** Issue #187: yalnız bekleyen başvuru onaylanabilir/reddedilebilir. */
  isPending(row: TeacherApplicationListItem): boolean {
    return row.status === 'Pending';
  }

  /** Issue #187: durum chip'inin i18n/CSS anahtarı; bilinmeyen değer → `pending`. */
  statusKey(row: TeacherApplicationListItem): StatusKey {
    return STATUS_KEYS[row.status] ?? 'pending';
  }

  isActing(teacherId: number): boolean {
    return this.actingIds().has(teacherId);
  }

  isRevealing(teacherId: number): boolean {
    return this.revealingIds().has(teacherId);
  }

  /** Satırda gösterilecek e-posta: açıldıysa tam adres, değilse listedeki maskeli adres. */
  emailFor(row: TeacherApplicationListItem): string {
    return this.revealedEmails().get(row.teacherId) ?? row.email;
  }

  isEmailRevealed(teacherId: number): boolean {
    return this.revealedEmails().has(teacherId);
  }

  /**
   * Issue #262: tam e-postayı audit'li detay ucundan ister. 404 → başvuru bulunamadı (listeyi yenile);
   * 429 → istek limiti (varsa `Retry-After` saniyesi ile); diğer hatalar genel mesaj.
   */
  revealEmail(row: TeacherApplicationListItem): void {
    const id = row.teacherId;
    // Security review (#187): backend tam e-postayı yalnız bekleyen başvuru için döner.
    if (!this.isPending(row) || this.isRevealing(id) || this.isEmailRevealed(id)) {
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
          // 404: başvuru artık yok → listeyi güncelle.
          if (err.status === 404) {
            this.load();
          }
        },
      });
  }

  /** auth-api ad çözümlemesi başarısızsa boş gelir; başvuru (teacherId) ile ayırt edilebilir fallback göster. */
  displayName(row: TeacherApplicationListItem): string {
    const name = row.fullName?.trim();
    return name ? name : this.text('unnamed', { teacherId: row.teacherId });
  }

  /** Issue #234: satırın başvuru türü etiketi — "Bağımsız" ya da "Okul: <ad>". */
  typeLabel(row: TeacherApplicationListItem): string {
    if (row.isIndependentTutor) {
      return this.text('types.independent');
    }
    const schoolName = row.requestedSchoolName?.trim();
    return schoolName
      ? this.text('types.school', { schoolName })
      : this.text('types.schoolUnknown', { schoolId: row.requestedSchoolId ?? '—' });
  }

  approve(row: TeacherApplicationListItem): void {
    if (this.isActing(row.teacherId) || !this.isPending(row)) {
      return;
    }
    this.act(row, this.adminService.approveTeacherApplication(row.teacherId), this.text('approved'), {
      status: 'Approved',
      rejectionReason: null,
    });
  }

  reject(row: TeacherApplicationListItem): void {
    if (this.isActing(row.teacherId) || !this.isPending(row)) {
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
        this.act(row, this.adminService.rejectTeacherApplication(row.teacherId, reason), this.text('rejected'), {
          status: 'Rejected',
          rejectionReason: reason,
        });
      });
  }

  private act(
    row: TeacherApplicationListItem,
    call: Observable<TeacherApplicationActionResult>,
    successMessage: string,
    decision: Decision,
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
          this.snackBar.open(successMessage, this.text('close'), { duration: 3000 });
          this.applyDecision(id, decision);
        },
        error: (err: HttpErrorResponse) => {
          this.snackBar.open(this.extractMessage(err, this.text('actionFailed')), this.text('close'), {
            duration: 5000,
          });
          // 404 / 409: kayıt artık Pending değil; listeyi güncel tut.
          if (err.status === 404 || err.status === 409) {
            this.load();
          }
        },
      });
  }

  /**
   * Issue #187 (review): başarılı karar yerelde yansıtılır — yeniden yükleme adminin paylaşımlı rate limit kovasını
   * (30/dk) ve bir audit satırını harcar; 429 alırsa karar verilmiş satır aksiyonlu kalırdı. Bekleyenlerde satır
   * düşer ve toplam azalır; Tümü'nde satırın durumu/karar anı/gerekçesi güncellenir. Yalnız mevcut sayfa boşalırsa
   * (ve başka kayıt varsa) yeniden yüklenir; gerekirse önceki sayfaya geçilir.
   * Karar verilmiş başvurunun tam e-postası detay ucundan dönmez (yalnız Pending) → açılmış e-posta da atılır.
   */
  private applyDecision(id: number, decision: Decision): void {
    this.forgetEmail(id);

    if (this.filter() === 'all') {
      const decidedAt = new Date().toISOString();
      this.applications.update((list) =>
        list.map((a) => (a.teacherId === id ? { ...a, ...decision, decidedAt } : a)),
      );
      return;
    }

    const before = this.applications().length;
    this.applications.update((list) => list.filter((a) => a.teacherId !== id));
    if (this.applications().length === before) {
      return;
    }
    const total = Math.max(0, this.totalCount() - 1);
    this.totalCount.set(total);
    if (this.applications().length === 0 && total > 0) {
      this.pageIndex.set(Math.min(this.pageIndex(), lastPageIndex(total, this.pageSize())));
      this.load();
    }
  }

  private forgetEmail(id: number): void {
    this.revealedEmails.update((map) => {
      if (!map.has(id)) return map;
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
