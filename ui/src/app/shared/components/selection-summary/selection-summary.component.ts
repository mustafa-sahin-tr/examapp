import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, output } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { MAT_BOTTOM_SHEET_DATA, MatBottomSheet, MatBottomSheetRef } from '@angular/material/bottom-sheet';

export interface SelectionSummaryItem {
  stepId: number;
  /** Güvenli Material Symbols adı (çoklu cevapta ilk seçeneğin ikonu). */
  icon: string;
  /** Soru başlığı; görünümde 2 satırda kesilir (Karar 3). */
  caption: string;
  /** Cevap etiketleri, virgülle birleştirilmiş. */
  value: string;
  /** Çoklu cevapta ilk ikon dışında kalan seçim sayısı ("+n"). */
  extraCount: number;
}

/** Arayüz metinleri çağıranın çeviri scope'undan gelir. */
export interface SelectionSummaryLabels {
  title: string;
  /** Şu anki soru satırı, ör. "Seçim bekleniyor". */
  pending: string;
  /** Kalan sorular satırı (sayı dahil biçimlenmiş), ör. "Kalan sorular: 3". */
  remaining: string;
  /** Düzenle eylemi; satır etiketi "<soru>: <cevap>. <edit>". */
  edit: string;
  /** Mobil şeritteki "Tümü" çipi. */
  all: string;
}

export type SelectionSummaryVariant = 'rail' | 'chips' | 'review';

/** Bottom sheet açılırken komponente verilen veri; o modda input'lar yerine bu okunur. */
export interface SelectionSummarySheetData {
  items: SelectionSummaryItem[];
  labels: SelectionSummaryLabels;
}

/**
 * Sürekli seçim özeti (issue #135).
 * - `rail`: masaüstü sağ ray (`<aside>`); cevaplananlar, "Seçim bekleniyor" (şu anki), "Kalan sorular".
 * - `chips`: mobil yatay kayan çip şeridi (`role=list`); "Tümü" çipi tam listeyi `mat-bottom-sheet`'te açar.
 * - `review`: yalnız cevaplar (son adım onay özeti ve bottom sheet içeriği).
 * Her satır/çip `<button>`; tıklanınca `edit` o adımın id'sini yayar.
 */
@Component({
  selector: 'app-selection-summary',
  standalone: true,
  imports: [MatIconModule],
  templateUrl: './selection-summary.component.html',
  styleUrl: './selection-summary.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SelectionSummaryComponent {
  readonly items = input<SelectionSummaryItem[]>([]);
  readonly labels = input<SelectionSummaryLabels | null>(null);
  readonly variant = input<SelectionSummaryVariant>('rail');
  /** Şu anki sorunun başlığı; verilirse "Seçim bekleniyor" satırı çizilir (yalnız `rail`). */
  readonly pendingCaption = input<string | null>(null);
  /** Şu anki sorudan sonra kalan soru sayısı (yalnız `rail`). */
  readonly remainingCount = input<number>(0);

  readonly edit = output<number>();

  private readonly bottomSheet = inject(MatBottomSheet);
  private readonly destroyRef = inject(DestroyRef);
  /** Bottom sheet içinde açıldıysa dolu gelir. */
  private readonly sheetData = inject<SelectionSummarySheetData | null>(MAT_BOTTOM_SHEET_DATA, { optional: true });
  private readonly sheetRef = inject<MatBottomSheetRef<SelectionSummaryComponent, number>>(MatBottomSheetRef, {
    optional: true,
  });

  readonly view = computed<SelectionSummaryVariant>(() => (this.sheetData ? 'review' : this.variant()));
  readonly rows = computed(() => this.sheetData?.items ?? this.items());
  readonly text = computed<SelectionSummaryLabels>(
    () => this.sheetData?.labels ?? this.labels() ?? { title: '', pending: '', remaining: '', edit: '', all: '' },
  );
  readonly inSheet = !!this.sheetData;

  rowAriaLabel(item: SelectionSummaryItem): string {
    return `${item.caption}: ${item.value}. ${this.text().edit}`;
  }

  onEdit(stepId: number): void {
    if (this.sheetRef) {
      this.sheetRef.dismiss(stepId);
      return;
    }
    this.edit.emit(stepId);
  }

  openAll(): void {
    const data: SelectionSummarySheetData = { items: this.rows(), labels: this.text() };
    this.bottomSheet
      .open<SelectionSummaryComponent, SelectionSummarySheetData, number>(SelectionSummaryComponent, {
        data,
        ariaLabel: this.text().title,
      })
      .afterDismissed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((stepId) => {
        if (typeof stepId === 'number') this.edit.emit(stepId);
      });
  }
}
