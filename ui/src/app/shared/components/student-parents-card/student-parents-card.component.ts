import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { Observable, finalize } from 'rxjs';

import { LinkedParent, PendingParentRequest } from '../../../models/parent-link.model';
import { ParentLinkService } from '../../../services/parent-link.service';
import { PARENT_LINKS_SCOPE, ParentLinkUi } from '../../utils/parent-link-ui';

/** "Velim ne görüyor?" bölümündeki görebildikleri / göremedikleri (çeviri anahtarları `parents.sees.*`, `parents.hidden.*`). */
export const PARENT_SEES_KEYS = ['summary', 'assignments', 'progress', 'schedule'] as const;
export const PARENT_HIDDEN_KEYS = ['messages', 'comments', 'answers', 'contact'] as const;

/**
 * Issue #436 (epic #435): öğrenci Ayarlar'ındaki (`/student-profile`) "Velilerim" kartı — SALT OKUNUR. Veli-öncelikli modelde
 * bağlantıyı veli kurar: öğrenci kod üretmez, yeni istek onaylamaz, bağlantı koparmaz. Kart bağlı velileri (ad + birincil
 * işareti) ve "Velim ne görüyor?" açıklamasını (#424'ten taşındı) gösterir. Geçiş dönemi: #419'dan kalan bekleyen istekler
 * (oluşturulmadan 30 gün) burada hâlâ onaylanıp reddedilebilir; yeni istekler buraya hiç düşmez.
 */
@Component({
  selector: 'app-student-parents-card',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope(PARENT_LINKS_SCOPE)],
  templateUrl: './student-parents-card.component.html',
  styleUrl: './student-parents-card.component.scss',
})
export class StudentParentsCardComponent implements OnInit {
  private readonly service = inject(ParentLinkService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly ui = inject(ParentLinkUi);

  protected readonly seesKeys = PARENT_SEES_KEYS;
  protected readonly hiddenKeys = PARENT_HIDDEN_KEYS;

  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly parents = signal<LinkedParent[]>([]);
  protected readonly legacyRequests = signal<PendingParentRequest[]>([]);
  protected readonly requiresParent = signal(true);
  protected readonly decidingId = signal<number | null>(null);

  /** Issue #437: velisi olmayan öğrenciye bilgi (yaptırım #440/#441'de). */
  protected readonly missingParent = computed(
    () => !this.loading() && !this.loadError() && this.requiresParent() && this.parents().length === 0
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
          this.legacyRequests.set(res.pendingRequests ?? []);
          this.requiresParent.set(res.requiresParent ?? true);
        },
        error: (err: HttpErrorResponse) =>
          this.loadError.set(this.service.extractError(err, this.ui.text('parents.loadError'))),
      });
  }

  /** Geçiş dönemi: eski isteği onaylar; liste sunucudan tazelenir. */
  protected approve(request: PendingParentRequest): void {
    this.decide(request, this.service.approve(request.linkId), 'parents.approved', 'parents.approveError');
  }

  protected reject(request: PendingParentRequest): void {
    this.decide(request, this.service.reject(request.linkId), 'parents.rejected', 'parents.rejectError');
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
