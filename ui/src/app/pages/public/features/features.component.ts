import { Component } from '@angular/core';
import { Title, Meta } from '@angular/platform-browser';

@Component({
  selector: 'app-features',
  standalone: true,
  templateUrl: './features.component.html',
  styleUrl: './features.component.scss',
})
export class FeaturesComponent {
  constructor(
    private title: Title,
    private meta: Meta
  ) {
    this.title.setTitle('Özellikler | ExamApp');
    this.meta.updateTag({ name: 'description', content: 'ExamApp platformunun öne çıkan özellikleri.' });
  }
}
