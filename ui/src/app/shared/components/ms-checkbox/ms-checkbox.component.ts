import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';

@Component({
  selector: 'app-custom-checkbox',
  standalone: true,
  templateUrl: './ms-checkbox.component.html',
  styleUrls: ['./ms-checkbox.component.scss'],
  imports: [CommonModule]
})
export class CustomCheckboxComponent {
  @Input() label: string = ''; // Checkbox etiketi
  @Input() checked: boolean = false; // Varsayılan olarak işaretli olup olmadığını belirler
  @Input() value: any; // Değer ataması için

  @Output() checkedChange = new EventEmitter<{ checked: boolean; value: any }>();

  toggleCheckbox() {
    console.log('Toggle checkbox');
    this.checked = !this.checked;
    this.checkedChange.emit({ checked: this.checked, value: this.value });
  }
}
