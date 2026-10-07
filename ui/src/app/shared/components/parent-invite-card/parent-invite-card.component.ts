import { Clipboard } from '@angular/cdk/clipboard';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { Observable, filter, finalize, switchMap } from 'rxjs';

import {
  LinkedParent,
  PARENT_LINK_ERROR_CODES,
  PendingParentRequest,
  ParentInviteCode,
  formatInviteCode,
} from '../../../models/parent-link.model';
import { ParentLinkService } from '../../../services/parent-link.service';
import { PARENT_LINKS_SCOPE, ParentLinkUi } from '../../utils/parent-link-ui';

export { PARENT_LINKS_SCOPE } from '../../utils/parent-link-ui';

/**
 * Issue #419: öğrenci Ayarlar'ındaki (`/student-profile`) "Veli davet kodu" kartı. Kod üretir (düz kod yalnızca üretim
 * yanıtında gelir ve yalnızca bu oturumda bellekte tutulur — sayfadan çıkınca kaybolur), kopyalatır, veli kodu girince
 * oluşan bekleyen istekleri (ad + maskeli e-posta + süre) onaylatır/reddettirir (review: kod tek başına bağlamaz), bağlı
 * velileri listeler ve koparır.
 */
@Component({
  selector: 'app-parent-invite-card',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, TranslocoDirective],
  providers: [provideTranslocoScope(PARENT_LINKS_SCOPE)],
  templateUrl: './parent-invite-card.component.html',
  styleUrl: './parent-invite-card.component.scss',
})
export class ParentInviteCardComponent implements OnInit {
  private readonly service = inject(ParentLinkService);
  private readonly clipboard = inject(Clipboard);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly ui = inject(ParentLinkUi);

  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly generating = signal(false);
  protected readonly revokingId = signal<number | null>(null);
  protected readonly parents = signal<LinkedParent[]>([]);
  protected readonly pendingRequests = signal<PendingParentRequest[]>([]);
  protected readonly decidingId = signal<number | null>(null);
  protected readonly maxParents = signal(4);
  protected readonly activeInviteExpiresAt = signal<string | null>(null);
  /** Bu oturumda üretilen kod — yalnızca bellekte; sayfa yenilenince tekrar gösterilmez. */
  protected readonly newCode = signal<ParentInviteCode | null>(null);

  protected readonly displayCode = computed(() => {
    const code = this.newCode();
    return code ? formatInviteCode(code.code) : null;
  });
  /** Tavan aktif + bekleyen bağlantıları sayar (sunucuyla aynı kural). */
  protected readonly limitReached = computed(
    () => this.parents().length + this.pendingRequests().length >= this.maxParents()
  );

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.service
      .getMyParents()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => {
          this.parents.set(res.items ?? []);
          this.pendingRequests.set(res.pendingRequests ?? []);
          this.maxParents.set(res.maxActiveParents || 4);
          this.activeInviteExpiresAt.set(res.activeInviteExpiresAt ?? null);
        },
        error: (err: HttpErrorResponse) =>
          this.loadError.set(this.service.extractError(err, this.ui.text('invite.loadError'))),
      });
  }

  protected generate(): void {
    if (this.generating()) return;
    this.generating.set(true);
    this.service
      .createInviteCode()
      .pipe(
        finalize(() => this.generating.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (code) => {
          this.newCode.set(code);
          this.activeInviteExpiresAt.set(code.expiresAt);
        },
        error: (err: HttpErrorResponse) => {
          if (this.service.errorCode(err) === PARENT_LINK_ERROR_CODES.studentLimitReached) {
            this.load();
          }
          this.ui.toast(this.service.extractError(err, this.ui.text('invite.generateError')));
        },
      });
  }

  protected copy(): void {
    const code = this.newCode();
    if (!code) return;
    const ok = this.clipboard.copy(code.code);
    this.ui.toast(this.ui.text(ok ? 'invite.copied' : 'invite.copyError'));
  }

  protected revoke(parent: LinkedParent): void {
    this.ui
      .confirm('invite.revokeConfirm', { name: parent.parentName })
      .pipe(
        filter((ok) => ok),
        switchMap(() => {
          this.revokingId.set(parent.linkId);
          return this.service.revoke(parent.linkId).pipe(finalize(() => this.revokingId.set(null)));
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          this.parents.update((list) => list.filter((p) => p.linkId !== parent.linkId));
          this.ui.toast(this.ui.text('invite.revoked'));
        },
        error: (err: HttpErrorResponse) =>
          this.ui.toast(this.service.extractError(err, this.ui.text('invite.revokeError'))),
      });
  }

  /** Bekleyen isteği onaylar; liste sunucudan tazelenir (onaylanan veli "Bağlı veliler"e geçer). */
  protected approve(request: PendingParentRequest): void {
    this.decide(request, this.service.approve(request.linkId), 'invite.approved', 'invite.approveError');
  }

  protected reject(request: PendingParentRequest): void {
    this.decide(request, this.service.reject(request.linkId), 'invite.rejected', 'invite.rejectError');
  }

  private decide(request: PendingParentRequest, call: Observable<void>, successKey: string, errorKey: string): void {
    if (this.decidingId() !== null) return;
    this.decidingId.set(request.linkId);
    call
      .pipe(
        finalize(() => this.decidingId.set(null)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          this.ui.toast(this.ui.text(successKey));
          this.load();
        },
        error: (err: HttpErrorResponse) => {
          this.ui.toast(this.service.extractError(err, this.ui.text(errorKey)));
          this.load();
        },
      });
  }
}
