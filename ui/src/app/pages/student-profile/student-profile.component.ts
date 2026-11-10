import { Component, DestroyRef, OnInit, ChangeDetectionStrategy, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { combineLatest, take } from 'rxjs';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';

import { StudentProfile } from '../../models/student-profile';
import { BadgeProgressSummary, BadgeService } from '../../services/badge.service';
import { AuthService } from '../../services/auth.service';
import { EntitlementStateService } from '../../services/entitlement-state.service';
import { StudentService } from '../../services/student.service';
import { STUDENT_SETTINGS_URL } from '../../shared/guards/settings.guard';
import { currentUserId } from '../../shared/utils/current-user-id.util';
import { LeaderboardComponent } from '../../shared/components/leaderboard/leaderboard.component';
import { PlanLockedCardComponent } from '../../shared/components/plan-locked-card/plan-locked-card.component';
import { OutcomeMasteryCardComponent } from '../../shared/components/outcome-mastery-card/outcome-mastery-card.component';
import { TopicProgressCardComponent } from '../../shared/components/topic-progress-card/topic-progress-card.component';
import { WeakTopicsCardComponent } from '../../shared/components/weak-topics-card/weak-topics-card.component';

/** Sayfanin Transloco scope'u: `public/i18n/student-profile/<lang>.json` (issue #183). */
const SCOPE = 'student-profile';

/**
 * Öğrencinin profili (#847): kim olduğu (avatar, ad, sınıf · okul, seviye) ve gelişimi. Ayarlar (sınıf, tema, hesap, veliler)
 * ayrı sayfada (`/student-settings`); rozetler kendi sayfasında (`/badges`).
 *
 * Sayılar tek kaynaktan: çözülen soru ve doğru cevap, ana sayfadaki "Son 4 hafta" ile aynı BadgeService özetinden (tüm
 * zamanlar); biten test sayısı profil ucundan (#501, tamamlanmış oturumlar).
 */
@Component({
  selector: 'app-student-profile',
  templateUrl: './student-profile.component.html',
  standalone: true,
  styleUrls: ['./student-profile.component.scss'],
  imports: [
    MatButtonModule,
    MatIconModule,
    RouterLink,
    TranslocoDirective,
    LeaderboardComponent,
    OutcomeMasteryCardComponent,
    PlanLockedCardComponent,
    TopicProgressCardComponent,
    WeakTopicsCardComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [provideTranslocoScope(SCOPE)],
})
export class StudentProfileComponent implements OnInit {
  private readonly studentService = inject(StudentService);
  private readonly badgeService = inject(BadgeService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly auth = inject(AuthService);
  private readonly entitlements = inject(EntitlementStateService);

  protected readonly settingsUrl = STUDENT_SETTINGS_URL;
  readonly student = signal<StudentProfile | null>(null);
  readonly gradeName = signal('');
  readonly summary = signal<BadgeProgressSummary | null>(null);

  /** Profil ucu adı boş dönebiliyor; oturumdaki kullanıcının adı yedek. */
  readonly fullName = computed(() => this.student()?.fullName || this.auth.user()?.fullName || '');
  readonly initials = computed(() =>
    this.fullName()
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toLocaleUpperCase('tr'))
      .join('')
  );
  /** Issue #1089: analiz kartları (konu, gelişim, kazanım) planda `analytics` ister; plan okunana dek hiçbiri istek atmaz. */
  readonly analytics = computed(() => (this.entitlements.loaded() ? this.entitlements.has('analytics') : null));
  /** "5. Sınıf · Okul adı" — eksik parça yazılmaz. */
  readonly subtitle = computed(() => [this.gradeName(), this.student()?.schoolName].filter(Boolean).join(' · '));

  ngOnInit(): void {
    this.entitlements.load().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.studentService
      .getProfile()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((profile) => {
        this.student.set(profile);
        this.loadGradeName(profile?.gradeId);
      });

    const userId = currentUserId();
    if (userId) {
      this.badgeService
        .getUserBadgeProgress(userId)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({ next: (response) => this.summary.set(response?.summary ?? null), error: () => this.summary.set(null) });
    }
  }

  onAvatarChange(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) {
      this.studentService.updateAvatar(file).subscribe((response) => {
        this.student.set(response);
        this.notify('profile.avatarUpdated');
      });
    }
  }

  private loadGradeName(gradeId: number | undefined): void {
    if (!gradeId) return;
    this.studentService
      .loadGrades()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((grades) => this.gradeName.set(grades.find((g) => g.id === gradeId)?.name ?? ''));
  }

  /**
   * Snackbar metni sablon disinda oldugu icin scope once `selectTranslate` ile yuklenir; sozluk
   * hazir oldugunda aksiyon etiketi senkron okunabilir.
   */
  private notify(messageKey: string): void {
    combineLatest([
      this.transloco.selectTranslate<string>(messageKey, {}, SCOPE),
      this.transloco.selectTranslate<string>('actions.ok', {}, SCOPE),
    ])
      .pipe(take(1), takeUntilDestroyed(this.destroyRef))
      .subscribe(([message, action]) => {
        this.snackBar.open(message, action, { duration: 3000 });
      });
  }
}
