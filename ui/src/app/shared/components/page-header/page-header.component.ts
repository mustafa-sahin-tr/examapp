import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/** #724: sayfanın tek `h1`'i, açıklaması ve eylemleri. Eylemler (düğme, filtre) içerik olarak verilir. */
@Component({
  selector: 'app-page-header',
  standalone: true,
  imports: [MatIconModule],
  templateUrl: './page-header.component.html',
  styleUrl: './page-header.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly description = input<string>('');
  /** İsteğe bağlı Material ikon adı; başlığın solunda süs olarak durur. */
  readonly icon = input<string>('');
}
