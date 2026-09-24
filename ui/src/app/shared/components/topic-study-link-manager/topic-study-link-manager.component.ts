import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList, moveItemInArray } from '@angular/cdk/drag-drop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleChange, MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { DatePipe } from '@angular/common';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { EMPTY, catchError, firstValueFrom, map, of, switchMap, take, tap } from 'rxjs';
import {
  MAX_ACTIVE_STUDY_LINKS,
  MAX_TOTAL_STUDY_LINKS,
  StudyLink,
  StudyLinkListResponse,
  StudyLinkScope,
} from '../../../models/study-link';
import {
  StudyLinkService,
  isTeacherNotApprovedError,
  rateLimitRetryAfter,
  studyLinkErrorMessage,
} from '../../../services/study-link.service';
import { AuthService } from '../../../services/auth.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../confirm-dialog/confirm-dialog.component';
import { StudyLinkDialogComponent, StudyLinkDialogData } from '../study-link-dialog/study-link-dialog.component';

/** Çalışma linki bileşenlerinin Transloco scope'u: `public/i18n/study-links/<lang>.json`. */
const STUDY_LINKS_SCOPE = 'study-links';

/** Liste tek sayfada gelir: kapsam başına pratikte az link olur (en fazla 7 aktif + pasifler). */
const PAGE_SIZE = 50;

/**
 * Issue #61 — bir konu ya da alt konunun çalışma linklerini yönetir (Admin + Teacher).
 * `subTopicId` verilirse alt konu linkleri; yalnız `topicId` verilirse konu seviyesi linkler gösterilir.
 * Aktif link sayacı her zaman görünür; sınır (7) dolunca "Link Ekle" kapanır ve kalıcı uyarı şeridi çıkar.
 * Sıralama sürükle-bırak (cdk) veya klavye/dokunmatik için yukarı/aşağı oklarıyla yapılır.
 */
