import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, model, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NgTemplateOutlet } from '@angular/common';
import { TranslocoDirective, TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import {
  STUDY_PAGE_BLOCK_MAX_TEXT,
  STUDY_PAGE_MAX_BLOCKS,
  STUDY_PAGE_PLATFORM_ICONS,
  StudyPageBlock,
  StudyPageBlockRequest,
  StudyPageBlockType,
  StudyPageLinkPlatform,
} from '../../models/study-page';
import { QuestionBankItem } from '../../models/question-bank';
import { StudyPageService } from '../../services/study-page.service';
import { StudyPageBlocksViewComponent } from './study-page-blocks-view.component';
import {
  StudyPageQuestionPickerData,
  StudyPageQuestionPickerDialogComponent,
} from './study-page-question-picker-dialog.component';

/** Editördeki blok: `key` yerel kimlik (yeni blokta `id` 0), `uploading` görsel yüklenirken. */
export interface StudyPageBlockDraft extends StudyPageBlock {
  key: string;
  uploading?: boolean;
}

interface BlockTypeOption {
  type: StudyPageBlockType;
  icon: string;
  /** `study-pages.blocks` altına göreli. */
  labelKey: string;
}

export const STUDY_PAGE_BLOCK_TYPES: readonly BlockTypeOption[] = [
  { type: StudyPageBlockType.Text, icon: 'notes', labelKey: 'type.text' },
  { type: StudyPageBlockType.Image, icon: 'image', labelKey: 'type.image' },
  { type: StudyPageBlockType.Link, icon: 'link', labelKey: 'type.link' },
  { type: StudyPageBlockType.Question, icon: 'quiz', labelKey: 'type.question' },
  { type: StudyPageBlockType.Flashcard, icon: 'style', labelKey: 'type.flashcard' },
];

let draftSeq = 0;

/** Sunucudan gelen blokları editör taslağına çevirir. */
export function toBlockDrafts(blocks: readonly StudyPageBlock[] | null | undefined): StudyPageBlockDraft[] {
  return (blocks ?? []).map((b) => ({ ...b, key: `b${++draftSeq}` }));
}

/**
 * Kayıt isteğinin blok listesi. Sorusu silinmiş (questionId null) ya da görseli yüklenmemiş blok gönderilmez;
 * tipe ait olmayan alanlar boş bırakılır (sunucu da temizler).
 */
export function toBlockRequests(drafts: readonly StudyPageBlockDraft[]): StudyPageBlockRequest[] {
  return drafts
    .filter((d) => (d.type !== StudyPageBlockType.Question || d.questionId != null) && (d.type !== StudyPageBlockType.Image || !!d.imageUrl))
    .map((d) => ({
      id: d.id || null,
      type: d.type,
      text: d.type === StudyPageBlockType.Question ? null : (d.text ?? null),
      backText: d.type === StudyPageBlockType.Flashcard ? (d.backText ?? null) : null,
      imageUrl: d.type === StudyPageBlockType.Image ? d.imageUrl : null,
      url: d.type === StudyPageBlockType.Link ? (d.url ?? '').trim() : null,
      platform: d.type === StudyPageBlockType.Link ? (d.platform ?? StudyPageLinkPlatform.Other) : null,
      questionId: d.type === StudyPageBlockType.Question ? d.questionId : null,
    }));
}

/**
 * Issue #933: çalışma sayfası bloklarını ekler / sıralar / siler; "Öğrenci görünümü" sekmesi blokları öğrencinin
 * göreceği gibi gösterir. Liste `blocks` model'inde tutulur, kaydı üst editör yapar.
 */
@Component({
  selector: 'app-study-page-blocks-editor',
  standalone: true,
  imports: [
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatSelectModule,
    NgTemplateOutlet,
    TranslocoDirective,
    TranslocoPipe,
    StudyPageBlocksViewComponent,
  ],
  templateUrl: './study-page-blocks-editor.component.html',
  styleUrl: './study-page-blocks-editor.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StudyPageBlocksEditorComponent {
  private readonly studyPages = inject(StudyPageService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  readonly blocks = model<StudyPageBlockDraft[]>([]);
  /** Soru seçicinin varsayılan dersi. */
  readonly subjectId = input<number | null>(null);

  readonly mode = signal<'edit' | 'preview'>('edit');

  protected readonly Type = StudyPageBlockType;
  protected readonly typeOptions = STUDY_PAGE_BLOCK_TYPES;
  protected readonly maxText = STUDY_PAGE_BLOCK_MAX_TEXT;
  protected readonly platforms = [StudyPageLinkPlatform.Other, StudyPageLinkPlatform.Eba, StudyPageLinkPlatform.YouTube];
  protected readonly platformIcons = STUDY_PAGE_PLATFORM_ICONS;
  protected readonly platformKeys: Record<StudyPageLinkPlatform, string> = {
    [StudyPageLinkPlatform.Other]: 'platform.other',
    [StudyPageLinkPlatform.Eba]: 'platform.eba',
    [StudyPageLinkPlatform.YouTube]: 'platform.youtube',
  };

  readonly canAdd = computed(() => this.blocks().length < STUDY_PAGE_MAX_BLOCKS);

  protected optionFor(type: StudyPageBlockType): BlockTypeOption {
    return this.typeOptions.find((o) => o.type === type) ?? this.typeOptions[0];
  }

  /** `index` konumuna (0 = en üst) yeni blok ekler; soru bloğunda seçici hemen açılır. */
  add(type: StudyPageBlockType, index: number): void {
    if (!this.canAdd()) return;
    const draft: StudyPageBlockDraft = {
      key: `n${++draftSeq}`,
      id: 0,
      sortOrder: 0,
      type,
      platform: type === StudyPageBlockType.Link ? StudyPageLinkPlatform.Other : null,
    };
    this.blocks.update((list) => [...list.slice(0, index), draft, ...list.slice(index)]);
    if (type === StudyPageBlockType.Question) {
      this.pickQuestion(draft.key);
    }
  }

  move(index: number, delta: -1 | 1): void {
    const target = index + delta;
    this.blocks.update((list) => {
      if (target < 0 || target >= list.length) return list;
      const next = [...list];
      [next[index], next[target]] = [next[target], next[index]];
      return next;
    });
  }

  remove(key: string): void {
    this.blocks.update((list) => list.filter((b) => b.key !== key));
  }

  patch(key: string, changes: Partial<StudyPageBlockDraft>): void {
    this.blocks.update((list) => list.map((b) => (b.key === key ? { ...b, ...changes } : b)));
  }

  protected inputValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  onImageSelected(key: string, event: Event): void {
    const inputEl = event.target as HTMLInputElement;
    const file = inputEl.files?.[0];
    inputEl.value = '';
    if (!file) return;
    this.patch(key, { uploading: true });
    this.studyPages
      .uploadBlockImage(file)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => this.patch(key, { imageUrl: res.imageUrl, uploading: false }),
        error: (err) => {
          this.patch(key, { uploading: false });
          const message = (err?.error as { message?: string } | null)?.message;
          this.snackBar.open(message || this.transloco.translate('study-pages.blocks.uploadFailed'), undefined, { duration: 4000 });
        },
      });
  }

  pickQuestion(key: string): void {
    this.dialog
      .open<StudyPageQuestionPickerDialogComponent, StudyPageQuestionPickerData, QuestionBankItem>(
        StudyPageQuestionPickerDialogComponent,
        { data: { subjectId: this.subjectId() }, width: '820px', maxWidth: '95vw' },
      )
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((item) => {
        if (item) {
          this.patch(key, { questionId: item.id, questionImageUrl: item.imageUrl, questionText: item.text ?? null });
        } else if (this.blocks().find((b) => b.key === key && b.questionId == null && !b.id)) {
          // Yeni soru bloğunda seçim yapılmadıysa boş blok kalmasın.
          this.remove(key);
        }
      });
  }
}
