import { Component, ChangeDetectionStrategy } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

@Component({
  selector: 'app-funfacts',
  imports: [TranslocoDirective],
  templateUrl: './funfacts.component.html',
  styleUrl: './funfacts.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
  standalone: true,
})
export class FunfactsComponent {

}
