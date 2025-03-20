import { Component } from '@angular/core';
import { Router, RouterModule } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterModule],
  standalone: true,
  template: `<router-outlet></router-outlet>`, // Standalone modda Router çalıştırılıyor
})
export class AppComponent {}
