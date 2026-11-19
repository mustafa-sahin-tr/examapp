import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { forkJoin } from 'rxjs';

import {
  SOLUTION_MAX_DURATION_MS,
  SOLUTION_PAGE_HEIGHT,
  SOLUTION_PAGE_WIDTH,
  SolutionStrokeDocument,
} from '../../../models/solution-recording';
import { SolutionRecordingService } from '../../../services/solution-recording.service';
import { penColor } from '../../utils/pen-color';
import { formatSolutionTime } from '../../utils/solution-strokes';
import { PenColorPickerComponent } from '../pen-color-picker/pen-color-picker.component';
import { SolutionPageComponent } from './solution-page.component';
import { SolutionPlayerComponent } from './solution-player.component';
import { SolutionStrokeCapture } from './solution-stroke-capture';

export interface SolutionRecordingDialogData {
  /** `record`: soru sahibi öğretmen (kaydet, değiştir, sil). `watch`: öğrenci, test oturumu satırıyla. */
  mode: 'record' | 'watch';
  /** `record` kipinde soru id'si, `watch` kipinde `TestInstanceQuestion.id`. */
  id: number;
  title: string;
  imageUrl: string | null;
  hasRecording: boolean;
}

export interface SolutionRecordingDialogResult {
  hasRecording: boolean;
}

type View = 'loading' | 'loadError' | 'empty' | 'recording' | 'paused' | 'review' | 'saved';
type MicError = 'micDenied' | 'micMissing' | 'micUnsupported';

interface Playable {
  strokes: SolutionStrokeDocument;
  durationMs: number;
  audio: Blob;
}

/** Tarayıcının üretebildiği ilk tür; MP4 / AAC her yerde çalındığı için önce o denenir. */
const AUDIO_TYPES = ['audio/mp4;codecs=mp4a.40.2', 'audio/mp4', 'audio/webm;codecs=opus', 'audio/webm', 'audio/ogg;codecs=opus'];
const CLOCK_TICK_MS = 200;

/**
 * Issue #1226: soru çözüm kaydı penceresi. Öğretmen soru görselinin olduğu sayfaya yazarken konuşur; kalem çizgileri
 * zamanlarıyla, ses MediaRecorder ile kaydedilir. Kayıttan sonra önizlenir, kaydedilir ya da yeniden çekilir. Öğrenci aynı
 * pencerede yalnız izler.
 */
