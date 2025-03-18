import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-pagination',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './pagination.component.html',
  styleUrls: ['./pagination.component.scss']
})
export class PaginationComponent {
  @Input() currentPage = 1; // Üst bileşenden gelen mevcut sayfa
  @Input() totalItems = 0; // Üst bileşenden gelen toplam öğe sayısı
  @Input() itemsPerPage = 10; // Sayfa başına gösterilecek öğe sayısı

  @Output() pageChange = new EventEmitter<number>(); // Sayfa değişikliklerini üst bileşene bildirir

  get totalPages(): number {
    return Math.ceil(this.totalItems / this.itemsPerPage); // Sayfa sayısını hesapla
  }

  changePage(page: number) {
    if (page >= 1 && page <= this.totalPages) {
      this.pageChange.emit(page);
    }
  }

  nextPage() {
    if (this.currentPage < this.totalPages) {
      this.changePage(this.currentPage + 1);
    }
  }

  previousPage() {
    if (this.currentPage > 1) {
      this.changePage(this.currentPage - 1);
    }
  }
}
