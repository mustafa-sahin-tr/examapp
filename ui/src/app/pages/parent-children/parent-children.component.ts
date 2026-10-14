import { Clipboard } from '@angular/cdk/clipboard';
import { HttpErrorResponse } from '@angular/common/http';
import { AfterViewInit, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable, filter, finalize, switchMap } from 'rxjs';

import {
  CoParent,
  LinkedChild,
  PARENT_INVITE_CODE_LENGTH,
  PARENT_LINK_ERROR_CODES,
  ParentInviteCode,
  formatInviteCode,
  normalizeInviteCodeInput,
} from '../../models/parent-link.model';
import { ParentLinkService } from '../../services/parent-link.service';
import { PARENT_LINKS_SCOPE, ParentLinkUi } from '../../shared/utils/parent-link-ui';
import {
  ChildPasswordResetDialogComponent,
  ChildPasswordResetDialogData,
} from './child-password-reset-dialog/child-password-reset-dialog.component';
import { CreateChildAccountComponent } from './create-child-account/create-child-account.component';
import { childTutorSearchCommands } from '../../shared/utils/booking-child-context';

/** Öğrenci başına açık veli tavanı (sunucu `maxParents` göndermezse). */
const DEFAULT_MAX_PARENTS = 4;

/**
 * Issue #419 → issue #436 (epic #435, veli-öncelikli model): velinin "Çocuklarım" sayfası. "İkinci veli olarak bağlan" formu
 * (çocuğun birincil velisinin ürettiği kod) bir İSTEK açar; birincil veli onaylayana kadar satır "Onay bekleniyor" görünür ve
 * öğrenciye ait hiçbir veri gelmez (iptal edilebilir). Birincil veli her çocuk için diğer velileri ve bekleyen istekleri görür,
 * onaylar/reddeder, diğer velileri kaldırır ve "ikinci veli davet kodu" üretir (düz kod yalnız bu oturumda bellekte). Birincil
 * olmayan veli başkalarının bağlantısını yönetemez (sunucu 403) ama kendi bağlantısından ayrılabilir; çocuğun tek velisi
 * ayrılamaz (düğme gösterilmez, sunucu 409 LastParentCannotLeave).
 * Issue #438: "Çocuk hesabı oluştur" formu (kullanıcı adı + şifre, e-postasız) ve veli tarafından açılmış çocuklar için
 * "Şifre sıfırla" (yeni şifreyi veli belirler; hiçbir yerde gösterilmez). `/my-children#create-child` forma kaydırır.
 * Issue #439: Active çocuk için "Ders talebi oluştur" → `/my-children/<studentId>/tutors` (veli modunda öğretmen arama).
 */
@Component({
  selector: 'app-parent-children',
  standalone: true,
  imports: [
    CreateChildAccountComponent,
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    RouterLink,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(PARENT_LINKS_SCOPE)],
  templateUrl: './parent-children.component.html',
  styleUrls: ['./parent-children.component.scss'],
})
export class ParentChildrenComponent implements OnInit, AfterViewInit {
  private readonly service = inject(ParentLinkService);
  private readonly clipboard = inject(Clipboard);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly route = inject(ActivatedRoute, { optional: true });
  protected readonly ui = inject(ParentLinkUi);

  protected readonly codeLength = PARENT_INVITE_CODE_LENGTH;
  /** Issue #439: çocuk adına özel ders talebi akışının başlangıcı. */
  protected readonly bookLessonLink = childTutorSearchCommands;
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly children = signal<LinkedChild[]>([]);
  protected readonly code = signal('');
  protected readonly submitting = signal(false);
  protected readonly redeemError = signal<string | null>(null);
  protected readonly revokingId = signal<number | null>(null);
  protected readonly decidingId = signal<number | null>(null);
  protected readonly generatingId = signal<number | null>(null);
  /** Bu oturumda üretilen ikinci veli kodları (çocuğun bağlantı id'si → kod) — yalnız bellekte. */
  protected readonly newCodes = signal<Record<number, ParentInviteCode>>({});

