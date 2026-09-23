import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
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
import { GradesService } from '../../../services/grades.service';
import { ApiResult, TaxonomyGrade, TaxonomySubject, TaxonomyTopic } from '../../../models/taxonomy';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  ManageSubjectGradesDialogComponent,
  ManageSubjectGradesDialogData,
} from './manage-subject-grades-dialog/manage-subject-grades-dialog.component';

type Level = 'subject' | 'topic' | 'subtopic';

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
  private readonly gradesService = inject(GradesService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);
  readonly subjects = signal<TaxonomySubject[]>([]);
  /** Tek kaynak: `GradesService.getGrades()` (ilk açılış); ağaç yanıtındaki `grades` kullanılmaz. */
  readonly grades = signal<TaxonomyGrade[]>([]);

  readonly selectedSubjectId = signal<number | null>(null);
  readonly selectedTopicId = signal<number | null>(null);

  /**
   * Issue #151: Sınıf filtresi her zaman belirli bir sınıftır ("Tüm Sınıflar"/"Sınıf atanmamış" yok).
   * `null` yalnızca ilk açılışta, admin henüz sınıf seçmemişken olur; seçimden sonra geri dönülemez.
   */
  readonly selectedGradeFilter = signal<number | null>(null);

  readonly selectedSubject = computed(
    () => this.subjects().find((s) => s.id === this.selectedSubjectId()) ?? null
  );
  /** Backend `gradeId` yalnız dersleri filtreler; konular seçili sınıfa göre istemcide süzülür. */
  readonly topics = computed(() => {
    const subject = this.selectedSubject();
    return subject ? this.gradeTopics(subject) : [];
  });
  readonly selectedTopic = computed(
    () => this.topics().find((t) => t.id === this.selectedTopicId()) ?? null
  );
  readonly subTopics = computed(() => this.selectedTopic()?.subTopics ?? []);

  // inline add fields
  newSubjectName = '';
  newTopicName = '';
  newSubTopicName = '';

  // inline edit state
  editing = signal<{ level: Level; id: number } | null>(null);
  editName = '';

  constructor() {
    this.preloadScope();
  }

  ngOnInit(): void {
    this.loadGrades();
  }

  /** İlk açılışta yalnız sınıf listesi yüklenir (Issue #151): sınıf seçilmeden ders/konu ağacı istenmez. */
  loadGrades(): void {
    this.loading.set(true);
    this.error.set(null);
    this.gradesService.getGrades().subscribe({
      next: (grades) => {
        this.grades.set(
          grades.map((g) => ({ id: g.id, name: g.name })).sort((a, b) => a.id - b.id)
        );
        this.loading.set(false);
      },
      error: () => {
        this.error.set(this.text('messages.gradesLoadFailed'));
        this.loading.set(false);
      },
    });
  }

  /** Hata kutusundaki "Tekrar dene": sınıf seçilmediyse sınıf listesini, seçildiyse ağacı yeniden ister. */
  retry(): void {
    if (this.selectedGradeFilter() == null) this.loadGrades();
    else this.load();
  }

  /**
   * Seçili sınıfın ders/konu ağacını yükler; sınıf seçili değilse istek atmaz.
   * @param autoSelectFirst seçim geçersizse ilk dersi otomatik seç (CRUD sonrası).
   * Filtre değişiminde `false` verilir: issue AC gereği alt seçimler sıfır kalır.
   */
  load(autoSelectFirst = true): void {
    const gradeId = this.selectedGradeFilter();
    if (gradeId == null) return;
    this.loading.set(true);
    this.error.set(null);
    this.admin.getTaxonomy({ gradeId }).subscribe({
      next: (tree) => {
        this.subjects.set(tree.subjects);
        // keep selections if still valid
        if (!this.subjects().some((s) => s.id === this.selectedSubjectId())) {
          this.selectedSubjectId.set(autoSelectFirst ? (this.subjects()[0]?.id ?? null) : null);
          this.selectedTopicId.set(null);
        }
        this.loading.set(false);
      },
      error: () => {
        // Eski sınıfın dersleri ekranda kalırsa yanlış gradeId ile konu yazılabilir.
        this.subjects.set([]);
        this.selectedSubjectId.set(null);
        this.selectedTopicId.set(null);
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

  // ---- grade filter (Issue #119, #151) ----

  setGradeFilter(gradeId: number | null | undefined): void {
    if (gradeId == null || gradeId === this.selectedGradeFilter()) return;
    this.selectedGradeFilter.set(gradeId);
    // AC: filtre değişince Ders → Konu → Alt Konu seçimleri ve eski sınıfın verisi hemen temizlenir;
    // yanıt gelene kadar önceki sınıfın dersine yeni sınıfın gradeId'siyle konu yazılamaz.
    this.subjects.set([]);
    this.selectedSubjectId.set(null);
    this.selectedTopicId.set(null);
    this.newTopicName = '';
    this.newSubTopicName = '';
    this.cancelEdit();
    this.load(false);
  }

  /** Dersin yalnız seçili sınıfa ait konuları. */
  gradeTopics(s: TaxonomySubject): TaxonomyTopic[] {
    const gradeId = this.selectedGradeFilter();
    return gradeId == null ? [] : s.topics.filter((t) => t.gradeId === gradeId);
  }

  /** Dersin bağlı olduğu sınıfların adları (chip listesi için). */
  subjectGradeNames(s: TaxonomySubject): string[] {
    return s.gradeIds.map((id) => this.gradeName(id));
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
    const gradeId = this.selectedGradeFilter();
    if (!name || gradeId == null) return;
    // "Sınıf atanmamış" filtresi kalktığı için yeni ders seçili sınıfa bağlı oluşturulur; aksi halde listede görünmez.
    await this.run(() => firstValueFrom(this.admin.createSubject({ name, gradeIds: [gradeId] })));
    this.newSubjectName = '';
  }

  async addTopic(): Promise<void> {
    const name = this.newTopicName.trim();
    const subjectId = this.selectedSubjectId();
    const gradeId = this.selectedGradeFilter();
    // Form yalnız sınıf ve ders seçiliyken görünür; sınıf her zaman filtreden gelir.
    if (!subjectId || gradeId == null) return;
    if (!name) {
      this.snack.open(this.text('topics.nameRequired'), this.close, { duration: 3000 });
      return;
    }
    await this.run(() => firstValueFrom(this.admin.createTopic({ name, subjectId, gradeId })));
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

  startEdit(level: Level, item: { id: number; name: string }): void {
    this.editing.set({ level, id: item.id });
    this.editName = item.name;
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
      // Issue #151: konunun sınıfı bu ekrandan değiştirilemez; backend DTO'su gradeId istediği için mevcut değer gönderilir.
      const t = original as TaxonomyTopic;
      await this.run(() =>
        firstValueFrom(
          this.admin.updateTopic(id, { name, subjectId: t.subjectId, gradeId: t.gradeId })
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
