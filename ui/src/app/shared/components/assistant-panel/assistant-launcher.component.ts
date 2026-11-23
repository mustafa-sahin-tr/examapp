import { Overlay, OverlayRef } from '@angular/cdk/overlay';
import { ComponentPortal } from '@angular/cdk/portal';
import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ComponentRef,
  DestroyRef,
  Injector,
  effect,
  inject,
  input,
  untracked,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { ASSISTANT_SCOPE } from '../../../models/assistant';
import { AssistantChatStore } from '../../../services/assistant-chat.store';
import { AssistantPanelService } from '../../../services/assistant-panel.service';
import { AssistantPanelComponent } from './assistant-panel.component';

export const ASSISTANT_PANEL_WIDTH_PX = 440;

/**
 * Issue #507: asistanın giriş noktası (sağ altta FAB, Ctrl+K / ⌘K) ve paneli taşıyan CDK Overlay. Panel
 * `mat-sidenav-content`'in stacking context'inde kalmasın diye gövdeye taşınır. Görünürlük `AssistantPanelService`'ten.
 */
@Component({
  selector: 'app-assistant-launcher',
  imports: [MatButtonModule, MatIconModule, MatTooltipModule, TranslocoDirective],
  providers: [provideTranslocoScope(ASSISTANT_SCOPE)],
  template: `
    @if (panel.visible() && !panel.isOpen()) {
      <button
        *transloco="let t; prefix: 'assistant'"
        mat-fab
        type="button"
        class="assistant-fab"
        [class.assistant-fab--mobile]="mobile()"
        data-testid="assistant-fab"
        [attr.aria-label]="t('open')"
        [matTooltip]="t('openShortcut')"
        matTooltipPosition="left"
        (click)="panel.open()"
      >
        <mat-icon>auto_awesome</mat-icon>
      </button>
    }
  `,
  styleUrl: './assistant-launcher.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown)': 'onKeydown($event)' },
})
export class AssistantLauncherComponent {
  readonly panel = inject(AssistantPanelService);
  private readonly store = inject(AssistantChatStore);
  private readonly overlay = inject(Overlay);
  private readonly injector = inject(Injector);
  private readonly document = inject(DOCUMENT);

  /** Mobil: tam ekran panel, FAB alt navigasyonun üstünde. */
  readonly mobile = input(false);

  private overlayRef: OverlayRef | null = null;
  private panelRef: ComponentRef<AssistantPanelComponent> | null = null;
  private returnFocus: HTMLElement | null = null;

  constructor() {
    effect(() => {
      const open = this.panel.isOpen() && this.panel.visible();
      const mobile = this.mobile();
      untracked(() => (open ? this.attach(mobile) : this.detach()));
    });
    // Test çözmeye girince / gizlenince açık panel kapanır.
    effect(() => {
      if (!this.panel.visible() && untracked(() => this.panel.isOpen())) this.panel.close();
    });
    // Kabuk yok olunca (çıkış): akan tur kesilir. Panelin kapanması turu kesmez; cevap arka planda tamamlanır.
    inject(DestroyRef).onDestroy(() => {
      this.store.cancel();
      this.detach();
      this.overlayRef?.dispose();
    });
  }

  onKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && event.key.toLowerCase() === 'k') {
      if (!this.panel.visible()) return;
      event.preventDefault();
      this.panel.toggle();
    }
  }

  private attach(mobile: boolean): void {
    if (this.panelRef) {
      this.panelRef.setInput('mobile', mobile);
      this.updateSize(mobile);
      return;
    }
    const active = this.document.activeElement;
    this.returnFocus = active instanceof HTMLElement ? active : null;
    if (!this.overlayRef) {
      this.overlayRef = this.overlay.create({
        positionStrategy: this.overlay.position().global().top('0').right('0'),
        scrollStrategy: this.overlay.scrollStrategies.noop(),
        hasBackdrop: false,
        panelClass: 'assistant-overlay-pane',
      });
      this.overlayRef.keydownEvents().subscribe((e) => {
        if (e.key === 'Escape') {
          e.preventDefault();
          this.panel.close();
        }
      });
    }
    this.updateSize(mobile);
    this.panelRef = this.overlayRef.attach(new ComponentPortal(AssistantPanelComponent, null, this.injector));
    this.panelRef.setInput('mobile', mobile);
    this.panelRef.instance.closed.subscribe(() => this.panel.close());
  }

  private detach(): void {
    if (!this.panelRef) return;
    this.overlayRef?.detach();
    this.panelRef = null;
    const target = this.returnFocus;
    this.returnFocus = null;
    if (target?.isConnected) target.focus();
  }

  private updateSize(mobile: boolean): void {
    this.overlayRef?.updateSize(
      mobile ? { width: '100vw', height: '100dvh' } : { width: `${ASSISTANT_PANEL_WIDTH_PX}px`, height: '100dvh', maxWidth: '100vw' }
    );
  }
}
