import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList, moveItemInArray } from '@angular/cdk/drag-drop';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { filter, switchMap } from 'rxjs';

import { authoringItemTitle, parseDefinition } from '../../../../models/activity-authoring';
import { Question } from '../../../../models/question';
import { QuestionService } from '../../../../services/question.service';
import {
  ActivityItemRemoveDialogComponent,
  ActivityItemRemoveDialogData,
} from './activity-item-remove-dialog.component';

type LoadState = 'loading' | 'loaded' | 'error';

/** Rapordaki tür adlarıyla aynı anahtarlar (`activity.report.types.*`); bilinmeyen tür MCQ sayılır. */
const KNOWN_TYPES = new Set([
  'matrixChoice',
  'slotFill',
  'dragPlace',
  'match',
  'textSelect',
  'dragDropLabeling',
  'openText',
  'mazeTree',
  'crossword',
  'wordSearch',
  'logicGrid',
  'regionPaint',
  'numberLine',
  'barChart',
  'gridDraw',
  'balanceScale',
  'clockSet',
]);

export interface ActivityItemRow {
  questionId: number;
  number: number;
  typeKey: string;
  preview: string;
  imageUrl: string | null;
  /** Görselsiz editörün formuna dönüşebilen madde (`/activity-editor`). */
  formEditable: boolean;
  /** Tuvalden gelen görselli madde: editörde açılmaz. */
  visual: boolean;
}

/**
 * Issue #684 (#647 dilim a): etkinlik detayında öğretmenin madde listesi — sıra no, küçük resim, tür rozeti, yönerge önizlemesi;
 * düzenleyebilen için görselsiz maddede Düzenle (`/activity-editor`) ve Kaldır (soft delete, soru havuzda kalır). Kaldırma yalnız
 * bundan sonra başlayan oturumlara uygulanır; başlamış oturum maddeyi başladığı hâliyle bitirir (backend #684).
 * Issue #685 (dilim b): sürükleyerek (`cdkDragHandle`; dar ekranda gizli) ya da ↑/↓ ile yeniden sıralama. Liste iyimser
 * güncellenir, bütün sıra `PUT test/{id}/order` ile yazılır; hata olursa eski sıra geri gelir. Başlamış oturumlar etkilenmez
 * (oturum sırası başlarken kopyalanır); ilk sıra değişiminde bunu söyleyen bilgi mesajı.
 * Scope sözlüğü üst sayfa (`worksheet-detail`) tarafından sağlanır.
 */
