import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

export type WizardStopState = 'done' | 'current' | 'upcoming' | 'branch' | 'final';

export interface WizardStop {
  /** 0 tabanlı durak sırası; tamamlanan duraklarda `goTo` bu değeri yayar. */
  index: number;
  state: WizardStopState;
  /** Son durak (form) için `true`; şu anki durak formsa da `true` kalır. */
  final: boolean;
}

/** Arayüz metinleri çağıranın çeviri scope'undan gelir. */
export interface WizardProgressLabels {
  /** Stepper'ın erişilebilir adı, ör. "Sihirbaz ilerlemesi". */
  ariaLabel: string;
  /** Durak öneki, ör. "Soru" → "Soru 2: tamamlandı, düzenle". */
  stop: string;
  done: string;
  current: string;
  upcoming: string;
  branch: string;
  final: string;
}

/**
 * Sihirbaz ilerlemesi — "durak yolu" (issue #135). Toplam = tamamlanan + şu an + kalan + form.
 * Masaüstünde duraklı stepper (tamamlananlar tıklanır, şu anki `aria-current="step"`), mobilde
 * segment çubuğu (`role=progressbar`). İki görünüm de render edilir; hangisinin görüneceğine CSS
 * karar verir (`display: none` erişilebilirlik ağacından da çıkarır).
 */
@Component({
  selector: 'app-wizard-progress',
  standalone: true,
  imports: [MatIconModule],
  templateUrl: './wizard-progress.component.html',
  styleUrl: './wizard-progress.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WizardProgressComponent {
  /** Tamamlanan (cevaplanmış ve geçilmiş) soru sayısı. */
  readonly done = input.required<number>();
  /** Şu anki sorudan sonra yolda kalan soru sayısı (form hariç). */
  readonly remaining = input<number>(0);
  /** Şu anki soruda dal henüz belli değil: ilk kalan durak dal düğümü çizilir. */
  readonly branchUnknown = input<boolean>(false);
  /** Şu an son durakta (form) mı? */
  readonly atFinal = input<boolean>(false);
  /** "Soru 3 / 6" gibi görünür sayaç metni. */
  readonly counterText = input<string>('');
  readonly labels = input.required<WizardProgressLabels>();

  /** Tamamlanmış bir durağa tıklanınca o durağın 0 tabanlı sırası. */
  readonly goTo = output<number>();

  readonly total = computed(() => this.stops().length);

  /** 1 tabanlı şu anki durak. */
  readonly currentPosition = computed(() => Math.min(this.done() + 1, this.total()));

  readonly stops = computed<WizardStop[]>(() => {
    const done = Math.max(0, this.done());
    const remaining = Math.max(0, this.remaining());
    const stops: WizardStop[] = [];
    for (let i = 0; i < done; i++) stops.push({ index: i, state: 'done', final: false });
    if (this.atFinal()) {
      stops.push({ index: done, state: 'current', final: true });
      return stops;
    }
    stops.push({ index: done, state: 'current', final: false });
    for (let i = 0; i < remaining; i++) {
      const state: WizardStopState = i === 0 && this.branchUnknown() ? 'branch' : 'upcoming';
      stops.push({ index: done + 1 + i, state, final: false });
    }
    stops.push({ index: done + 1 + remaining, state: 'upcoming', final: true });
    return stops;
  });

  stopLabel(stop: WizardStop): string {
    const labels = this.labels();
    const name = stop.final ? labels.final : `${labels.stop} ${stop.index + 1}`;
    const state = labels[stop.state];
    return `${name}: ${state}`;
  }
}
