import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy } from '@angular/core';
import { FormGroup, ReactiveFormsModule, FormsModule } from '@angular/forms';

import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { TranslocoDirective } from '@jsverse/transloco';
import { WorksheetKind } from '../../models/activity';

@Component({
  selector: 'app-test-form',
  standalone: true,
  templateUrl: './test-form.component.html',
  styleUrls: ['./test-form.component.scss'],
  // Scope provider'ı `test-create-enhanced` verir; bu komponent yalnızca onun altında kullanılıyor.
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [
    ReactiveFormsModule,
    FormsModule,
    MatCheckboxModule,
    MatButtonModule,
    MatIconModule,
    MatButtonToggleModule,
    TranslocoDirective
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
  /** Edit modda alt aksiyonlar (Kaydet/İptal) sidebar'a taşındığı için gizlenir. */
  @Input() isEditMode = false;
  /** Kompakt varyant: bölüm kartları/notları gizlenir. */
  @Input() compact = false;
  /**
   * Issue #573: tür değiştirilemez (sayfanın öğrenci oturumu var — backend 409 `WorksheetKindLocked`). Kontrolü parent
   * disable eder; bu girdi yalnız açıklama metnini gösterir.
   */
  @Input() kindLocked = false;

  readonly kinds = WorksheetKind;

  @Output() onBookChange = new EventEmitter<any>();
  @Output() openNewBookAdd = new EventEmitter<void>();
  @Output() onNewBookBlur = new EventEmitter<void>();
  @Output() openNewBookTestAdd = new EventEmitter<void>();
  @Output() onSubjectChange = new EventEmitter<any>();
  @Output() onTopicChange = new EventEmitter<any>();
  @Output() onSubmit = new EventEmitter<void>();
  @Output() onCancel = new EventEmitter<void>();
  @Output() onGradeChange = new EventEmitter<any>();

  value(name: string): any {
    return this.form?.get(name)?.value;
  }

  /** Issue #573: formda tür kontrolü var mı (eski/kompakt formlar taşımaz). */
  get hasKindControl(): boolean {
    return !!this.form?.get('kind');
  }

  /** Issue #573: etkinlik sayfası — süre ve çalışma testi alanları gizlenir, açıklama "Yönerge" olur. */
  get isActivity(): boolean {
    return this.value('kind') === WorksheetKind.Activity;
  }

  /** Ders/Konu/Alt Konu alanları yalnızca sınıf seçiliyken kullanılabilir. */
  get gradeSelected(): boolean {
    return !!this.value('gradeId');
  }

  subjectChange(value: any) {
    this.onSubjectChange.emit(value);
  }
  topicChange(value: any) {
    this.onTopicChange.emit(value);
  }
  gradeChange(value: any) {
    this.onGradeChange.emit(value);
  }
  bookChange(value: any) {
    this.onBookChange.emit({ value });
  }
  togglePractice() {
    const c = this.form.get('isPracticeTest');
    c?.setValue(!c.value);
  }
  /** Issue #105: öğrenci yorum/soru anahtarı; kontrol yoksa (eski form) no-op. */
  toggleComments() {
    const c = this.form.get('commentsEnabled');
    if (!c || c.disabled) {
      return;
    }
    c.setValue(!c.value);
  }
}
