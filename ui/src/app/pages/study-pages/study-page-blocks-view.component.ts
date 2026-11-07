import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { STUDY_PAGE_PLATFORM_ICONS, StudyPageBlock, StudyPageBlockType, StudyPageLinkPlatform } from '../../models/study-page';
import { externalUrlHost, safeExternalUrl } from '../../shared/utils/study-item-display.util';

/** Görüntülenecek blok: güvenli bağlantı ve platform ikonu önceden hesaplanır. */
interface ViewBlock {
  key: string;
  block: StudyPageBlock;
  href: string | null;
  host: string | null;
  platformIcon: string;
}

/**
 * Issue #933: çalışma sayfası bloklarını sırayla, öğrencinin göreceği gibi gösterir (öğrenci sayfası ve editör önizlemesi).
 * Çeviriler `study-pages` scope'unda; çağıran bileşen scope'u sağlar. Soru bloğu `solve` olayı yayar, akışı çağıran açar.
 */
@Component({
  selector: 'app-study-page-blocks-view',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective],
  templateUrl: './study-page-blocks-view.component.html',
  styleUrl: './study-page-blocks-view.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StudyPageBlocksViewComponent {
  readonly blocks = input.required<readonly StudyPageBlock[]>();
  /** false: soru bloğunda "Çöz" düğmesi pasif (editör önizlemesi, öğretmen). */
  readonly solvable = input(true);
  /** Düğmesi meşgul olan soru (oturum açılırken). */
  readonly solvingQuestionId = input<number | null>(null);
  readonly solve = output<number>();

  protected readonly Type = StudyPageBlockType;
  /** Arka yüzü açık bilgi kartları (blok anahtarı). */
  protected readonly flipped = signal<ReadonlySet<string>>(new Set());

  protected readonly items = computed<ViewBlock[]>(() =>
    this.blocks()
      .filter((b) => b.type !== StudyPageBlockType.Question || b.questionId != null)
      .map((block, index) => ({
        key: `${block.id || 'new'}-${index}`,
        block,
        href: block.type === StudyPageBlockType.Link ? safeExternalUrl(block.url) : null,
        host: block.type === StudyPageBlockType.Link ? externalUrlHost(block.url) : null,
        platformIcon: STUDY_PAGE_PLATFORM_ICONS[block.platform ?? StudyPageLinkPlatform.Other] ?? 'link',
      })),
  );

  protected toggleFlip(key: string): void {
    this.flipped.update((set) => {
      const next = new Set(set);
      if (!next.delete(key)) next.add(key);
      return next;
    });
  }
}
