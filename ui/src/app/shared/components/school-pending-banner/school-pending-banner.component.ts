import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService } from '../../../services/auth.service';

/**
 * Issue #361: öğrencinin kendi kaydında seçtiği okul üyeliği henüz onaylanmadıysa "Okul onayı bekleniyor" bandı.
 * Kaynak reaktif profil (`AuthService.user`, `POST /api/exam/auth/refresh` → `student.pendingSchoolName`); onay sonrası
 * profil tazelenince band kendiliğinden kaybolur. Bekleyen üyelik yoksa hiçbir şey render edilmez.
 */
@Component({
  selector: 'app-school-pending-banner',
  standalone: true,
  imports: [MatIconModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (pending(); as p) {
      <div class="school-pending" role="status">
        <mat-icon class="school-pending__icon" aria-hidden="true">hourglass_top</mat-icon>
        <div class="school-pending__text">
          <strong>{{ 'dashboard.schoolPending.title' | transloco }}</strong>
          @if (p.school) {
            <span>{{ 'dashboard.schoolPending.body' | transloco: { school: p.school } }}</span>
          } @else {
            <span>{{ 'dashboard.schoolPending.bodyGeneric' | transloco }}</span>
          }
        </div>
      </div>
    }
  `,
  styleUrl: './school-pending-banner.component.scss',
})
export class SchoolPendingBannerComponent {
  private readonly auth = inject(AuthService);

  /** Bekleyen üyelik (yoksa null). Okul adı gelmediyse (ad çözülemedi / eski yanıt) genel metin gösterilir. */
  protected readonly pending = computed<{ school: string | null } | null>(() => {
    const student = this.auth.user()?.student;
    if (student?.pendingSchoolId == null) {
      return null;
    }
    return { school: student.pendingSchoolName?.trim() || null };
  });
}
