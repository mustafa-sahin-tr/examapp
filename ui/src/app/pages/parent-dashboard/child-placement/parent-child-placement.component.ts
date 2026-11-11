import { ChangeDetectionStrategy, Component, DestroyRef, OnChanges, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoService, provideTranslocoScope } from '@jsverse/transloco';

import { PlacementReport } from '../../../models/placement.model';
import { PlacementService } from '../../../services/placement.service';
import { PlacementReportComponent } from '../../../shared/components/placement-report/placement-report.component';

/** Issue #1114: veli panelinin Plan sekmesinde çocuğun seviye tespiti raporu; çözülmemişse (ya da hata) hiçbir şey çizilmez. */
@Component({
  selector: 'app-parent-child-placement',
  standalone: true,
  imports: [PlacementReportComponent],
  providers: [provideTranslocoScope('placement')],
  template: `
    @if (report(); as r) {
      <app-placement-report class="pcp" [report]="r" audience="parent" [heading]="heading()" />
    }
  `,
  styles: `
    .pcp {
      margin-bottom: 1rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ParentChildPlacementComponent implements OnChanges {
  private readonly service = inject(PlacementService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  readonly studentId = input.required<number>();
  readonly report = signal<PlacementReport | null>(null);
  readonly heading = signal<string | null>(null);

  constructor() {
    this.transloco
      .selectTranslate('parent.title', {}, 'placement')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((title: string) => this.heading.set(title));
  }

  ngOnChanges(): void {
    this.report.set(null);
    this.service
      .childReport(this.studentId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (report) => this.report.set(report), error: () => this.report.set(null) });
  }
}
