import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { SolutionStrokeDocument } from '../../../models/solution-recording';
import { drawnStrokesAt, formatSolutionTime } from '../../utils/solution-strokes';
import { SolutionPageComponent } from './solution-page.component';

/**
 * Issue #1226: çözüm oynatıcı. Saat sestir: her karede sesin konumuna kadar olan çizgiler çizilir; sarınca sayfa o ana
 * göre yeniden çizilir. Süre kayıttan gelir (MediaRecorder dosyaları süreyi taşımayabilir).
 */
@Component({
  selector: 'app-solution-player',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, SolutionPageComponent, TranslocoDirective],
  providers: [provideTranslocoScope('solution-recording')],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="player" *transloco="let t; prefix: 'solution-recording.player'">
      <app-solution-page [imageUrl]="imageUrl()" [strokes]="drawn()" [width]="strokes().width" [height]="strokes().height"
        [label]="t('pageAria')" />
      <div class="player__controls">
        <button mat-icon-button type="button" data-testid="solution-play" [disabled]="!ready()"
          [attr.aria-label]="playing() ? t('pause') : t('play')" (click)="toggle()">
          <mat-icon>{{ playing() ? 'pause' : 'play_arrow' }}</mat-icon>
        </button>
        <input class="player__seek" type="range" min="0" step="100" data-testid="solution-seek" [max]="durationMs()"
          [value]="timeMs()" [disabled]="!ready()" [attr.aria-label]="t('seek')"
          [attr.aria-valuetext]="timeLabel()" (input)="seek($any($event.target).valueAsNumber)" />
        <span class="player__time" data-testid="solution-time">{{ timeLabel() }}</span>
      </div>
      @if (failed()) {
        <p class="player__error" role="alert">{{ t('audioError') }}</p>
      }
      @if (audioUrl(); as src) {
        <audio #audio preload="auto" [src]="src" (loadedmetadata)="onMetadata()" (timeupdate)="onTimeUpdate()"
          (play)="onPlay()" (pause)="onPause()" (ended)="onEnded()" (error)="failed.set(true)"></audio>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
    }

    .player__controls {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-top: 8px;
    }

    .player__seek {
      flex: 1 1 auto;
      min-width: 0;
      accent-color: var(--primaryColor);
    }

    .player__time {
      flex: 0 0 auto;
      font-variant-numeric: tabular-nums;
      font-size: 13px;
    }

    .player__error {
      margin: 8px 0 0;
      color: var(--mat-sys-error, #b3261e);
      font-size: 13px;
    }
  `,
})
export class SolutionPlayerComponent {
  readonly imageUrl = input<string | null>(null);
  readonly strokes = input.required<SolutionStrokeDocument>();
  readonly durationMs = input.required<number>();
  readonly audio = input.required<Blob>();

  protected readonly audioUrl = signal<string | null>(null);
  protected readonly timeMs = signal(0);
  protected readonly playing = signal(false);
  protected readonly ready = signal(false);
  protected readonly failed = signal(false);
  protected readonly drawn = computed(() => drawnStrokesAt(this.strokes(), this.timeMs()));
  protected readonly timeLabel = computed(
    () => `${formatSolutionTime(this.timeMs())} / ${formatSolutionTime(this.durationMs())}`,
  );

  private readonly audioElement = viewChild<ElementRef<HTMLAudioElement>>('audio');
  private frame: number | null = null;
  /** Süresi bilinmeyen dosyada tarayıcıya süreyi buldurmak için sona sarılırken true. */
  private priming = false;

  constructor() {
    effect((onCleanup) => {
      const url = URL.createObjectURL(this.audio());
      this.audioUrl.set(url);
      this.timeMs.set(0);
      this.ready.set(false);
      this.failed.set(false);
      onCleanup(() => URL.revokeObjectURL(url));
    });
    inject(DestroyRef).onDestroy(() => this.stopLoop());
  }

  protected toggle(): void {
    const audio = this.audioElement()?.nativeElement;
    if (!audio) return;
    if (!audio.paused) {
      audio.pause();
      return;
    }
    if (this.timeMs() >= this.durationMs()) this.seek(0);
    void audio.play().catch(() => this.failed.set(true));
  }

  protected seek(timeMs: number): void {
    const audio = this.audioElement()?.nativeElement;
    if (!audio || !Number.isFinite(timeMs)) return;
    const clamped = Math.min(Math.max(timeMs, 0), this.durationMs());
    audio.currentTime = clamped / 1000;
    this.timeMs.set(clamped);
  }

  protected onMetadata(): void {
    const audio = this.audioElement()?.nativeElement;
    if (!audio) return;
    if (Number.isFinite(audio.duration)) {
      this.ready.set(true);
      return;
    }
    // MediaRecorder dosyası süresini taşımıyor: sona sarınca tarayıcı süreyi bulur, sonra başa dönülür.
    this.priming = true;
    audio.currentTime = 1e101;
  }

  protected onTimeUpdate(): void {
    const audio = this.audioElement()?.nativeElement;
    if (!audio) return;
    if (this.priming) {
      this.priming = false;
      audio.currentTime = 0;
      this.ready.set(true);
      return;
    }
    if (audio.paused) this.sync(audio);
  }

  protected onPlay(): void {
    this.playing.set(true);
    const tick = () => {
      const audio = this.audioElement()?.nativeElement;
      if (!audio) return;
      this.sync(audio);
      this.frame = requestAnimationFrame(tick);
    };
    this.stopLoop();
    this.frame = requestAnimationFrame(tick);
  }

  protected onPause(): void {
    this.playing.set(false);
    this.stopLoop();
  }

  protected onEnded(): void {
    this.onPause();
    if (!this.priming) this.timeMs.set(this.durationMs());
  }

  private sync(audio: HTMLAudioElement): void {
    if (!this.priming) this.timeMs.set(Math.min(Math.round(audio.currentTime * 1000), this.durationMs()));
  }

  private stopLoop(): void {
    if (this.frame !== null) cancelAnimationFrame(this.frame);
    this.frame = null;
  }
}
