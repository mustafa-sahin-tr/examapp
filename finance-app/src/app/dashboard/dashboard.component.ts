import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { DashboardSummary, AssetType, Portfolio } from '../models/asset.model';
import { PortfolioService } from '../services/portfolio.service';

@Component({
  selector: 'app-dashboard',
  imports: [CommonModule, RouterLink],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  dashboardSummary$!: Observable<DashboardSummary>;
  AssetType = AssetType;
  lastUpdated = new Date();

  constructor(private portfolioService: PortfolioService) {}

  ngOnInit(): void {
    this.dashboardSummary$ = this.portfolioService.getDashboardSummary();
    this.refreshLastUpdated();

    // Debug: API'dan gelen verileri kontrol et
    this.portfolioService.getPortfolio().subscribe((portfolios) => {
      console.log('🔍 Portfolio Debug:', portfolios);
    });
  }

  refreshLastUpdated(): void {
    this.lastUpdated = new Date();
  }

  getAssetTypeDisplayName(type: AssetType): string {
    const displayNames = {
      [AssetType.Stock]: 'BIST 100',
      [AssetType.USStock]: 'US Stocks',
      [AssetType.Gold]: 'Gold',
      [AssetType.Silver]: 'Silver',
      [AssetType.Fund]: 'Funds',
      [AssetType.FixedDeposit]: 'Vadeli Mevduat',
    };
    return displayNames[type];
  }

  getAssetTypeRoute(type: AssetType): string {
    const routes = {
      [AssetType.Stock]: '/bist100',
      [AssetType.USStock]: '/us-stocks',
      [AssetType.Gold]: '/precious-metals',
      [AssetType.Silver]: '/precious-metals',
      [AssetType.Fund]: '/funds',
      [AssetType.FixedDeposit]: '/fixed-deposits', // Vadeli mevduat için yeni route
    };
    return routes[type];
  }

  getTotalValueByType(portfolios: Portfolio[]): number {
    return portfolios.reduce((sum, p) => sum + p.currentValue, 0);
  }

  getTotalPLByType(portfolios: Portfolio[]): number {
    return portfolios.reduce((sum, p) => sum + p.profitLoss, 0);
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
}