@Component({
  selector: 'app-topic-study-link-manager',
  standalone: true,
  imports: [
    CdkDropList,
    CdkDrag,
    CdkDragHandle,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    MatSlideToggleModule,
    MatTooltipModule,
    DatePipe,
    TranslocoDirective,
    TranslocoPipe,
  ],
  providers: [provideTranslocoScope(STUDY_LINKS_SCOPE)],
  templateUrl: './topic-study-link-manager.component.html',
  styleUrls: ['./topic-study-link-manager.component.scss'],
})
export class TopicStudyLinkManagerComponent {
  private readonly service = inject(StudyLinkService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly auth = inject(AuthService);

  /** Admin tüm linkleri, öğretmen yalnız kendi eklediklerini değiştirebilir (backend `NotOwner` ile aynı kural). */
  readonly isAdmin = this.auth.hasRealmRole('Admin') || this.auth.hasRole('Admin');
  /** `createdByUserId` ile aynı kimlik uzayı: exam API profil sağlayıcısının döndürdüğü kullanıcı kimliği. */
  private readonly currentUserId = computed(() => this.auth.user()?.id ?? null);

  readonly topicId = input<number | null>(null);
  readonly subTopicId = input<number | null>(null);
  /** Başlıkta ve dialog'da gösterilen konu / alt konu adı. */
  readonly scopeName = input<string>('');

  readonly scope = computed<StudyLinkScope | null>(() => {
    const subTopicId = this.subTopicId();
    if (subTopicId != null) return { subTopicId };
    const topicId = this.topicId();
    return topicId != null ? { topicId } : null;
  });
  readonly isTopicLevel = computed(() => this.subTopicId() == null && this.topicId() != null);

  readonly links = signal<StudyLink[]>([]);
  readonly activeCount = signal(0);
  readonly maxActive = signal(MAX_ACTIVE_STUDY_LINKS);
  readonly totalCount = signal(0);
  readonly maxTotal = MAX_TOTAL_STUDY_LINKS;
  /**
   * 403 `TeacherNotApproved`: onaysız öğretmen hiçbir yönetim ucunu (liste dahil) kullanamaz. Doluysa genel hata
   * bandı yerine bilgilendirme gösterilir ve tüm ekleme/düzenleme kontrolleri gizlenir.
   */
  readonly notApproved = signal<string | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  /** Bir yazma isteği sürüyor (çift tıklama / eşzamanlı sıralama engeli). */
  readonly busy = signal(false);

  readonly limitReached = computed(() => this.activeCount() >= this.maxActive());
  /** Aktif + pasif toplam sınır (30) doldu mu — yeni link eklenemez. */
  readonly totalLimitReached = computed(() => this.totalCount() >= this.maxTotal);
  readonly addBlocked = computed(() => this.limitReached() || this.totalLimitReached());
  readonly showSkeleton = computed(() => this.loading() && this.links().length === 0);
  readonly isEmpty = computed(
    () => !this.loading() && !this.error() && !this.notApproved() && this.links().length === 0
  );

  private readonly reloadTick = signal(0);

  /** `links()`'in ait olduğu kapsam; girdi kapsamıyla eşleşmezse satırlar bayattır. */
  private readonly loadedScopeKey = signal<string | null>(null);

  /**
   * Satır işlemleri (taşı/sürükle/düzenle/aktif-pasif/sil) kilitli mi: yazma sürüyor, liste yükleniyor ya da
   * kapsam değişti ama yeni liste henüz gelmedi — önceki kapsamın id'leriyle istek gönderilmesin.
   */
  readonly locked = computed(
    () => this.busy() || this.loading() || scopeKey(this.scope()) !== this.loadedScopeKey()
  );

  constructor() {
    // Snackbar / confirm metinleri şablon dışında senkron `translate()` ile okunur; scope baştan yüklensin.
    this.transloco.load(`${STUDY_LINKS_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();

    // Kapsam değişince (ya da "tekrar dene"/yazma sonrası) liste yeniden istenir; eski istek iptal edilir.
    toObservable(computed(() => ({ scope: this.scope(), tick: this.reloadTick() })))
      .pipe(
        switchMap(({ scope }) => {
          const key = scopeKey(scope);
          this.error.set(null);
          this.notApproved.set(null);
          // Yeni kapsam: önceki kapsamın satırları ve sayaçları hemen temizlenir (bayat id'lerle işlem gönderilmesin).
          // Aynı kapsamda yeniden yükleme (retry / yazma sonrası) listeyi korur, yalnız işlemler kilitlenir.
          if (key !== this.loadedScopeKey()) this.resetList();
          if (!scope) {
            this.loadedScopeKey.set(key);
            this.loading.set(false);
            return EMPTY;
          }
          this.loading.set(true);
          return this.service.list({ ...scope, includeInactive: true, skip: 0, take: PAGE_SIZE }).pipe(
            map((res): { key: string; res: StudyLinkListResponse } | null => ({ key, res })),
            catchError((err: unknown) => {
              this.links.set([]);
              if (isTeacherNotApprovedError(err)) this.markNotApproved(err);
              else this.error.set(studyLinkErrorMessage(err) ?? this.text('loadFailed'));
              return of(null);
            }),
            tap(() => this.loading.set(false))
          );
        }),
        takeUntilDestroyed()
      )
      .subscribe((loaded) => {
        if (!loaded) return;
        this.applyList(loaded.res);
        this.loadedScopeKey.set(loaded.key);
      });
  }

  reload(): void {
    this.reloadTick.update((n) => n + 1);
  }

  /** Öğretmen yalnız kendi eklediği linki düzenleyebilir / aktif-pasif yapabilir / silebilir. */
  canModify(link: StudyLink): boolean {
    if (this.notApproved()) return false;
    if (this.isAdmin) return true;
    const userId = this.currentUserId();
    return userId != null && link.createdByUserId === userId;
  }

  sourceIcon(link: Pick<StudyLink, 'sourceType'>): string {
    return link.sourceType === 'YouTube' ? 'smart_display' : 'link';
  }

  // ---- create / edit ----

  async openCreate(): Promise<void> {
    const scope = this.scope();
    if (!scope || this.addBlocked() || this.notApproved() || this.locked()) return;
    await this.openDialog({ scope }, 'created');
  }

  async openEdit(link: StudyLink): Promise<void> {
    const scope = this.scope();
    if (!scope || this.locked() || !this.canModify(link)) return;
    await this.openDialog({ scope, link }, 'updated');
  }

  private async openDialog(data: Pick<StudyLinkDialogData, 'scope' | 'link'>, successKey: string): Promise<void> {
    const saved = await firstValueFrom(
      this.dialog
        .open<StudyLinkDialogComponent, StudyLinkDialogData, StudyLink>(StudyLinkDialogComponent, {
          data: {
            ...data,
            scopeName: this.scopeName() || undefined,
            activeLimitReached: this.limitReached(),
            maxActiveLinks: this.maxActive(),
          },
          autoFocus: 'first-tabbable',
          width: '560px',
          maxWidth: '100vw',
        })
        .afterClosed()
    );
    if (!saved) return;
    this.notify(successKey);
    this.reload();
  }

  // ---- active toggle ----

  toggleActive(link: StudyLink, event: MatSlideToggleChange): void {
    if (this.locked() || !this.canModify(link)) {
      event.source.checked = link.isActive;
      return;
    }
    const isActive = event.checked;
    this.busy.set(true);
    this.service
      .update(link.id, { title: link.title, url: link.url, sourceType: link.sourceType, isActive })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.notify(isActive ? 'activated' : 'deactivated');
          this.reload();
        },
        error: (err: unknown) => {
          this.busy.set(false);
          // Bağlı değer değişmediği için görsel durum elle geri alınır (ör. 409 ActiveLimitReached).
          event.source.checked = link.isActive;
          this.showError(err);
          this.reload();
        },
      });
  }

  // ---- delete ----

  async remove(link: StudyLink): Promise<void> {
    if (this.locked() || !this.canModify(link)) return;
    const data: ConfirmDialogData = {
      title: this.text('deleteTitle'),
      message: this.text('deleteMessage', { title: link.title }),
      confirmText: this.text('deleteConfirm'),
      icon: 'delete',
      confirmColor: 'warn',
    };
    const ok = await firstValueFrom(this.dialog.open(ConfirmDialogComponent, { data }).afterClosed());
    if (!ok) return;

    this.busy.set(true);
    this.service.delete(link.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.notify('deleted');
        this.reload();
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.showError(err);
      },
    });
  }

  // ---- reorder ----

  drop(event: CdkDragDrop<StudyLink[]>): void {
    this.move(event.previousIndex, event.currentIndex);
  }

  moveUp(index: number): void {
    this.move(index, index - 1);
  }

  moveDown(index: number): void {
    this.move(index, index + 1);
  }

  /** Listeyi iyimser olarak yeniden sıralar ve tüm kapsamın sırasını sunucuya yazar; hata olursa geri alır. */
  private move(from: number, to: number): void {
    const scope = this.scope();
    const previous = this.links();
    if (!scope || this.locked() || this.notApproved() || from === to || to < 0 || to >= previous.length) return;

    const next = [...previous];
    moveItemInArray(next, from, to);
    this.links.set(next);

    this.busy.set(true);
    this.service
      .reorder({ ...scope, items: next.map((link, index) => ({ id: link.id, sortOrder: index })) })
      .subscribe({
        next: (res) => {
          this.busy.set(false);
          this.applyList(res);
        },
        error: (err: unknown) => {
          this.busy.set(false);
          this.links.set(previous);
          this.showError(err);
        },
      });
  }

  // ---- helpers ----

  private resetList(): void {
    this.links.set([]);
    this.activeCount.set(0);
    this.totalCount.set(0);
    this.loadedScopeKey.set(null);
  }

  private applyList(res: StudyLinkListResponse): void {
    this.links.set([...res.items].sort((a, b) => a.sortOrder - b.sortOrder || a.id - b.id));
    this.activeCount.set(res.activeCount);
    this.maxActive.set(res.maxActiveLinks || MAX_ACTIVE_STUDY_LINKS);
    this.totalCount.set(res.totalCount);
  }

  private markNotApproved(err: unknown): void {
    this.notApproved.set(studyLinkErrorMessage(err) ?? this.text('notApproved'));
  }

  /**
   * Yazma hatası → snackbar. Sunucu metni (409 ActiveLimitReached/TotalLimitReached, 403 NotOwner, 429 düz metin)
   * önceliklidir; 429 gövdesi boşsa Retry-After ile yerel metin. Onay kaldırıldıysa (403 TeacherNotApproved)
   * kontroller de gizlenir.
   */
  private showError(err: unknown): void {
    if (isTeacherNotApprovedError(err)) this.markNotApproved(err);
    const retryAfter = rateLimitRetryAfter(err);
    const fallback =
      retryAfter === undefined
        ? this.text('actionFailed')
        : retryAfter === null
          ? this.text('rateLimitedNoWait')
          : this.text('rateLimited', { seconds: retryAfter });
    this.snack.open(studyLinkErrorMessage(err) ?? fallback, this.text('close'), { duration: 5000 });
  }

  private notify(key: string): void {
    this.snack.open(this.text(key), this.text('close'), { duration: 3000 });
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${STUDY_LINKS_SCOPE}.manager.${key}`, params) ?? '';
  }
}

/** Kapsamın karşılaştırma anahtarı (`null` → "none"). */
function scopeKey(scope: StudyLinkScope | null): string {
  if (!scope) return 'none';
  return scope.subTopicId != null ? `subTopic:${scope.subTopicId}` : `topic:${scope.topicId}`;
}
