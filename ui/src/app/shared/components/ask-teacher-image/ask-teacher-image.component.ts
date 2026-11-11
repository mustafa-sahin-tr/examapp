import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { AskTeacherService } from '../../../services/ask-teacher.service';

/**
 * Issue #1111: soru / cevap görseli. Görsel yetkili uçtan blob olarak gelir (URL yok); nesne URL'i değişimde ve yok olurken
 * bırakılır. Dokununca tam boyut yeni sekmede açılır.
 */
@Component({
  selector: 'app-ask-teacher-image',
  standalone: true,
  imports: [MatIconModule, MatProgressSpinnerModule],
  template: `
    @if (url(); as src) {
      <a [href]="src" target="_blank" rel="noopener" class="ati__link">
        <img [src]="src" [alt]="alt()" class="ati__img" data-testid="ask-teacher-image" />
      </a>
    } @else if (failed()) {
      <mat-icon class="ati__broken" aria-hidden="true">broken_image</mat-icon>
    } @else {
      <mat-spinner diameter="24" />
    }
  `,
  styles: `
    :host { display: block; }
    .ati__link { display: block; }
    .ati__img {
      display: block;
      max-width: 100%;
      max-height: var(--ati-max-height, 320px);
      border-radius: 8px;
      border: 1px solid var(--ms-border);
      object-fit: contain;
      background: var(--ms-background-hover);
    }
    .ati__broken { color: var(--ms-text-weak, var(--ms-border-strong)); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AskTeacherImageComponent {
  private readonly service = inject(AskTeacherService);

  readonly questionId = input.required<number>();
  /** true: öğretmenin cevap görseli. */
  readonly answer = input(false);
  readonly alt = input('');

  protected readonly url = signal<string | null>(null);
  protected readonly failed = signal(false);

  constructor() {
    effect((onCleanup) => {
      const id = this.questionId();
      const answer = this.answer();
      let objectUrl: string | null = null;
      this.failed.set(false);
      this.url.set(null);
      const sub = this.service.image(id, answer).subscribe({
        next: (blob) => {
          objectUrl = URL.createObjectURL(blob);
          this.url.set(objectUrl);
        },
        error: () => this.failed.set(true),
      });
      onCleanup(() => {
        sub.unsubscribe();
        if (objectUrl) URL.revokeObjectURL(objectUrl);
      });
    });
    inject(DestroyRef).onDestroy(() => this.url.set(null));
  }
}
