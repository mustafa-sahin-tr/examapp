import { Component, EventEmitter, Input, OnChanges, OnInit, Output, SimpleChanges } from '@angular/core';
import { CommonModule, NgFor } from '@angular/common';

@Component({
  selector: 'app-pagination',
  templateUrl: './pagination.component.html',
  styleUrls: ['./pagination.component.scss'],
  standalone: true,
  imports: [NgFor,CommonModule]
})
export class PaginationComponent implements OnInit, OnChanges {
  @Input() currentPage = 1;         // Mevcut sayfa
  @Input() totalItems = 0;          // Toplam öğe sayısı
  @Input() itemsPerPage = 10;       // Sayfa başına öğe
  @Output() pageChanged = new EventEmitter<number>();

  totalPages = 0;
  visiblePages: number[] = [];

  ngOnInit(): void {
    this.calculateTotalPages();
    this.updateVisiblePages();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['totalItems'] || changes['itemsPerPage'] || changes['currentPage']) {
      this.calculateTotalPages();
      this.updateVisiblePages();
    }
  }

  calculateTotalPages() {
    this.totalPages = Math.ceil(this.totalItems / this.itemsPerPage);
  }

  changePage(page: number) {
    if (page === this.currentPage || page < 1 || page > this.totalPages) return;

    this.currentPage = page;
    this.updateVisiblePages();
    this.pageChanged.emit(page);
  }

  updateVisiblePages() {
    const maxVisible = 9;  // 1 + ... + 7 + ... + last (toplam 9 eleman)
    const pages: number[] = [];

    if (this.totalPages <= maxVisible) {
      // Tüm sayfaları göster
      for (let i = 1; i <= this.totalPages; i++) {
        pages.push(i);
      }
    } else {
      const left = Math.max(2, this.currentPage - 3);
      const right = Math.min(this.totalPages - 1, this.currentPage + 3);

      pages.push(1);

      if (left > 2) {
        pages.push(-1); // ... sol taraf
      }

      for (let i = left; i <= right; i++) {
        pages.push(i);
      }

      if (right < this.totalPages - 1) {
        pages.push(-2); // ... sağ taraf
      }

      pages.push(this.totalPages);
    }

    this.visiblePages = pages;
  }

  skipLeft() {
    this.changePage(Math.max(1, this.currentPage - 7));
  }

  skipRight() {
    this.changePage(Math.min(this.totalPages, this.currentPage + 7));
  }
}
