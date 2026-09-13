import { Component } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

@Component({
  selector: 'app-service-area',
  imports: [TranslocoDirective],
  templateUrl: './service-area.component.html',
  styleUrls: ['./service-area.component.scss'],
  standalone: true,
})
export class ServiceAreaComponent {}
