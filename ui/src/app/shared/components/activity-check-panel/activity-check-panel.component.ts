import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective } from '@jsverse/transloco';

import { OpenTextRowStatus } from '../../../models/open-text';

/** Kontrol/çözüm/bitirme isteğinin kullanıcıya gösterilen hata türü. */
export type ActivityPanelError = 'rateLimited' | 'network' | 'checkRequired' | 'generic';

/** Son kontrolün özeti (yalnız bilgi satırı — puan/yüzde değil). */
export interface ActivityCheckSummary {
  correct: number;
  total: number;
}

/**
 * Geçerli maddenin bir satırı: `correct` null = henüz kontrol yok; `solution` yalnız çözüm açıkken. Issue #583: açık uçlu
 * maddede `status` doludur — ✓/✗ yerine "Değerlendirme bekliyor" ya da öğretmenin değerlendirmesi gösterilir.
 */
export interface ActivityResultRow {
  label: string;
  correct: boolean | null;
  solution?: string | null;
  status?: OpenTextRowStatus | null;
}

/** Issue #583: açık uçlu satır durumunun ikonu. */
export const OPEN_TEXT_ROW_ICON: Record<OpenTextRowStatus, string> = {
  pending: 'hourglass_top',
  sufficient: 'check_circle',
  partial: 'adjust',
  insufficient: 'error_outline',
};

/**
 * Issue #573: "Cevapları kontrol et" paneli — kontrol / yeniden kontrol, satır bazlı sonuç (✓/✗ + metin; renk tek sinyal
 * değil), "Doğru cevapları göster" (ilk kontrolden önce kapalı), "Cevabımı geri getir", "Etkinliği bitir" ve hata durumları
 * (429 → bekle, ağ → cevapların kayıtlı, 409 → önce kontrol et). Sonuç özeti `aria-live` ile duyurulur.
 *
 * OnPush, durumsuz: tüm durum sahibinden (oynatıcı sayfası) gelir; panel yalnız olay yayar.
 */
@Component({
  selector: 'app-activity-check-panel',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  templateUrl: './activity-check-panel.component.html',
  styleUrl: './activity-check-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityCheckPanelComponent {
  readonly checkCount = input(0);
  readonly checking = input(false);
  readonly revealing = input(false);
  readonly finishing = input(false);
  readonly revealed = input(false);
  readonly summary = input<ActivityCheckSummary | null>(null);
  /** Kontrolden sonra en az bir cevap değişti (özet eskidi). */
  readonly stale = input(false);
  /** Boş yerleştirmesi olan maddelerin etiketleri (a, c …) — engellemeyen uyarı. */
  readonly blankItems = input<string[]>([]);
  /**
   * Issue #574: boş uyarısının metni — `placement` (yalnız sürükle-bırak maddeleri boş: "boş yerleştirme") ya da `generic`
   * (matris / çoktan seçmeli de var: "boş bırakılan yer").
   */
  readonly blankKind = input<'placement' | 'generic'>('placement');
  readonly rows = input<ActivityResultRow[]>([]);
  readonly error = input<ActivityPanelError | null>(null);
  /** `generic` hatada gösterilecek sunucu mesajı (yerelleştirilmiş). */
  readonly errorMessage = input<string | null>(null);
  /**
   * W4: ağ hatasında "Cevapların kayıtlı" denebilir mi. Kayıt hatası varsa ya da hata bitirmeden geldiyse false →
   * "henüz kaydedilmedi" metni.
   */
  readonly answersSaved = input(true);

  /**
   * Issue #576 (E2E): dar ekranda kontrol sonrası panel ekranın çoğunu kaplıyordu. `collapsible` açıkken (sahibi dar ekranda
   * verir) panel daraltılabilir; `collapsed` iken yalnız kompakt çubuk: özet + "Yeniden kontrol et" + genişlet oku. Hata
   * varsa ya da henüz kontrol yoksa daraltılmış hal uygulanmaz (uyarı / ilk kontrol düğmesi görünür kalmalı).
   */
  readonly collapsible = input(false);
  readonly collapsed = input(false);
  readonly expand = output<void>();
  readonly collapse = output<void>();

  readonly check = output<void>();
  readonly reveal = output<void>();
  readonly restore = output<void>();
  readonly fixWrong = output<void>();
  readonly finish = output<void>();
  /** W2: hata kutusundaki "Tekrar dene" — sahibi başarısız adımı (kontrol/çözüm/bitir) yeniden dener. */
  readonly retry = output<void>();

  readonly openTextIcon = OPEN_TEXT_ROW_ICON;

  readonly hasChecked = computed(() => this.checkCount() > 0);
  readonly wrongCount = computed(() => {
    const s = this.summary();
    return s ? Math.max(0, s.total - s.correct) : 0;
  });
  readonly busy = computed(() => this.checking() || this.revealing() || this.finishing());
  readonly compact = computed(
    () => this.collapsible() && this.collapsed() && this.hasChecked() && !this.error() && !this.revealed()
  );
}
