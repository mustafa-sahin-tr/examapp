import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import {
  TranslocoDirective,
  TranslocoPipe,
  TranslocoService,
  provideTranslocoScope,
} from '@jsverse/transloco';
import { firstValueFrom, take } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { ApiResult, TaxonomyFilter, TaxonomySubject, TaxonomyTopic } from '../../../models/taxonomy';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  ManageSubjectGradesDialogComponent,
  ManageSubjectGradesDialogData,
} from './manage-subject-grades-dialog/manage-subject-grades-dialog.component';

type Level = 'subject' | 'topic' | 'subtopic';
/** Sınıf filtresi: 'all' → en az bir sınıfa bağlı dersler, 'unassigned' → sınıfsız dersler, number → o sınıfa bağlı dersler. */
export type GradeFilter = number | 'all' | 'unassigned';

/** Yönetim ekranlarının ortak Transloco scope'u: `public/i18n/admin/<lang>.json` (issue #183). */
const ADMIN_SCOPE = 'admin';

@Component({
  selector: 'app-taxonomy-manager',
  standalone: true,
  templateUrl: './taxonomy-manager.component.html',
  styleUrls: ['./taxonomy-manager.component.scss'],
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatProgressBarModule,
    MatButtonToggleModule,
    MatDialogModule,
    MatSnackBarModule,
    TranslocoDirective,
    TranslocoPipe,
  ],
  providers: [provideTranslocoScope(ADMIN_SCOPE)],
})
export class TaxonomyManagerComponent implements OnInit {
  private readonly admin = inject(AdminService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);
  readonly subjects = signal<TaxonomySubject[]>([]);
  readonly grades = signal<{ id: number; name: string }[]>([]);

  readonly selectedSubjectId = signal<number | null>(null);
  readonly selectedTopicId = signal<number | null>(null);

  /** Issue #119: Sınıf filtresi. Backend sadece gradeId/unassigned filtreler; 'all' için sınıfsız dersler istemcide gizlenir. */
  readonly selectedGradeFilter = signal<GradeFilter>('all');
  readonly visibleSubjects = computed(() =>
    this.selectedGradeFilter() === 'all'
      ? this.subjects().filter((s) => s.gradeIds.length > 0)
      : this.subjects()
  );

  readonly selectedSubject = computed(
    () => this.visibleSubjects().find((s) => s.id === this.selectedSubjectId()) ?? null
  );
  readonly topics = computed(() => this.selectedSubject()?.topics ?? []);
  readonly selectedTopic = computed(
    () => this.topics().find((t) => t.id === this.selectedTopicId()) ?? null
  );
  readonly subTopics = computed(() => this.selectedTopic()?.subTopics ?? []);

  // inline add fields
  newSubjectName = '';
  newTopicName = '';
  newTopicGradeId: number | null = null;
  newSubTopicName = '';

  // inline edit state
  editing = signal<{ level: Level; id: number } | null>(null);
  editName = '';
  editGradeId: number | null = null;

  constructor() {
    this.preloadScope();
  }

  ngOnInit(): void {
    this.load();
  }

  /**
   * @param autoSelectFirst seçim geçersizse ilk dersi otomatik seç (ilk yükleme / CRUD sonrası).
   * Filtre değişiminde `false` verilir: issue AC gereği alt seçimler sıfır kalır.
   */
  load(autoSelectFirst = true): void {
    this.loading.set(true);
    this.error.set(null);
    this.admin.getTaxonomy(this.filterParams()).subscribe({
      next: (tree) => {
        this.subjects.set(tree.subjects);
        this.grades.set(tree.grades);
        // keep selections if still valid
        if (!this.visibleSubjects().some((s) => s.id === this.selectedSubjectId())) {
          this.selectedSubjectId.set(autoSelectFirst ? (this.visibleSubjects()[0]?.id ?? null) : null);
          this.selectedTopicId.set(null);
        }
        this.loading.set(false);
      },
      error: () => {
        this.error.set(this.text('messages.taxonomyLoadFailed'));
        this.snack.open(this.text('messages.taxonomyLoadFailed'), this.close, { duration: 4000 });
        this.loading.set(false);
      },
    });
  }

  selectSubject(id: number): void {
    this.selectedSubjectId.set(id);
    this.selectedTopicId.set(null);
    this.cancelEdit();
  }

  selectTopic(id: number): void {
    this.selectedTopicId.set(id);
    this.cancelEdit();
  }

  gradeName(id: number): string {
    return this.grades().find((g) => g.id === id)?.name ?? `#${id}`;
  }

  // ---- grade filter (Issue #119) ----

  setGradeFilter(filter: GradeFilter | null | undefined): void {
    if (filter == null || filter === this.selectedGradeFilter()) return;
    this.selectedGradeFilter.set(filter);
    // AC: filtre değişince Ders → Konu → Alt Konu seçimleri sıfırlanır
    this.selectedSubjectId.set(null);
    this.selectedTopicId.set(null);
    this.cancelEdit();
    this.load(false);
  }

  private filterParams(): TaxonomyFilter | undefined {
    const f = this.selectedGradeFilter();
    if (f === 'unassigned') return { unassigned: true };
    if (typeof f === 'number') return { gradeId: f };
    return undefined;
  }

  /** Dersin bağlı olduğu sınıfların adları (chip listesi için). */
  subjectGradeNames(s: TaxonomySubject): string[] {
    return s.gradeIds.map((id) => this.gradeName(id));
  }