  protected readonly canSubmit = computed(
    () => !this.submitting() && normalizeInviteCodeInput(this.code()).length === PARENT_INVITE_CODE_LENGTH
  );
  /**
   * Issue #438: açık bağlantı sayısı — çocuk hesabı formu tavanı buna göre gizler. Sunucu kuralı `ParentLinkPrimary.IsOpen(now)`:
   * Active ∨ süresi dolmamış Pending (taraflar silinmemiş). my-children listesi zaten bu kuralla süzülür; istemci yalnız liste
   * alındıktan sonra süresi dolan Pending'leri düşer (`pendingExpiresAt` sunucuda kuruluş yoluna göre hesaplanır). Yenileme
   * sırasında son bilinen liste geçerli kalır; liste yüklenemezse bilinmiyor (null): form açık kalır, tavanı sunucu yine uygular.
   */
  protected readonly openChildCount = computed(() => {
    if (this.loadError()) return null;
    const now = Date.now();
    return this.children().filter((c) => isOpenLink(c, now)).length;
  });
  protected readonly isEmpty = computed(() => !this.loading() && !this.loadError() && this.children().length === 0);
  /** Bu oturumda üretilen kodların görünen biçimi (bağlantı id'si → XXXX-XXXX-XXXX). */
  protected readonly displayCodes = computed(() => {
    const formatted: Record<number, string> = {};
    for (const [linkId, code] of Object.entries(this.newCodes())) {
      formatted[Number(linkId)] = formatInviteCode(code.code);
    }
    return formatted;
  });

  ngOnInit(): void {
    this.load();
  }

  /** Veli paneli boş durumu `/my-children#create-child` ile gelir: forma kaydır. */
  ngAfterViewInit(): void {
    if (this.route?.snapshot.fragment === 'create-child') {
      setTimeout(() => document.getElementById('create-child')?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
    }
  }

  /** Issue #438: yeni çocuk hesabı açıldı — liste sunucudan tazelenir (birincil Active bağlantı). */
  protected onChildCreated(): void {
    this.load();
  }

  /** Issue #438: veli tarafından açılmış çocuğun şifresini veli belirler (diyalog; şifre yanıtta/ekranda yok). */
  protected resetPassword(child: LinkedChild): void {
    if (!child.studentId) return;
    const data: ChildPasswordResetDialogData = { studentId: child.studentId, studentName: child.studentName ?? '' };
    this.dialog
      .open(ChildPasswordResetDialogComponent, { data, autoFocus: 'first-tabbable' })
      .afterClosed()
      .pipe(
        filter((ok) => ok === true),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(() => this.ui.toast(this.ui.text('resetPw.done')));
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

  /**
   * Kendi bağlantısı: birincil olmayan veli her zaman ayrılabilir; birincil veli yalnız başka bir Active veli varsa (o birincil
   * olur). Çocuğun tek velisi ayrılamaz — her öğrencinin velisi olmalı (#437).
   */
  protected canLeave(child: LinkedChild): boolean {
    return !child.isPrimary || (child.coParents ?? []).some((c) => c.status === 'Active');
  }

  /** Kendi Active bağlantısından ayrılır ya da kendi bekleyen isteğini iptal eder (aynı uç). */
  protected revoke(child: LinkedChild): void {
    const pending = child.status === 'Pending';
    const prefix = pending ? 'children.cancel' : child.isPrimary ? 'children.revoke' : 'children.leave';
    this.ui
      .confirm(`${prefix}Confirm`, { name: child.studentName ?? '' })
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
          this.ui.toast(this.ui.text(pending ? 'children.cancelled' : child.isPrimary ? 'children.revoked' : 'children.left'));
        },
        error: (err: HttpErrorResponse) => {
          this.ui.toast(this.service.extractError(err, this.ui.text('children.revokeError')));
          if (this.service.errorCode(err) === PARENT_LINK_ERROR_CODES.lastParentCannotLeave) {
            this.load();
          }
        },
      });
  }

