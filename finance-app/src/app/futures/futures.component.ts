import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Observable } from 'rxjs';
import { AssetType, Portfolio } from '../models/asset.model';
import { PortfolioService } from '../services/portfolio.service';

@Component({
  selector: 'app-fixed-deposits',
  imports: [CommonModule],
  templateUrl: './futures.component.html',
  styleUrl: './futures.component.scss',
})
export class FuturesComponent implements OnInit {
  AssetType = AssetType;
  fixedDeposits$!: Observable<Portfolio[]>;
  selectedDeposit: Portfolio | null = null;

  constructor(private portfolioService: PortfolioService) {}

  ngOnInit(): void {
    this.fixedDeposits$ = this.portfolioService.getPortfolioByType(AssetType.FixedDeposit);
  }

  formatCurrency(amount: number, currency: string = 'TRY'): string {
    return new Intl.NumberFormat('tr-TR', {
      style: 'currency',
      currency: currency,
    }).format(amount);
  }

  formatPercentage(percentage: number): string {
    return `${percentage > 0 ? '+' : ''}${percentage.toFixed(2)}%`;
  }

  getTotalValue(deposits: Portfolio[]): number {
    return deposits.reduce((sum, deposit) => sum + deposit.currentValue, 0);
  }

  getTotalPL(deposits: Portfolio[]): number {
    return deposits.reduce((sum, deposit) => sum + deposit.profitLoss, 0);
  }

  getTotalPLPercentage(deposits: Portfolio[]): number {
    const totalCost = deposits.reduce((sum, deposit) => sum + deposit.totalCost, 0);
    const totalPL = this.getTotalPL(deposits);
    return totalCost > 0 ? (totalPL / totalCost) * 100 : 0;
  }

  openDepositDetail(deposit: Portfolio): void {
    console.log('Opening deposit detail for:', deposit);
    this.selectedDeposit = deposit;
    document.body.style.overflow = 'hidden';
  }

  closeDepositDetail(): void {
    this.selectedDeposit = null;
    document.body.style.overflow = 'auto';
  }

  // Transaction helper fonksiyonları
  getTransactionTypeLabel(type: string): string {
    switch (type) {
      case 'DEPOSIT_ADD':
        return 'PARA EKLE';
      case 'DEPOSIT_WITHDRAW':
        return 'PARA ÇIKAR';
      case 'DEPOSIT_INCOME':
        return 'FAİZ GELİRİ';
      default:
        return type;
    }
  }

  isDepositOrIncome(type: string): boolean {
    return type === 'DEPOSIT_ADD' || type === 'DEPOSIT_INCOME';
  }

  isWithdraw(type: string): boolean {
    return type === 'DEPOSIT_WITHDRAW';
  }

  getTransactionDetailText(transaction: any): string {
    const currency = this.selectedDeposit?.asset?.currency || 'TRY';

    switch (transaction.type) {
      case 'DEPOSIT_ADD':
        return `${this.formatCurrency(transaction.quantity * transaction.price, currency)} tutar eklendi`;
      case 'DEPOSIT_WITHDRAW':
        return `${this.formatCurrency(transaction.quantity * transaction.price, currency)} tutar çekildi`;
      case 'DEPOSIT_INCOME':
        return `${this.formatCurrency(transaction.quantity * transaction.price, currency)} faiz geliri`;
      default:
        return `${transaction.quantity} × ${this.formatCurrency(transaction.price, currency)}`;
    }
  }

  formatDate(date: Date): string {
    return new Intl.DateTimeFormat('tr-TR').format(date);
  }
}
