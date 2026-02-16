import { Component } from '@angular/core';
import { Title, Meta } from '@angular/platform-browser';

@Component({
  selector: 'app-pricing',
  standalone: true,
  templateUrl: './pricing.component.html',
  styleUrl: './pricing.component.scss',
})
export class PricingComponent {
  constructor(
    private title: Title,
    private meta: Meta
  ) {
    this.title.setTitle('Fiyatlandırma | ExamApp');
    this.meta.updateTag({ name: 'description', content: 'ExamApp fiyatlandırma seçenekleri ve paketler.' });
  }
}
