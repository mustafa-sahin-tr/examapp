import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective } from '@jsverse/transloco';
import { PEN_COLORS, penColor, penColorCss, selectPenColor } from '../../utils/pen-color';

/** Issue #1241: kalem rengi seçici (düğme + menü); karalama alanı, soru üstü çizim ve çözüm kaydı (#1226) aynı seçimi paylaşır. */
@Component({
  selector: 'app-pen-color-picker',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t; prefix: 'test-solve.penColor'">
      <button mat-icon-button type="button" class="pen-color" data-testid="pen-color-button" [matMenuTriggerFor]="menu"
        [matTooltip]="t('label')" [attr.aria-label]="t('current', { color: t(color()) })">
        <span class="pen-color__dot" [style.background-color]="css(color())"></span>
      </button>
      <mat-menu #menu="matMenu">
        @for (option of colors; track option) {
          <button mat-menu-item type="button" role="menuitemradio" [attr.aria-checked]="option === color()"
            [attr.data-testid]="'pen-color-' + option" (click)="select(option)">
            <mat-icon [style.visibility]="option === color() ? 'visible' : 'hidden'">check</mat-icon>
            <span class="pen-color__dot pen-color__dot--menu" [style.background-color]="css(option)"></span>
            <span>{{ t(option) }}</span>
          </button>
        }
      </mat-menu>
    </ng-container>
  `,
  styles: `
    .pen-color__dot {
      display: inline-block;
      width: 18px;
      height: 18px;
      border-radius: 50%;
      // Varsayılan renk: yüzeyin mürekkep rengi.
      background-color: var(--pen-color-default, currentColor);
      box-shadow: 0 0 0 2px color-mix(in srgb, currentColor 35%, transparent);
      vertical-align: middle;
    }

    .pen-color__dot--menu {
      margin-inline-end: 12px;
    }
  `,
})
export class PenColorPickerComponent {
  protected readonly colors = PEN_COLORS;
  protected readonly color = penColor;
  protected readonly css = penColorCss;
  protected readonly select = selectPenColor;
}
