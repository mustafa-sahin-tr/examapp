import { CommonModule } from '@angular/common';
import {
  Component,
  EventEmitter,
  Input,
  Output,
  signal,
  WritableSignal,
  computed,
  OnChanges,
  SimpleChanges,
  OnInit,
} from '@angular/core';

@Component({
  selector: 'app-question-navigator',
  imports: [CommonModule],
  standalone: true,
  templateUrl: './question-navigator.component.html',
  styleUrl: './question-navigator.component.scss',
})
export class QuestionNavigatorComponent implements OnInit {
  @Output() questionSelected = new EventEmitter<number>();
  @Input() set questions(value: { status: 'correct' | 'incorrect' | 'unknown' }[]) {
    this._questions = value ?? [];
    this.inputTick.set(this.inputTick() + 1);
    this.clampSelectionAndPage();
  }
  private _questions: { status: 'correct' | 'incorrect' | 'unknown' }[] = [];
  get questions() {
    return this._questions;
  }

  @Input() pageSize = 50;

  selectedQuestion: WritableSignal<number | null> = signal(0);

  private pageIndex = signal(0);

  // Computed() does not automatically re-run when plain @Input properties change.
  // We bump this tick in ngOnChanges to make computed signals reactive to input updates.
  private inputTick = signal(0);

  readonly currentPage = computed(() => this.pageIndex() + 1);

  readonly pageCount = computed(() => {
    this.inputTick();
    const size = Math.max(1, this.pageSize || 1);
    const count = Math.ceil((this.questions?.length ?? 0) / size);
    return Math.max(1, count);
  });

  readonly pageStart = computed(() => {
    const size = Math.max(1, this.pageSize || 1);
    return this.pageIndex() * size;
  });

  readonly visibleQuestions = computed(() => {
    this.inputTick();
    const list = this.questions ?? [];
    const size = Math.max(1, this.pageSize || 1);
    const start = this.pageStart();
    return list.slice(start, start + size);
  });

  readonly canPrev = computed(() => this.pageIndex() > 0);
  readonly canNext = computed(() => this.pageIndex() < this.pageCount() - 1);

  // ngOnChanges(changes: SimpleChanges): void {
  //   if (changes['questions'] || changes['pageSize']) {
  //     this.inputTick.set(this.inputTick() + 1);
  //     this.clampSelectionAndPage();
  //   }
  // }

  ngOnInit(): void {
    this.clampSelectionAndPage();
  }

  private clampSelectionAndPage() {
    const total = this.questions?.length ?? 0;
    const size = Math.max(1, this.pageSize || 1);

    // Clamp selected index.
    const currentSelected = this.selectedQuestion();
    if (currentSelected == null) {
      if (total > 0) {
        this.selectedQuestion.set(0);
      }
    } else if (total === 0) {
      this.selectedQuestion.set(null);
    } else if (currentSelected >= total) {
      this.selectedQuestion.set(total - 1);
    } else if (currentSelected < 0) {
      this.selectedQuestion.set(0);
    }

    // Clamp pageIndex to valid bounds.
    // NOTE: Do NOT auto-sync pageIndex to selectedQuestion here.
    // In some parent templates, `questions` input may be recreated frequently (new array reference),
    // which triggers ngOnChanges and would otherwise reset paging back to page 0.
    const maxPage = Math.max(0, this.pageCount() - 1);
    const currentPage = this.pageIndex();
    this.pageIndex.set(Math.min(Math.max(currentPage, 0), maxPage));

    // If selection exists but is outside the current page (e.g. after data shrink), move to the correct page.
    const selected = this.selectedQuestion();
    if (selected != null && selected >= 0 && selected < total) {
      const selectedPage = Math.floor(selected / size);
      if (selectedPage !== this.pageIndex()) {
        // Only force sync when necessary (selection not visible).
        const start = this.pageIndex() * size;
        const end = start + size;
        const isSelectionVisible = selected >= start && selected < end;
        // if (!isSelectionVisible) {
        //   this.pageIndex.set(Math.min(Math.max(selectedPage, 0), maxPage));
        // }
      }
    }
  }

  selectQuestion(index: number) {
    const size = Math.max(1, this.pageSize || 1);
    this.selectedQuestion.set(index);
    this.pageIndex.set(Math.floor(index / size));
    this.questionSelected.emit(index);
  }

  prevPage() {
    if (!this.canPrev()) {
      return;
    }
    this.pageIndex.set(this.pageIndex() - 1);
  }

  nextPage() {
    if (!this.canNext()) {
      return;
    }
    const newPageIndex = this.pageIndex() + 1;
    this.pageIndex.set(newPageIndex);
    this.clampSelectionAndPage();
  }

  // For template convenience
  globalIndex(localIndex: number): number {
    return this.pageStart() + localIndex;
  }
}
