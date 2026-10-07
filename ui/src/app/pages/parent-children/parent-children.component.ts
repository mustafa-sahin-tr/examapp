import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { filter, finalize, switchMap } from 'rxjs';

import { LinkedChild, PARENT_INVITE_CODE_LENGTH, normalizeInviteCodeInput } from '../../models/parent-link.model';
import { ParentLinkService } from '../../services/parent-link.service';
import { PARENT_LINKS_SCOPE, ParentLinkUi } from '../../shared/utils/parent-link-ui';

/**
 * Issue #419 (epic #407 V1): velinin "Çocuklarım" sayfası — "Çocuk ekle" (davet kodu) formu + bağlı çocukların minimal
 * listesi (ad, sınıf, okul) ve bağlantıyı kaldırma. Kod bir İSTEK açar; çocuk onaylayana kadar satır "Onay bekleniyor"
 * olarak görünür ve öğrenciye ait hiçbir veri gelmez (iptal edilebilir). Tam veli paneli V2'de (#420).
 */
@Component({
  selector: 'app-parent-children',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(PARENT_LINKS_SCOPE)],
  templateUrl: './parent-children.component.html',
  styleUrls: ['./parent-children.component.scss'],
})
export class ParentChildrenComponent implements OnInit {
  private readonly service = inject(ParentLinkService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly ui = inject(ParentLinkUi);

  protected readonly codeLength = PARENT_INVITE_CODE_LENGTH;
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly children = signal<LinkedChild[]>([]);
  protected readonly code = signal('');
  protected readonly submitting = signal(false);
  protected readonly redeemError = signal<string | null>(null);
  protected readonly revokingId = signal<number | null>(null);

  protected readonly canSubmit = computed(
    () => !this.submitting() && normalizeInviteCodeInput(this.code()).length === PARENT_INVITE_CODE_LENGTH
  );
  protected readonly isEmpty = computed(() => !this.loading() && !this.loadError() && this.children().length === 0);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.service
      .getMyChildren()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (items) => this.children.set(items ?? []),
        error: (err: HttpErrorResponse) =>
          this.loadError.set(this.service.extractError(err, this.ui.text('children.loadError'))),
      });
  }

  protected onCodeInput(value: string): void {
    this.code.set(value);
    this.redeemError.set(null);
  }

  protected submit(): void {
    if (!this.canSubmit()) return;
    this.submitting.set(true);
    this.redeemError.set(null);
    this.service
      .redeem(normalizeInviteCodeInput(this.code()))
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (child) => {
          this.code.set('');
          this.children.update((list) => [...list.filter((c) => c.linkId !== child.linkId), child]);
          this.ui.toast(this.ui.text('children.requested'));
        },
        error: (err: HttpErrorResponse) =>
          this.redeemError.set(this.service.extractError(err, this.ui.text('children.redeemError'))),
      });
  }

  /** Aktif bağlantıyı kaldırır ya da bekleyen isteği iptal eder (aynı uç). */
  protected revoke(child: LinkedChild): void {
    const pending = child.status === 'Pending';
    this.ui
      .confirm(pending ? 'children.cancelConfirm' : 'children.revokeConfirm', { name: child.studentName ?? '' })
      .pipe(
        filter((ok) => ok),
        switchMap(() => {
          this.revokingId.set(child.linkId);
          return this.service.revoke(child.linkId).pipe(finalize(() => this.revokingId.set(null)));
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          this.children.update((list) => list.filter((c) => c.linkId !== child.linkId));
          this.ui.toast(this.ui.text(pending ? 'children.cancelled' : 'children.revoked'));
        },
        error: (err: HttpErrorResponse) =>
          this.ui.toast(this.service.extractError(err, this.ui.text('children.revokeError'))),
      });
  }
}
