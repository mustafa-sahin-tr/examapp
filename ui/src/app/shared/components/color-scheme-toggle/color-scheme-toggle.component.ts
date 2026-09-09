import { Component, computed, inject } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ColorSchemeService } from '../../../services/color-scheme.service';

/**
 * Global toolbar'daki dark/light renk şeması anahtarı (issue #81).
 * Worksheet kart temasını değiştiren `ThemeSwitcherComponent` ile karıştırılmamalı.
 */
@Component({
  selector: 'app-color-scheme-toggle',
  standalone: true,
  imports: [MatIconModule, MatSlideToggleModule, MatTooltipModule],
  templateUrl: './color-scheme-toggle.component.html',
  styleUrl: './color-scheme-toggle.component.scss',
})
export class ColorSchemeToggleComponent {
  private readonly colorSchemeService = inject(ColorSchemeService);

  readonly colorScheme = this.colorSchemeService.colorScheme;
  readonly isLight = computed(() => this.colorScheme() === 'light');
  readonly tooltip = computed(() =>
    this.isLight() ? 'Koyu temaya geç' : 'Açık temaya geç'
  );

  toggle(): void {
    this.colorSchemeService.toggle();
  }
}
