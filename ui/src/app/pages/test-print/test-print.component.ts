import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { forkJoin } from 'rxjs';
import { WorksheetKind } from '../../models/activity';
import { Question } from '../../models/question';
import { Test } from '../../models/test-instance';
import { QuestionService } from '../../services/question.service';
import { TestService } from '../../services/test.service';
import { StorageImageRetryDirective } from '../../shared/directives/storage-image-retry.directive';
import { createStorageImageResolver } from '../../shared/utils/storage-image-refresh.util';

export type PrintExclusionReason = 'unpublished' | 'licenseExpired' | 'publisherLicense' | 'interactive';

export interface PrintItem {
  question: Question;
  number: number;
  /** Aynı kapsamın ardışık sorularında kapsam yalnız ilkinde basılır. */
  showPassage: boolean;
  imageUrl: string | null;
  /** Şıklar soru görselinin içinde (işaretlenmiş kitap sorusu): tam görsel basılır, şıklar ayrıca çizilmez. */
  answersInImage: boolean;
  keyLetter: string | null;
  keyText: string | null;
}

export interface PrintExclusion {
  reason: PrintExclusionReason;
  positions: number[];
}

const MCQ_TYPE = 'mcq';
const EXCLUSION_ORDER: readonly PrintExclusionReason[] = ['unpublished', 'licenseExpired', 'publisherLicense', 'interactive'];

function answerLetter(index: number): string {
  return String.fromCharCode(65 + index);
}

function exclusionReason(question: Question): PrintExclusionReason | null {
  if (question.printBlock) {
    return question.printBlock;
  }
  const type = question.interactionType?.trim();
  return type && type !== MCQ_TYPE ? 'interactive' : null;
}

function toPrintItem(question: Question, number: number, previousPassageId: number | null): PrintItem {
  const answers = question.answers ?? [];
  const answersInImage = !!question.imageUrlV2 && answers.some((a) => !!a.imageUrl);
  const correctIndex = answers.findIndex((a) => a.id === question.correctAnswerId);
  let keyLetter: string | null = null;
  if (correctIndex >= 0) {
    // Görseldeki şıkların harfi işaretlemede etikete yazılır; metin şıklarının harfi sayfadaki sıradan gelir.
    const label = answersInImage ? answers[correctIndex].text?.trim() : '';
    keyLetter = label || answerLetter(correctIndex);
  }
  return {
    question,
    number,
    showPassage: !!question.passage && question.passage.id !== previousPassageId,
    imageUrl: question.imageUrl?.trim() || null,
    answersInImage,
    keyLetter,
    keyText: keyLetter ? null : question.practiceCorrectAnswer?.trim() || null,
  };
}

/** Issue #701: testi kağıda basılacak hâle getirir; basılamayan sorular çıkarılır, numaralar basılanlara göre verilir. */
export function buildPrintSheet(questions: Question[]): { items: PrintItem[]; exclusions: PrintExclusion[] } {
  const items: PrintItem[] = [];
  const excluded = new Map<PrintExclusionReason, number[]>();
  let previousPassageId: number | null = null;

  questions.forEach((question, index) => {
    const reason = exclusionReason(question);
    if (reason) {
      excluded.set(reason, [...(excluded.get(reason) ?? []), index + 1]);
      return;
    }
    items.push(toPrintItem(question, items.length + 1, previousPassageId));
    previousPassageId = question.passage?.id ?? null;
  });

  const exclusions = EXCLUSION_ORDER.filter((reason) => excluded.has(reason)).map((reason) => ({
    reason,
    positions: excluded.get(reason)!,
  }));
  return { items, exclusions };
}

function imageSettled(img: HTMLImageElement): Promise<void> {
  if (img.complete) {
    return Promise.resolve();
  }
  return new Promise((resolve) => {
    img.addEventListener('load', () => resolve(), { once: true });
    img.addEventListener('error', () => resolve(), { once: true });
  });
}

/**
 * Issue #701: testin kağıt çıktısı. PDF tarayıcının yazdırma penceresinden kaydedilir ("PDF olarak kaydet"): Türkçe yazı
 * tipleri ve görseller ekrandakiyle aynı kalır, sunucuya PDF kütüphanesi eklenmez. Rota uygulama kabuğunun dışındadır.
 */
@Component({
  selector: 'app-test-print',
  standalone: true,
  templateUrl: './test-print.component.html',
  styleUrls: ['./test-print.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, RouterLink, MatButtonModule, MatCheckboxModule, MatIconModule, StorageImageRetryDirective],
  providers: [provideTranslocoScope('test-print')],
})
export class TestPrintComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly testService = inject(TestService);
  private readonly questionService = inject(QuestionService);
  private readonly titleService = inject(Title);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly testId = Number(this.route.snapshot.paramMap.get('id'));
  protected readonly test = signal<Test | null>(null);
  private readonly questions = signal<Question[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly includeKey = signal(true);
  protected readonly preparing = signal(false);
  /** Yüklenemeyen görsel sayısı; öğretmen uyarıyı gördükten sonra ikinci tıklamada yine de yazdırabilir. */
  protected readonly brokenImages = signal(0);

  private readonly sheet = computed(() => buildPrintSheet(this.questions()));
  protected readonly items = computed(() => this.sheet().items);
  protected readonly exclusions = computed(() => this.sheet().exclusions);
  protected readonly isActivity = computed(() => this.test()?.kind === WorksheetKind.Activity);
  protected readonly canPrint = computed(() => !this.isActivity() && this.items().length > 0);

  protected readonly imageRefresh = createStorageImageResolver(() => this.questionService.getAll(this.testId));

  ngOnInit(): void {
    const previousTitle = this.titleService.getTitle();
    this.destroyRef.onDestroy(() => this.titleService.setTitle(previousTitle));
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    forkJoin({ test: this.testService.get(this.testId), questions: this.questionService.getAll(this.testId) })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ test, questions }) => {
          this.test.set(test);
          this.questions.set(questions);
          // Tarayıcı PDF dosyasını sayfa başlığıyla adlandırır.
          this.titleService.setTitle(test.name);
          this.loading.set(false);
        },
        error: () => {
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected async print(): Promise<void> {
    const images = Array.from(this.host.nativeElement.querySelectorAll('img'));
    this.preparing.set(true);
    await Promise.all(images.map(imageSettled));
    this.preparing.set(false);

    const broken = images.filter((img) => img.naturalWidth === 0).length;
    const alreadyWarned = this.brokenImages() === broken;
    this.brokenImages.set(broken);
    if (broken > 0 && !alreadyWarned) {
      return;
    }
    window.print();
  }

  protected answerLetter(index: number): string {
    return answerLetter(index);
  }

  protected answerColumns(question: Question): string {
    return `repeat(${Math.max(1, question.answerColCount || 1)}, 1fr)`;
  }
}
