import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoDirective } from '@jsverse/transloco';
import { Subscription, timer } from 'rxjs';

import { BookImportCall, BookImportEvent, BookImportStage } from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';
import { BOOK_IMPORTS_POLL_MS } from './book-imports.component';

type Translate = (key: string, params?: Record<string, unknown>) => string;

/** Akışın bir satırı: olay ya da (isteğe bağlı) yapay zekâ çağrısı. */
export interface BookImportActivityRow {
  key: string;
  at: string;
  stage: BookImportStage;
  event?: BookImportEvent;
  call?: BookImportCall;
}

export type BookImportActivityTone = 'info' | 'ok' | 'warn' | 'danger' | 'muted';

const SHOW_CALLS_KEY = 'bookImports.activity.calls';

const TONES: Record<string, BookImportActivityTone> = {
  pagesReady: 'ok',
  cleaningFinished: 'ok',
  cleaningNothingFound: 'ok',
  setupFinished: 'ok',
  setupConfirmed: 'ok',
  finished: 'ok',
  cleaningUnavailable: 'warn',
  paused: 'warn',
  cleaningFailed: 'danger',
  failed: 'danger',
  cancelled: 'danger',
};

function readShowCalls(): boolean {
  try {
    return localStorage.getItem(SHOW_CALLS_KEY) === '1';
  } catch {
    return false;
  }
}

/**
 * Issue #1265: aktarımın zaman damgalı akışı (konsol gibi, en yeni altta). İş sürerken yalnız yeni satırları yoklar
 * (`afterId`); yapay zekâ çağrıları isteğe bağlı (tercih tarayıcıda kalır). Metin olay kodundan çevrilir (`activity.*`).
 */
@Component({
  selector: 'app-book-import-activity',
  standalone: true,
  imports: [CurrencyPipe, DatePipe, DecimalPipe, MatButtonModule, MatIconModule, MatSlideToggleModule, TranslocoDirective],
  templateUrl: './book-import-activity.component.html',
  styleUrl: './book-import-activity.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BookImportActivityComponent {
  readonly jobId = input.required<string>();
  /** İş sürüyorsa yoklama sürer; bitince bir kez daha okunur (son satırlar). */
  readonly active = input(false);

  private readonly api = inject(BookImportService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly log = viewChild<ElementRef<HTMLElement>>('log');

  readonly events = signal<BookImportEvent[]>([]);
  readonly calls = signal<BookImportCall[]>([]);
  readonly showCalls = signal(readShowCalls());
  readonly open = signal(true);
  readonly loaded = signal(false);
  readonly failed = signal(false);

  readonly rows = computed<BookImportActivityRow[]>(() => {
    const rows: BookImportActivityRow[] = this.events().map((event) => ({
      key: 'e' + event.id,
      at: event.at,
      stage: event.stage,
      event,
    }));
    if (this.showCalls())
      rows.push(...this.calls().map((call) => ({ key: 'c' + call.id, at: call.at, stage: call.stage, call })));
    return rows.sort((a, b) => a.at.localeCompare(b.at));
  });

  private poll?: Subscription;
  private request?: Subscription;

  constructor() {
    effect(() => {
      this.jobId();
      this.active();
      this.showCalls();
      untracked(() => this.refresh());
    });
  }

  toggleCalls(show: boolean): void {
    try {
      localStorage.setItem(SHOW_CALLS_KEY, show ? '1' : '0');
    } catch {
      // tercih yalnız bu oturumda kalır
    }
    this.calls.set([]);
    this.showCalls.set(show);
  }

  refresh(): void {
    this.poll?.unsubscribe();
    this.request?.unsubscribe();
    const events = this.events();
    const calls = this.calls();
    const withCalls = this.showCalls();
    this.request = this.api
      .activity(
        this.jobId(),
        events.length > 0 ? events[events.length - 1].id : 0,
        withCalls,
        calls.length > 0 ? calls[calls.length - 1].id : 0,
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (activity) => {
          const atBottom = this.atBottom();
          if (activity.events.length > 0) this.events.update((list) => [...list, ...activity.events]);
          if (withCalls && activity.calls?.length) this.calls.update((list) => [...list, ...activity.calls!]);
          this.loaded.set(true);
          this.failed.set(false);
          if (atBottom) afterNextRender(() => this.scrollToEnd(), { injector: this.injector });
          this.schedule();
        },
        error: () => {
          this.failed.set(true);
          this.schedule();
        },
      });
  }

  tone(row: BookImportActivityRow): BookImportActivityTone {
    if (row.call) return 'muted';
    const event = row.event!;
    if (event.code === 'testFinished')
      return event.data?.['status'] === 'Failed' ? 'danger' : event.data?.['status'] === 'NeedsReview' ? 'warn' : 'ok';
    return TONES[event.code] ?? 'info';
  }

  /** Olayın metni: kod çeviri anahtarı, değerleri parametre; bazı kodlarda değere göre ayrı anahtar. */
  text(event: BookImportEvent, t: Translate): string {
    const data = event.data ?? {};
    const params: Record<string, unknown> = { ...data, stage: t('stages.' + event.stage) };
    for (const key of ['cost', 'ceiling'])
      if (typeof data[key] === 'number') params[key] = '$' + (data[key] as number).toFixed(4);
    if (typeof data['resumeAt'] === 'string')
      params['resumeAt'] = new Date(data['resumeAt']).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    switch (event.code) {
      case 'cleaningAnalysisFinished': {
        const found = data['watermark'] && data['markColour'] ? 'both' : data['watermark'] ? 'watermark' : 'marks';
        return t('activity.cleaningAnalysisFinished.' + found);
      }
      case 'testStarted':
      case 'testFinished': {
        const name = (data['title'] as string | null) || t('activity.testNo', { order: data['order'] });
        const pages = data['fileFrom'] != null ? t('activity.testPages', { from: data['fileFrom'], to: data['fileTo'] }) : '';
        const key = event.code === 'testStarted' ? 'testStarted' : 'testFinished.' + (data['status'] ?? 'Done');
        return t('activity.' + key, { ...params, name, pages });
      }
      case 'paused':
        return t('activity.paused.' + (data['reason'] ?? 'Budget'), params);
      case 'setupConfirmed': {
        const sharing = String(data['sharing'] ?? 'Private');
        return t('activity.setupConfirmed', {
          sharing: t('sharing.options.' + sharing.charAt(0).toLowerCase() + sharing.slice(1) + '.label'),
        });
      }
      case 'retried':
        return t(data['tests'] != null ? 'activity.retriedTests' : 'activity.retried', params);
      default:
        return t('activity.' + event.code, params);
    }
  }

  private schedule(): void {
    if (!this.active()) return;
    this.poll = timer(BOOK_IMPORTS_POLL_MS)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.refresh());
  }

  private atBottom(): boolean {
    const el = this.log()?.nativeElement;
    return !el || el.scrollHeight - el.scrollTop - el.clientHeight < 24;
  }

  private scrollToEnd(): void {
    const el = this.log()?.nativeElement;
    if (el) el.scrollTop = el.scrollHeight;
  }
}
