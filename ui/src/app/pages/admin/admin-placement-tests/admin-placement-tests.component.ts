import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { Observable } from 'rxjs';

import { AdminPlacementTest } from '../../../models/placement.model';
import { PlacementService } from '../../../services/placement.service';
import { PageHeaderComponent } from '../../../shared/components/page-header/page-header.component';

/** Issue #1114: sınıf başına seviye tespit testini üretme ve yayımlama. */
@Component({
  selector: 'app-admin-placement-tests',
  standalone: true,
  imports: [MatButtonModule, MatProgressSpinnerModule, TranslocoDirective, PageHeaderComponent],
  providers: [provideTranslocoScope('placement')],
  templateUrl: './admin-placement-tests.component.html',
  styleUrl: './admin-placement-tests.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminPlacementTestsComponent {
  private readonly service = inject(PlacementService);
  private readonly destroyRef = inject(DestroyRef);

  readonly tests = signal<AdminPlacementTest[] | null>(null);
  /** İşlemdeki sınıf. */
  readonly busy = signal<number | null>(null);
  /** Sınıf → hata anahtarı (scope içi). */
  readonly errors = signal<Record<number, string>>({});
  readonly loadError = signal(false);

  constructor() {
    this.service
      .adminList()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (tests) => this.tests.set(tests), error: () => this.loadError.set(true) });
  }

  generate(test: AdminPlacementTest): void {
    this.run(test.gradeId, this.service.adminGenerate(test.gradeId));
  }

  togglePublished(test: AdminPlacementTest): void {
    this.run(test.gradeId, this.service.adminSetPublished(test.gradeId, !test.publishedAtUtc));
  }

  subjects(test: AdminPlacementTest): string {
    return test.subjects.map((s) => `${s.subjectName} (${s.total})`).join(', ');
  }

  private run(gradeId: number, call: Observable<AdminPlacementTest>): void {
    if (this.busy() !== null) return;
    this.busy.set(gradeId);
    this.errors.update(({ [gradeId]: _, ...rest }) => rest);
    call.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (updated) => {
        this.busy.set(null);
        this.tests.update((tests) => tests?.map((t) => (t.gradeId === gradeId ? updated : t)) ?? null);
      },
      error: (err: unknown) => {
        this.busy.set(null);
        const key = err instanceof HttpErrorResponse && err.status === 409 ? 'admin.notEnough' : 'admin.error';
        this.errors.update((e) => ({ ...e, [gradeId]: key }));
      },
    });
  }
}
