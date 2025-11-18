import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  signal,
} from '@angular/core';
import { BadgePathPoint, BadgeThropyItem } from '../badge-thropy/badge-thropy.types';

interface BadgePathSegment {
  path: string;
  midX: number;
  midY: number;
  angle: number;
}

interface BadgePathRenderLayout {
  viewBox: string;
  height: number;
  pathD: string;
  pathSegments: BadgePathSegment[];
  points: BadgePathPoint[];
  nodePoints: BadgePathPoint[];
  rowCount: number;
  isMultiRow: boolean;
  maskRadius: number;
}

@Component({
  selector: 'app-badge-path',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './badge-path.component.html',
  styleUrls: ['./badge-path.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BadgePathComponent implements OnChanges {
  private static nextInstanceId = 0;

  @Input({ required: true }) items: BadgeThropyItem[] = [];
  @Input() maxPerRow: number = 4;
  @Output() badgeSelected = new EventEmitter<string>();
  readonly selectedBadge = signal<BadgeThropyItem | null>(null);

  readonly layout = signal<BadgePathRenderLayout | null>(null);
  readonly gradientId = `badge-path-gradient-${BadgePathComponent.nextInstanceId++}`;

  ngOnChanges(changes: SimpleChanges): void {
    if ('items' in changes || 'maxPerRow' in changes) {
      this.layout.set(this.buildLayout(this.items ?? []));
    }
  }

  trackById(_: number, item: BadgeThropyItem): string {
    return item.id;
  }

  selectBadge(badge: BadgeThropyItem): void {
    if (!badge) {
      return;
    }

    const isSame = this.selectedBadge()?.id === badge.id;
    this.selectedBadge.set(isSame ? null : badge);

    if (!isSame) {
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
      const rowSpan = rowSlotSpacing * Math.max(rowLength - 1, 0);
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
    let pathD = `M ${orderedPoints[0].xPercent} ${orderedPoints[0].yPercent}`;
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
        // Calculate midpoint and angle for arrow
        const midX = (prevPoint.xPercent + currentPoint.xPercent) / 2;
        const midY = (prevPoint.yPercent + currentPoint.yPercent) / 2;
        const dx = currentPoint.xPercent - prevPoint.xPercent;
        const dy = currentPoint.yPercent - prevPoint.yPercent;
        const angle = Math.atan2(dy, dx) * (180 / Math.PI);

        pathSegments.push({
          path: segmentPath,
          midX,
          midY,
          angle,
        });
      }
    }

    const nodePoints = orderedPoints.slice(0, pointCount).map((point) => {
      return { ...point };
    });

    const height = (rowCount === 1 ? 200 : 200) + Math.max(0, rowCount - 1) * 120;
    const maskRadius = 8;

    return {
      viewBox: '0 0 100 100',
      height,
      pathD,
      pathSegments,
      points: orderedPoints.slice(0, pointCount),
      nodePoints,
      rowCount,
      isMultiRow: rowCount > 1,
      maskRadius,
    };
  }

  private clamp(value: number, min: number, max: number): number {
    return Math.max(min, Math.min(max, value));
  }
}
