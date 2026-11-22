import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, take } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { HomeworkService, StudentHomework } from '../../services/homework.service';
import { LocaleService } from '../../services/locale.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { backendMessage } from '../../shared/utils/admin-action-error.util';
import { HOMEWORK_SCOPE } from '../homework/homework.component';

/**
 * Issue #1282 (#1071 S7): öğrencinin düz ödevleri. "Yapılacaklar" (öğretmen henüz kontrol etmedi; gecikmişler işaretli) ve
 * "Kontrol edilenler" (Yapıldı / Kısmen / Yapılmadı + öğretmen notu). Öğrenci "Yaptım" der ya da geri alır; öğretmen kontrol
 * ettikten sonra işaret kilitlenir (sunucu 409). Puan yok.
 */
@Component({
  selector: 'app-my-homework',
  standalone: true,
  imports: [PageHeaderComponent, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope(HOMEWORK_SCOPE)],
  templateUrl: './my-homework.component.html',
  styleUrl: './my-homework.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyHomeworkComponent implements OnInit {
  private readonly service = inject(HomeworkService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly items = signal<StudentHomework[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly savingId = signal<number | null>(null);

  /** Kontrol edilmemişler, teslim gününe göre (sunucu sırası). */
  protected readonly open = computed(() => this.items().filter((h) => !h.teacherStatus));
  /** Kontrol edilenler, en yeni teslim önce. */
  protected readonly checked = computed(() =>
    this.items()
      .filter((h) => !!h.teacherStatus)
      .sort((a, b) => b.dueDate.localeCompare(a.dueDate)),
  );

  constructor() {
    this.transloco.load(`${HOMEWORK_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.service
      .mine()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (items) => this.items.set(items ?? []),
        error: (err: HttpErrorResponse) => this.loadError.set(backendMessage(err) ?? this.t('state.error')),
      });
  }

  protected toggleDone(h: StudentHomework): void {
    this.savingId.set(h.id);
    this.service
      .markDone(h.id, !h.studentDoneAt)
      .pipe(
        finalize(() => this.savingId.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (updated) => this.items.set(this.items().map((x) => (x.id === updated.id ? updated : x))),
        error: (err: HttpErrorResponse) => {
          this.snackBar.open(backendMessage(err) ?? this.t('state.markError'), undefined, { duration: 4000 });
          if (err.status === 409 || err.status === 404) {
            this.load();
          }
        },
      });
  }

  protected workText(h: StudentHomework): string {
    return [h.bookName, h.pages ? this.t('pagesText', { pages: h.pages }) : null].filter(Boolean).join(' · ');
  }

  protected formatDay(iso: string): string {
    const [y, m, d] = iso.split('-').map(Number);
    const date = new Date(y, (m ?? 1) - 1, d ?? 1);
    return Number.isNaN(date.getTime())
      ? '—'
      : date.toLocaleDateString(this.localeService.localeDefinition().angularLocale, { day: 'numeric', month: 'short', weekday: 'short' });
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate(`${HOMEWORK_SCOPE}.${key}`, params);
  }
}