  /** Filtreye göre boş liste başlığı / ipucu. */
  subjectsEmptyText(): { title: string; hint: string } {
    const f = this.selectedGradeFilter();
    if (f === 'unassigned') {
      return {
        title: this.text('subjects.emptyUnassignedTitle'),
        hint: this.text('subjects.emptyUnassignedHint'),
      };
    }
    if (typeof f === 'number') {
      return {
        title: this.text('subjects.emptyGradeTitle'),
        hint: this.text('subjects.emptyGradeHint'),
      };
    }
    if (this.subjects().length > 0) {
      return {
        title: this.text('subjects.emptyLinkedTitle'),
        hint: this.text('subjects.emptyLinkedHint'),
      };
    }
    return { title: this.text('subjects.emptyTitle'), hint: this.text('subjects.emptyHint') };
  }

  /** Scope'a göreli anahtarı senkron çözer; sözlük şablon render edilirken yüklenmiş olur. */
  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${ADMIN_SCOPE}.taxonomy.${key}`, params) ?? '';
  }

  /** Snackbar kapatma butonunun metni. */
  private get close(): string {
    return this.text('messages.close');
  }

  async manageGrades(s: TaxonomySubject): Promise<void> {
    const data: ManageSubjectGradesDialogData = {
      subjectId: s.id,
      subjectName: s.name,
      grades: this.grades(),
      selectedGradeIds: s.gradeIds,
    };
    const changed = await firstValueFrom(
      this.dialog
        .open<ManageSubjectGradesDialogComponent, ManageSubjectGradesDialogData, boolean>(
          ManageSubjectGradesDialogComponent,
          // ESC/backdrop kapatılırsa `applied` bilgisi afterClosed'a taşınmaz; sadece butonlarla kapansın.
          { data, autoFocus: 'first-tabbable', disableClose: true }
        )
        .afterClosed()
    );
    if (changed) this.load();
  }

  // ---- create ----

  async addSubject(): Promise<void> {
    const name = this.newSubjectName.trim();
    if (!name) return;
    await this.run(() => firstValueFrom(this.admin.createSubject({ name })));
    this.newSubjectName = '';
  }

  async addTopic(): Promise<void> {
    const name = this.newTopicName.trim();
    const subjectId = this.selectedSubjectId();
    if (!name || !subjectId || !this.newTopicGradeId) {
      this.snack.open(this.text('topics.nameAndGradeRequired'), this.close, { duration: 3000 });
      return;
    }
    await this.run(() =>
      firstValueFrom(this.admin.createTopic({ name, subjectId, gradeId: this.newTopicGradeId! }))
    );
    this.newTopicName = '';
  }

  async addSubTopic(): Promise<void> {
    const name = this.newSubTopicName.trim();
    const topicId = this.selectedTopicId();
    if (!name || !topicId) return;
    await this.run(() => firstValueFrom(this.admin.createSubTopic({ name, topicId })));
    this.newSubTopicName = '';
  }

  // ---- edit ----

  startEdit(level: Level, item: { id: number; name: string; gradeId?: number }): void {
    this.editing.set({ level, id: item.id });
    this.editName = item.name;
    this.editGradeId = item.gradeId ?? null;
  }

  cancelEdit(): void {
    this.editing.set(null);
  }

  isEditing(level: Level, id: number): boolean {
    const e = this.editing();
    return !!e && e.level === level && e.id === id;
  }

  async saveEdit(
    level: Level,
    original: TaxonomySubject | TaxonomyTopic | { id: number }
  ): Promise<void> {
    const name = this.editName.trim();
    if (!name) return;
    const id = original.id;

    if (level === 'subject') {
      await this.run(() => firstValueFrom(this.admin.updateSubject(id, { name })));
    } else if (level === 'topic') {
      const t = original as TaxonomyTopic;
      await this.run(() =>
        firstValueFrom(
          this.admin.updateTopic(id, {
            name,
            subjectId: t.subjectId,
            gradeId: this.editGradeId ?? t.gradeId,
          })
        )
      );
    } else {
      const topicId = this.selectedTopicId()!;
      await this.run(() => firstValueFrom(this.admin.updateSubTopic(id, { name, topicId })));
    }
    this.cancelEdit();
  }

  // ---- delete ----

  async remove(level: Level, item: { id: number; name: string }): Promise<void> {
    const data: ConfirmDialogData = {
      title: this.text(`delete.${level}Title`),
      message: this.text(`delete.${level}Message`, { name: item.name }),
      confirmText: this.text('delete.confirm'),
      icon: 'delete',
      confirmColor: 'warn',
    };
    const ok = await firstValueFrom(
      this.dialog.open(ConfirmDialogComponent, { data }).afterClosed()
    );
    if (!ok) return;

    if (level === 'subject') {
      await this.run(() => firstValueFrom(this.admin.deleteSubject(item.id)));
      if (this.selectedSubjectId() === item.id) this.selectedSubjectId.set(null);
    } else if (level === 'topic') {
      await this.run(() => firstValueFrom(this.admin.deleteTopic(item.id)));
      if (this.selectedTopicId() === item.id) this.selectedTopicId.set(null);
    } else {
      await this.run(() => firstValueFrom(this.admin.deleteSubTopic(item.id)));
    }
  }

  private async run(action: () => Promise<ApiResult>): Promise<void> {
    this.busy.set(true);
    try {
      const res = await action();
      this.snack.open(res.message, this.close, { duration: 3000 });
      if (res.success) this.load();
    } catch (err: unknown) {
      const msg =
        (err as { error?: { message?: string } } | null)?.error?.message ?? this.text('messages.actionFailed');
      this.snack.open(msg, this.close, { duration: 4000 });
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Şablon dışı metinler (snackbar, dialog, hata mesajı) senkron `translate()` ile okunur;
   * sözlük şablon render edilmeden de hazır olsun diye scope burada yüklenir.
   */
  private preloadScope(): void {
    this.transloco
      .load(`${ADMIN_SCOPE}/${this.transloco.getActiveLang()}`)
      .pipe(take(1))
      .subscribe();
  }

}
