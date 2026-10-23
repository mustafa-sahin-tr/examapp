import { Component, ChangeDetectionStrategy } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

@Component({
  selector: 'app-edu-blog',
  imports: [TranslocoDirective],
  templateUrl: './edu-blog.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './edu-blog.component.scss'
})
export class EduBlogComponent {

}
