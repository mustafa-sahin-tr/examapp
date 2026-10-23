import { Component } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

@Component({
  selector: 'app-service-area',
  imports: [TranslocoDirective, MatIconModule],
  templateUrl: './service-area.component.html',
  styleUrls: ['./service-area.component.scss'],
  standalone: true,
})
export class ServiceAreaComponent {}
