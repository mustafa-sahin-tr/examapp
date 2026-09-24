import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { AuthService } from '../../services/auth.service';
import { SignalRService } from '../../services/signalr.service';

/** Çeviriler kendi Transloco scope'unda: `public/i18n/teacher-approval/<lang>.json`. */
const TEACHER_APPROVAL_SCOPE = 'teacher-approval';

/**
 * Sayfanın gösterdiği durum. `approved`: hesap onaylı (öğretmen özellikleri açık); `pending`: ilk başvuru
 * inceleniyor; `rejected`: başvuru reddedildi (gerekçe gösterilir); `noRecord`: öğretmen kaydı yok (kayıt tamamlanmamış).
 */
export type TeacherApprovalView = 'approved' | 'pending' | 'rejected' | 'noRecord';

/**
 * Issue #287 — öğretmen hesabı onay durumu. Onaysız öğretmenin tek öğretmen ekranı: guard/interceptor buraya
 * yönlendirir. Durum `POST /api/exam/auth/refresh` profilinden (`teacher.teacherAccountApproved`,
 * `teacherApplicationStatus`, `rejectionReason`) okunur; sayfa açılışta ve "Durumu yenile" ile profili tazeler.
 * Karar push'u (SignalR `TeacherApplicationDecided`) gelince de kendiliğinden yenilenir.
 * Bu sayfa hiçbir öğretmen ucunu çağırmaz — 403 `TeacherNotApproved` döngüsü oluşmaz.
 */
@Component({
  selector: 'app-teacher-approval-pending',
  standalone: true,
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule, RouterLink, TranslocoDirective],
  providers: [provideTranslocoScope(TEACHER_APPROVAL_SCOPE)],
  templateUrl: './teacher-approval-pending.component.html',
  styleUrl: './teacher-approval-pending.component.scss',
})
export class TeacherApprovalPendingComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly signalR = inject(SignalRService);
  private readonly destroyRef = inject(DestroyRef);

  readonly loading = signal(false);
  readonly error = signal(false);
  /** İlk yenileme bitti mi — bitmeden önbellekteki eksik profil "kayıt yok" diye gösterilmez. */
  readonly loaded = signal(false);
  /** Sunucu profil döndürmedi (refresh → null): öğretmen kaydı tamamlanmamış. */
  private readonly profileMissing = signal(false);

  readonly view = computed<TeacherApprovalView>(() => {
    const teacher = this.auth.user()?.teacher;
    if (this.profileMissing() || !teacher) {
      return 'noRecord';
    }
    if (teacher.teacherAccountApproved === true && !this.auth.isUnapprovedTeacher()) {
      return 'approved';
    }
    return teacher.teacherApplicationStatus === 'Rejected' ? 'rejected' : 'pending';
  });

  readonly rejectionReason = computed(() => this.auth.user()?.teacher?.rejectionReason?.trim() || null);

  /** İlk yüklemede tam spinner; sonraki yenilemelerde içerik kalır, buton meşgul olur. */
  readonly showSpinner = computed(() => this.loading() && !this.loaded());

  ngOnInit(): void {
    this.refresh();
    this.signalR.teacherApplicationDecided$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.refresh());
  }

  /** Profili (ve onay durumunu) sunucudan yeniden okur. */
  refresh(): void {
    if (this.loading()) {
      return;
    }
    this.loading.set(true);
    this.error.set(false);
    this.auth
      .refreshProfile()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (profile) => {
          this.profileMissing.set(!profile);
          this.loading.set(false);
          this.loaded.set(true);
        },
        error: () => {
          this.loading.set(false);
          this.error.set(true);
        },
      });
  }

  goToDashboard(): void {
    void this.router.navigate(['/dashboard']);
  }
}
