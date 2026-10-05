import { TranslocoDirective } from '@jsverse/transloco';
import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  inject,
  signal,
} from '@angular/core';
import { LocaleService } from '../../../services/locale.service';
import { BadgeDetailComponent } from '../badge-detail/badge-detail.component';
import { BadgeMedallionComponent } from '../badge-medallion/badge-medallion.component';
import { BadgeTranslate, badgeAriaLabel, badgeStateText, isEarnedState } from '../badge-medallion/badge-state.util';
import { BadgePathPoint, BadgeThropyItem } from '../badge-thropy/badge-thropy.types';

/**
 * Issue #149: bağlantı çizgisi durumu — kazanılmış segment dolu `--ms-action-border`, sıradaki (son kazanılandan
 * ilk kazanılmamışa) `--ms-action-border-focus`, sonrakiler kesikli.
 */
export type BadgePathSegmentStatus = 'earned' | 'next' | 'pending';

interface BadgePathSegment {
  path: string;
  status: BadgePathSegmentStatus;
}

interface BadgePathRenderLayout {
  viewBox: string;
  height: number;
  pathSegments: BadgePathSegment[];
  points: BadgePathPoint[];
  nodePoints: BadgePathPoint[];
  rowCount: number;
  isMultiRow: boolean;
  maskRadius: number;
  /** Komşu düğümler arası en dar yatay aralık (izin %'si); düğüm genişliği bununla sınırlanır (çakışma olmasın). */
  slotPercent: number;
}

