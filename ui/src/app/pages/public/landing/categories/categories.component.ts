import { Component } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

@Component({
  selector: 'app-categories',
  imports: [TranslocoDirective],
  templateUrl: './categories.component.html',
  styleUrl: './categories.component.scss',
  standalone: true,
})
export class CategoriesComponent {

}
