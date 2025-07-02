import { Component, OnInit, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Observable } from 'rxjs';
import { Portfolio, AssetType } from '../models/asset.model';
import { PortfolioService, HistoricalInvestment } from '../services/portfolio.service';

@Component({
  selector: 'app-asset-portfolio',
  imports: [CommonModule],
  templateUrl: './asset-portfolio.component.html',
  styleUrl: './asset-portfolio.component.scss'
})
export class AssetPortfolioComponent implements OnInit {
  @Input() assetType!: AssetType;
  @Input() title!: string;
  
  portfolios$!: Observable<Portfolio[]>;
  historicalInvestments$!: Observable<HistoricalInvestment[]>;
  totalProfitLoss$!: Observable<{totalPL: number, realizedPL: number, unrealizedPL: number}>;
  
  selectedPortfolio: Portfolio | null = null;
  selectedHistorical: HistoricalInvestment | null = null;
  showHistorical: boolean = false;

  constructor(private portfolioService: PortfolioService) {}

  ngOnInit(): void {
    this.portfolios$ = this.portfolioService.getPortfolioByType(this.assetType);
    this.historicalInvestments$ = this.portfolioService.getHistoricalInvestmentsByType(this.assetType);
    this.totalProfitLoss$ = this.portfolioService.getTotalProfitLoss();
  }

  toggleHistoricalView(): void {
    this.showHistorical = !this.showHistorical;
  }

  openPortfolioDetail(portfolio: Portfolio): void {
    this.selectedPortfolio = portfolio;
    this.selectedHistorical = null;
    document.body.style.overflow = 'hidden';
  }

  openHistoricalDetail(historical: HistoricalInvestment): void {
    this.selectedHistorical = historical;
    this.selectedPortfolio = null;
    document.body.style.overflow = 'hidden';
  }

  closePortfolioDetail(): void {
    this.selectedPortfolio = null;
    this.selectedHistorical = null;
    document.body.style.overflow = 'auto';
  }

  formatCurrency(amount: number, currency: string = 'TRY'): string {
    return new Intl.NumberFormat('tr-TR', {
      style: 'currency',
      currency: currency
    }).format(amount);
  }

  formatPercentage(percentage: number): string {
    return `${percentage > 0 ? '+' : ''}${percentage.toFixed(2)}%`;
  }

  formatDate(date: Date): string {
    return new Intl.DateTimeFormat('tr-TR').format(date);
  }
}
