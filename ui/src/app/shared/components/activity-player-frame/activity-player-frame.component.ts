import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective } from '@jsverse/transloco';

/** Madde adımlayıcısındaki tek adımın durumu. `correct`/`wrong` yalnız son kontrolden sonra (ve madde değişmediyse). */
export type ActivityStepState = 'empty' | 'answered' | 'correct' | 'wrong';

export interface ActivityStep {
  state: ActivityStepState;
}

/** Adım etiketi: 1, 2, 3 … */
export function activityStepLabel(index: number): string {
  // Başlıktaki "Madde n / N" ile aynı numara (eskiden a, b, c…).
  return String(index + 1);
}

/**
 * Issue #573: etkinlik oynatıcısının çerçevesi — başlık + "Madde n / N", ilerleme çubuğu, yönerge kutusu, a/b/c adımlayıcı,
 * yansıtılan içerik (`<ng-content>`; görselli ya da görselsiz madde aynı çerçevede) ve Önceki/Sonraki. Alt çubuk
 * (kontrol paneli) `activityFooter` özniteliğiyle yansıtılır. Puan/yüzde göstermez.
 *
 * OnPush: tüm girdiler signal input; çerçeve kendi durumu tutmaz, gezinme `stepSelected` ile sahibine bildirilir.
 */
@Component({
  selector: 'app-activity-player-frame',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, TranslocoDirective],
  templateUrl: './activity-player-frame.component.html',
  styleUrl: './activity-player-frame.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityPlayerFrameComponent {
  readonly title = input.required<string>();
  /** 0 tabanlı geçerli madde. */
  readonly currentIndex = input.required<number>();
  readonly steps = input.required<ActivityStep[]>();
  /** Geçerli maddenin yönergesi (yoksa sayfa yönergesi); boşsa kutu çizilmez. */
  readonly instruction = input<string | null>(null);

  /** Çıkış sürerken (bekleyen kayıtlar yazılıyor) düğme pasif. */
  readonly exitBusy = input(false);

  /** Adım / Önceki / Sonraki ile seçilen madde (0 tabanlı, sınırlar içinde). */
  readonly stepSelected = output<number>();
  /** "← Etkinliklerim": cevaplar kayıtlı, etkinlik sonra kaldığı yerden sürer (sahibi kayıtları bekleyip listeye döner). */
  readonly exit = output<void>();

  readonly total = computed(() => this.steps().length);
  readonly displayIndex = computed(() => Math.min(this.currentIndex(), Math.max(0, this.total() - 1)));
  readonly progress = computed(() => (this.total() ? ((this.displayIndex() + 1) / this.total()) * 100 : 0));
  readonly canPrev = computed(() => this.displayIndex() > 0);
  readonly canNext = computed(() => this.displayIndex() < this.total() - 1);
  readonly trimmedInstruction = computed(() => this.instruction()?.trim() || null);

  readonly label = activityStepLabel;

  stateKey(index: number, state: ActivityStepState): string {
    if (state === 'correct') return 'stateCorrect';
    if (state === 'wrong') return 'stateWrong';
    if (index === this.displayIndex()) return 'stateCurrent';
    return state === 'answered' ? 'stateAnswered' : 'stateEmpty';
  }

  select(index: number): void {
    if (index < 0 || index >= this.total() || index === this.displayIndex()) return;
    this.stepSelected.emit(index);
  }
}
