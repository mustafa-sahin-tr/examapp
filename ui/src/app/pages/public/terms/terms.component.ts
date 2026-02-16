import { Component } from '@angular/core';
import { Title, Meta } from '@angular/platform-browser';

@Component({
  selector: 'app-terms',
  standalone: true,
  templateUrl: './terms.component.html',
  styleUrls: ['./terms.component.scss'],
})
export class TermsComponent {
  constructor(
    private title: Title,
    private meta: Meta
  ) {
    this.title.setTitle('Kullanım Şartları | ExamApp');
    this.meta.updateTag({
      name: 'description',
      content: 'ExamApp kullanım şartları: Hizmetlerimizin kullanımına ilişkin kurallar ve koşullar.',
    });
    this.meta.updateTag({ name: 'robots', content: 'index, follow' });
  }
}