@Component({
  selector: 'app-activity-item-list',
  standalone: true,
  imports: [CdkDrag, CdkDragHandle, CdkDropList, MatButtonModule, MatIconModule, MatTooltipModule, TranslocoDirective],
  templateUrl: './activity-item-list.component.html',
  styleUrl: './activity-item-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityItemListComponent implements OnInit {
  private readonly questionService = inject(QuestionService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  readonly worksheetId = input.required<number>();
  readonly gradeId = input<number | null | undefined>(null);
  readonly canEdit = input(false);

  readonly addTextItem = output<void>();
  readonly addFromBank = output<void>();
  /** Bir madde kaldırıldı — üst sayfa sayacı / detayı tazeler. */
  readonly removed = output<number>();

  readonly state = signal<LoadState>('loading');
  readonly rows = signal<ActivityItemRow[]>([]);
  readonly removingId = signal<number | null>(null);
  /** Issue #685: sıra kaydediliyor — liste soluk (`aria-busy`), düğmeler pasif (odak düğmede kalır). */
  readonly saving = signal(false);
  /** `aria-live=polite` duyurusu ("Madde 2, 3. sıraya taşındı"). */
  readonly announcement = signal('');
  readonly busy = computed(() => this.saving() || this.removingId() !== null);
  private reorderNoteShown = false;

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.questionService
      .getAll(this.worksheetId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (questions) => {
          this.rows.set(toRows(questions ?? []));
          this.state.set('loaded');
        },
        error: () => this.state.set('error'),
      });
  }

  edit(row: ActivityItemRow): void {
    const gradeId = this.gradeId();
    this.router.navigate(['/activity-editor'], {
      queryParams: { testId: this.worksheetId(), ...(gradeId ? { gradeId } : {}), questionId: row.questionId },
    });
  }

  remove(row: ActivityItemRow): void {
    if (this.busy()) return;
    this.dialog
      .open<ActivityItemRemoveDialogComponent, ActivityItemRemoveDialogData, boolean>(
        ActivityItemRemoveDialogComponent,
        {
          data: { number: row.number, typeName: this.typeName(row) },
          width: '440px',
          maxWidth: '95vw',
        }
      )
      .afterClosed()
      .pipe(
        filter((ok) => ok === true),
        switchMap(() => {
          this.removingId.set(row.questionId);
          return this.questionService.removeQuestionFromTest(this.worksheetId(), row.questionId);
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          this.removingId.set(null);
          this.rows.update((rows) => renumber(rows.filter((r) => r.questionId !== row.questionId)));
          this.snackBar.open(this.tr('removed', { number: row.number }), this.tr('dismiss'), { duration: 3000 });
          this.removed.emit(row.questionId);
        },
        error: (error) => {
          this.removingId.set(null);
          this.snackBar.open(error?.error?.message ?? this.tr('removeFailed'), this.tr('dismiss'), { duration: 4000 });
        },
      });
  }

  drop(event: CdkDragDrop<ActivityItemRow[]>): void {
    this.move(event.previousIndex, event.currentIndex);
  }

  /** ↑/↓: taşımadan sonra odak aynı maddenin aynı düğmesinde (uçta pasifse diğerinde) kalır. */
  moveBy(index: number, delta: -1 | 1): void {
    const row = this.rows()[index];
    if (!this.move(index, index + delta) || !row) return;
    afterNextRender(() => this.focusMoveButton(row.questionId, delta), { injector: this.injector });
  }

  private move(from: number, to: number): boolean {
    const previous = this.rows();
    if (!this.canEdit() || this.busy() || from === to || to < 0 || to >= previous.length) return false;

    const next = [...previous];
    moveItemInArray(next, from, to);
    this.rows.set(renumber(next));
    this.announcement.set(this.tr('moved', { from: previous[from].number, to: to + 1 }));
    this.persist(next, previous);
    return true;
  }

  private persist(next: ActivityItemRow[], previous: ActivityItemRow[]): void {
    this.saving.set(true);
    this.questionService
      .reorderTestQuestions(
        this.worksheetId(),
        next.map((r) => r.questionId)
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.saving.set(false);
          if (!this.reorderNoteShown) {
            this.reorderNoteShown = true;
            this.snackBar.open(this.tr('reorderNote'), this.tr('dismiss'), { duration: 8000 });
          }
        },
        error: () => {
          this.saving.set(false);
          this.rows.set(previous);
          this.snackBar
            .open(this.tr('reorderFailed'), this.tr('retry'), { duration: 6000 })
            .onAction()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe(() => {
              if (this.busy() || this.rows() !== previous) return;
              this.rows.set(renumber(next));
              this.persist(next, previous);
            });
        },
      });
  }

  private focusMoveButton(questionId: number, delta: -1 | 1): void {
    const row = this.host.nativeElement.querySelector(`[data-question-id="${questionId}"]`);
    const same = row?.querySelector<HTMLButtonElement>(
      `[data-testid="activity-item-move-${delta < 0 ? 'up' : 'down'}"]`
    );
    const other = row?.querySelector<HTMLButtonElement>(
      `[data-testid="activity-item-move-${delta < 0 ? 'down' : 'up'}"]`
    );
    const atEdge = same?.getAttribute('data-edge') === 'true';
    (atEdge ? other : same)?.focus();
  }

  typeName(row: ActivityItemRow): string {
    return this.transloco.translate<string>(`worksheet-detail.activity.report.types.${row.typeKey}`);
  }

  private tr(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`worksheet-detail.activity.items.${key}`, params);
  }
}

function renumber(rows: ActivityItemRow[]): ActivityItemRow[] {
  return rows.map((r, i) => (r.number === i + 1 ? r : { ...r, number: i + 1 }));
}

/** Liste ucunun (`bytest`) sorularından satırlar; sıra sunucunun (Order) sırası, numara konumdan. */
export function toRows(questions: Question[]): ActivityItemRow[] {
  return questions.map((q, index) => {
    const type = q.interactionType?.trim() ?? '';
    const form = parseDefinition(q.interactionType, q.interactionPlan, q.interactionSolution);
    const instruction = q.passage?.instruction;
    const preview = (form ? authoringItemTitle(form, instruction) : instruction?.trim()) || q.text?.trim() || '';
    const imageUrl = q.imageUrl || null;
    return {
      questionId: q.id,
      number: index + 1,
      typeKey: KNOWN_TYPES.has(type) ? type : 'mcq',
      preview,
      imageUrl,
      formEditable: form !== null,
      visual: form === null && imageUrl !== null,
    };
  });
}
