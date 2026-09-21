import { Component } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

@Component({
  selector: 'app-discover',
  imports: [TranslocoDirective],
  templateUrl: './discover.component.html',
  styleUrls: ['./discover.component.scss'],
  standalone: true,
})
export class DiscoverComponent {}
