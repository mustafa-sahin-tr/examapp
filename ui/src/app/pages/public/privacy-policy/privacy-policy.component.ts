import { Component } from '@angular/core';
import { Title, Meta } from '@angular/platform-browser';

@Component({
  selector: 'app-privacy-policy',
  standalone: true,
  templateUrl: './privacy-policy.component.html',
  styleUrl: './privacy-policy.component.scss',
})
export class PrivacyPolicyComponent {
  constructor(
    private title: Title,
    private meta: Meta
  ) {
    this.title.setTitle('Gizlilik Politikası | ExamApp');
    this.meta.updateTag({ name: 'description', content: 'ExamApp gizlilik politikası ve veri koruma taahhüdü.' });
  }
}
