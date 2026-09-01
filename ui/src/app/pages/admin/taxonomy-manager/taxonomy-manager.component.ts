import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { TaxonomySubject, TaxonomyTopic } from '../../../models/taxonomy';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/components/confirm-dialog/confirm-dialog.component';

type Level = 'subject' | 'topic' | 'subtopic';

@Component({
  selector: 'app-taxonomy-manager',
  standalone: true,
  templateUrl: './taxonomy-manager.component.html',
  styleUrls: ['./taxonomy-manager.component.scss'],
  imports: [
    CommonModule,
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatProgressBarModule,
    MatDialogModule,
    MatSnackBarModule,
  ],
})
export class TaxonomyManagerComponent implements OnInit {
  private readonly admin = inject(AdminService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);

  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly subjects = signal<TaxonomySubject[]>([]);
  readonly grades = signal<{ id: number; name: string }[]>([]);

  readonly selectedSubjectId = signal<number | null>(null);
  readonly selectedTopicId = signal<number | null>(null);

  readonly selectedSubject = computed(
    () => this.subjects().find((s) => s.id === this.selectedSubjectId()) ?? null
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

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.admin.getTaxonomy().subscribe({
      next: (tree) => {
        this.subjects.set(tree.subjects);
        this.grades.set(tree.grades);
        // keep selections if still valid
        if (!this.subjects().some((s) => s.id === this.selectedSubjectId())) {
          this.selectedSubjectId.set(this.subjects()[0]?.id ?? null);
          this.selectedTopicId.set(null);
        }
        this.loading.set(false);
      },
      error: () => {
        this.snack.open('Taksonomi yüklenemedi', 'Kapat', { duration: 4000 });
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

  async saveEdit(level: Level, original: TaxonomySubject | TaxonomyTopic | { id: number }): Promise<void> {
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
    const labels: Record<Level, string> = { subject: 'ders', topic: 'konu', subtopic: 'alt konu' };
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
    } else {
      await this.run(() => firstValueFrom(this.admin.deleteSubTopic(item.id)));
    }
  }

  private async run(action: () => Promise<{ success: boolean; message: string }>): Promise<void> {
    this.busy.set(true);
    try {
      const res = await action();
      this.snack.open(res.message, 'Kapat', { duration: 3000 });
      if (res.success) this.load();
    } catch (err: any) {
      const msg = err?.error?.message ?? 'İşlem başarısız';
      this.snack.open(msg, 'Kapat', { duration: 4000 });
    } finally {
      this.busy.set(false);
    }
  }
}
