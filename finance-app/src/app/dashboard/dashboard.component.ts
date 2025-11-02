import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Observable, finalize } from 'rxjs';
import { DashboardSummary, AssetType, Portfolio } from '../models/asset.model';
import { PortfolioService } from '../services/portfolio.service';
import { TransactionExportService } from '../services/transaction-export.service';
import { AssetService } from '../services/asset.service';
import { ProfitLossChartComponent } from '../components/profit-loss-chart.component';
import { CurrencySelectorComponent } from '../components/currency-selector-new.component';
import { ExchangeRateService } from '../services/exchange-rate.service';

@Component({
  selector: 'app-dashboard',
  imports: [CommonModule, RouterLink, ProfitLossChartComponent, CurrencySelectorComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  dashboardSummary$!: Observable<DashboardSummary>;
  AssetType = AssetType;
  lastUpdated = new Date();
  importResults: { success: number; errors: string[] } | null = null;
  emailSending = false;

  constructor(
    private portfolioService: PortfolioService,
    private transactionExportService: TransactionExportService,
    private assetService: AssetService,
    private exchangeRateService: ExchangeRateService
  ) {}

  ngOnInit(): void {
    this.dashboardSummary$ = this.portfolioService.getDashboardSummary();
    this.refreshLastUpdated();

    // Debug: API'dan gelen verileri kontrol et
    this.portfolioService.getPortfolio().subscribe((portfolios) => {
      console.log('🔍 Portfolio Debug:', portfolios);
    });

    // Debug: Dashboard summary'yi kontrol et
    this.dashboardSummary$.subscribe((summary) => {
      console.log('📊 Dashboard Summary Debug:', summary);
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
    const total = portfolios.reduce((sum, p) => sum + p.currentValue, 0);
    console.log('📊 getTotalValueByType:', total, portfolios);
    return total;
  }

  getTotalPLByType(portfolios: Portfolio[]): number {
    const total = portfolios.reduce((sum, p) => sum + p.profitLoss, 0);
    console.log('📈 getTotalPLByType:', total, portfolios);
    return total;
  }

  formatCurrency(amount: number, currency?: string): string {
    const selectedCurrency = currency || this.exchangeRateService.getSelectedCurrency();
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: selectedCurrency,
    }).format(amount);
  }

  formatPercentage(percentage: number): string {
    return `${percentage.toFixed(2)}%`;
  }

  // Export/Import Methods
  exportTransactions(): void {
    console.log('🚀 Export button clicked - starting export process...');

    this.transactionExportService.exportTransactionsToExcel().subscribe({
      next: () => {
        console.log('✅ Transactions exported successfully');
        alert('Transactions exported successfully!');
      },
      error: (error) => {
        console.error('❌ Export failed:', error);
        alert('Export failed: ' + error);
      },
    });
  }

  downloadTemplate(): void {
    this.transactionExportService.downloadTemplate().catch((error) => {
      console.error('Template download failed:', error);
      alert('Template download failed: ' + error);
    });
  }

  onFileSelected(event: Event): void {
    const target = event.target as HTMLInputElement;
    const file = target.files?.[0];

    if (file) {
      this.importTransactions(file);
    }
  }

  importTransactions(file: File): void {
    this.importResults = null;

    this.transactionExportService.importTransactionsFromExcel(file).subscribe({
      next: (results) => {
        this.importResults = results;
        console.log('Import completed:', results);

        // Refresh the page data if there were successful imports
        if (results.success > 0) {
          // Refresh assets and dashboard
          this.assetService.refreshAssets();
          this.dashboardSummary$ = this.portfolioService.getDashboardSummary();
          this.refreshLastUpdated();
        }
      },
      error: (error) => {
        console.error('Import failed:', error);
        this.importResults = { success: 0, errors: [error.toString()] };
      },
    });
  }

  sendSummaryEmail(): void {
    // const recipientEmail = prompt('Gmail adresini girin:');
    // if (!recipientEmail) {
    //   return;
    // }

    const recipientEmail = 'mtafasahin@gmail.com';

    const message = undefined; // prompt('E-postaya eklenecek not (opsiyonel):') ?? undefined;
    const subject = `Portföy Özeti - ${new Date().toLocaleDateString('tr-TR')}`;

    this.emailSending = true;
    this.portfolioService
      .emailDashboardSummary({ recipientEmail, subject, message })
      .pipe(finalize(() => (this.emailSending = false)))
      .subscribe({
        next: () => {
          alert('Özet e-posta ile gönderildi.');
        },
        error: (error) => {
          console.error('E-posta gönderimi başarısız:', error);
          alert('E-posta gönderilemedi. Sunucu ayarlarını kontrol edin.');
        },
      });
  }
}
