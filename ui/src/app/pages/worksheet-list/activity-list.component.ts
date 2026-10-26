import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { catchError, forkJoin, of } from 'rxjs';

import { WorksheetKind } from '../../models/activity';
import { AssignedWorksheet } from '../../models/assignment';
import { Test } from '../../models/test-instance';
import { TestService } from '../../services/test.service';
import { WorksheetListViewCardComponent } from './worksheet-list-view-card.component';

/**
 * Issue #573: öğrencinin "Etkinlikler" sekmesi — atanmış (başlanmamış), devam eden ve tamamlanan etkinlik sayfaları.
 * Kaynaklar tür filtresiyle (`kind=1`) okunur: `assignments/active`, `list?statuses=0|1`. Kart, mevcut worksheet kartıdır
 * (etkinlikte puan/yüzde/süre göstermez). Scope sözlüğü üst sayfa (`worksheet-list`) tarafından sağlanır.
 */
@Component({
  selector: 'app-activity-list',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective, WorksheetListViewCardComponent],
  templateUrl: './activity-list.component.html',
  styleUrl: './activity-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityListComponent implements OnInit {
  private readonly testService = inject(TestService);
  private readonly destroyRef = inject(DestroyRef);

  readonly subjectNames = input<Map<number, string>>(new Map());
  readonly gradeNames = input<Map<number, string>>(new Map());

  readonly loading = signal(true);
  readonly error = signal(false);
  /** S10: kaynaklardan biri yüklenemedi ama diğerleri geldi — engellemeyen uyarı. */
  readonly partialError = signal(false);
  readonly assignments = signal<AssignedWorksheet[]>([]);
  readonly inProgress = signal<Test[]>([]);
  readonly completed = signal<Test[]>([]);

  /** Atanmış ama başlanmamış etkinlikler (başlananlar "Devam ediyor"da). */
  readonly fresh = computed<Test[]>(() => {
    const open = new Set([...this.inProgress(), ...this.completed()].map((t) => t.id));
    return this.assignments()
      .filter((a) => !a.hasStarted && !a.isCompleted && !open.has(a.worksheetId))
      .map((a) => this.toTest(a));
  });

  readonly isEmpty = computed(
    () => this.fresh().length === 0 && this.inProgress().length === 0 && this.completed().length === 0
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.partialError.set(false);
    const kind = WorksheetKind.Activity;
    forkJoin({
      assignments: this.testService.getActiveAssignments(kind).pipe(catchError(() => of(null))),
      inProgress: this.testService
        .listWorksheets({ statuses: [0], kind, pageSize: 50, sortBy: 'recent' })
        .pipe(catchError(() => of(null))),
      completed: this.testService
        .listWorksheets({ statuses: [1], kind, pageSize: 50, sortBy: 'recent' })
        .pipe(catchError(() => of(null))),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(({ assignments, inProgress, completed }) => {
        this.loading.set(false);
        if (!assignments && !inProgress && !completed) {
          this.error.set(true);
          return;
        }
        this.partialError.set(!assignments || !inProgress || !completed);
        this.assignments.set((assignments ?? []).filter((a) => (a.kind ?? kind) === kind));
        this.inProgress.set(inProgress?.items ?? []);
        this.completed.set(completed?.items ?? []);
      });
  }

  assignmentFor(worksheetId: number | null): AssignedWorksheet | null {
    return this.assignments().find((a) => a.worksheetId === worksheetId) ?? null;
  }

  subjectName(id?: number): string {
    return id != null ? this.subjectNames().get(id) ?? '' : '';
  }

  gradeName(id?: number): string {
    return id != null ? this.gradeNames().get(id) ?? '' : '';
  }

  private toTest(a: AssignedWorksheet): Test {
    return {
      id: a.worksheetId,
      name: a.name,
      description: a.description,
      gradeId: a.gradeId,
      maxDurationSeconds: a.maxDurationSeconds,
      isPracticeTest: false,
      imageUrl: a.imageUrl ?? undefined,
      subtitle: a.subtitle ?? undefined,
      badgeText: a.badgeText ?? undefined,
      questionCount: a.questionCount,
      subjectId: a.subjectId ?? undefined,
      topicId: a.topicId ?? undefined,
      subTopicId: a.subTopicId ?? undefined,
      kind: WorksheetKind.Activity,
    };
  }
}
