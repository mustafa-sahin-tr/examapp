import { CommonModule } from '@angular/common';
import {
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { BadgeProgressItem, BadgeService } from '../../../services/badge.service';
import { AuthService } from '../../../services/auth.service';
import { BadgePathComponent } from '../badge-path/badge-path.component';
import { BadgeThropyItem, BadgeThropyPath, BadgePathLayout, BadgePathPoint } from './badge-thropy.types';

@Component({
  selector: 'app-badge-thropy',
  imports: [CommonModule, BadgePathComponent],
  templateUrl: './badge-thropy.component.html',
  styleUrls: ['./badge-thropy.component.scss'],
})
export class BadgeThropyComponent implements OnInit, OnChanges {
  @Input() title: string = 'Başarı Rozetleri';
  @Input() subtitle: string = 'Çalışma alışkanlıklarınıza göre rozetlerin durumunu takip edin.';
  @Input() userId: number = 0;

  @Output() badgeSelected = new EventEmitter<string>();

  private readonly badgeService = inject(BadgeService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly authService = inject(AuthService);
  private readonly numberFormatter = new Intl.NumberFormat('tr-TR');

  readonly badges = signal<BadgeThropyItem[]>([]);
  readonly badgePaths = signal<BadgeThropyPath[]>([]);
  readonly standaloneBadges = signal<BadgeThropyItem[]>([]);
  readonly isLoading = signal(false);
  readonly selectedBadge = signal<BadgeThropyItem | null>(null);

  ngOnInit(): void {
    this.loadBadges(this.userId);
  }

  ngOnChanges(changes: SimpleChanges): void {
    const change = changes['userId'];
    if (change && !change.isFirstChange()) {
      const previousValue = (change.previousValue as number | undefined) ?? 0;
      const currentValue = (change.currentValue as number | undefined) ?? 0;
      if (currentValue !== previousValue) {
        this.loadBadges(currentValue);
      }
    }
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

  trackByBadgeId(_: number, badge: BadgeThropyItem): string {
    return badge.id;
  }

  private loadBadges(userId: number): void {
    this.isLoading.set(true);
    this.selectedBadge.set(null);
    const storedUserId = this.authService.getUserIdFromLocalStorage();
    this.badgeService
      .getUserBadgeProgress(storedUserId || userId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          const mapped = (response?.badgeProgress ?? []).map((item) => this.mapBadge(item));
          this.badges.set(mapped);

          const partition = this.partitionBadges(mapped);
          this.badgePaths.set(partition.paths);
          this.standaloneBadges.set(partition.standalone);

          const initial = this.resolveInitialSelection(partition.paths, partition.standalone);
          if (initial) {
            this.selectedBadge.set(initial);
            this.badgeSelected.emit(initial.id);
          } else {
            this.selectedBadge.set(null);
          }
          this.isLoading.set(false);
        },
        error: (error) => {
          console.error('BadgeThropyComponent: unable to load badge progress', error);
          this.badges.set([]);
          this.badgePaths.set([]);
          this.standaloneBadges.set([]);
          this.isLoading.set(false);
        },
      });
  }

  private mapBadge(source: BadgeProgressItem): BadgeThropyItem {
    const progressValue = source.targetValue > 0 ? (source.currentValue / source.targetValue) * 100 : 0;
    const progressPercent = Math.max(0, Math.min(Math.round(progressValue), 100));

    const completedAmount = this.formatNumber(source.currentValue);
    const totalAmount = this.formatNumber(source.targetValue);

    return {
      id: source.badgeDefinitionId,
      name: source.name,
      iconUrl: source.iconUrl,
      description: source.description,
      currentValue: source.currentValue,
      targetValue: source.targetValue,
      progressPercent,
      completedLabel: completedAmount,
      totalLabel: totalAmount,
      isCompleted: !!source.isCompleted,
      earnedDateUtc: source.earnedDateUtc,
      pathKey: source.pathKey ?? null,
      pathName: source.pathName ?? null,
      pathOrder: Number.isFinite(source.pathOrder as number) ? Number(source.pathOrder) : null,
    };
  }

  private formatNumber(value: number): string {
    if (!Number.isFinite(value) || value < 0) {
      return '0';
    }

    return this.numberFormatter.format(value);
  }

  private partitionBadges(items: BadgeThropyItem[]): { paths: BadgeThropyPath[]; standalone: BadgeThropyItem[] } {
    const pathMap = new Map<string, BadgeThropyPath>();
    const standalone: BadgeThropyItem[] = [];

    for (const item of items) {
      if (item.pathKey) {
        const key = item.pathKey;
        let group = pathMap.get(key);
        if (!group) {
          group = {
            key,
            name: item.pathName || item.name,
            badges: [],
            completedCount: 0,
            completionPercent: 0,
            layout: null,
          };
          pathMap.set(key, group);
        }
        group.badges.push(item);
      } else {
        standalone.push(item);
      }
    }

    const paths = Array.from(pathMap.values()).map((path) => {
      path.badges.sort((a, b) => {
        const orderA = a.pathOrder ?? Number.MAX_SAFE_INTEGER;
        const orderB = b.pathOrder ?? Number.MAX_SAFE_INTEGER;
        if (orderA !== orderB) {
          return orderA - orderB;
        }
        if (a.targetValue !== b.targetValue) {
          return a.targetValue - b.targetValue;
        }
        return a.name.localeCompare(b.name);
      });

      const completedCount = path.badges.filter((badge) => badge.isCompleted).length;
      const totalCount = Math.max(path.badges.length, 1);
      path.completedCount = completedCount;
      path.completionPercent = Math.round((completedCount / totalCount) * 100);
      path.layout = this.createLoopLayout(path.badges, path.key);

      if (!path.name && path.badges.length) {
        path.name = path.badges[0].pathName || path.badges[0].name;
      }

      return path;
    });

    paths.sort((a, b) => {
      const orderA = a.badges[0]?.pathOrder ?? Number.MAX_SAFE_INTEGER;
      const orderB = b.badges[0]?.pathOrder ?? Number.MAX_SAFE_INTEGER;
      if (orderA !== orderB) {
        return orderA - orderB;
      }
      return a.name.localeCompare(b.name);
    });

    standalone.sort((a, b) => a.name.localeCompare(b.name));

    return { paths, standalone };
  }

  private createLoopLayout(badges: BadgeThropyItem[], pathKey: string): BadgePathLayout {
    const total = badges.length;

    if (!total) {
      return {
        viewBox: '0 0 100 100',
        height: 280,
        pathD: 'M 10 50 C 10 20 90 20 90 50 C 90 80 10 80 10 50',
        arrowId: 'badge-path-arrow-default',
        points: [],
      };
    }

    const viewBox = '0 0 100 100';
    const height = total > 6 ? 360 : total > 4 ? 320 : 280;
    const safeKey = (pathKey || 'default').replace(/[^a-zA-Z0-9_-]/g, '-');
    const arrowId = `badge-path-arrow-${safeKey}`;

    const left = 12;
    const right = 88;
    const horizontal = right - left;
    const topBase = 30;
    const bottomBase = 70;
    const curveSpread = 6;

    const topCount = Math.ceil(total / 2);
    const bottomCount = total - topCount;
    const points: BadgePathPoint[] = [];

    const createPoint = (ratio: number, yBase: number, invert?: boolean): BadgePathPoint => {
      const curve = Math.sin(ratio * Math.PI) * curveSpread;
      const xPercent = invert ? right - horizontal * ratio : left + horizontal * ratio;
      const yPercent = yBase + (invert ? curve : -curve);
      return { xPercent, yPercent };
    };

    if (topCount === total) {
      for (let index = 0; index < total; index++) {
        const ratio = total > 1 ? index / (total - 1) : 0.5;
        const curve = total > 2 ? Math.sin(ratio * Math.PI) * curveSpread : 0;
        const xPercent = left + horizontal * ratio;
        const yPercent = 50 - curve;
        points.push({ xPercent, yPercent });
      }
    } else {
      for (let index = 0; index < topCount; index++) {
        const ratio = topCount > 1 ? index / (topCount - 1) : 0.5;
        points.push(createPoint(ratio, topBase, false));
      }

      for (let index = 0; index < bottomCount; index++) {
        const ratio = bottomCount > 1 ? index / (bottomCount - 1) : 0.5;
        points.push(createPoint(ratio, bottomBase, true));
      }
    }

    const topCurveY = 18;
    const bottomCurveY = 82;
    const pathD = [
      `M ${left + 0.01} 50`,
      `C ${left} ${topCurveY} ${right} ${topCurveY} ${right} 50`,
      `C ${right} ${bottomCurveY} ${left} ${bottomCurveY} ${left} 50`,
    ].join(' ');

    return {
      viewBox,
      height,
      pathD,
      arrowId,
      points,
    };
  }

  private resolveInitialSelection(paths: BadgeThropyPath[], standalone: BadgeThropyItem[]): BadgeThropyItem | null {
    for (const path of paths) {
      const nextPending = path.badges.find((badge) => !badge.isCompleted);
      if (nextPending) {
        return nextPending;
      }
      if (path.badges.length) {
        return path.badges[path.badges.length - 1];
      }
    }

    return standalone.length ? standalone[0] : null;
  }
}
