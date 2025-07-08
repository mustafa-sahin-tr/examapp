import { Component, OnInit, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Observable, map } from 'rxjs';
import { Portfolio, AssetType } from '../models/asset.model';
import { PortfolioService, HistoricalInvestment } from '../services/portfolio.service';

interface SortConfig {
  column: string;
  direction: 'asc' | 'desc';
}

@Component({
  selector: 'app-asset-portfolio',
  imports: [CommonModule],
  templateUrl: './asset-portfolio.component.html',
  styleUrl: './asset-portfolio.component.scss',
})
export class AssetPortfolioComponent implements OnInit {
  @Input() assetType!: AssetType;
  @Input() title!: string;

  portfolios$!: Observable<Portfolio[]>;
  historicalInvestments$!: Observable<HistoricalInvestment[]>;
  totalProfitLoss$!: Observable<{ totalPL: number; realizedPL: number; unrealizedPL: number }>;

  selectedPortfolio: Portfolio | null = null;
  selectedHistorical: HistoricalInvestment | null = null;
  showHistorical: boolean = false;

  // Sorting için değişkenler
  currentSort: SortConfig = { column: '', direction: 'asc' };
  historicalSort: SortConfig = { column: '', direction: 'asc' };

  constructor(private portfolioService: PortfolioService) {}

  ngOnInit(): void {
    this.portfolios$ = this.portfolioService
      .getPortfolioByType(this.assetType)
      .pipe(map((portfolios) => this.sortPortfolios(portfolios, this.currentSort)));
    this.historicalInvestments$ = this.portfolioService
      .getHistoricalInvestmentsByType(this.assetType)
      .pipe(map((historicals) => this.sortHistoricals(historicals, this.historicalSort)));
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
      currency: currency,
    }).format(amount);
  }

  formatPercentage(percentage: number): string {
    return `${percentage > 0 ? '+' : ''}${percentage.toFixed(2)}%`;
  }

  formatDate(date: Date): string {
    return new Intl.DateTimeFormat('tr-TR').format(date);
  }

  // Sorting fonksiyonları
  sortPortfolios(portfolios: Portfolio[], sortConfig: SortConfig): Portfolio[] {
    if (!sortConfig.column) return portfolios;

    return [...portfolios].sort((a, b) => {
      let aVal: any;
      let bVal: any;

      switch (sortConfig.column) {
        case 'asset':
          aVal = a.asset?.symbol || '';
          bVal = b.asset?.symbol || '';
          break;
        case 'quantity':
          aVal = a.totalQuantity;
          bVal = b.totalQuantity;
          break;
        case 'avgPrice':
          aVal = a.averagePrice;
          bVal = b.averagePrice;
          break;
        case 'currentPrice':
          aVal = a.asset?.currentPrice || 0;
          bVal = b.asset?.currentPrice || 0;
          break;
        case 'totalValue':
          aVal = a.currentValue;
          bVal = b.currentValue;
          break;
        case 'profitLoss':
          aVal = a.profitLoss;
          bVal = b.profitLoss;
          break;
        case 'profitLossPercentage':
          aVal = a.profitLossPercentage;
          bVal = b.profitLossPercentage;
          break;
        default:
          return 0;
      }

      if (typeof aVal === 'string' && typeof bVal === 'string') {
        return sortConfig.direction === 'asc' ? aVal.localeCompare(bVal) : bVal.localeCompare(aVal);
      } else {
        return sortConfig.direction === 'asc' ? aVal - bVal : bVal - aVal;
      }
    });
  }

  sortHistoricals(historicals: HistoricalInvestment[], sortConfig: SortConfig): HistoricalInvestment[] {
    if (!sortConfig.column) return historicals;

    return [...historicals].sort((a, b) => {
      let aVal: any;
      let bVal: any;

      switch (sortConfig.column) {
        case 'asset':
          aVal = a.assetSymbol || '';
          bVal = b.assetSymbol || '';
          break;
        case 'totalBought':
          aVal = a.totalBought;
          bVal = b.totalBought;
          break;
        case 'totalSold':
          aVal = a.totalSold;
          bVal = b.totalSold;
          break;
        case 'avgBuyPrice':
          aVal = a.averageBuyPrice;
          bVal = b.averageBuyPrice;
          break;
        case 'avgSellPrice':
          aVal = a.averageSellPrice;
          bVal = b.averageSellPrice;
          break;
        case 'realizedPL':
          aVal = a.realizedProfitLoss;
          bVal = b.realizedProfitLoss;
          break;
        default:
          return 0;
      }

      if (typeof aVal === 'string' && typeof bVal === 'string') {
        return sortConfig.direction === 'asc' ? aVal.localeCompare(bVal) : bVal.localeCompare(aVal);
      } else {
        return sortConfig.direction === 'asc' ? aVal - bVal : bVal - aVal;
      }
    });
  }

  // Sort değiştirme fonksiyonları
  sortCurrentPortfolio(column: string): void {
    if (this.currentSort.column === column) {
      // Aynı kolona tıklandıysa yönü değiştir
      this.currentSort.direction = this.currentSort.direction === 'asc' ? 'desc' : 'asc';
    } else {
      // Yeni kolona tıklandıysa o kolonu ascending olarak ayarla
      this.currentSort = { column, direction: 'asc' };
    }

    // Portfolios observable'ını yeniden oluştur
    this.portfolios$ = this.portfolioService
      .getPortfolioByType(this.assetType)
      .pipe(map((portfolios) => this.sortPortfolios(portfolios, this.currentSort)));
  }

  sortHistoricalPortfolio(column: string): void {
    if (this.historicalSort.column === column) {
      // Aynı kolona tıklandıysa yönü değiştir
      this.historicalSort.direction = this.historicalSort.direction === 'asc' ? 'desc' : 'asc';
    } else {
      // Yeni kolona tıklandıysa o kolonu ascending olarak ayarla
      this.historicalSort = { column, direction: 'asc' };
    }

    // Historical investments observable'ını yeniden oluştur
    this.historicalInvestments$ = this.portfolioService
      .getHistoricalInvestmentsByType(this.assetType)
      .pipe(map((historicals) => this.sortHistoricals(historicals, this.historicalSort)));
  }

  // Sort direction'ını check etmek için helper fonksiyonlar
  getSortIcon(column: string, isHistorical: boolean = false): string {
    const currentSortConfig = isHistorical ? this.historicalSort : this.currentSort;

    if (currentSortConfig.column === column) {
      return currentSortConfig.direction === 'asc' ? '↑' : '↓';
    }
    return '↕'; // Default sort icon
  }
}
