import { Component } from '@angular/core';
import { Title, Meta } from '@angular/platform-browser';

@Component({
  selector: 'app-faq',
  standalone: true,
  templateUrl: './faq.component.html',
  styleUrl: './faq.component.scss',
})
export class FaqComponent {
  constructor(
    private title: Title,
    private meta: Meta
  ) {
    this.title.setTitle('Sıkça Sorulan Sorular | ExamApp');
    this.meta.updateTag({ name: 'description', content: 'ExamApp hakkında sıkça sorulan sorular ve yanıtları.' });
  }
}
