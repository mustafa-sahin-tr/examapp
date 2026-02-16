import { Component } from '@angular/core';
import { Title, Meta } from '@angular/platform-browser';

@Component({
  selector: 'app-contact',
  standalone: true,
  templateUrl: './contact.component.html',
  styleUrl: './contact.component.scss',
})
export class ContactComponent {
  constructor(
    private title: Title,
    private meta: Meta
  ) {
    this.title.setTitle('İletişim | ExamApp');
    this.meta.updateTag({ name: 'description', content: 'ExamApp iletişim ve destek kanalları.' });
  }
}
