import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** #724: sayfanın tek `h1`'i, açıklaması ve eylemleri. Eylemler (düğme, filtre) içerik olarak verilir. */
@Component({
  selector: 'app-page-header',
  standalone: true,
  templateUrl: './page-header.component.html',
  styleUrl: './page-header.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly description = input<string>('');
}
