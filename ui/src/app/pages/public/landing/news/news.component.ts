import { Component } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

@Component({
  selector: 'app-news',
  imports: [TranslocoDirective],
  templateUrl: './news.component.html',
  styleUrls: ['./news.component.scss'],
  standalone: true,
})
export class NewsComponent {}
