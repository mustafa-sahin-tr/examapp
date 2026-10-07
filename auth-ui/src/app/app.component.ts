import { Component, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterModule } from '@angular/router';
import { BRAND_NAME } from './shared/brand';

@Component({
  selector: 'app-root',
  imports: [RouterModule],
  standalone: true,
  template: `<router-outlet></router-outlet>`, // Standalone modda Router çalıştırılıyor
})
export class AppComponent {
  constructor() {
    // Sekme başlığı marka sabitinden gelir (issue #409); index.html'deki başlık yalnız açılış öncesi yedektir.
    inject(Title).setTitle(BRAND_NAME);
  }
}