@Component({
  selector: 'app-solution-recording-dialog',
  standalone: true,
  imports: [
    MatButtonModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    NgTemplateOutlet,
    PenColorPickerComponent,
    SolutionPageComponent,
    SolutionPlayerComponent,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope('solution-recording')],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './solution-recording-dialog.component.html',
  styleUrl: './solution-recording-dialog.component.scss',
})
export class SolutionRecordingDialogComponent implements OnInit {
  protected readonly data = inject<SolutionRecordingDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<SolutionRecordingDialogComponent, SolutionRecordingDialogResult>>(MatDialogRef);
  private readonly service = inject(SolutionRecordingService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly canRecord = this.data.mode === 'record';
  protected readonly maxLabel = formatSolutionTime(SOLUTION_MAX_DURATION_MS);
  protected readonly view = signal<View>('loading');
  protected readonly playable = signal<Playable | null>(null);
  protected readonly micError = signal<MicError | null>(null);
  protected readonly saveError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly confirmDelete = signal(false);
  protected readonly confirmClose = signal(false);
  protected readonly elapsedMs = signal(0);
  protected readonly elapsedLabel = computed(() => formatSolutionTime(this.elapsedMs()));
  protected readonly capture = new SolutionStrokeCapture(() => this.elapsed(), SOLUTION_PAGE_WIDTH, SOLUTION_PAGE_HEIGHT);
  /** Kaydedilmemiş kayıt var: pencere sorulmadan kapanmaz. */
  protected readonly unsaved = computed(() => ['recording', 'paused', 'review'].includes(this.view()));

  private hasRecording = this.data.hasRecording;
  private recorder: MediaRecorder | null = null;
  private stream: MediaStream | null = null;
  private chunks: Blob[] = [];
  private elapsedBase = 0;
  private runningSince: number | null = null;
  private clockTimer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.release());
  }

  ngOnInit(): void {
    if (this.hasRecording) {
      this.load();
    } else {
      this.view.set(this.canRecord ? 'empty' : 'loadError');
    }
  }

  protected load(): void {
    this.view.set('loading');
    const { mode, id } = this.data;
    forkJoin({
      recording: mode === 'record' ? this.service.get(id) : this.service.getForStudent(id),
      audio: mode === 'record' ? this.service.audio(id) : this.service.audioForStudent(id),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ recording, audio }) => {
          this.playable.set({ strokes: recording.strokes, durationMs: recording.durationMs, audio });
          this.view.set('saved');
        },
        error: () => this.view.set('loadError'),
      });
  }

  // ---- Kayıt ----------------------------------------------------------------------------------------------------

  protected async startRecording(): Promise<void> {
    this.micError.set(null);
    this.saveError.set(null);
    this.confirmDelete.set(false);
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') {
      this.micError.set('micUnsupported');
      return;
    }
    let stream: MediaStream;
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch (error) {
      const name = (error as DOMException)?.name;
      this.micError.set(name === 'NotAllowedError' || name === 'SecurityError' ? 'micDenied' : 'micMissing');
      return;
    }
    const mimeType = AUDIO_TYPES.find((type) => MediaRecorder.isTypeSupported(type));
    let recorder: MediaRecorder;
    try {
      recorder = new MediaRecorder(stream, mimeType ? { mimeType, audioBitsPerSecond: 48_000 } : undefined);
    } catch {
      stream.getTracks().forEach((track) => track.stop());
      this.micError.set('micUnsupported');
      return;
    }
    this.release();
    this.stream = stream;
    this.recorder = recorder;
    this.chunks = [];
    this.capture.reset();
    this.elapsedBase = 0;
    this.runningSince = null;
    this.elapsedMs.set(0);
    this.playable.set(null);
    recorder.ondataavailable = (event) => {
      if (event.data.size > 0) this.chunks.push(event.data);
    };
    recorder.onstart = () => {
      this.runningSince = performance.now();
      this.view.set('recording');
    };
    recorder.onstop = () => this.onRecorderStopped(recorder);
    recorder.start(1000);
    this.clockTimer = setInterval(() => this.tick(), CLOCK_TICK_MS);
  }

  protected pause(): void {
    if (this.recorder?.state !== 'recording') return;
    this.capture.finish();
    this.recorder.pause();
    this.stopClock();
    this.view.set('paused');
  }

  protected resume(): void {
    if (this.recorder?.state !== 'paused') return;
    this.recorder.resume();
    this.runningSince = performance.now();
    this.view.set('recording');
  }

  protected finish(): void {
    if (!this.recorder || this.recorder.state === 'inactive') return;
    this.capture.finish();
    this.stopClock();
    this.recorder.stop();
  }

  protected draw(event: PointerEvent): void {
    if (this.view() === 'recording') this.capture.start(event, penColor());
  }

  protected save(): void {
    const playable = this.playable();
    if (!playable || this.busy()) return;
    this.busy.set(true);
    this.saveError.set(null);
    this.service
      .save(this.data.id, playable.strokes, playable.durationMs, playable.audio)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.hasRecording = true;
          this.view.set('saved');
        },
        error: (error: HttpErrorResponse) => {
          this.busy.set(false);
          this.saveError.set(typeof error.error?.message === 'string' ? error.error.message : '');
        },
      });
  }

  protected delete(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.saveError.set(null);
    this.service
      .delete(this.data.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.hasRecording = false;
          this.confirmDelete.set(false);
          this.playable.set(null);
          this.view.set('empty');
        },
        error: () => {
          this.busy.set(false);
          this.saveError.set('');
        },
      });
  }

  protected close(force = false): void {
    if (this.unsaved() && !force) {
      this.confirmClose.set(true);
      return;
    }
    this.dialogRef.close({ hasRecording: this.hasRecording });
  }

  // ---- Saat ve kaynaklar ------------------------------------------------------------------------------------------

  /** Kaydın başından geçen süre (ms); duraklamalar sayılmaz, böylece sesle aynı saati gösterir. */
  private elapsed(): number {
    return this.elapsedBase + (this.runningSince !== null ? performance.now() - this.runningSince : 0);
  }

  private tick(): void {
    const elapsed = this.elapsed();
    this.elapsedMs.set(Math.min(elapsed, SOLUTION_MAX_DURATION_MS));
    if (elapsed >= SOLUTION_MAX_DURATION_MS) this.finish();
  }

  private stopClock(): void {
    this.elapsedBase = Math.min(this.elapsed(), SOLUTION_MAX_DURATION_MS);
    this.runningSince = null;
    this.elapsedMs.set(this.elapsedBase);
  }

  private onRecorderStopped(recorder: MediaRecorder): void {
    if (recorder !== this.recorder) return;
    const audio = new Blob(this.chunks, { type: recorder.mimeType || this.chunks[0]?.type || 'audio/webm' });
    const durationMs = Math.max(1, Math.round(this.elapsedBase));
    this.release();
    this.playable.set({
      strokes: { v: 1, width: SOLUTION_PAGE_WIDTH, height: SOLUTION_PAGE_HEIGHT, strokes: this.capture.strokes() },
      durationMs,
      audio,
    });
    this.view.set('review');
  }

  private release(): void {
    if (this.clockTimer !== null) clearInterval(this.clockTimer);
    this.clockTimer = null;
    const recorder = this.recorder;
    this.recorder = null;
    if (recorder && recorder.state !== 'inactive') {
      recorder.onstop = null;
      recorder.stop();
    }
    this.stream?.getTracks().forEach((track) => track.stop());
    this.stream = null;
  }
}
