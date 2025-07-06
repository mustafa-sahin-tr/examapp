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
}
