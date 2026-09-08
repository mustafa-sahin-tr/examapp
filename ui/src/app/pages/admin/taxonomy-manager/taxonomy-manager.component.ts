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
import { firstValueFrom } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import {
  School,
  TaxonomyFilter,
  TaxonomySubject,
  TaxonomyTopic,
} from '../../../models/taxonomy';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  ManageSubjectGradesDialogComponent,
  ManageSubjectGradesDialogData,
} from './manage-subject-grades-dialog/manage-subject-grades-dialog.component';

type Level = 'subject' | 'topic' | 'subtopic' | 'school';
/** Sınıf filtresi: 'all' → en az bir sınıfa bağlı dersler, 'unassigned' → sınıfsız dersler, number → o sınıfa bağlı dersler. */
export type GradeFilter = number | 'all' | 'unassigned';

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
  ],
})
export class TaxonomyManagerComponent implements OnInit {
  private readonly admin = inject(AdminService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);
  readonly subjects = signal<TaxonomySubject[]>([]);
  readonly grades = signal<{ id: number; name: string }[]>([]);

  readonly schoolsLoading = signal(false);
  readonly schoolsError = signal<string | null>(null);
  readonly schools = signal<School[]>([]);

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
  newSchoolName = '';
  newSchoolCity = '';

  // inline edit state
  editing = signal<{ level: Level; id: number } | null>(null);
  editName = '';
  editGradeId: number | null = null;
  editCity = '';

  ngOnInit(): void {
    this.load();
    this.loadSchools();
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
        this.error.set('Taksonomi yüklenemedi');
        this.snack.open('Taksonomi yüklenemedi', 'Kapat', { duration: 4000 });
        this.loading.set(false);
      },
    });
  }

  loadSchools(): void {
    this.schoolsLoading.set(true);
    this.schoolsError.set(null);
    this.admin.getSchools().subscribe({
      next: (list) => {
        this.schools.set(list);
        this.schoolsLoading.set(false);
      },
      error: () => {
        this.schoolsError.set('Okullar yüklenemedi');
        this.schoolsLoading.set(false);
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
      return { title: 'Sınıf atanmamış ders yok', hint: 'Tüm dersler en az bir sınıfa bağlı' };
    }
    if (typeof f === 'number') {
      return {
        title: 'Bu sınıfa bağlı ders yok',
        hint: 'Bir dersin "Sınıfları Yönet" aksiyonundan bu sınıfı ekleyebilirsin',
      };
    }
    if (this.subjects().length > 0) {
      return {
        title: 'Sınıfa bağlı ders yok',
        hint: '"Sınıf atanmamış" filtresinden derslere sınıf ata',
      };
    }
    return { title: 'Henüz ders eklenmemiş', hint: 'Yukarıdaki alandan ilk dersi ekle' };
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
      this.snack.open('Konu adı ve sınıf gerekli', 'Kapat', { duration: 3000 });
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

  async addSchool(): Promise<void> {
    const name = this.newSchoolName.trim();
    if (!name) return;
    const city = this.newSchoolCity.trim();
    await this.runSchools(() =>
      firstValueFrom(this.admin.createSchool({ name, city: city || null }))
    );
    this.newSchoolName = '';
    this.newSchoolCity = '';
  }

  // ---- edit ----

  startEdit(
    level: Level,
    item: { id: number; name: string; gradeId?: number; city?: string | null }
  ): void {
    this.editing.set({ level, id: item.id });
    this.editName = item.name;
    this.editGradeId = item.gradeId ?? null;
    this.editCity = item.city ?? '';
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
    original: TaxonomySubject | TaxonomyTopic | School | { id: number }
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
    } else if (level === 'subtopic') {
      const topicId = this.selectedTopicId()!;
      await this.run(() => firstValueFrom(this.admin.updateSubTopic(id, { name, topicId })));
    } else {
      const city = this.editCity.trim();
      await this.runSchools(() =>
        firstValueFrom(this.admin.updateSchool(id, { name, city: city || null }))
      );
    }
    this.cancelEdit();
  }

  // ---- delete ----

  async remove(level: Level, item: { id: number; name: string }): Promise<void> {
    const labels: Record<Level, string> = {
      subject: 'ders',
      topic: 'konu',
      subtopic: 'alt konu',
      school: 'okul',
    };
    const data: ConfirmDialogData = {
      title: `${labels[level]} sil`,
      message: `"${item.name}" ${labels[level]}unu silmek istediğine emin misin?`,
      confirmText: 'Sil',
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
    } else if (level === 'subtopic') {
      await this.run(() => firstValueFrom(this.admin.deleteSubTopic(item.id)));
    } else {
      await this.runSchools(() => firstValueFrom(this.admin.deleteSchool(item.id)));
    }
  }

  private async run(action: () => Promise<{ success: boolean; message: string }>): Promise<void> {
    this.busy.set(true);
    try {
      const res = await action();
      this.snack.open(res.message, 'Kapat', { duration: 3000 });
      if (res.success) this.load();
    } catch (err: unknown) {
      const msg =
        (err as { error?: { message?: string } } | null)?.error?.message ?? 'İşlem başarısız';
      this.snack.open(msg, 'Kapat', { duration: 4000 });
    } finally {
      this.busy.set(false);
    }
  }

  private async runSchools(
    action: () => Promise<{ success: boolean; message: string }>
  ): Promise<void> {
    this.busy.set(true);
    try {
      const res = await action();
      this.snack.open(res.message, 'Kapat', { duration: 3000 });
      if (res.success) this.loadSchools();
    } catch (err: unknown) {
      const msg =
        (err as { error?: { message?: string } } | null)?.error?.message ?? 'İşlem başarısız';
      this.snack.open(msg, 'Kapat', { duration: 4000 });
    } finally {
      this.busy.set(false);
    }
  }
}
