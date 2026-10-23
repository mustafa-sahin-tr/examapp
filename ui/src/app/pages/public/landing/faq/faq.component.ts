import { Component, ChangeDetectionStrategy } from '@angular/core';
import { MatExpansionModule } from '@angular/material/expansion';
import { TranslocoDirective } from '@jsverse/transloco';

@Component({
  selector: 'app-faq',
  imports: [MatExpansionModule, TranslocoDirective],
  templateUrl: './faq.component.html',
  styleUrl: './faq.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
  standalone: true,
})
export class FaqComponent {}
