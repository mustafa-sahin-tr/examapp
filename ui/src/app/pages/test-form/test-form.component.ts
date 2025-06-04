import { Component, Input, Output, EventEmitter } from '@angular/core';
import { FormGroup, ReactiveFormsModule, FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatOptionModule } from '@angular/material/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-test-form',
  standalone: true,
  templateUrl: './test-form.component.html',
  styleUrls: ['./test-form.component.scss'],
  imports: [
    CommonModule,
    ReactiveFormsModule,
    FormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatOptionModule,
    MatCheckboxModule,
    MatButtonModule,
  ],
})
export class TestFormComponent {
  @Input() form!: FormGroup;
  @Input() books: any[] = [];
  @Input() bookTests: any[] = [];
  @Input() grades: any[] = [];
  @Input() subjects: any[] = [];
  @Input() topics: any[] = [];
  @Input() subtopics: any[] = [];
  @Input() showAddBookInput = false;
  @Input() showAddBookTestInput = false;

  @Output() onBookChange = new EventEmitter<any>();
  @Output() openNewBookAdd = new EventEmitter<void>();
  @Output() onNewBookBlur = new EventEmitter<void>();
  @Output() openNewBookTestAdd = new EventEmitter<void>();
  @Output() onSubjectChange = new EventEmitter<any>();
  @Output() onTopicChange = new EventEmitter<any>();
  @Output() onSubmit = new EventEmitter<void>();
  @Output() onCancel = new EventEmitter<void>();

  // Template'te çağrılan fonksiyonlar
  subjectChange(value: any) {
    this.onSubjectChange.emit(value);
  }
  topicChange(value: any) {
    this.onTopicChange.emit(value);
  }
}