@Component({
  selector: 'app-badge-path',
  standalone: true,
  imports: [TranslocoDirective, BadgeMedallionComponent, BadgeDetailComponent],
  templateUrl: './badge-path.component.html',
  styleUrls: ['./badge-path.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BadgePathComponent implements OnChanges {
  private static nextInstanceId = 0;
  private readonly localeService = inject(LocaleService);

  @Input({ required: true }) items: BadgeThropyItem[] = [];
  @Input() maxPerRow: number = 4;
  /**
   * Issue #149 (CR U1): seçimin tek kaynağı üst bileşen (BadgeThropy). Bu yoldaki bir düğümün id'si ise düğüm
   * vurgulanır ve detay paneli bu yolun altında açılır; başka yoldaysa/null ise panel yok.
   */
  @Input() selectedId: string | null = null;
  /** Düğüm etkinleştirildi (her tıklamada); seçim/aç-kapa kararını üst bileşen verir. */
  @Output() badgeSelected = new EventEmitter<string>();

  readonly layout = signal<BadgePathRenderLayout | null>(null);
  /** Düğüm altlarını çizgiden ayıran SVG maskesinin sayfa içinde tekil kimliği. */
  readonly maskId = `badge-path-mask-${BadgePathComponent.nextInstanceId}`;
  /** Detay panelinin id'si (`aria-controls` hedefi). */
  readonly detailId = `badge-path-detail-${BadgePathComponent.nextInstanceId++}`;

  /** `selectedId` bu yolun bir düğümüyse o öğe. */
  get selectedItem(): BadgeThropyItem | null {
    return this.selectedId ? (this.items ?? []).find((item) => item.id === this.selectedId) ?? null : null;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ('items' in changes || 'maxPerRow' in changes) {
      this.layout.set(this.buildLayout(this.items ?? []));
    }
  }

  /** Düğümün erişilebilir adı ("Kilitli: Soru Avcısı V, 0/500"); medalyon `aria-hidden`. */
  ariaLabel(t: BadgeTranslate, item: BadgeThropyItem): string {
    return badgeAriaLabel(t, {
      state: item.state,
      name: item.name,
      current: item.completedLabel,
      target: item.totalLabel,
      earnedDateUtc: item.earnedDateUtc,
      locale: this.localeService.localeDefinition().angularLocale,
    });
  }

  stateText(t: BadgeTranslate, item: BadgeThropyItem): string {
    return badgeStateText(t, {
      state: item.state,
      current: item.completedLabel,
      target: item.totalLabel,
      earnedDateUtc: item.earnedDateUtc,
      locale: this.localeService.localeDefinition().angularLocale,
    });
  }

  selectBadge(badge: BadgeThropyItem): void {
    if (badge) {
      this.badgeSelected.emit(badge.id);
    }
  }

  private buildLayout(items: BadgeThropyItem[]): BadgePathRenderLayout | null {
    const total = items.length;

    if (!total) {
      return null;
    }

    const safeMaxPerRow = Math.max(2, Math.min(this.maxPerRow || 0, 6));
    const rows: Array<{ start: number; length: number }> = [];
    for (let start = 0; start < total; start += safeMaxPerRow) {
      rows.push({ start, length: Math.min(safeMaxPerRow, total - start) });
    }

    const rowCount = rows.length;
    const left = 10;
    const right = 90;
    const width = right - left;
    const baseSlot = width / Math.max(safeMaxPerRow - 1, 1);
    const slotSpacing = Math.min(baseSlot, 20);
    const startY = rowCount > 1 ? 20 : 50;
    const endY = rowCount > 1 ? 80 : 50;
    const rowSpacing = rowCount > 1 ? (endY - startY) / Math.max(rowCount - 1, 1) : 0;

    const points: BadgePathPoint[] = [];
    const metas: Array<{ row: number; index: number; length: number }> = [];

    rows.forEach((row, rowIndex) => {
      const rowLength = row.length;
      const isSingleRow = rowCount === 1;
      const fixedGap = 22;
      const rowSlotSpacing = isSingleRow ? fixedGap : slotSpacing;
      const rowStart = left;
      const rowY = isSingleRow ? 50 : startY + rowIndex * rowSpacing;

      // For serpentine layout: odd rows should start from the right side aligned with previous row's end
      const isReversedRow = !isSingleRow && rowIndex % 2 === 1;
      const maxRowSpan = slotSpacing * Math.max(safeMaxPerRow - 1, 1);

      for (let offset = 0; offset < rowLength; offset++) {
        const badgeIndex = row.start + offset;
        let xPercent: number;

        if (rowLength === 1) {
          xPercent = isReversedRow ? rowStart + maxRowSpan : rowStart;
        } else if (isSingleRow) {
          // Single row always goes left to right
          xPercent = rowStart + offset * rowSlotSpacing;
        } else if (isReversedRow) {
          // Multi-row: odd rows go right to left (serpentine)
          // Start from the rightmost position (where previous row ended)
          xPercent = rowStart + maxRowSpan - offset * rowSlotSpacing;
        } else {
          // Multi-row: even rows go left to right
          xPercent = rowStart + offset * rowSlotSpacing;
        }

        points[badgeIndex] = {
          xPercent: this.clamp(xPercent, left, right),
          yPercent: this.clamp(rowY, 12, 88),
        };
        metas[badgeIndex] = { row: rowIndex, index: offset, length: rowLength };
      }
    });

    const orderedPoints = points.filter((point): point is BadgePathPoint => !!point);
    const orderedMetas = metas.filter((meta): meta is { row: number; index: number; length: number } => !!meta);
    const pointCount = Math.min(total, orderedPoints.length, orderedMetas.length);

    if (pointCount === 0) {
      return null;
    }

    const verticalBend = Math.max(rowSpacing * 0.6, 8);
    const horizontalBend = Math.max(Math.min(slotSpacing * 1.1, width * 0.2), 6);
    const singleRowBend = rowCount === 1 ? Math.max(horizontalBend, 8) : horizontalBend;
    const pathSegments: BadgePathSegment[] = [];

    for (let index = 1; index < pointCount; index++) {
      const prevPoint = orderedPoints[index - 1];
      const currentPoint = orderedPoints[index];
      const prevMeta = orderedMetas[index - 1];
      const currentMeta = orderedMetas[index];

      if (!prevPoint || !currentPoint || !prevMeta || !currentMeta) {
        continue;
      }

      let segmentPath = '';
      if (prevMeta.row === currentMeta.row) {
        const direction = prevMeta.row % 2 === 0 ? 1 : -1;
        const bendAmount = rowCount === 1 ? singleRowBend : horizontalBend;
        const cp1x = this.clamp(prevPoint.xPercent + bendAmount * direction, left, right);
        const cp2x = this.clamp(currentPoint.xPercent - bendAmount * direction, left, right);
        // Add a slight vertical bend even for single rows to ensure the path is drawn
        const yBend = rowCount === 1 ? 2 : 0;
        const cp1y = prevPoint.yPercent - yBend;
        const cp2y = currentPoint.yPercent - yBend;
        segmentPath = `M ${prevPoint.xPercent} ${prevPoint.yPercent} C ${cp1x} ${cp1y} ${cp2x} ${cp2y} ${currentPoint.xPercent} ${currentPoint.yPercent}`;
      } else {
        const verticalDir = currentMeta.row > prevMeta.row ? 1 : -1;
        const prevDirection = prevMeta.row % 2 === 0 ? 1 : -1;
        const nextDirection = currentMeta.row % 2 === 0 ? 1 : -1;
        const cp1x = this.clamp(prevPoint.xPercent + horizontalBend * prevDirection, left, right);
        const cp1y = prevPoint.yPercent + verticalBend * verticalDir;
        const cp2x = this.clamp(currentPoint.xPercent - horizontalBend * nextDirection, left, right);
        const cp2y = currentPoint.yPercent - verticalBend * verticalDir;
        segmentPath = `M ${prevPoint.xPercent} ${prevPoint.yPercent} C ${cp1x} ${cp1y} ${cp2x} ${cp2y} ${currentPoint.xPercent} ${currentPoint.yPercent}`;
      }

      if (segmentPath) {
        const status = this.segmentStatus(items[index - 1], items[index]);
        pathSegments.push({
          path: segmentPath,
          status,
        });
      }
    }

    const nodePoints = orderedPoints.slice(0, pointCount).map((point) => {
      return { ...point };
    });

    const baseHeight = 220;
    const perRowIncrement = 140;
    const height = baseHeight + Math.max(0, rowCount - 1) * perRowIncrement;
    const maskRadius = rowCount > 1 ? 9 : 8;
    const slotPercent = rowCount > 1 ? slotSpacing : total > 1 ? 22 : 100;

    return {
      viewBox: '0 0 100 100',
      height,
      pathSegments,
      points: orderedPoints.slice(0, pointCount),
      nodePoints,
      rowCount,
      isMultiRow: rowCount > 1,
      maskRadius,
      slotPercent,
    };
  }

  private segmentStatus(previous: BadgeThropyItem | undefined, current: BadgeThropyItem | undefined): BadgePathSegmentStatus {
    if (current && isEarnedState(current.state)) {
      return 'earned';
    }
    if (previous && isEarnedState(previous.state)) {
      return 'next';
    }
    return 'pending';
  }

  private clamp(value: number, min: number, max: number): number {
    return Math.max(min, Math.min(max, value));
  }
}
