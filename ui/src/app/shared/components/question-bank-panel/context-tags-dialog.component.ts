import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { forkJoin } from 'rxjs';
import { MAX_QUESTION_SKILLS, Skill, SkillCategory, skillLabelKey } from '../../../models/skill';
import { SkillService } from '../../../services/skill.service';

export interface ContextTagsDialogData {
  questionId: number;
}

/**
 * Issue #703: kayıtlı sorunun bağlam temelli bayrağı ve beceri etiketleri. Etiket öğretmenin beyanıdır; ekran uygunluk
 * iddiası yazmaz. Kapanışta kaydedilen `QuestionContextTags` döner.
 */
@Component({
  selector: 'app-context-tags-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, MatButtonModule, MatCheckboxModule, MatDialogModule],
  providers: [provideTranslocoScope('question-bank')],
  styleUrls: ['./context-tags-dialog.component.scss'],
  template: `
    <ng-container *transloco="let t; prefix: 'question-bank'">
      <h2 mat-dialog-title>{{ t('contextTags.dialogTitle', { id: data.questionId }) }}</h2>
      <mat-dialog-content>
        @if (error()) {
          <p role="alert" data-testid="context-tags-error">{{ t('contextTags.' + error()) }}</p>
        }
        @if (!loading()) {
          <mat-checkbox data-testid="context-based-toggle" [checked]="isContextBased()"
            (change)="isContextBased.set($event.checked)">
            {{ t('contextTags.isContextBased') }}
          </mat-checkbox>
          <p class="ct-hint">{{ t('contextTags.hint') }}</p>
          <p class="ct-hint" aria-live="polite">{{ t('contextTags.selectedCount', { count: selectedIds().size, max: max }) }}</p>
          @for (group of groups(); track group.category) {
            <section class="ct-group" [attr.aria-label]="t('contextTags.category.' + group.category)">
              <h3>{{ t('contextTags.category.' + group.category) }}</h3>
              @for (skill of group.skills; track skill.id) {
                <mat-checkbox [attr.data-testid]="'skill-' + skill.code" [checked]="selectedIds().has(skill.id)"
                  [disabled]="!selectedIds().has(skill.id) && atLimit()" (change)="toggle(skill.id)">
                  {{ t(labelKey(skill.code)) }}<span class="ct-code">{{ skill.code }}</span>
                </mat-checkbox>
              }
            </section>
          }
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ t('contextTags.cancel') }}</button>
        <button mat-flat-button type="button" data-testid="context-tags-save" [disabled]="loading() || saving()" (click)="save()">
          {{ t('contextTags.save') }}
        </button>
      </mat-dialog-actions>
    </ng-container>
  `,
})
export class ContextTagsDialogComponent implements OnInit {
  protected readonly data = inject<ContextTagsDialogData>(MAT_DIALOG_DATA);
  private readonly service = inject(SkillService);
  private readonly dialogRef = inject(MatDialogRef<ContextTagsDialogComponent>);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly max = MAX_QUESTION_SKILLS;
  protected readonly labelKey = skillLabelKey;

  private readonly skills = signal<Skill[]>([]);
  protected readonly isContextBased = signal(false);
  protected readonly selectedIds = signal<ReadonlySet<number>>(new Set());
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<'loadError' | 'saveError' | null>(null);

  protected readonly atLimit = computed(() => this.selectedIds().size >= MAX_QUESTION_SKILLS);
  protected readonly groups = computed(() =>
    [SkillCategory.Integrated, SkillCategory.HigherOrder].map((category) => ({
      category,
      skills: this.skills().filter((skill) => skill.category === category),
    })),
  );

  ngOnInit(): void {
    forkJoin({ skills: this.service.list(), tags: this.service.getForQuestion(this.data.questionId) })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ skills, tags }) => {
          this.skills.set(skills);
          this.isContextBased.set(tags.isContextBased);
          this.selectedIds.set(new Set(tags.skills.map((skill) => skill.id)));
          this.loading.set(false);
        },
        // Mevcut etiketler bilinmeden kaydetmek onları silerdi; kaydet kapalı kalır.
        error: () => this.error.set('loadError'),
      });
  }

  protected toggle(skillId: number): void {
    const next = new Set(this.selectedIds());
    if (next.has(skillId)) next.delete(skillId);
    else if (next.size < MAX_QUESTION_SKILLS) next.add(skillId);
    this.selectedIds.set(next);
  }

  protected save(): void {
    this.saving.set(true);
    this.error.set(null);
    this.service
      .setForQuestion(this.data.questionId, this.isContextBased(), [...this.selectedIds()])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (tags) => this.dialogRef.close(tags),
        error: () => {
          this.saving.set(false);
          this.error.set('saveError');
        },
      });
  }
}
