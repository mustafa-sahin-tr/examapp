import { Component, Input } from '@angular/core';
import { Passage } from '../../../models/question';

import { SafeHtmlPipe } from '../../../services/safehtml';

@Component({
  selector: 'app-passage-card',
  imports: [SafeHtmlPipe],
  templateUrl: './passage-card.component.html',
  styleUrl: './passage-card.component.scss',
  standalone: true
})
export class PassageCardComponent {
  @Input() passage: Passage | undefined;
}
