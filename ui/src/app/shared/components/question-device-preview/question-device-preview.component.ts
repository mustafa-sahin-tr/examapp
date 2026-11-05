import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  Output,
  ViewChild,
  computed,
  signal,
} from '@angular/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { AnswerChoice, QuestionRegion } from '../../../models/draws';
import { QuestionCanvasViewComponentv5 } from '../question-canvas-view-v5/question-canvas-view-v5.component';

export type PreviewDevice = 'phone' | 'tablet' | 'desktop';

const DEVICE_STORAGE_KEY = 'questionPreview.device';

/**
 * Öğrencinin test çözme ekranında soru alanının ölçülen boyutu (issue #780). Düzen bu alana göre seçildiği
 * için önizleme aynı boyutu kullanır; ekran başlığı, alt çubuk veya panel değişirse yeniden ölçülmelidir.
 */
export const PREVIEW_DEVICES: Record<PreviewDevice, { icon: string; viewport: number; width: number; height: number }> = {
  phone: { icon: 'smartphone', viewport: 390, width: 364, height: 604 },
  tablet: { icon: 'tablet_mac', viewport: 768, width: 734, height: 812 },
  desktop: { icon: 'desktop_windows', viewport: 1440, width: 1112, height: 732 },
};

function readStoredDevice(): PreviewDevice {
  try {
    const stored = localStorage.getItem(DEVICE_STORAGE_KEY);
    return stored && stored in PREVIEW_DEVICES ? (stored as PreviewDevice) : 'desktop';
  } catch {
    return 'desktop';
  }
}

/** Soruyu öğrencinin göreceği düzenle gösterir; alan sütuna sığmazsa düzeni bozmadan görsel olarak küçültülür. */
@Component({
  selector: 'app-question-device-preview',
  standalone: true,
  imports: [MatButtonToggleModule, MatIconModule, TranslocoDirective, QuestionCanvasViewComponentv5],
  templateUrl: './question-device-preview.component.html',
  styleUrls: ['./question-device-preview.component.scss'],
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class QuestionDevicePreviewComponent implements AfterViewInit, OnDestroy {
  @Input({ required: true }) questionRegion!: QuestionRegion;
  @Output() choiceSelected = new EventEmitter<AnswerChoice>();
  @ViewChild('frameArea') private frameAreaRef?: ElementRef<HTMLElement>;

  readonly deviceIds = Object.keys(PREVIEW_DEVICES) as PreviewDevice[];
  readonly devices = PREVIEW_DEVICES;
  readonly device = signal<PreviewDevice>(readStoredDevice());
  private readonly availableWidth = signal(0);
  private resizeObserver?: ResizeObserver;

  readonly stage = computed(() => PREVIEW_DEVICES[this.device()]);
  readonly scale = computed(() => {
    const available = this.availableWidth();
    return available > 0 ? Math.min(1, available / this.stage().width) : 1;
  });

  ngAfterViewInit(): void {
    const area = this.frameAreaRef?.nativeElement;
    if (!area) {
      return;
    }
    this.availableWidth.set(area.clientWidth);
    this.resizeObserver = new ResizeObserver(() => this.availableWidth.set(area.clientWidth));
    this.resizeObserver.observe(area);
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
  }

  selectDevice(device: PreviewDevice): void {
    this.device.set(device);
    try {
      localStorage.setItem(DEVICE_STORAGE_KEY, device);
    } catch {
      // Depolama kapalıysa seçim yalnız bu oturumda geçerlidir.
    }
  }
}
