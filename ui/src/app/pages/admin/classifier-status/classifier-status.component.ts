import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective } from '@jsverse/transloco';
import { ClassifierStatus } from '../../../models/classifier-status.model';
import { ClassifierStatusService } from '../../../services/classifier-status.service';
import { LocaleService } from '../../../services/locale.service';
import { ClassifierCacheComponent } from '../classifier-cache/classifier-cache.component';

export type ClassifierState = 'running' | 'disabled' | 'noKey';

export function classifierState(status: ClassifierStatus): ClassifierState {
  if (!status.active) return 'disabled';
  return status.apiKeyConfigured ? 'running' : 'noKey';
}

/**
 * Issue #897: "Sınıflandırıcı" — etkin sağlayıcı (#469, varsayılan Claude), model, kitap modu ve son 24 saat. Gemini önbelleği
 * yalnız Gemini seçiliyken anlamlıdır; Claude'dayken "kullanılmıyor" satırı gösterilir, eski "API anahtarı eksik" uyarısı çıkmaz.
 */
@Component({
  selector: 'app-classifier-status',
  standalone: true,
  imports: [ClassifierCacheComponent, MatButtonModule, MatIconModule, MatProgressBarModule, TranslocoDirective],
  templateUrl: './classifier-status.component.html',
  styleUrl: './classifier-status.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClassifierStatusComponent implements OnInit {
  private readonly service = inject(ClassifierStatusService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly status = signal<ClassifierStatus | null>(null);
  readonly state = computed(() => {
    const status = this.status();
    return status ? classifierState(status) : null;
  });

  private readonly dateTime = computed(
    () => new Intl.DateTimeFormat(this.localeService.localeDefinition().angularLocale, { dateStyle: 'medium', timeStyle: 'short' }),
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.service
      .get()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (status) => {
          this.status.set(status);
          this.loading.set(false);
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  formatDate(value: string): string {
    return this.dateTime().format(new Date(value));
  }

  /** Sayılar servis yeniden başladıysa 24 saatten kısa bir pencereyi kapsar; o zaman başlangıç anı yazılır. */
  restartedRecently(status: ClassifierStatus): boolean {
    return Date.now() - new Date(status.since).getTime() < 24 * 60 * 60 * 1000;
  }
}
