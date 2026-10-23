import { Component, ChangeDetectionStrategy } from '@angular/core';
import { Router, RouterModule } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterModule],
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `<router-outlet></router-outlet>`, // Standalone modda Router çalıştırılıyor
})
export class AppComponent {}
