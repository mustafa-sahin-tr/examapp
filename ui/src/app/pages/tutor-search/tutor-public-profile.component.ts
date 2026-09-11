import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { Location } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { finalize } from 'rxjs';
import { TutorPublicProfile } from '../../models/tutor.model';
import { TeacherService } from '../../services/teacher.service';

/**
 * Issue #95 — öğrencinin gördüğü tekil öğretmen profili (dersler, ders şekli, ücret, tam tanıtım).
 * Backend 404'ü "kayıt yok / onaylı değil" ayrımı yapmadan döner; UI de generic mesaj gösterir.
 */
@Component({
  selector: 'app-tutor-public-profile',
  standalone: true,
  imports: [MatButtonModule, MatChipsModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './tutor-public-profile.component.html',
  styleUrls: ['./tutor-public-profile.component.scss'],
})
export class TutorPublicProfileComponent implements OnInit {
  private readonly teacherService = inject(TeacherService);
  private readonly location = inject(Location);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);

  /** Route parametresi; uygulamada component input binding açık değil, paramMap'ten okunur. */
  private readonly teacherId = Number(this.route.snapshot.paramMap.get('id'));

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly notFound = signal(false);
  readonly profile = signal<TutorPublicProfile | null>(null);

  readonly initials = computed(() => {
    const parts = (this.profile()?.fullName ?? '').trim().split(/\s+/).filter(Boolean);
    if (!parts.length) {
      return '?';
    }
    return parts
      .slice(0, 2)
      .map((p) => p.charAt(0).toLocaleUpperCase('tr-TR'))
      .join('');
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    if (!Number.isFinite(this.teacherId)) {
      this.loading.set(false);
      this.notFound.set(true);
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    this.notFound.set(false);

    this.teacherService
      .getTutorPublicProfile(this.teacherId)
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (profile) => this.profile.set(profile),
        error: (err: HttpErrorResponse) => {
          this.profile.set(null);
          if (err.status === 404) {
            this.notFound.set(true);
            return;
          }
          const body = err.error as { message?: string } | null;
          this.error.set(body?.message || 'Öğretmen profili yüklenirken bir sorun oluştu.');
        },
      });
  }

  goBack(): void {
    this.location.back();
  }
}
