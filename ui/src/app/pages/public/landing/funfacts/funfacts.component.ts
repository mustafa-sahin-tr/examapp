import { Component } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

@Component({
  selector: 'app-funfacts',
  imports: [TranslocoDirective],
  templateUrl: './funfacts.component.html',
  styleUrl: './funfacts.component.scss',
  standalone: true,
})
export class FunfactsComponent {

}