  // ---- birincil veli: diğer veliler ----------------------------------------------------------------------------------

  /**
   * Açık veli sayısı tavanda mı — sunucunun sayımı (`openParents`: birincil + Active + tüm bekleyen istekler, geçiş dönemindeki
   * eski istekler dahil); alan yoksa listedeki satırlardan tahmin.
   */
  protected limitReached(child: LinkedChild): boolean {
    const open = child.openParents ?? 1 + (child.coParents?.length ?? 0);
    return open >= (child.maxParents || DEFAULT_MAX_PARENTS);
  }

  protected newCodeExpiry(child: LinkedChild): string | null {
    return this.newCodes()[child.linkId]?.expiresAt ?? null;
  }

  protected generateCode(child: LinkedChild): void {
    if (this.generatingId() !== null) return;
    this.generatingId.set(child.linkId);
    this.service
      .createSecondParentCode(child.linkId)
      .pipe(
        finalize(() => this.generatingId.set(null)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (code) => {
          this.newCodes.update((map) => ({ ...map, [child.linkId]: code }));
          this.children.update((list) =>
            list.map((c) => (c.linkId === child.linkId ? { ...c, secondParentCodeExpiresAt: code.expiresAt } : c))
          );
        },
        error: (err: HttpErrorResponse) => {
          const errorCode = this.service.errorCode(err);
          if (
            errorCode === PARENT_LINK_ERROR_CODES.studentLimitReached ||
            errorCode === PARENT_LINK_ERROR_CODES.notPrimaryParent
          ) {
            this.load();
          }
          this.ui.toast(this.service.extractError(err, this.ui.text('family.generateError')));
        },
      });
  }

  protected copyCode(child: LinkedChild): void {
    const code = this.newCodes()[child.linkId];
    if (!code) return;
    const ok = this.clipboard.copy(code.code);
    this.ui.toast(this.ui.text(ok ? 'family.copied' : 'family.copyError'));
  }

  protected approveRequest(request: CoParent): void {
    this.decide(request, this.service.approve(request.linkId), 'family.approved', 'family.approveError');
  }

  protected rejectRequest(request: CoParent): void {
    this.decide(request, this.service.reject(request.linkId), 'family.rejected', 'family.rejectError');
  }

  protected removeCoParent(coParent: CoParent): void {
    this.ui
      .confirm('family.removeConfirm', { name: coParent.parentName })
      .pipe(
        filter((ok) => ok),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(() => this.decide(coParent, this.service.revoke(coParent.linkId), 'family.removed', 'family.removeError'));
  }

  /** Onay / ret / kaldırma sonrası liste sunucudan tazelenir (birincil devri ve tavan sunucuda hesaplanır). */
  private decide(target: CoParent, call: Observable<void>, successKey: string, errorKey: string): void {
    if (this.decidingId() !== null) return;
    this.decidingId.set(target.linkId);
    call
      .pipe(
        finalize(() => this.decidingId.set(null)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          this.ui.toast(this.ui.text(successKey, { name: target.parentName }));
          this.load();
        },
        error: (err: HttpErrorResponse) => {
          this.ui.toast(this.service.extractError(err, this.ui.text(errorKey)));
          this.load();
        },
      });
  }
}

/** Sunucunun `ParentLinkPrimary.IsOpen` kuralının istemci karşılığı: Active ya da bitişi gelmemiş Pending. */
function isOpenLink(child: LinkedChild, now: number): boolean {
  if (child.status === 'Active') return true;
  if (child.status !== 'Pending') return false;
  const expiresAt = child.pendingExpiresAt ? Date.parse(child.pendingExpiresAt) : NaN;
  // Bitiş bilinmiyorsa (eski yanıt) sunucu açık saydığı için listelemiştir: açık say.
  return Number.isNaN(expiresAt) || expiresAt > now;
}
