import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { InteractionBox } from '../../../models/interaction-ids';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';

/**
 * Issue #576: soru görselinin bir bölgesini (kart / eşleştirme öğesi kırpması) gösterir. Ayrı görsel üretilmez: aynı soru
 * görseli taşan bir kapta kaydırılıp ölçeklenir, böylece imzalı URL ve yeniden deneme (`appStorageImageRetry`, #365 S3)
 * soru görseliyle aynı kalır. Kutu soru bölgesi pikselindedir (`sourceWidth`/`sourceHeight` = region.width/height).
 */
@Component({
  selector: 'app-question-image-crop',
  standalone: true,
  imports: [StorageImageRetryDirective],
  templateUrl: './question-image-crop.component.html',
  styleUrl: './question-image-crop.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionImageCropComponent {
  readonly src = input.required<string>();
  readonly box = input.required<InteractionBox>();
  readonly sourceWidth = input.required<number>();
  readonly sourceHeight = input.required<number>();
  readonly alt = input('');
  /** Kırpmanın en büyük yüksekliği (px); genişlik orana göre küçülür. */
  readonly maxHeight = input(96);
  readonly imageRefresh = input<StorageImageResolver | null>(null);

  readonly geometry = computed(() => {
    const box = this.box();
    const w = this.sourceWidth();
    const h = this.sourceHeight();
    if (!box || !w || !h || box.width <= 0 || box.height <= 0) return null;
    const ratio = box.width / box.height;
    return {
      ratio: `${box.width} / ${box.height}`,
      width: `min(100%, ${Math.round(this.maxHeight() * ratio)}px)`,
      imgWidth: (w / box.width) * 100,
      left: -(box.x / box.width) * 100,
      top: -(box.y / box.height) * 100,
    };
  });
}
