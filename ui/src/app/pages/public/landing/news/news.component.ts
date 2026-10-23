import { Component } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

@Component({
  selector: 'app-news',
  imports: [TranslocoDirective, MatIconModule],
  templateUrl: './news.component.html',
  styleUrls: ['./news.component.scss'],
  standalone: true,
})
export class NewsComponent {}
