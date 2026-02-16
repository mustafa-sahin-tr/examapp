import { Component } from '@angular/core';
import { Title, Meta } from '@angular/platform-browser';

@Component({
  selector: 'app-about',
  standalone: true,
  templateUrl: './about.component.html',
  styleUrl: './about.component.scss',
})
export class AboutComponent {
  constructor(
    private title: Title,
    private meta: Meta
  ) {
    this.title.setTitle('Hakkında | ExamApp');
    this.meta.updateTag({ name: 'description', content: 'ExamApp hakkında detaylı bilgi ve vizyon.' });
  }
}
